using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

public record ReportAlertRequest(RoadAlertType Type, AlertSeverity Severity, double Lat, double Lng, string? Notes, string? PhotoUrl);
public record ConfirmAlertRequest(AlertConfirmation Vote, double? Lat, double? Lng);

[ApiController]
[Authorize]
[Route("api/road-alerts")]
public class RoadAlertsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public RoadAlertsController(AppDbContext db, AuditService audit) { _db = db; _audit = audit; }

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")!.Value);

    [HttpGet("active")]
    public async Task<IEnumerable<object>> Active([FromQuery] double? lat, [FromQuery] double? lng, [FromQuery] double? radiusKm)
    {
        // Auto-expire alerts whose window has passed
        var stale = await _db.RoadAlerts
            .Where(a => a.IsActive && a.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();
        if (stale.Count > 0)
        {
            stale.ForEach(a => a.IsActive = false);
            await _db.SaveChangesAsync();
        }

        var q = _db.RoadAlerts.Where(a => a.IsActive);
        var all = await q.OrderByDescending(a => a.ReportedAt).Take(200).ToListAsync();

        // Optional radius filter (default: return all if no coords)
        if (lat.HasValue && lng.HasValue)
        {
            var r = radiusKm ?? 50;
            all = all.Where(a => GeoUtils.HaversineMeters(lat.Value, lng.Value, a.Lat, a.Lng) <= r * 1000).ToList();
        }

        return all.Select(a => new
        {
            a.Id,
            type = a.Type.ToString(),
            severity = a.Severity.ToString(),
            a.Lat, a.Lng,
            a.Notes,
            a.PhotoUrl,
            a.ReportedAt,
            a.ExpiresAt,
            a.ConfirmationsStillThere,
            a.ConfirmationsCleared,
            confidence = ConfidenceScore(a)
        });
    }

    private static double ConfidenceScore(RoadAlert a)
    {
        // Newer + more "still there" confirmations + fewer "cleared" = higher
        var hoursOld = (DateTime.UtcNow - a.ReportedAt).TotalHours;
        var ageDecay = Math.Max(0.2, 1.0 - hoursOld / 24.0);
        var votes = 1 + a.ConfirmationsStillThere - a.ConfirmationsCleared;
        return Math.Round(Math.Min(1.0, votes * 0.25) * ageDecay, 2);
    }

    [HttpPost]
    [Authorize(Roles = "Driver,FleetOfficer,Admin")]
    public async Task<ActionResult<object>> Report(ReportAlertRequest req)
    {
        var a = new RoadAlert
        {
            Type = req.Type,
            Severity = req.Severity,
            Lat = req.Lat, Lng = req.Lng,
            Notes = req.Notes,
            PhotoUrl = req.PhotoUrl,
            ReportedByUserId = CurrentUserId
        };
        // Higher severity persists longer
        a.ExpiresAt = req.Severity switch
        {
            AlertSeverity.High => DateTime.UtcNow.AddHours(48),
            AlertSeverity.Medium => DateTime.UtcNow.AddHours(24),
            _ => DateTime.UtcNow.AddHours(8)
        };
        _db.RoadAlerts.Add(a);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("ReportRoadAlert", "RoadAlert", a.Id, $"{req.Type}/{req.Severity}");
        return new { a.Id };
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = "Driver,FleetOfficer,Admin")]
    public async Task<IActionResult> Confirm(int id, ConfirmAlertRequest req)
    {
        var a = await _db.RoadAlerts.FindAsync(id);
        if (a == null || !a.IsActive) return NotFound();

        // One vote per user per alert
        var existing = await _db.RoadAlertConfirmations
            .FirstOrDefaultAsync(c => c.RoadAlertId == id && c.UserId == CurrentUserId);
        if (existing != null) return Conflict(new { error = "Already voted" });

        _db.RoadAlertConfirmations.Add(new RoadAlertConfirmation
        {
            RoadAlertId = id,
            UserId = CurrentUserId,
            Vote = req.Vote,
            Lat = req.Lat,
            Lng = req.Lng
        });
        if (req.Vote == AlertConfirmation.StillThere) a.ConfirmationsStillThere++;
        else a.ConfirmationsCleared++;

        // 2+ "cleared" votes retire the alert
        if (a.ConfirmationsCleared >= 2) a.IsActive = false;
        // 3+ "still there" votes extend the window
        else if (a.ConfirmationsStillThere >= 3 && a.ExpiresAt < DateTime.UtcNow.AddHours(12))
            a.ExpiresAt = DateTime.UtcNow.AddHours(24);

        await _db.SaveChangesAsync();
        await _audit.LogAsync("ConfirmRoadAlert", "RoadAlert", id, req.Vote.ToString());
        return Ok(new { active = a.IsActive, a.ConfirmationsStillThere, a.ConfirmationsCleared });
    }
}
