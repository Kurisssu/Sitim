using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sitim.Core.Entities;
using Sitim.Infrastructure.Data;
using Sitim.Infrastructure.Identity;

namespace Sitim.Api.Security
{
    /// <summary>
    /// Seeds roles, a platform-level SuperAdmin, a demo institution, and a demo Admin user.
    /// All operations are idempotent – safe to run on every startup.
    /// </summary>
    public static class IdentitySeeder
    {
        // Well-known GUID for the demo institution (stable across restarts) for DEV only.
        public static readonly Guid DemoInstitutionId = new("00000000-0000-0000-0000-000000000001");

        public static async Task SeedAsync(IServiceProvider services, IConfiguration config, CancellationToken ct)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;

            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var db = sp.GetRequiredService<AppDbContext>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

            // 1. Seed all roles
            foreach (var role in new[]
            {
                SitimRoles.SuperAdmin,
                SitimRoles.Admin,
                SitimRoles.Doctor,
                SitimRoles.Technician
            })
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var res = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                    if (!res.Succeeded)
                        throw new InvalidOperationException($"Failed to create role {role}: " +
                            string.Join("; ", res.Errors.Select(e => e.Description)));
                }
            }

            // 2. Seed demo institution
            var demoInstitution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == DemoInstitutionId, ct);
            if (demoInstitution is null)
            {
                demoInstitution = new Institution
                {
                    Id = DemoInstitutionId,
                    Name = "Clinica Demo",
                    Slug = "clinica-demo",
                    OrthancLabel = "clinica-demo",
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Institutions.Add(demoInstitution);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seeded demo institution '{Name}'.", demoInstitution.Name);
            }

            // 3. Seed platform SuperAdmin (no institution)
            var superAdminEmail = config["Seed:SuperAdminEmail"];
            var superAdminPassword = config["Seed:SuperAdminPassword"];
            if (!string.IsNullOrWhiteSpace(superAdminEmail) && !string.IsNullOrWhiteSpace(superAdminPassword))
            {
                await EnsureUser(userManager, logger, superAdminEmail, superAdminPassword,
                    institutionId: null, role: SitimRoles.SuperAdmin, ct);
            }

            // 4. Seed demo institution Admin (from legacy Seed:AdminEmail config)
            var adminEmail = config["Seed:AdminEmail"];
            var adminPassword = config["Seed:AdminPassword"];
            if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminPassword))
            {
                await EnsureUser(userManager, logger, adminEmail, adminPassword,
                    institutionId: DemoInstitutionId, role: SitimRoles.Admin, ct);
            }
        }

        private static async Task EnsureUser(
            UserManager<ApplicationUser> userManager,
            ILogger logger,
            string email,
            string password,
            Guid? institutionId,
            string role,
            CancellationToken ct)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    InstitutionId = institutionId,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                };

                var create = await userManager.CreateAsync(user, password);
                if (!create.Succeeded)
                    throw new InvalidOperationException($"Failed to create user '{email}': " +
                        string.Join("; ", create.Errors.Select(e => e.Description)));

                logger.LogInformation("Seeded user {Email} with role {Role}.", email, role);
            }
            else if (user.InstitutionId != institutionId)
            {
                // Update institution link if it changed (e.g. first run migration)
                user.InstitutionId = institutionId;
                await userManager.UpdateAsync(user);
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                var add = await userManager.AddToRoleAsync(user, role);
                if (!add.Succeeded)
                    throw new InvalidOperationException($"Failed to add role {role} to user '{email}': " +
                        string.Join("; ", add.Errors.Select(e => e.Description)));
            }
        }
    }
}
