using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Sitim.Web.Services;

/// <summary>
/// Blazor Server AuthenticationStateProvider with cookie-based persistence.
///
/// Flow:
///   1. On first GetAuthenticationStateAsync (new circuit), check AuthTokenStore.
///   2. If empty, try to restore JWT from the "sitim_auth" HttpOnly cookie via IHttpContextAccessor.
///   3. Parse JWT claims (email, roles, exp) — no extra API call needed.
///   4. On login: save token in memory + call sitimAuth.setCookie via JS interop.
///   5. On logout: clear memory + call sitimAuth.clearCookie via JS interop.
/// </summary>
public sealed class SitimAuthStateProvider : AuthenticationStateProvider
{
    private readonly AuthTokenStore _store;
    private readonly SitimApiClient _api;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IJSRuntime _js;
    private bool _cookieChecked;

    public SitimAuthStateProvider(
        AuthTokenStore store,
        SitimApiClient api,
        IHttpContextAccessor httpContextAccessor,
        IJSRuntime js)
    {
        _store = store;
        _api = api;
        _httpContextAccessor = httpContextAccessor;
        _js = js;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_store.IsAuthenticated)
            return Task.FromResult(BuildAuthState());

        if (!_cookieChecked)
        {
            _cookieChecked = true;
            TryRestoreFromCookie();

            if (_store.IsAuthenticated)
                return Task.FromResult(BuildAuthState());
        }

        return Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private void TryRestoreFromCookie()
    {
        try
        {
            var cookie = _httpContextAccessor.HttpContext?.Request.Cookies["sitim_auth"];
            if (string.IsNullOrWhiteSpace(cookie))
                return;

            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(cookie))
                return;

            var jwt = handler.ReadJwtToken(cookie);

            if (jwt.ValidTo < DateTime.UtcNow)
                return;

            var email = jwt.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.Email ||
                c.Type == JwtRegisteredClaimNames.Email)?.Value;

            var userId = jwt.Claims.FirstOrDefault(c =>
                c.Type == ClaimTypes.NameIdentifier ||
                c.Type == JwtRegisteredClaimNames.Sub)?.Value;

            var roles = jwt.Claims
                .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value)
                .ToList();

            _store.Token = cookie;
            _store.Email = email;
            _store.UserId = Guid.TryParse(userId, out var uid) ? uid : null;
            _store.Roles = roles;
        }
        catch
        {
            // Invalid cookie — ignore
        }
    }

    public async Task<string?> LoginAsync(string email, string password)
    {
        var loginResult = await _api.LoginAsync(email, password);
        if (loginResult is null)
            return "Email sau parolă incorectă.";

        _store.Token = loginResult.AccessToken;

        var me = await _api.GetMeAsync();
        if (me is null)
        {
            _store.Clear();
            return "Nu s-au putut obține datele utilizatorului.";
        }

        _store.UserId = me.UserId;
        _store.Email = me.Email;
        _store.Roles = me.Roles;
        _store.InstitutionId = me.InstitutionId;
        _store.InstitutionName = me.InstitutionName;

        // Persist JWT in browser cookie via JS interop
        await _js.InvokeVoidAsync("sitimAuth.setCookie",
            loginResult.AccessToken, loginResult.ExpiresInSeconds);

        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return null;
    }

    public async Task LogoutAsync()
    {
        _store.Clear();
        await _js.InvokeVoidAsync("sitimAuth.clearCookie");
        // NotifyAuthenticationStateChanged is intentionally omitted here.
        // The caller must navigate with forceLoad: true, which tears down the entire Blazor
        // circuit and starts a fresh one — no token in store, no cookie → user is anonymous.
        // Calling Notify before navigation would cause all mounted components to re-render
        // in an unauthenticated state (triggering API calls that return 401).
    }

    private AuthenticationState BuildAuthState()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _store.UserId?.ToString() ?? ""),
            new(ClaimTypes.Email, _store.Email ?? ""),
            new(ClaimTypes.Name, _store.Email ?? ""),
        };

        foreach (var role in _store.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}
