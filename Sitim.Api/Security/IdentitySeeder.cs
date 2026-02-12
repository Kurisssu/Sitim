using Microsoft.AspNetCore.Identity;
using Sitim.Infrastructure.Identity;
using System.Data;

namespace Sitim.Api.Security
{
    /// <summary>
    /// Seeds roles and a dev admin user (idempotent).
    /// In production, you would provision users differently.
    /// </summary>
    public static class IdentitySeeder
    {
        public static async Task SeedAsync(IServiceProvider services, IConfiguration config, CancellationToken ct)
        {
            using var scope = services.CreateScope();
            var sp = scope.ServiceProvider;

            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

            // Roles
            foreach (var role in new[] { SitimRoles.Admin, SitimRoles.Doctor, SitimRoles.Technician })
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    var res = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                    if (!res.Succeeded)
                    {
                        throw new InvalidOperationException("Failed to create role " + role + ": " + string.Join("; ", res.Errors.Select(e => e.Description)));
                    }
                }

                // Dev Admin user from config (appsettings.Development.json)
                var adminEmail = config["Seed:AdminEmail"];
                var adminPassword = config["Seed:AdminPassword"];

                if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
                {
                    logger.LogInformation("Seed admin user skipped (Seed:AdminEmail/Seed:AdminPassword not configured).");
                    return;
                }

                var user = await userManager.FindByEmailAsync(adminEmail);
                if (user is null)
                {
                    user = new ApplicationUser
                    {
                        Id = Guid.NewGuid(),
                        UserName = adminEmail,
                        Email = adminEmail,
                        EmailConfirmed = true
                    };

                    var create = await userManager.CreateAsync(user, adminPassword);
                    if (!create.Succeeded)
                        throw new InvalidOperationException("Failed to create adin user: " + string.Join(";", create.Errors.Select(e => e.Description)));
                }

                if (!await userManager.IsInRoleAsync(user, SitimRoles.Admin))
                {
                    var add = await userManager.AddToRoleAsync(user, SitimRoles.Admin);
                    if (!add.Succeeded)
                        throw new InvalidOperationException("Failed to add admin role: " + string.Join("; ", add.Errors.Select(e => e.Description)));
                }

                logger.LogInformation("Identity seed completed (admin={AdminEmail}).", adminEmail);
            }
        }
    }
}
