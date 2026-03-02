using Sitim.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sitim.Core.Services
{
    /// <summary>
    /// Keeps a minimal local cache (PostgreSQL) of studies that exist in Orthanc.
    /// This is useful for:
    /// - faster UI lists (no "N calls" to Orthanc each time),
    /// - being able to store our own metadata later (analysis jobs, reports, etc.).
    /// </summary>
    public interface IStudyCacheService
    {
        /// <summary>
        /// Reads from our local DB (does NOT contact Orthanc).
        /// </summary>
        Task<IReadOnlyList<StudySummary>> ListLocalAsync(CancellationToken ct);
        /// <summary>
        /// Reads a single study from our local DB (does NOT contact Orthanc).
        /// </summary>
        Task<StudyDetails?> GetLocalAsync(string orthancStudyId, CancellationToken ct);
        /// <summary>
        /// Fetches details from Orthanc and upserts them into DB, then returns the DB view.
        /// </summary>
        Task<StudyDetails?> SyncFromOrthancAsync(string orthancStudyId, CancellationToken ct);
        /// <summary>
        /// Sync all studies currently present in Orthanc(MVP).
        /// Returns how many studies were synced.
        /// </summary>
        Task<int> SyncAllFromOrthancAsync(CancellationToken ct);
    }
}
