namespace Sitim.Core.Entities
{
    /// <summary>
    /// Represents a medical institution (hospital, clinic) that acts as a tenant in the platform.
    /// All business data (patients, studies, analyses) is scoped to an institution.
    /// </summary>
    public sealed class Institution
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Display name, e.g. "Spitalul Clinic Județean Cluj".</summary>
        public string Name { get; set; } = default!;

        /// <summary>URL-safe unique identifier, e.g. "scj-cluj". Used in logging and API paths.</summary>
        public string Slug { get; set; } = default!;

        /// <summary>
        /// Label applied to Orthanc studies belonging to this institution (Orthanc Labels API).
        /// Must be unique across institutions.
        /// </summary>
        public string OrthancLabel { get; set; } = default!;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
