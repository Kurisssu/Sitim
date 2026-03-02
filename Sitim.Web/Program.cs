using Microsoft.AspNetCore.Components.Authorization;
using Radzen;
using Sitim.Web.Components;
using Sitim.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Razor + Blazor Server
builder.Services.AddRazorComponents()
      .AddInteractiveServerComponents()
      .AddHubOptions(options => options.MaximumReceiveMessageSize = 10 * 1024 * 1024);

builder.Services.AddControllers();

// Radzen
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "Sitim.WebTheme";
    options.Duration = TimeSpan.FromDays(365);
});

// ── Auth services ────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthTokenStore>();
builder.Services.AddScoped<SitimAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<SitimAuthStateProvider>());

// ASP.NET Core authorization middleware needs a registered scheme
// even though Blazor uses AuthenticationStateProvider for auth state.
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "BlazorServer";
    options.DefaultChallengeScheme = "BlazorServer";
})
.AddCookie("BlazorServer", options =>
{
    options.LoginPath = "/login";
});
builder.Services.AddAuthorizationCore();

// ── HTTP client for API calls ────────────────────────────
var apiBaseUrl = builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7006";

builder.Services.AddHttpClient<SitimApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(120);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Dev: accept self-signed certs from API
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

var app = builder.Build();

var forwardingOptions = new ForwardedHeadersOptions()
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
};
forwardingOptions.KnownIPNetworks.Clear();
forwardingOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardingOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseAntiforgery();
app.MapControllers();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();