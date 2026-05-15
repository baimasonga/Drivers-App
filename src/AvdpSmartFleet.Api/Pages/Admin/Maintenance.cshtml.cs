using AvdpSmartFleet.Api.Domain;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class MaintenanceModel : PageModel
{
    private readonly VehicleHealthService _health;
    public MaintenanceModel(VehicleHealthService health) => _health = health;

    public List<VehicleHealth> Items { get; set; } = new();
    public int HealthyCount { get; set; }
    public int DueCount { get; set; }
    public int OverdueCount { get; set; }
    public int IssueCount { get; set; }
    public int GroundedCount { get; set; }

    public async Task OnGetAsync()
    {
        Items = await _health.GetAllAsync();
        HealthyCount = Items.Count(i => i.Status == HealthStatus.Healthy);
        DueCount = Items.Count(i => i.Status == HealthStatus.ServiceDue);
        OverdueCount = Items.Count(i => i.Status == HealthStatus.Overdue);
        IssueCount = Items.Count(i => i.Status == HealthStatus.RepeatedIssues);
        GroundedCount = Items.Count(i => i.Status == HealthStatus.Grounded);
    }
}
