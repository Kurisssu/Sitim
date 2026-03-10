using Sitim.Core.Models;

namespace Sitim.Core.Services
{
    public interface IOrthancClient
    {
        Task<IReadOnlyList<string>> GetStudyIdsAsync(CancellationToken ct);
        Task<OrthancStudyDetails?> GetStudyAsync(string orthancStudyId, CancellationToken ct);
        /// <summary>
        /// Upload a single DICOM instance into Orthanc (POST /instances).
        /// </summary>
        Task<OrthancUploadResult> UploadInstanceAsync(Stream dicomStream, CancellationToken ct);

        /// <summary>
        /// Downloads the Orthanc study archive (ZIP) (GET /studies/{id}/archive).
        /// </summary>
        Task DownloadStudyArchiveAsync(string orthancStudyId, Stream destination, CancellationToken ct);

        /// <summary>
        /// Applies a label to an Orthanc study (PUT /studies/{id}/labels/{label}).
        /// Available in Orthanc 1.9.0+. Labels are used to scope studies per institution.
        /// </summary>
        Task SetStudyLabelAsync(string orthancStudyId, string label, CancellationToken ct);

        /// <summary>
        /// Returns all study IDs that have the given label (GET /studies?labels={label}).
        /// Falls back to GetStudyIdsAsync if labels are not supported.
        /// </summary>
        Task<IReadOnlyList<string>> GetStudyIdsByLabelAsync(string label, CancellationToken ct);

        /// <summary>
        /// Permanently deletes a study from Orthanc (DELETE /studies/{id}).
        /// Returns true if deleted, false if not found.
        /// Throws on other errors.
        /// </summary>
        Task<bool> DeleteStudyAsync(string orthancStudyId, CancellationToken ct);
    }
}
