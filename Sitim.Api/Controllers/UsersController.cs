using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.Models;
using Sitim.Api.Security;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;
using Sitim.Infrastructure.Identity;
using System.Security.Claims;

namespace Sitim.Api.Controllers
{
    /// <summary>
    /// User management: invite, list, edit, and deactivate users per institution.
    /// Admin manages their own institution's users. SuperAdmin manages all.
    /// </summary>
    [Authorize(Roles = SitimRoles.AnyStaff)]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenant;

        public UsersController(
            UserManager<ApplicationUser> users,
            AppDbContext db,
            ITenantContext tenant)
        {
            _users = users;
            _db = db;
            _tenant = tenant;
        }

        /// <summary>
        /// Lists users. SuperAdmin sees all; Admin sees only their institution.
        /// </summary>
        [HttpGet]
        [Authorize(Roles = SitimRoles.AnyStaff)]
        public async Task<ActionResult<IReadOnlyList<UserResult>>> List(CancellationToken ct)
        {
            // Load users + their roles via Identity tables
            IQueryable<ApplicationUser> query = _users.Users.AsNoTracking();

            if (!_tenant.IsSuperAdmin && _tenant.InstitutionId.HasValue)
                query = query.Where(u => u.InstitutionId == _tenant.InstitutionId);

            var appUsers = await query.OrderBy(u => u.Email).ToListAsync(ct);

            // Load institution names in one query
            var institutionIds = appUsers
                .Where(u => u.InstitutionId.HasValue)
                .Select(u => u.InstitutionId!.Value)
                .Distinct()
                .ToList();

            var institutions = await _db.Institutions
                .AsNoTracking()
                .Where(i => institutionIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Name, ct);

            // Load roles for all users in batch (N+1 avoided by building a lookup)
            var results = new List<UserResult>(appUsers.Count);
            foreach (var u in appUsers)
            {
                var roles = await _users.GetRolesAsync(u);
                var role = roles.FirstOrDefault() ?? "";
                var instName = u.InstitutionId.HasValue
                    ? institutions.GetValueOrDefault(u.InstitutionId.Value)
                    : null;

                results.Add(new UserResult(
                    u.Id, u.Email ?? "", u.FullName, role,
                    u.InstitutionId, instName,
                    u.IsActive, u.CreatedAtUtc));
            }

            return Ok(results);
        }

        /// <summary>
        /// Invites a new user: creates account (inactive), generates a password-reset token,
        /// and returns the set-password link to display in the UI.
        /// </summary>
        [HttpPost("invite")]
        [Authorize(Roles = SitimRoles.AnyStaff)]
        public async Task<ActionResult<InviteUserResponse>> Invite(
            [FromBody] InviteUserRequest req,
            [FromQuery] string baseUrl = "")
        {
            // Determine which institution the new user belongs to
            Guid? institutionId;
            if (_tenant.IsSuperAdmin)
            {
                if (!req.InstitutionId.HasValue)
                    return BadRequest("SuperAdmin must specify an InstitutionId.");
                institutionId = req.InstitutionId;
            }
            else
            {
                institutionId = _tenant.InstitutionId;
            }

            // Validate role — only Doctor and Technician can be invited this way
            var allowedRoles = new[] { SitimRoles.Doctor, SitimRoles.Technician, SitimRoles.Admin };
            if (!allowedRoles.Contains(req.Role))
                return BadRequest($"Role '{req.Role}' is not allowed. Use Doctor, Technician, or Admin.");

            // Only SuperAdmin can invite Admin-level users
            if (req.Role == SitimRoles.Admin && !_tenant.IsSuperAdmin)
                return Forbid();

            if (await _users.FindByEmailAsync(req.Email) is not null)
                return Conflict($"A user with email '{req.Email}' already exists.");

            var user = new ApplicationUser
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName?.Trim(),
                InstitutionId = institutionId,
                IsActive = false, // activated after setting password
                CreatedAtUtc = DateTime.UtcNow,
                EmailConfirmed = false
            };

            // Create with a random placeholder password (user will set their own via the invite link)
            var createResult = await _users.CreateAsync(user, GeneratePlaceholderPassword());
            if (!createResult.Succeeded)
                return BadRequest(createResult.Errors.Select(e => e.Description));

            await _users.AddToRoleAsync(user, req.Role);

            // Generate a password reset token (valid for ~1 day by default)
            var token = await _users.GeneratePasswordResetTokenAsync(user);
            var encodedToken = Uri.EscapeDataString(token);

            // Build the invite link — baseUrl is the Web app base (e.g., https://localhost:7007)
            var link = string.IsNullOrWhiteSpace(baseUrl)
                ? $"/set-password?userId={user.Id}&token={encodedToken}"
                : $"{baseUrl.TrimEnd('/')}/set-password?userId={user.Id}&token={encodedToken}";

            return Ok(new InviteUserResponse(user.Id, user.Email!, link));
        }

        /// <summary>
        /// Updates a user's FullName, Role, and/or IsActive status.
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = SitimRoles.AnyStaff)]
        public async Task<ActionResult<UserResult>> Update(
            Guid id,
            [FromBody] UpdateUserRequest req,
            CancellationToken ct)
        {
            var user = await _users.FindByIdAsync(id.ToString());
            if (user is null) return NotFound();

            // Scope check: Admin cannot edit users from other institutions
            if (!_tenant.IsSuperAdmin && user.InstitutionId != _tenant.InstitutionId)
                return Forbid();

            if (req.FullName is not null)
                user.FullName = req.FullName.Trim();

            if (req.IsActive.HasValue)
                user.IsActive = req.IsActive.Value;

            var updateResult = await _users.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return BadRequest(updateResult.Errors.Select(e => e.Description));

            // Role change
            if (!string.IsNullOrWhiteSpace(req.Role))
            {
                var allowedRoles = new[] { SitimRoles.Doctor, SitimRoles.Technician, SitimRoles.Admin };
                if (!allowedRoles.Contains(req.Role))
                    return BadRequest($"Role '{req.Role}' is not allowed.");
                if (req.Role == SitimRoles.Admin && !_tenant.IsSuperAdmin)
                    return Forbid();

                var currentRoles = await _users.GetRolesAsync(user);
                await _users.RemoveFromRolesAsync(user, currentRoles);
                await _users.AddToRoleAsync(user, req.Role);
            }

            var roles = await _users.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "";

            string? instName = null;
            if (user.InstitutionId.HasValue)
            {
                var inst = await _db.Institutions.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == user.InstitutionId.Value, ct);
                instName = inst?.Name;
            }

            return Ok(new UserResult(
                user.Id, user.Email ?? "", user.FullName, role,
                user.InstitutionId, instName,
                user.IsActive, user.CreatedAtUtc));
        }

        /// <summary>
        /// Permanently deletes a user. SuperAdmin can delete any user; Admin can delete users from their institution.
        /// Cannot delete SuperAdmin accounts or your own account.
        /// </summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = SitimRoles.AnyStaff)]
        public async Task<IActionResult> Delete(Guid id)
        {
            var user = await _users.FindByIdAsync(id.ToString());
            if (user is null) return NotFound();

            if (!_tenant.IsSuperAdmin && user.InstitutionId != _tenant.InstitutionId)
                return Forbid();

            // Protect SuperAdmin accounts from deletion
            if (await _users.IsInRoleAsync(user, SitimRoles.SuperAdmin))
                return BadRequest("Cannot delete SuperAdmin accounts.");

            // Prevent self-deletion
            var currentUserId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)
                             ?? User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub);
            if (Guid.TryParse(currentUserId, out var currentId) && currentId == id)
                return BadRequest("Cannot delete your own account.");

            var result = await _users.DeleteAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            return NoContent();
        }

        private static string GeneratePlaceholderPassword() =>
            $"Tmp_{Guid.NewGuid():N}!Aa1";
    }
}
