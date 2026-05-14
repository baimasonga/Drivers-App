using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class RequestDetailModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ReconciliationService _recon;
    public RequestDetailModel(AppDbContext db, ReconciliationService recon)
    {
        _db = db; _recon = recon;
    }

    public record TripDto(int Id, string Code, string Status, string Purpose, string Requester, string? Department,
        string? Destination, string? Vehicle, int? VehicleId, string? Driver, int? DriverId,
        DateTime PlannedDeparture, DateTime PlannedReturn, DateTime? StartedAt, DateTime? CompletedAt, double? Score);
    public record LogDto(DateTime EventAt, string EventType, double? Lat, double? Lng, int? Odometer, string? Notes);
    public record GateDto(DateTime At, string Action, int? Odo);
    public record ExDto(int Id, string Type, string Status, string Description, string? Explanation);
    public record VehicleOpt(int Id, string Plate);
    public record DriverOpt(int Id, string Name);

    public TripDto? Trip { get; set; }
    public List<LogDto> Logs { get; set; } = new();
    public List<GateDto> Gates { get; set; } = new();
    public List<ExDto> Exceptions { get; set; } = new();
    public List<VehicleOpt> Vehicles { get; set; } = new();
    public List<DriverOpt> Drivers { get; set; } = new();
    public string? Message { get; set; }

    public async Task OnGetAsync(int id) => await LoadAsync(id);

    public async Task<IActionResult> OnPostAssignAsync(int id, [FromForm] int vehicleId, [FromForm] int driverId)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t != null && (t.Status == TripStatus.PendingFleetReview || t.Status == TripStatus.PendingApproval))
        {
            t.AssignedVehicleId = vehicleId;
            t.AssignedDriverId = driverId;
            t.Status = TripStatus.PendingApproval;
            await _db.SaveChangesAsync();
            Message = "Vehicle and driver assigned. Awaiting manager approval.";
        }
        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, [FromForm] string action, [FromForm] string? reason)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t != null && t.Status == TripStatus.PendingApproval)
        {
            if (action == "approve")
            {
                t.Status = TripStatus.ApprovedForDispatch;
                t.ApprovedAt = DateTime.UtcNow;
                Message = "Approved for dispatch.";
            }
            else
            {
                t.Status = TripStatus.Rejected;
                t.RejectionReason = reason;
                Message = "Request rejected.";
            }
            await _db.SaveChangesAsync();
        }
        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync(int id, [FromForm] string action, [FromForm] string? note)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t != null && t.Status == TripStatus.PendingVerification)
        {
            t.Status = action == "verify" ? TripStatus.Verified : TripStatus.Queried;
            await _db.SaveChangesAsync();
            Message = action == "verify" ? "Trip verified." : "Trip queried — driver explanation required.";
        }
        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t != null && (t.Status == TripStatus.Verified || t.Status == TripStatus.Queried))
        {
            t.Status = TripStatus.Closed;
            await _db.SaveChangesAsync();
            Message = "Trip closed.";
        }
        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostReconcileAsync(int id)
    {
        var result = await _recon.ReconcileAsync(id);
        var t = await _db.TravelRequests.FindAsync(id);
        if (t != null)
        {
            t.ComplianceScore = result.ComplianceScore;
            foreach (var ex in result.Exceptions) _db.TripExceptions.Add(ex);
            await _db.SaveChangesAsync();
            Message = $"Reconciliation re-run. Score {result.ComplianceScore:F0}%, {result.Exceptions.Count} new exception(s).";
        }
        await LoadAsync(id);
        return Page();
    }

    private async Task LoadAsync(int id)
    {
        var t = await _db.TravelRequests
            .Include(x => x.Requester)
            .Include(x => x.AssignedVehicle)
            .Include(x => x.AssignedDriver).ThenInclude(d => d!.User)
            .Include(x => x.DestinationGeofence)
            .Include(x => x.TripLogs)
            .Include(x => x.GateClearances)
            .Include(x => x.Exceptions)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return;

        Trip = new TripDto(
            t.Id, t.RequestCode, t.Status.ToString(), t.Purpose, t.Requester.FullName, t.Department,
            t.DestinationGeofence?.Name ?? t.SpecificDestination,
            t.AssignedVehicle?.PlateNumber, t.AssignedVehicleId,
            t.AssignedDriver?.User.FullName, t.AssignedDriverId,
            t.PlannedDeparture, t.PlannedReturn, t.StartedAt, t.CompletedAt, t.ComplianceScore);

        Logs = t.TripLogs.OrderBy(l => l.EventAt)
            .Select(l => new LogDto(l.EventAt, l.EventType, l.PhoneLat, l.PhoneLng, l.OdometerReading, l.Notes))
            .ToList();
        Gates = t.GateClearances.OrderBy(g => g.EventAt)
            .Select(g => new GateDto(g.EventAt, g.Action.ToString(), g.OdometerReading))
            .ToList();
        Exceptions = t.Exceptions
            .Select(e => new ExDto(e.Id, e.Type.ToString(), e.Status.ToString(), e.Description, e.DriverExplanation))
            .ToList();

        Vehicles = await _db.Vehicles.Where(v => v.Status != VehicleStatus.OutOfService)
            .Select(v => new VehicleOpt(v.Id, v.PlateNumber)).ToListAsync();
        Drivers = await _db.Drivers.Include(d => d.User)
            .Select(d => new DriverOpt(d.Id, d.User.FullName)).ToListAsync();
    }
}
