using System.Net.Http.Json;

namespace AvdpSmartFleet.DriverApp.Services;

public record LoginResponse(string Token, int UserId, string FullName, string Role);
public record TripAssignment(int Id, string RequestCode, string Status, string Purpose,
    DateTime PlannedDeparture, DateTime PlannedReturn, string Vehicle, string? Destination);
public record TripLogRequest(int TravelRequestId, string EventType,
    double? PhoneLat, double? PhoneLng, string? Notes, int? OdometerReading,
    string? LocalUuid, DateTime? EventAt);

public record DiversionRequest(int TravelRequestId, string Reason, double? CurrentLat, double? CurrentLng, string? NewDestination, bool Emergency);

public class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) => _http = http;

    public async Task<LoginResponse?> LoginAsync(string email, string password)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login", new { email, password });
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<LoginResponse>();
    }

    public async Task<List<TripAssignment>?> GetMyAssignmentsAsync()
    {
        try { return await _http.GetFromJsonAsync<List<TripAssignment>>("api/trips/my-assignments"); }
        catch { return null; }
    }

    public async Task<bool> LogEventAsync(TripLogRequest req)
    {
        var resp = await _http.PostAsJsonAsync("api/trips/log", req);
        return resp.IsSuccessStatusCode;
    }

    public async Task<bool> RequestDiversionAsync(DiversionRequest req)
    {
        var resp = await _http.PostAsJsonAsync($"api/trips/{req.TravelRequestId}/diversion", req);
        return resp.IsSuccessStatusCode;
    }
}
