using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace AvdpSmartFleet.Api.Tests;

public class AuthTests : IClassFixture<TestWebApp>
{
    private readonly TestWebApp _app;
    public AuthTests(TestWebApp app) => _app = app;

    public record LoginResp(string Token, int UserId, string FullName, string Role);

    [Fact]
    public async Task Login_with_seeded_admin_succeeds()
    {
        var client = _app.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@avdp.sl", password = "admin123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResp>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.Token));
        Assert.Equal("Admin", body.Role);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = _app.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@avdp.sl", password = "wrong" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Five_failed_logins_lock_account()
    {
        var client = _app.CreateClient();
        // Use a unique email — the test class fixture is shared, so seeded admin
        // could be locked by prior tests. We register a fresh user via admin auth.
        var adminLogin = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@avdp.sl", password = "admin123" });
        var admin = await adminLogin.Content.ReadFromJsonAsync<LoginResp>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", admin!.Token);
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Test Lockout",
            email = "lockout@avdp.sl",
            password = "correct123",
            role = 1,
            department = "QA",
            phone = (string?)null
        });
        client.DefaultRequestHeaders.Authorization = null;

        HttpResponseMessage? last = null;
        for (int i = 0; i < 5; i++)
            last = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "lockout@avdp.sl", password = "bad" });
        Assert.Equal(HttpStatusCode.Unauthorized, last!.StatusCode);

        // 6th attempt — even with correct password — should be locked
        var locked = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "lockout@avdp.sl", password = "correct123" });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
    }
}
