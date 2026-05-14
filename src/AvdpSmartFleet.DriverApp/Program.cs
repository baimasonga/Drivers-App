using AvdpSmartFleet.DriverApp;
using AvdpSmartFleet.DriverApp.Services;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddSingleton(new AppConfig { ApiBaseUrl = apiBase });
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBase) });

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthState>());
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<GeolocationService>();
builder.Services.AddScoped<OfflineQueue>();

await builder.Build().RunAsync();
