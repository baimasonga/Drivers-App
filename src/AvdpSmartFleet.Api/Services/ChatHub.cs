using System.Security.Claims;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AvdpSmartFleet.Api.Services;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    public ChatHub(AppDbContext db) => _db = db;

    public async Task JoinRoom(string room)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, room);
    }

    public async Task LeaveRoom(string room)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room);
    }

    public async Task Send(string room, string body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000) return;

        var sub = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? Context.User?.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var userId)) return;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return;

        var msg = new ChatMessage
        {
            Room = room,
            SenderUserId = user.Id,
            SenderName = user.FullName,
            SenderRole = user.Role.ToString(),
            Body = body.Trim()
        };
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        await Clients.Group(room).SendAsync("messageReceived", new
        {
            id = msg.Id,
            room,
            senderUserId = msg.SenderUserId,
            senderName = msg.SenderName,
            senderRole = msg.SenderRole,
            body = msg.Body,
            sentAt = msg.SentAt
        });
    }
}
