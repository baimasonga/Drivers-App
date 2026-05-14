using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class GeofencesModel : PageModel
{
    private readonly AppDbContext _db;
    public GeofencesModel(AppDbContext db) => _db = db;
    public record Row(string Name, string? Category, double Lat, double Lng, int Radius, bool Provisional);
    public List<Row> Items { get; set; } = new();
    public async Task OnGetAsync()
    {
        Items = await _db.Geofences
            .Select(g => new Row(g.Name, g.Category, g.CenterLat, g.CenterLng, g.RadiusMeters, g.IsProvisional))
            .ToListAsync();
    }
}
