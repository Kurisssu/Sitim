using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sitim.Api.Options;
using Sitim.Core.Models;
using Sitim.Core.Services;
using Sitim.Api.Security;
using Microsoft.AspNetCore.Authorization;

namespace Sitim.Api.Controllers
{
    [Authorize(Roles = SitimRoles.AnyStaff)]
    [ApiController]
    [Route("api/studies")]
    public sealed class StudiesController : ControllerBase
    {
        private readonly IOrthancClient _orthanc;
        private readonly OhifOptions _ohif;

        public StudiesController(IOrthancClient orthanc, IOptions<OhifOptions> ohif)
        {
            _orthanc = orthanc;
            _ohif = ohif.Value;
        }

        /// <summary>
        /// MVP: returns a list of studies from Orthanc.
        /// For small thesis datasets, we also fetch minimal tags (1 call per study).
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<StudySummary>>> GetStudies(CancellationToken ct)
        {
            var ids = await _orthanc.GetStudyIdsAsync(ct);

            // Fetch minimal details with limited parallelism (avoid hammering Orthanc)
            var results = new List<StudySummary>(ids.Count);
            var gate = new SemaphoreSlim(5);

            var tasks = ids.Select(async id =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var d = await _orthanc.GetStudyAsync(id, ct);
                    if (d is null) return null; // Skip missing studies

                    return new StudySummary(
                        OrthancStudyId: d.OrthancStudyId,
                        StudyInstanceUid: d.StudyInstanceUid,
                        PatientId: d.PatientId,
                        PatientName: d.PatientName,
                        StudyDate: d.StudyDate,
                        ModalitiesInStudy: d.ModalitiesInStudy
                    );
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            var summaries = await Task.WhenAll(tasks);
            results.AddRange(summaries.Where(s => s is not null)!);

            // Optional: sort by date descending when available
            results.Sort((a, b) => string.Compare(b.StudyDate, a.StudyDate, StringComparison.Ordinal));

            return Ok(results);
        }

        [HttpGet("{orthancStudyId}")]
        public async Task<ActionResult<StudyDetails>> GetStudy(string orthancStudyId, CancellationToken ct)
        {
            var d = await _orthanc.GetStudyAsync(orthancStudyId, ct);
            if (d is null) return NotFound("Study not found in Orthanc.");

            return Ok(new StudyDetails(
                OrthancStudyId: d.OrthancStudyId,
                StudyInstanceUid: d.StudyInstanceUid,
                PatientId: d.PatientId,
                PatientName: d.PatientName,
                StudyDate: d.StudyDate,
                ModalitiesInStudy: d.ModalitiesInStudy,
                SeriesOrthancIds: d.SeriesOrthancIds
            ));
        }

        [HttpGet("{orthancStudyId}/viewer-link")]
        public async Task<ActionResult<object>> GetViewerLink(string orthancStudyId, CancellationToken ct)
        {
            var d = await _orthanc.GetStudyAsync(orthancStudyId, ct);
            if (d is null) return NotFound("Study not found in Orthanc.");

            if (string.IsNullOrWhiteSpace(d.StudyInstanceUid))
                return Problem(statusCode: 500, title: "StudyInstanceUID is missing in Orthanc response.");

            var baseUrl = (_ohif.BaseUrl ?? "").TrimEnd('/');
            var uid = Uri.EscapeDataString(d.StudyInstanceUid);

            // OHIF supports deep-link via query parameter StudyInstanceUIDs
            var url = $"{baseUrl}/viewer?StudyInstanceUIDs={uid}";

            return Ok(new { url });
        }
    }
}
