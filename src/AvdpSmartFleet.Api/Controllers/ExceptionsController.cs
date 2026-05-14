using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

public record DiversionRequest(int TravelRequestId, string Reason, double? CurrentLat, double? CurrentLng, string? NewDestination, bool Emergency);
public record DiversionDecision(bool Approve, string? Note);
public record DriverExplanation(string Explanation);
public record OfficerNote(string Note, bool Resolve);

[ApiController]
[Authorize]
[Route("api")]
public class ExceptionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    public ExceptionsController(AppDbContext db, AuditService audit) { _db = db; _audit = audit; }

    private int CurrentUserId =>
        int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")!.Value);

    [HttpPost("trips/{id:int}/diversion")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> RequestDiversion(int id, DiversionRequest req)
    {
        var t = await _db.TravelRequests.FindAsync(id);
        if (t == null) return NotFound();
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (driver == null || t.AssignedDriverId != driver.Id) return Forbid();
        if (t.Status < TripStatus.TripStarted || t.Status >= TripStatus.ReturnedToBase)
            return BadRequest(new { error = "Trip not active" });

        var details = $"Reason: {req.Reason}; NewDest: {req.NewDestination}; Emergency: {req.Emergency}";
        var ex = new TripException
        {
            TravelRequestId = t.Id,
            Type = req.Emergency ? ExceptionType.RouteDeviation : ExceptionType.RouteDeviation,
            Status = ExceptionStatus.Open,
            Description = $"Diversion requested: {details}"
        };
        _db.TripExceptions.Add(ex);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("RequestDiversion", "TravelRequest", t.Id, details);
        return Ok(new { exceptionId = ex.Id });
    }

    [HttpPost("exceptions/{id:int}/decision")]
    [Authorize(Roles = "FleetOfficer,Manager,Admin")]
    public async Task<IActionResult> DecideDiversion(int id, DiversionDecision dec)
    {
        var ex = await _db.TripExceptions.FindAsync(id);
        if (ex == null) return NotFound();
        ex.Status = dec.Approve ? ExceptionStatus.Resolved : ExceptionStatus.Escalated;
        ex.OfficerNote = dec.Note;
        ex.ResolvedAt = DateTime.UtcNow;
        ex.ResolvedById = CurrentUserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(dec.Approve ? "ApproveDiversion" : "RejectDiversion", "TripException", id, dec.Note);
        return Ok(new { status = ex.Status.ToString() });
    }

    [HttpPost("exceptions/{id:int}/explain")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IActionResult> Explain(int id, DriverExplanation body)
    {
        var ex = await _db.TripExceptions
            .Include(e => e.TravelRequest)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (ex == null) return NotFound();
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (driver == null || ex.TravelRequest.AssignedDriverId != driver.Id) return Forbid();

        ex.DriverExplanation = body.Explanation;
        ex.Status = ExceptionStatus.UnderReview;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("DriverExplain", "TripException", id, body.Explanation);
        return Ok(new { status = ex.Status.ToString() });
    }

    [HttpPost("exceptions/{id:int}/resolve")]
    [Authorize(Roles = "FleetOfficer,Manager,Admin")]
    public async Task<IActionResult> Resolve(int id, OfficerNote body)
    {
        var ex = await _db.TripExceptions.FindAsync(id);
        if (ex == null) return NotFound();
        ex.OfficerNote = body.Note;
        ex.Status = body.Resolve ? ExceptionStatus.Resolved : ExceptionStatus.Escalated;
        ex.ResolvedAt = DateTime.UtcNow;
        ex.ResolvedById = CurrentUserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(body.Resolve ? "ResolveException" : "EscalateException", "TripException", id, body.Note);
        return Ok(new { status = ex.Status.ToString() });
    }

    [HttpGet("trips/{id:int}/exceptions")]
    public async Task<IEnumerable<object>> ForTrip(int id) =>
        await _db.TripExceptions.Where(e => e.TravelRequestId == id)
            .OrderByDescending(e => e.DetectedAt)
            .Select(e => new
            {
                e.Id, Type = e.Type.ToString(), Status = e.Status.ToString(),
                e.Description, e.DriverExplanation, e.OfficerNote, e.DetectedAt, e.ResolvedAt
            }).ToListAsync();

    [HttpGet("my-queries")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<IEnumerable<object>> MyQueries()
    {
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == CurrentUserId);
        if (driver == null) return Enumerable.Empty<object>();
        return await _db.TripExceptions
            .Include(e => e.TravelRequest)
            .Where(e => e.TravelRequest.AssignedDriverId == driver.Id
                        && (e.Status == ExceptionStatus.Open || e.Status == ExceptionStatus.UnderReview)
                        && (e.DriverExplanation == null || e.Status == ExceptionStatus.Open))
            .OrderByDescending(e => e.DetectedAt)
            .Select(e => new
            {
                e.Id,
                TripId = e.TravelRequestId,
                TripCode = e.TravelRequest.RequestCode,
                Type = e.Type.ToString(),
                Status = e.Status.ToString(),
                e.Description,
                e.DriverExplanation,
                e.DetectedAt
            }).ToListAsync();
    }
}
