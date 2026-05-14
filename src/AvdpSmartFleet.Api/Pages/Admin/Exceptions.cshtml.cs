using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class ExceptionsModel : PageModel
{
    private readonly AppDbContext _db;
    public ExceptionsModel(AppDbContext db) => _db = db;
    public record Row(int TripId, string TripCode, string Type, string Status, string Description, DateTime DetectedAt);
    public List<Row> Items { get; set; } = new();
    public async Task OnGetAsync()
    {
        Items = await _db.TripExceptions
            .Include(e => e.TravelRequest)
            .OrderByDescending(e => e.DetectedAt)
            .Take(200)
            .Select(e => new Row(
                e.TravelRequestId,
                e.TravelRequest.RequestCode,
                e.Type.ToString(),
                e.Status.ToString(),
                e.Description,
                e.DetectedAt))
            .ToListAsync();
    }
}
