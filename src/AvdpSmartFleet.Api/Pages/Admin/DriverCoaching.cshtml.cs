using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AvdpSmartFleet.Api.Pages.Admin;

public class DriverCoachingModel : PageModel
{
    private readonly DriverCoachingService _svc;
    public DriverCoachingModel(DriverCoachingService svc) => _svc = svc;

    public DriverCoaching? Coaching { get; set; }

    public async Task OnGetAsync(int id)
    {
        Coaching = await _svc.ForDriverAsync(id);
    }
}
