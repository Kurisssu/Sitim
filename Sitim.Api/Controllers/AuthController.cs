using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sitim.Api.Models;
using Sitim.Api.Security;
using Sitim.Infrastructure.Identity;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Sitim.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly ITokenService _tokens;

        public AuthController(UserManager<ApplicationUser> users, ITokenService tokens)
        {
            _users = users;
            _tokens = tokens;
        }

        /// <summary>
        /// Login with email+password and receive a JWT access token.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] Models.LoginRequest req)
        {
            var user = await _users.FindByEmailAsync(req.Email);
            if (user is null)
                return Unauthorized();

            var ok = await _users.CheckPasswordAsync(user, req.Password);
            if (!ok)
                return Unauthorized();

            var roles = await _users.GetRolesAsync(user);
            var (token, expiresIn) = _tokens.CreateAccessToken(user, (IReadOnlyList<string>)roles);

            return Ok(new AuthResponse(token, expiresIn));
        }

        /// <summary>
        /// Returns the current user's identity and roles.
        /// </summary>
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<MeResponse>> Me()
        {
            var idStr =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue(JwtRegisteredClaimNames.Sub);

            if (!Guid.TryParse(idStr, out var userId))
                return Unauthorized();

            var user = await _users.FindByIdAsync(userId.ToString());
            if (user is null)
                return Unauthorized();

            var roles = await _users.GetRolesAsync(user);
            return Ok(new MeResponse(userId, user.Email ?? "", (IReadOnlyList<string>)roles));
        }

    }
}
