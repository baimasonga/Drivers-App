using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class VehiclesModel : PageModel
{
    private readonly AppDbContext _db;
    public VehiclesModel(AppDbContext db) => _db = db;
    public record Row(string Plate, string Make, string Model, string Type, string? HomeBase, string Status, string? GpsUnit, int? Odometer);
    public List<Row> Items { get; set; } = new();
    public async Task OnGetAsync()
    {
        Items = await _db.Vehicles.Include(v => v.GpsUnit)
            .Select(v => new Row(v.PlateNumber, v.Make, v.Model, v.Type, v.HomeBase,
                v.Status.ToString(), v.GpsUnit != null ? v.GpsUnit.ExternalUnitId : null, v.CurrentOdometer))
            .ToListAsync();
    }
}
