using AvdpSmartFleet.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AvdpSmartFleet.Api.Tests;

/// <summary>
/// WebApplicationFactory variant that swaps SQLite for an in-memory file per test instance,
/// so each test run starts with a fresh seeded database.
/// </summary>
public class TestWebApp : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"avdp-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((ctx, c) =>
        {
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Jwt:Key"] = "test-secret-key-32-chars-minimum-xxxxxx",
                ["Jwt:Issuer"] = "AvdpSmartFleet",
                ["Jwt:Audience"] = "AvdpSmartFleetClients",
                ["GpsTrace:UseReal"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the registered DbContext so we can use our temp connection
            var d = services.SingleOrDefault(x => x.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (d != null) services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
