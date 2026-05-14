using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class RequestsModel : PageModel
{
    private readonly AppDbContext _db;
    public RequestsModel(AppDbContext db) => _db = db;

    public record Row(int Id, string Code, string Requester, string? Department, string Purpose,
        DateTime PlannedDeparture, string? Vehicle, string? Driver, string Status, double? Score);
    public List<Row> Items { get; set; } = new();

    public async Task OnGetAsync()
    {
        Items = await _db.TravelRequests
            .Include(t => t.Requester)
            .Include(t => t.AssignedVehicle)
            .Include(t => t.AssignedDriver).ThenInclude(d => d!.User)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new Row(
                t.Id,
                t.RequestCode,
                t.Requester.FullName,
                t.Department,
                t.Purpose,
                t.PlannedDeparture,
                t.AssignedVehicle != null ? t.AssignedVehicle.PlateNumber : null,
                t.AssignedDriver != null ? t.AssignedDriver.User.FullName : null,
                t.Status.ToString(),
                t.ComplianceScore))
            .ToListAsync();
    }
}
