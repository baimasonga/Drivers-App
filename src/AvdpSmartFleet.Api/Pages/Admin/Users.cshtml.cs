using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class UsersModel : PageModel
{
    private readonly AppDbContext _db;
    public UsersModel(AppDbContext db) => _db = db;

    public record Row(int Id, string FullName, string Email, string Role, string? Department, bool IsActive, DateTime? LastLoginAt);
    public List<Row> Items { get; set; } = new();
    public string? Message { get; set; }
    public string? Error { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string fullName,
        [FromForm] string email,
        [FromForm] string? phone,
        [FromForm] UserRole role,
        [FromForm] string? department,
        [FromForm] string password)
    {
        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            Error = "A user with that email already exists.";
            await LoadAsync();
            return Page();
        }
        var u = new User
        {
            FullName = fullName, Email = email, Phone = phone, Department = department,
            Role = role, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };
        _db.Users.Add(u);
        await _db.SaveChangesAsync();
        if (role == UserRole.Driver)
        {
            _db.Drivers.Add(new Driver { UserId = u.Id });
            await _db.SaveChangesAsync();
        }
        Message = $"User {email} created.";
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var u = await _db.Users.FindAsync(id);
        if (u != null)
        {
            u.IsActive = !u.IsActive;
            await _db.SaveChangesAsync();
            Message = $"{u.Email} is now {(u.IsActive ? "active" : "disabled")}.";
        }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        Items = await _db.Users.OrderBy(u => u.FullName)
            .Select(u => new Row(u.Id, u.FullName, u.Email, u.Role.ToString(),
                u.Department, u.IsActive, u.LastLoginAt))
            .ToListAsync();
    }
}
