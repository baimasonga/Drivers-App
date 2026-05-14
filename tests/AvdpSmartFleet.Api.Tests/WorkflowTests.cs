using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AvdpSmartFleet.Api.Tests;

public class WorkflowTests : IClassFixture<TestWebApp>
{
    private readonly TestWebApp _app;
    public WorkflowTests(TestWebApp app) => _app = app;

    private record LoginResp(string Token, int UserId, string FullName, string Role);
    private record CreateResp(int Id, string RequestCode, string Status);
    private record StatusResp(string Status);

    private async Task<string> LoginAsync(HttpClient c, string email, string password)
    {
        var r = await c.PostAsJsonAsync("/api/auth/login", new { email, password });
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadFromJsonAsync<LoginResp>();
        return body!.Token;
    }

    private HttpClient WithToken(HttpClient c, string token)
    {
        c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }

    [Fact]
    public async Task Full_happy_path_runs_reconciliation_and_assigns_compliance_score()
    {
        var client = _app.CreateClient();

        var reqTok = await LoginAsync(client, "requester@avdp.sl", "user123");
        var fleetTok = await LoginAsync(client, "fleet@avdp.sl", "fleet123");
        var mgrTok = await LoginAsync(client, "manager@avdp.sl", "manager123");
        var gateTok = await LoginAsync(client, "gate@avdp.sl", "gate123");
        var drvTok = await LoginAsync(client, "driver@avdp.sl", "driver123");

        // 1. Requester creates trip
        WithToken(client, reqTok);
        var dep = DateTime.UtcNow.AddHours(1);
        var ret = DateTime.UtcNow.AddHours(9);
        var createResp = await client.PostAsJsonAsync("/api/travel-requests", new
        {
            purpose = "Field monitoring at Kambia IVS",
            requestedVehicleType = "Pickup",
            destinationDistrict = "Kambia",
            destinationGeofenceId = 2,
            plannedDeparture = dep,
            plannedReturn = ret,
            priority = 0
        });
        var createBody = await createResp.Content.ReadAsStringAsync();
        Assert.True(createResp.IsSuccessStatusCode,
            $"Create failed: {(int)createResp.StatusCode} {createResp.StatusCode}. Body: {createBody}");
        var created = await createResp.Content.ReadFromJsonAsync<CreateResp>();
        Assert.NotNull(created);
        var tripId = created!.Id;

        // 2. Fleet assigns
        WithToken(client, fleetTok);
        var assignResp = await client.PostAsJsonAsync($"/api/travel-requests/{tripId}/assign",
            new { vehicleId = 1, driverId = 1 });
        assignResp.EnsureSuccessStatusCode();

        // 3. Manager approves
        WithToken(client, mgrTok);
        var approveResp = await client.PostAsJsonAsync($"/api/travel-requests/{tripId}/approve",
            new { approve = true, rejectionReason = (string?)null });
        approveResp.EnsureSuccessStatusCode();

        // 4. Gate exit
        WithToken(client, gateTok);
        var exitResp = await client.PostAsJsonAsync("/api/trips/gate-clearance", new
        {
            travelRequestId = tripId,
            action = 0,
            odometerReading = 45230
        });
        exitResp.EnsureSuccessStatusCode();

        // 5. Driver logs
        WithToken(client, drvTok);
        foreach (var ev in new[] { "Departed", "Arrived", "Returning" })
        {
            var r = await client.PostAsJsonAsync("/api/trips/log",
                new { travelRequestId = tripId, eventType = ev });
            r.EnsureSuccessStatusCode();
        }

        // 6. Gate entry — triggers reconciliation
        WithToken(client, gateTok);
        var entryResp = await client.PostAsJsonAsync("/api/trips/gate-clearance", new
        {
            travelRequestId = tripId,
            action = 1,
            odometerReading = 45580
        });
        entryResp.EnsureSuccessStatusCode();

        // 7. Fetch detail and assert compliance score + status
        WithToken(client, fleetTok);
        var detail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"/api/travel-requests/{tripId}");
        Assert.Equal("PendingVerification", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("complianceScore").GetDouble() > 0);
    }

    [Fact]
    public async Task Unauthorized_endpoint_returns_401_without_token()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/api/travel-requests");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Requester_cannot_assign_vehicle()
    {
        var client = _app.CreateClient();
        var reqTok = await LoginAsync(client, "requester@avdp.sl", "user123");
        WithToken(client, reqTok);

        var dep = DateTime.UtcNow.AddHours(1);
        var ret = DateTime.UtcNow.AddHours(9);
        var create = await client.PostAsJsonAsync("/api/travel-requests", new
        {
            purpose = "Test trip",
            destinationGeofenceId = 2,
            plannedDeparture = dep,
            plannedReturn = ret,
            priority = 0
        });
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<CreateResp>();

        // Requester tries to assign — should be forbidden
        var assign = await client.PostAsJsonAsync($"/api/travel-requests/{created!.Id}/assign",
            new { vehicleId = 1, driverId = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
    }
}
