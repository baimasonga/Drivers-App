using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/coaching")]
public class CoachingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DriverCoachingService _svc;
    public CoachingController(AppDbContext db, DriverCoachingService svc) { _db = db; _svc = svc; }

    [HttpGet("my")]
    [Authorize(Roles = "Driver,Admin")]
    public async Task<ActionResult<DriverCoaching>> My()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var uid)) return Unauthorized();
        var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == uid);
        if (driver == null) return NotFound();
        var coaching = await _svc.ForDriverAsync(driver.Id);
        return coaching == null ? NotFound() : Ok(coaching);
    }

    [HttpGet("drivers/{driverId:int}")]
    [Authorize(Roles = "FleetOfficer,Manager,Auditor,Admin")]
    public async Task<ActionResult<DriverCoaching>> Driver(int driverId)
    {
        var c = await _svc.ForDriverAsync(driverId);
        return c == null ? NotFound() : Ok(c);
    }
}
