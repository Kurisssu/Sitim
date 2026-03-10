using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sitim.Infrastructure.Services;

namespace Sitim.Infrastructure.Data
{
    /// <summary>
    /// Used by EF Core CLI tools (migrations, scaffolding) at design-time.
    /// Provides a hardcoded dev connection string and a NullTenantContext.
    /// </summary>
    public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseNpgsql(
                "Host=localhost;Port=5432;Database=sitim;Username=sitim;Password=sitim_dev_password");

            return new AppDbContext(optionsBuilder.Options, NullTenantContext.Instance);
        }
    }
}
