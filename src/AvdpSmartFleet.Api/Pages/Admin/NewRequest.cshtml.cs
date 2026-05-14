using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class NewRequestModel : PageModel
{
    private readonly AppDbContext _db;
    public NewRequestModel(AppDbContext db) => _db = db;

    public record UserOpt(int Id, string Name, string? Department);
    public record GeofenceOpt(int Id, string Name, string? Category);
    public List<UserOpt> Requesters { get; set; } = new();
    public List<GeofenceOpt> Geofences { get; set; } = new();
    public string? Error { get; set; }
    public string? Created { get; set; }

    public async Task OnGetAsync() => await LoadOptionsAsync();

    public async Task<IActionResult> OnPostAsync(
        [FromForm] int requesterId,
        [FromForm] string purpose,
        [FromForm] int? destinationGeofenceId,
        [FromForm] string? destinationDistrict,
        [FromForm] DateTime plannedDeparture,
        [FromForm] DateTime plannedReturn,
        [FromForm] string? requestedVehicleType,
        [FromForm] Priority priority)
    {
        await LoadOptionsAsync();
        if (plannedReturn <= plannedDeparture)
        {
            Error = "Planned return must be after planned departure.";
            return Page();
        }

        var requester = await _db.Users.FindAsync(requesterId);
        if (requester == null) { Error = "Requester not found."; return Page(); }

        var year = DateTime.UtcNow.Year;
        var seq = await _db.TravelRequests.CountAsync(t => t.CreatedAt.Year == year) + 1;
        var code = $"AVDP-TRIP-{year}-{seq:D4}";

        var t = new TravelRequest
        {
            RequestCode = code,
            RequesterId = requester.Id,
            Department = requester.Department,
            Purpose = purpose,
            RequestedVehicleType = requestedVehicleType,
            DestinationDistrict = destinationDistrict,
            DestinationGeofenceId = destinationGeofenceId,
            PlannedDeparture = DateTime.SpecifyKind(plannedDeparture, DateTimeKind.Utc),
            PlannedReturn = DateTime.SpecifyKind(plannedReturn, DateTimeKind.Utc),
            Priority = priority,
            Status = TripStatus.PendingFleetReview
        };
        _db.TravelRequests.Add(t);
        await _db.SaveChangesAsync();
        Created = $"Created {code}";
        return Page();
    }

    private async Task LoadOptionsAsync()
    {
        Requesters = await _db.Users
            .Where(u => u.IsActive && u.Role == UserRole.Requester)
            .OrderBy(u => u.FullName)
            .Select(u => new UserOpt(u.Id, u.FullName, u.Department))
            .ToListAsync();
        Geofences = await _db.Geofences
            .OrderBy(g => g.Name)
            .Select(g => new GeofenceOpt(g.Id, g.Name, g.Category))
            .ToListAsync();
    }
}
