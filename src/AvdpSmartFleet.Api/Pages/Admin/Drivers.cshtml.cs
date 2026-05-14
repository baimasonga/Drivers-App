using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class DriversModel : PageModel
{
    private readonly AppDbContext _db;
    public DriversModel(AppDbContext db) => _db = db;
    public record Row(string Name, string? Phone, string Email, string? License, bool Available);
    public List<Row> Items { get; set; } = new();
    public async Task OnGetAsync()
    {
        Items = await _db.Drivers.Include(d => d.User)
            .Select(d => new Row(d.User.FullName, d.User.Phone, d.User.Email, d.LicenseNumber, d.IsAvailable))
            .ToListAsync();
    }
}
