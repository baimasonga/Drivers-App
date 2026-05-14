using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;

namespace AvdpSmartFleet.Api.Services;

public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, string? targetType = null, int? targetId = null, string? details = null)
    {
        int? actor = null;
        var sub = _http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? _http.HttpContext?.User.FindFirst("sub")?.Value;
        if (int.TryParse(sub, out var id)) actor = id;

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        await _db.SaveChangesAsync();
    }
}
