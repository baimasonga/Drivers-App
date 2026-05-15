using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class VehicleServiceModel : PageModel
{
    private readonly AppDbContext _db;
    public VehicleServiceModel(AppDbContext db) => _db = db;

    public Vehicle? Vehicle { get; set; }
    public string Plate => Vehicle?.PlateNumber ?? "";
    public List<ServiceRecord> History { get; set; } = new();
    public string? Message { get; set; }

    public async Task OnGetAsync(int id) => await LoadAsync(id);

    public async Task<IActionResult> OnPostLogAsync(
        int id,
        [FromForm] ServiceType type,
        [FromForm] int? odometerAt,
        [FromForm] DateTime? performedAt,
        [FromForm] string? servicedBy,
        [FromForm] decimal? costSll,
        [FromForm] string? notes)
    {
        var v = await _db.Vehicles.FindAsync(id);
        if (v == null) return NotFound();

        var s = new ServiceRecord
        {
            VehicleId = id,
            Type = type,
            OdometerAt = odometerAt,
            PerformedAt = performedAt.HasValue
                ? DateTime.SpecifyKind(performedAt.Value, DateTimeKind.Utc)
                : DateTime.UtcNow,
            Notes = notes,
            CostSll = costSll,
            ServicedBy = servicedBy
        };
        _db.ServiceRecords.Add(s);

        if (type is ServiceType.OilChange or ServiceType.FullService or ServiceType.BrakeService)
        {
            v.LastServiceOdometer = odometerAt ?? v.CurrentOdometer;
            v.LastServiceAt = s.PerformedAt;
        }
        if (odometerAt.HasValue && (v.CurrentOdometer == null || odometerAt > v.CurrentOdometer))
            v.CurrentOdometer = odometerAt;

        await _db.SaveChangesAsync();
        Message = $"Logged {type} on {s.PerformedAt:yyyy-MM-dd}.";
        await LoadAsync(id);
        return Page();
    }

    private async Task LoadAsync(int id)
    {
        Vehicle = await _db.Vehicles.FindAsync(id);
        History = await _db.ServiceRecords
            .Where(s => s.VehicleId == id)
            .OrderByDescending(s => s.PerformedAt)
            .ToListAsync();
    }
}
