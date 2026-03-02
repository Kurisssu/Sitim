using Microsoft.AspNetCore.Mvc;

namespace Sitim.Web.Controllers;

/// <summary>
/// Manages the HttpOnly auth cookie that persists the JWT across page refreshes.
/// Lives in Sitim.Web (not the API) — sets/clears the cookie in the browser.
/// </summary>
[ApiController]
[Route("auth/cookie")]
[IgnoreAntiforgeryToken]
public sealed class AuthCookieController : ControllerBase
{
    public sealed record SetCookieRequest(string Token, int ExpiresInSeconds);

    /// <summary>
    /// Stores the JWT in an HttpOnly, Secure cookie.
    /// </summary>
    [HttpPost("set")]
    public IActionResult Set([FromBody] SetCookieRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest();

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromSeconds(request.ExpiresInSeconds > 0
                ? request.ExpiresInSeconds
                : 3600) // fallback 1h
        };

        Response.Cookies.Append("sitim_auth", request.Token, cookieOptions);
        return Ok();
    }

    /// <summary>
    /// Deletes the auth cookie.
    /// </summary>
    [HttpPost("clear")]
    public IActionResult Clear()
    {
        Response.Cookies.Delete("sitim_auth", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return Ok();
    }
}
