using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public int TotalRequests { get; set; }
    public int Approved { get; set; }
    public int Completed { get; set; }
    public int OpenExceptions { get; set; }
    public int UnauthorizedMovements { get; set; }
    public double AvgCompliance { get; set; }
    public double Co2KgMonth { get; set; }
    public double KmMonth { get; set; }

    public record Row(int Id, string RequestCode, string Purpose, string Requester, string? Vehicle, string Status, double? ComplianceScore);
    public List<Row> Recent { get; set; } = new();

    public async Task OnGetAsync()
    {
        var from = DateTime.UtcNow.AddDays(-30);
        TotalRequests = await _db.TravelRequests.CountAsync(t => t.CreatedAt >= from);
        Approved = await _db.TravelRequests.CountAsync(t => t.CreatedAt >= from && t.ApprovedAt != null);
        Completed = await _db.TravelRequests.CountAsync(t => t.CompletedAt >= from);
        OpenExceptions = await _db.TripExceptions.CountAsync(e => e.Status == ExceptionStatus.Open);
        UnauthorizedMovements = await _db.UnauthorizedMovements.CountAsync(u => !u.Resolved);
        AvgCompliance = await _db.TravelRequests
            .Where(t => t.CompletedAt >= from && t.ComplianceScore != null)
            .AverageAsync(t => (double?)t.ComplianceScore) ?? 0;
        Co2KgMonth = await _db.TravelRequests
            .Where(t => t.CompletedAt >= from && t.Co2Kg != null)
            .SumAsync(t => (double?)t.Co2Kg) ?? 0;
        KmMonth = await _db.TravelRequests
            .Where(t => t.CompletedAt >= from && t.DistanceKm != null)
            .SumAsync(t => (double?)t.DistanceKm) ?? 0;

        Recent = await _db.TravelRequests
            .Include(t => t.Requester)
            .Include(t => t.AssignedVehicle)
            .OrderByDescending(t => t.CreatedAt)
            .Take(15)
            .Select(t => new Row(
                t.Id,
                t.RequestCode,
                t.Purpose,
                t.Requester.FullName,
                t.AssignedVehicle != null ? t.AssignedVehicle.PlateNumber : null,
                t.Status.ToString(),
                t.ComplianceScore))
            .ToListAsync();
    }
}
