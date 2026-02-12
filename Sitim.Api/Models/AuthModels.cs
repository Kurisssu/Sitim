namespace Sitim.Api.Models
{
    public sealed record LoginRequest(string Email, string Password);

    public sealed record AuthResponse(string AccessToken, int ExpiresInSeconds);

    public sealed record MeResponse(Guid UserId, string Email, IReadOnlyList<string> Roles);
}
