using System.Net.Http.Headers;

namespace Sitim.Web.Services;

/// <summary>
/// DelegatingHandler that attaches the JWT Bearer token to every outgoing HTTP request.
/// Token is read from <see cref="AuthTokenStore"/>.
/// </summary>
public sealed class AuthTokenHandler : DelegatingHandler
{
    private readonly AuthTokenStore _store;

    public AuthTokenHandler(AuthTokenStore store)
    {
        _store = store;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _store.Token;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
