using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace AvdpSmartFleet.DriverApp.Services;

public class AuthState : AuthenticationStateProvider
{
    private const string TokenKey = "auth_token";
    private const string NameKey = "auth_name";
    private const string RoleKey = "auth_role";
    private readonly ILocalStorageService _storage;
    private readonly HttpClient _http;

    public string? Token { get; private set; }
    public string? UserName { get; private set; }
    public string? Role { get; private set; }

    public AuthState(ILocalStorageService storage, HttpClient http)
    {
        _storage = storage;
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        Token ??= await _storage.GetItemAsStringAsync(TokenKey);
        UserName ??= await _storage.GetItemAsStringAsync(NameKey);
        Role ??= await _storage.GetItemAsStringAsync(RoleKey);

        if (string.IsNullOrEmpty(Token))
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        _http.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, UserName ?? ""),
            new(ClaimTypes.Role, Role ?? "")
        };
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt")));
    }

    public async Task SignInAsync(string token, string name, string role)
    {
        Token = token; UserName = name; Role = role;
        await _storage.SetItemAsStringAsync(TokenKey, token);
        await _storage.SetItemAsStringAsync(NameKey, name);
        await _storage.SetItemAsStringAsync(RoleKey, role);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SignOutAsync()
    {
        Token = UserName = Role = null;
        await _storage.RemoveItemAsync(TokenKey);
        await _storage.RemoveItemAsync(NameKey);
        await _storage.RemoveItemAsync(RoleKey);
        _http.DefaultRequestHeaders.Authorization = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
