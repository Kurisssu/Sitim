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
    }
}
