using System.Text;
using System.Threading.RateLimiting;
using AvdpSmartFleet.Api.Data;
using AvdpSmartFleet.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "AvdpSmartFleet")
    .WriteTo.Console()
    .WriteTo.File("logs/avdp-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var conn = builder.Configuration.GetConnectionString("Default") ?? "Data Source=avdp_smartfleet.db";
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(conn));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<JwtTokenService>();
// Switch between real and mock GPS-Trace client via config.
// Set "GpsTrace:UseReal": true and provide Token to use the live API.
if (builder.Configuration.GetValue<bool>("GpsTrace:UseReal"))
{
    builder.Services.AddHttpClient<IGpsTraceClient, GpsTraceHttpClient>();
}
else
{
    builder.Services.AddSingleton<IGpsTraceClient, MockGpsTraceClient>();
}
builder.Services.AddScoped<ReconciliationService>();
builder.Services.AddHostedService<GpsPollingService>();

builder.Services.AddControllers();
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AVDP SmartFleet API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        [new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Reference = new Microsoft.OpenApi.Models.OpenApiReference
            {
                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = new List<string>()
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Resolve config lazily so test overrides applied after Program.cs runs are honored.
        var jwtKey = builder.Configuration["Jwt:Key"] ?? "dev-secret-change-me-please-32-chars-long-xxxx";
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

// CORS — wide open in Development, restricted via config in Production
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (builder.Environment.IsProduction() && allowedOrigins.Length > 0)
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    else
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

// Rate limiting: stricter on auth, generous on the rest
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    o.AddPolicy("api", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 10
        }));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseRateLimiter();
app.UseStaticFiles();

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapRazorPages();
app.MapHealthChecks("/healthz");
app.MapGet("/", () => Results.Redirect("/admin"));

// Public visibility for the Program type so the integration test project's WebApplicationFactory can find it
app.Run();

public partial class Program { }
