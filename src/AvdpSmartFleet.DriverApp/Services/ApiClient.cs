using System.Net.Http.Json;

namespace AvdpSmartFleet.DriverApp.Services;

public record LoginResponse(string Token, int UserId, string FullName, string Role);
public record TripAssignment(int Id, string RequestCode, string Status, string Purpose,
    DateTime PlannedDeparture, DateTime PlannedReturn, string Vehicle, string? Destination);
public record TripLogRequest(int TravelRequestId, string EventType,
    double? PhoneLat, double? PhoneLng, string? Notes, int? OdometerReading,
    string? LocalUuid, DateTime? EventAt, string? PhotoUrl = null);

public record DiversionRequest(int TravelRequestId, string Reason, double? CurrentLat, double? CurrentLng, string? NewDestination, bool Emergency);

public record DriverQuery(int Id, int TripId, string TripCode, string Type, string Status,
    string Description, string? DriverExplanation, DateTime DetectedAt);

public record UploadResult(string Url, long Size);

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

    public async Task<List<DriverQuery>?> GetMyQueriesAsync()
    {
        try { return await _http.GetFromJsonAsync<List<DriverQuery>>("api/my-queries"); }
        catch { return null; }
    }

    public async Task<bool> ExplainAsync(int exceptionId, string explanation)
    {
        var resp = await _http.PostAsJsonAsync($"api/exceptions/{exceptionId}/explain",
            new { explanation });
        return resp.IsSuccessStatusCode;
    }

    public async Task<UploadResult?> UploadPhotoAsync(Stream content, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var sc = new StreamContent(content);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);
        var resp = await _http.PostAsync("api/photos/upload", form);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<UploadResult>();
    }
}
