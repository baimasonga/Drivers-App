using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Dtos;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/travel-requests")]
public class TravelRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public TravelRequestsController(AppDbContext db, AuditService audit)
    {
        _db = db; _audit = audit;
    }

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")!.Value);

    [HttpGet]
    public async Task<IEnumerable<object>> List([FromQuery] TripStatus? status, [FromQuery] int take = 100)
    {
        var q = _db.TravelRequests
            .Include(t => t.Requester)
            .Include(t => t.AssignedDriver).ThenInclude(d => d!.User)
            .Include(t => t.AssignedVehicle)
            .Include(t => t.DestinationGeofence)
            .AsQueryable();
        if (status.HasValue) q = q.Where(t => t.Status == status.Value);
        return await q.OrderByDescending(t => t.CreatedAt).Take(take)
            .Select(t => new
            {
                t.Id,
                t.RequestCode,
                Status = t.Status.ToString(),
                t.Purpose,
                Requester = t.Requester.FullName,
                t.Department,
                Destination = t.DestinationGeofence != null ? t.DestinationGeofence.Name : t.SpecificDestination,
                Vehicle = t.AssignedVehicle != null ? t.AssignedVehicle.PlateNumber : null,
                Driver = t.AssignedDriver != null ? t.AssignedDriver.User.FullName : null,
                t.PlannedDeparture,
                t.PlannedReturn,
                Priority = t.Priority.ToString(),
                t.ComplianceScore
            }).ToListAsync();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<object>> Get(int id)
    {
        var t = await _db.TravelRequests
            .Include(x => x.Requester)
            .Include(x => x.AssignedDriver).ThenInclude(d => d!.User)
            .Include(x => x.AssignedVehicle).ThenInclude(v => v!.GpsUnit)
            .Include(x => x.DestinationGeofence)
            .Include(x => x.TripLogs)
            .Include(x => x.GateClearances)
            .Include(x => x.Exceptions)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        return Ok(new
        {
            t.Id,
            t.RequestCode,
            Status = t.Status.ToString(),
            t.Purpose,
            Requester = new { t.Requester.Id, t.Requester.FullName, t.Requester.Department },
            Destination = new
            {
                Geofence = t.DestinationGeofence?.Name,
                t.SpecificDestination,
                t.DestinationDistrict,
                t.DestinationLat,
                t.DestinationLng
            },
            Vehicle = t.AssignedVehicle == null ? null : new
            {
                t.AssignedVehicle.Id,
                t.AssignedVehicle.PlateNumber,
                t.AssignedVehicle.Make,
                t.AssignedVehicle.Model,
                GpsUnit = t.AssignedVehicle.GpsUnit?.ExternalUnitId
            },
            Driver = t.AssignedDriver == null ? null : new
            {
                t.AssignedDriver.Id,
                Name = t.AssignedDriver.User.FullName,
                t.AssignedDriver.User.Phone
            },
            t.PlannedDeparture,
            t.PlannedReturn,
            t.StartedAt,
            t.CompletedAt,
            t.StartOdometer,
            t.EndOdometer,
            t.ComplianceScore,
            TripLogs = t.TripLogs.OrderBy(l => l.EventAt).Select(l => new
            {
                l.Id, l.EventType, l.EventAt, l.PhoneLat, l.PhoneLng, l.Notes, l.OdometerReading
            }),
            GateEvents = t.GateClearances.OrderBy(g => g.EventAt).Select(g => new
            {
                g.Id, Action = g.Action.ToString(), g.EventAt, g.OdometerReading
            }),
            Exceptions = t.Exceptions.Select(e => new
            {
                e.Id, Type = e.Type.ToString(), Status = e.Status.ToString(), e.Description, e.DetectedAt
            })
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(CreateTravelRequest req)
    {
        var user = await _db.Users.FindAsync(CurrentUserId);
        if (user == null) return Unauthorized();

        var year = DateTime.UtcNow.Year;
        var seq = await _db.TravelRequests.CountAsync(t => t.CreatedAt.Year == year) + 1;
        var code = $"AVDP-TRIP-{year}-{seq:D4}";

        var t = new TravelRequest
        {
            RequestCode = code,
            RequesterId = user.Id,
            Department = user.Department,
            Purpose = req.Purpose,
            RequestedVehicleType = req.RequestedVehicleType,
            DestinationDistrict = req.DestinationDistrict,
            SpecificDestination = req.SpecificDestination,
            DestinationGeofenceId = req.DestinationGeofenceId,
            DestinationLat = req.DestinationLat,
            DestinationLng = req.DestinationLng,
            PlannedDeparture = req.PlannedDeparture,
            PlannedReturn = req.PlannedReturn,
            Priority = req.Priority,
            PassengerList = req.PassengerList,
            Status = TripStatus.PendingFleetReview
        };
        _db.TravelRequests.Add(t);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("SubmitRequest", "TravelRequest", t.Id, code);
        return new { t.Id, t.RequestCode, Status = t.Status.ToString() };
    }

    [HttpPost("{id:int}/assign")]
    [Authorize(Roles = "FleetOfficer,Admin")]
    public async Task<IActionResult> Assign(int id, AssignVehicleDriverRequest req)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status != TripStatus.PendingFleetReview && t.Status != TripStatus.PendingApproval)
            return BadRequest(new { error = $"Cannot assign in status {t.Status}" });

        var vehicle = await _db.Vehicles.FindAsync(req.VehicleId);
        var driver = await _db.Drivers.Include(d => d.User).FirstOrDefaultAsync(d => d.Id == req.DriverId);
        if (vehicle == null || driver == null) return BadRequest(new { error = "Vehicle or driver not found" });
        if (vehicle.Status == VehicleStatus.OutOfService) return BadRequest(new { error = "Vehicle out of service" });

        // Overlap check
        bool overlap = await _db.TravelRequests.AnyAsync(x =>
            x.Id != id &&
            (x.AssignedVehicleId == req.VehicleId || x.AssignedDriverId == req.DriverId) &&
            x.Status >= TripStatus.ApprovedForDispatch && x.Status <= TripStatus.Returning &&
            x.PlannedDeparture < t.PlannedReturn && x.PlannedReturn > t.PlannedDeparture);
        if (overlap) return BadRequest(new { error = "Vehicle or driver already assigned in overlapping window" });

        t.AssignedVehicleId = req.VehicleId;
        t.AssignedDriverId = req.DriverId;
        t.Status = TripStatus.PendingApproval;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("AssignVehicleDriver", "TravelRequest", t.Id, $"V={req.VehicleId} D={req.DriverId}");
        return Ok(new { Status = t.Status.ToString() });
    }

    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Approve(int id, ApproveRequest req)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status != TripStatus.PendingApproval) return BadRequest(new { error = "Not pending approval" });

        if (req.Approve)
        {
            t.Status = TripStatus.ApprovedForDispatch;
            t.ApprovedById = CurrentUserId;
            t.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            t.Status = TripStatus.Rejected;
            t.RejectionReason = req.RejectionReason;
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(req.Approve ? "ApproveRequest" : "RejectRequest", "TravelRequest", t.Id, req.RejectionReason);
        return Ok(new { Status = t.Status.ToString() });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] string reason)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        if (t.Status >= TripStatus.TripStarted)
            return BadRequest(new { error = "Cannot cancel after trip has started" });

        // Requester can cancel own draft/submitted; FleetOfficer/Manager/Admin can cancel later stages
        bool isOwner = t.RequesterId == CurrentUserId;
        bool isOfficer = User.IsInRole("FleetOfficer") || User.IsInRole("Manager") || User.IsInRole("Admin");
        if (!isOwner && !isOfficer) return Forbid();

        t.Status = TripStatus.Cancelled;
        t.RejectionReason = reason;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("CancelRequest", "TravelRequest", t.Id, reason);
        return Ok(new { Status = t.Status.ToString() });
    }
}
