using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/scorecards")]
public class ScorecardController : ControllerBase
{
    private readonly AppDbContext _db;
    public ScorecardController(AppDbContext db) => _db = db;

    [HttpGet("leaderboard")]
    public async Task<IEnumerable<object>> Leaderboard([FromQuery] int days = 30)
    {
        var from = DateTime.UtcNow.AddDays(-days);
        var rows = await _db.Drivers
            .Include(d => d.User)
            .Select(d => new
            {
                d.Id,
                Name = d.User.FullName,
                Trips = _db.TravelRequests.Count(t => t.AssignedDriverId == d.Id && t.CompletedAt >= from),
                AvgScore = _db.TravelRequests
                    .Where(t => t.AssignedDriverId == d.Id && t.CompletedAt >= from && t.ComplianceScore != null)
                    .Average(t => (double?)t.ComplianceScore) ?? 0,
                KmDriven = _db.TravelRequests
                    .Where(t => t.AssignedDriverId == d.Id && t.CompletedAt >= from && t.DistanceKm != null)
                    .Sum(t => (double?)t.DistanceKm) ?? 0,
                OpenQueries = _db.TripExceptions.Count(e =>
                    e.TravelRequest.AssignedDriverId == d.Id
                    && e.Status == ExceptionStatus.Open
                    && e.DetectedAt >= from)
            })
            .ToListAsync();

        return rows
            .OrderByDescending(r => r.AvgScore)
            .ThenByDescending(r => r.Trips)
            .Select((r, i) => new
            {
                rank = i + 1,
                r.Id,
                r.Name,
                r.Trips,
                avgScore = Math.Round(r.AvgScore, 1),
                kmDriven = Math.Round(r.KmDriven, 1),
                r.OpenQueries,
                badge = BadgeFor(r.AvgScore, r.Trips, r.OpenQueries)
            });
    }

    [HttpGet("my")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<ActionResult<object>> MyScorecard()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var userId)) return Unauthorized();
        var driver = await _db.Drivers.Include(d => d.User).FirstOrDefaultAsync(d => d.UserId == userId);
        if (driver == null) return NotFound();

        var from = DateTime.UtcNow.AddDays(-30);
        var trips = await _db.TravelRequests
            .Where(t => t.AssignedDriverId == driver.Id && t.CompletedAt >= from)
            .ToListAsync();
        var openQueries = await _db.TripExceptions
            .CountAsync(e => e.TravelRequest.AssignedDriverId == driver.Id && e.Status == ExceptionStatus.Open);
        var avg = trips.Where(t => t.ComplianceScore.HasValue).Select(t => t.ComplianceScore!.Value).DefaultIfEmpty(0).Average();
        var kmTotal = trips.Where(t => t.DistanceKm.HasValue).Sum(t => t.DistanceKm!.Value);

        return Ok(new
        {
            driver.Id,
            name = driver.User.FullName,
            trips = trips.Count,
            avgScore = Math.Round(avg, 1),
            kmDriven = Math.Round(kmTotal, 1),
            openQueries,
            badge = BadgeFor(avg, trips.Count, openQueries),
            streak = await ComplianceStreak(driver.Id)
        });
    }

    private async Task<int> ComplianceStreak(int driverId)
    {
        var recent = await _db.TravelRequests
            .Where(t => t.AssignedDriverId == driverId && t.ComplianceScore != null)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => t.ComplianceScore)
            .Take(50)
            .ToListAsync();
        int streak = 0;
        foreach (var s in recent) { if (s >= 90) streak++; else break; }
        return streak;
    }

    private static string BadgeFor(double avgScore, int trips, int openQueries)
    {
        if (trips == 0) return "Newcomer";
        if (openQueries > 0) return "Needs attention";
        return avgScore switch
        {
            >= 95 => "Gold star",
            >= 90 => "Excellent",
            >= 80 => "Reliable",
            >= 70 => "Improving",
            _ => "Coach"
        };
    }
}
