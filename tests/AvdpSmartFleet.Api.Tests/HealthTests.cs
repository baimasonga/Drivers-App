using Xunit;

namespace AvdpSmartFleet.Api.Tests;

public class HealthTests : IClassFixture<TestWebApp>
{
    private readonly TestWebApp _app;
    public HealthTests(TestWebApp app) => _app = app;

    [Fact]
    public async Task Healthz_returns_200()
    {
        var client = _app.CreateClient();
        var resp = await client.GetAsync("/healthz");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }
}
