namespace Sitim.Web.Services;

/// <summary>
/// Circuit-scoped in-memory store for JWT token and user info.
/// Populated from cookie on circuit start, or from login flow.
/// </summary>
public sealed class AuthTokenStore
{
    public string? Token { get; set; }
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
    public List<string> Roles { get; set; } = [];
    public Guid? InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);
    public bool IsSuperAdmin => Roles.Contains("SuperAdmin");

    public void Clear()
    {
        Token = null;
        UserId = null;
        Email = null;
        Roles = [];
        InstitutionId = null;
        InstitutionName = null;
    }
}
