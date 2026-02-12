namespace Sitim.Api.Security
{
    public static class SitimRoles
    {
        public const string Admin = "Admin";
        public const string Doctor = "Doctor";
        public const string Technician = "Technician";

        // Common role bundles for [Authorize(Roles="...")]
        public const string AnyStaff = Admin + "," + Doctor + "," + Technician;
        public const string CanImport = Admin + "," + Technician;
        public const string CanAnalyze = Admin + "," + Doctor + "," + Technician;
    }
}
