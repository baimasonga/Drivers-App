using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Dtos;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly AuditService _audit;

    public AuthController(AppDbContext db, JwtTokenService jwt, AuditService audit)
    {
        _db = db; _jwt = jwt; _audit = audit;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid credentials" });

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Login", "User", user.Id);

        var token = _jwt.Issue(user);
        return new LoginResponse(token, user.Id, user.FullName, user.Role.ToString());
    }

    [HttpPost("register")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<int>> Register(RegisterRequest req)
    {
        if (await _db.Users.AnyAsync(u => u.Email == req.Email))
            return BadRequest(new { error = "Email already exists" });

        var user = new User
        {
            FullName = req.FullName,
            Email = req.Email,
            Phone = req.Phone,
            Department = req.Department,
            Role = req.Role,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        if (req.Role == UserRole.Driver)
        {
            _db.Drivers.Add(new Driver { UserId = user.Id });
            await _db.SaveChangesAsync();
        }

        await _audit.LogAsync("CreateUser", "User", user.Id, $"Role={req.Role}");
        return user.Id;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<object>> Me()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var id)) return Unauthorized();
        var u = await _db.Users.FindAsync(id);
        if (u == null) return NotFound();
        return new { u.Id, u.FullName, u.Email, Role = u.Role.ToString(), u.Department };
    }
}
