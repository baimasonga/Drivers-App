using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Services;

public record VehicleHealth(
    int VehicleId,
    string PlateNumber,
    HealthStatus Status,
    int? KmSinceService,
    int? KmUntilService,
    DateTime? LastServiceAt,
    int RecentIssues,
    string Recommendation);

public class VehicleHealthService
{
    private readonly AppDbContext _db;
    public VehicleHealthService(AppDbContext db) => _db = db;

    public async Task<List<VehicleHealth>> GetAllAsync()
    {
        var vehicles = await _db.Vehicles.ToListAsync();
        var result = new List<VehicleHealth>();
        foreach (var v in vehicles)
        {
            result.Add(await ComputeAsync(v));
        }
        return result.OrderByDescending(h => (int)h.Status).ToList();
    }

    public async Task<VehicleHealth?> GetForVehicleAsync(int id)
    {
        var v = await _db.Vehicles.FindAsync(id);
        return v == null ? null : await ComputeAsync(v);
    }

    private async Task<VehicleHealth> ComputeAsync(Vehicle v)
    {
        int? kmSince = (v.CurrentOdometer.HasValue && v.LastServiceOdometer.HasValue)
            ? v.CurrentOdometer.Value - v.LastServiceOdometer.Value : null;
        int? kmUntil = kmSince.HasValue ? v.ServiceIntervalKm - kmSince.Value : null;

        var twoWeeks = DateTime.UtcNow.AddDays(-14);
        var recentIssues = await _db.TripLogs
            .Where(l => l.EventType == "Issue"
                        && l.TravelRequest.AssignedVehicleId == v.Id
                        && l.EventAt >= twoWeeks)
            .CountAsync();

        HealthStatus status = HealthStatus.Healthy;
        string rec = "OK — no action needed.";

        if (v.Status == VehicleStatus.OutOfService)
        {
            status = HealthStatus.Grounded;
            rec = "Vehicle is out of service. Repair before re-deploying.";
        }
        else if (recentIssues >= 3)
        {
            status = HealthStatus.RepeatedIssues;
            rec = $"⚠️ {recentIssues} driver-reported issues in the last 14 days. Schedule inspection.";
        }
        else if (kmSince.HasValue && kmSince.Value > v.ServiceIntervalKm)
        {
            status = HealthStatus.Overdue;
            rec = $"❌ Service overdue by {kmSince.Value - v.ServiceIntervalKm:N0} km. Schedule immediately.";
        }
        else if (kmUntil.HasValue && kmUntil.Value <= 500)
        {
            status = HealthStatus.ServiceDue;
            rec = $"⏰ Service due in {kmUntil.Value:N0} km. Book a slot soon.";
        }
        else if (!v.LastServiceAt.HasValue && v.CreatedAt < DateTime.UtcNow.AddMonths(-6))
        {
            status = HealthStatus.ServiceDue;
            rec = "⏰ No service record on file for 6+ months. Log a service or schedule one.";
        }

        return new VehicleHealth(v.Id, v.PlateNumber, status, kmSince, kmUntil,
            v.LastServiceAt, recentIssues, rec);
    }
}
