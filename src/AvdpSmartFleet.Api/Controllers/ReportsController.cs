using System.Text;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReportsController(AppDbContext db) => _db = db;

    [HttpGet("trips.csv")]
    public async Task<IActionResult> TripsCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _db.TravelRequests
            .Include(t => t.Requester)
            .Include(t => t.AssignedVehicle)
            .Include(t => t.AssignedDriver).ThenInclude(d => d!.User)
            .Include(t => t.DestinationGeofence)
            .AsQueryable();
        if (from.HasValue) q = q.Where(t => t.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(t => t.CreatedAt <= to.Value);

        var rows = await q.OrderByDescending(t => t.CreatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Code,Status,Requester,Department,Purpose,Destination,Vehicle,Driver,PlannedDeparture,PlannedReturn,StartedAt,CompletedAt,StartOdo,EndOdo,ComplianceScore");
        foreach (var t in rows)
        {
            string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                Esc(t.RequestCode),
                Esc(t.Status.ToString()),
                Esc(t.Requester.FullName),
                Esc(t.Department),
                Esc(t.Purpose),
                Esc(t.DestinationGeofence?.Name ?? t.SpecificDestination),
                Esc(t.AssignedVehicle?.PlateNumber),
                Esc(t.AssignedDriver?.User.FullName),
                Esc(t.PlannedDeparture.ToString("yyyy-MM-dd HH:mm")),
                Esc(t.PlannedReturn.ToString("yyyy-MM-dd HH:mm")),
                Esc(t.StartedAt?.ToString("yyyy-MM-dd HH:mm")),
                Esc(t.CompletedAt?.ToString("yyyy-MM-dd HH:mm")),
                t.StartOdometer?.ToString() ?? "",
                t.EndOdometer?.ToString() ?? "",
                t.ComplianceScore?.ToString("F1") ?? ""));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"avdp-trips-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("exceptions.csv")]
    public async Task<IActionResult> ExceptionsCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _db.TripExceptions.Include(e => e.TravelRequest).AsQueryable();
        if (from.HasValue) q = q.Where(e => e.DetectedAt >= from.Value);
        if (to.HasValue) q = q.Where(e => e.DetectedAt <= to.Value);

        var rows = await q.OrderByDescending(e => e.DetectedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("TripCode,Type,Status,Description,DriverExplanation,OfficerNote,DetectedAt,ResolvedAt");
        foreach (var e in rows)
        {
            string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                Esc(e.TravelRequest.RequestCode),
                Esc(e.Type.ToString()),
                Esc(e.Status.ToString()),
                Esc(e.Description),
                Esc(e.DriverExplanation),
                Esc(e.OfficerNote),
                Esc(e.DetectedAt.ToString("yyyy-MM-dd HH:mm")),
                Esc(e.ResolvedAt?.ToString("yyyy-MM-dd HH:mm"))));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"avdp-exceptions-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("vehicle-utilization.csv")]
    public async Task<IActionResult> UtilizationCsv([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        from ??= DateTime.UtcNow.AddDays(-30);
        to ??= DateTime.UtcNow;
        var data = await _db.Vehicles
            .Select(v => new
            {
                v.PlateNumber, v.Make, v.Model,
                Trips = _db.TravelRequests.Count(t => t.AssignedVehicleId == v.Id && t.CompletedAt >= from && t.CompletedAt <= to),
                KmDriven = _db.TravelRequests
                    .Where(t => t.AssignedVehicleId == v.Id && t.CompletedAt >= from && t.CompletedAt <= to && t.StartOdometer != null && t.EndOdometer != null)
                    .Sum(t => (int?)(t.EndOdometer - t.StartOdometer)) ?? 0
            })
            .ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine($"# AVDP Vehicle Utilization {from:yyyy-MM-dd} → {to:yyyy-MM-dd}");
        sb.AppendLine("Plate,Make,Model,Trips,KmDriven");
        foreach (var v in data)
            sb.AppendLine($"{v.PlateNumber},{v.Make},{v.Model},{v.Trips},{v.KmDriven}");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"avdp-utilization-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
