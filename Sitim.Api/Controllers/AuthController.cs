using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.Models;
using Sitim.Api.Security;
using Sitim.Infrastructure.Data;
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
        private readonly AppDbContext _db;

        public AuthController(UserManager<ApplicationUser> users, ITokenService tokens, AppDbContext db)
        {
            _users = users;
            _tokens = tokens;
            _db = db;
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

            // Inactive users cannot log in
            if (!user.IsActive)
                return Unauthorized();

            var ok = await _users.CheckPasswordAsync(user, req.Password);
            if (!ok)
                return Unauthorized();

            var roles = await _users.GetRolesAsync(user);

            string? institutionSlug = null;
            if (user.InstitutionId.HasValue)
            {
                var inst = await _db.Institutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == user.InstitutionId.Value);
                institutionSlug = inst?.Slug;
            }

            var (token, expiresIn) = _tokens.CreateAccessToken(
                user,
                (IReadOnlyList<string>)roles,
                user.InstitutionId,
                institutionSlug);

            return Ok(new AuthResponse(token, expiresIn));
        }

        /// <summary>
        /// Returns the current user's identity, roles, and institution.
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

            string? institutionName = null;
            if (user.InstitutionId.HasValue)
            {
                var inst = await _db.Institutions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == user.InstitutionId.Value);
                institutionName = inst?.Name;
            }

            return Ok(new MeResponse(
                userId,
                user.Email ?? "",
                (IReadOnlyList<string>)roles,
                user.InstitutionId,
                institutionName));
        }

        /// <summary>
        /// Sets a new password using the password-reset token from the invite link.
        /// Activates the user account on success.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("set-password")]
        public async Task<IActionResult> SetPassword([FromBody] Models.SetPasswordRequest req)
        {
            var user = await _users.FindByIdAsync(req.UserId.ToString());
            if (user is null)
                return BadRequest("Invalid request.");

            var result = await _users.ResetPasswordAsync(user, req.Token, req.NewPassword);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            // Activate the account after password is set
            user.IsActive = true;
            user.EmailConfirmed = true;
            await _users.UpdateAsync(user);

            return Ok();
        }
    }
}
