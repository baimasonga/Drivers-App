using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly AppDbContext _db;
    public ChatController(AppDbContext db) => _db = db;

    [HttpGet("rooms/{room}/messages")]
    public async Task<IEnumerable<object>> History(string room, [FromQuery] int take = 50)
    {
        // Authorization: drivers can only read their own trip's room
        if (room.StartsWith("trip-") && int.TryParse(room[5..], out var tripId))
        {
            if (User.IsInRole("Driver"))
            {
                var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value;
                if (int.TryParse(sub, out var uid))
                {
                    var driver = await _db.Drivers.FirstOrDefaultAsync(d => d.UserId == uid);
                    var trip = await _db.TravelRequests.FindAsync(tripId);
                    if (driver == null || trip == null || trip.AssignedDriverId != driver.Id)
                        return Enumerable.Empty<object>();
                }
            }
        }

        return await _db.ChatMessages
            .Where(m => m.Room == room)
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .Select(m => new
            {
                m.Id, m.Room,
                m.SenderUserId, m.SenderName, m.SenderRole,
                m.Body, m.SentAt
            })
            .ToListAsync()
            .ContinueWith(t => (IEnumerable<object>)t.Result.OrderBy(x => ((dynamic)x).SentAt).ToList());
    }
}
