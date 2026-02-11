using System;
using System.Collections.Generic;
using System.Text;

namespace Sitim.Core.Entities
{
    /// <summary>
    /// Minimal patient record stored in our DB.
    /// We keep it intentionally simple for MVP (license thesis scope).
    /// </summary>
    public sealed class Patient
    {
        public Guid Id { get; set; }

        /// <summary>
        /// DICOM PatientID (0010,0020). Can be missing in some test datasets.
        /// </summary>
        public string? PatientId { get; set; }

        /// <summary>
        /// DICOM PatientName (0010,0010).
        /// </summary>
        public string? PatientName { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<ImagingStudy> Studies { get; set; } = new();
    }
}
