using Microsoft.JSInterop;

namespace AvdpSmartFleet.DriverApp.Services;

public record GeoPosition(double Lat, double Lng, double? Accuracy);

public class GeolocationService
{
    private readonly IJSRuntime _js;
    public GeolocationService(IJSRuntime js) => _js = js;

    public async Task<GeoPosition?> GetCurrentAsync()
    {
        try
        {
            var result = await _js.InvokeAsync<GeoPosition?>("getCurrentPosition");
            return result;
        }
        catch
        {
            return null;
        }
    }
}
