using Microsoft.AspNetCore.Identity;

namespace Sitim.Infrastructure.Identity
{
    /// <summary>
    /// Application user (Identity) using Guid as primary key.
    /// </summary>
    public sealed class ApplicationUser : IdentityUser<Guid>
    {
        // Extend later: FullName, Department, etc.
    }
}
