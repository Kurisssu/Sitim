using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.Services;
using Sitim.Core.Entities;
using Sitim.Core.Models;
using Sitim.Infrastructure.Data;
using Sitim.Api.Security;

namespace Sitim.Api.Controllers
{
    [Authorize(Roles = SitimRoles.CanAnalyze)]
    [ApiController]
    [Route("api/[controller]")]
    public sealed class AnalysesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IAnalysisJobScheduler _scheduler;

        public AnalysesController(AppDbContext db, IAnalysisJobScheduler scheduler)
        {
            _db = db;
            _scheduler = scheduler;
        }

        /// <summary>
        /// Creates an analysis job and enqueues it for background execution.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<AnalysisJobDto>> Create([FromBody] CreateAnalysisRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.OrthancStudyId))
                return BadRequest("OrthancStudyId is required.");

            var modelKey = string.IsNullOrWhiteSpace(req.ModelKey) ? "demo" : req.ModelKey.Trim();

            var job = new AnalysisJob
            {
                Id = Guid.NewGuid(),
                OrthancStudyId = req.OrthancStudyId.Trim(),
                ModelKey = modelKey,
                Status = AnalysisStatus.Queued,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.AnalysisJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            var hangfireId = await _scheduler.EnqueueAsync(job.Id, ct);

            return CreatedAtAction(nameof(GetById), new { id = job.Id }, ToDto(job, hangfireId));
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<AnalysisJobDto>> GetById(Guid id, CancellationToken ct)
        {
            var job = await _db.AnalysisJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (job is null)
                return NotFound();

            return Ok(ToDto(job, job.HangfireJobId));
        }

        [HttpGet("by-study/{orthancStudyId}")]
        public async Task<ActionResult<IReadOnlyList<AnalysisJobDto>>> ListByStudy(string orthancStudyId, CancellationToken ct)
        {
            var jobs = await _db.AnalysisJobs
                .AsNoTracking()
                .Where(x => x.OrthancStudyId == orthancStudyId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToListAsync(ct);

            return Ok(jobs.Select(j => ToDto(j, j.HangfireJobId)).ToList());
        }

        /// <summary>
        /// Re-enqueues an existing job (useful after Failures).
        /// </summary>
        [HttpPost("{id:guid}/enqueue")]
        public async Task<ActionResult> Enqueue(Guid id, CancellationToken ct)
        {
            await _scheduler.EnqueueAsync(id, ct);
            return Accepted();
        }

        /// <summary>
        /// Enqueue all unfinished jobs (Queued or Failed). Useful after a restart.
        /// </summary>
        [HttpPost("enqueue-unfinished")]
        public async Task<ActionResult<int>> EnqueueUnfinished(CancellationToken ct)
        {
            var ids = await _db.AnalysisJobs
                .Where(x => x.Status == AnalysisStatus.Queued || x.Status == AnalysisStatus.Failed)
                .Select(x => x.Id)
                .ToListAsync(ct);

            foreach (var id in ids)
                await _scheduler.EnqueueAsync(id, ct);

            return Ok(ids.Count);
        }

        /// <summary>
        /// Returns the result.json for a completed job.
        /// </summary>
        [HttpGet("{id:guid}/result")]
        public async Task<IActionResult> GetResult(Guid id, CancellationToken ct)
        {
            var job = await _db.AnalysisJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (job is null)
                return NotFound();

            if (job.Status != AnalysisStatus.Succeeded || string.IsNullOrWhiteSpace(job.ResultJsonPath))
                return BadRequest("Result not available yet.");

            if (!System.IO.File.Exists(job.ResultJsonPath))
                return NotFound("Result file not found on disk.");

            var json = await System.IO.File.ReadAllTextAsync(job.ResultJsonPath, ct);
            return Content(json, "application/json");
        }

        private AnalysisJobDto ToDto(AnalysisJob job, string? hangfireJobId)
        {
            var resultUrl = job.Status == AnalysisStatus.Succeeded
                ? Url.Action(nameof(GetResult), values: new { id = job.Id })
                : null;

            return new AnalysisJobDto(
                job.Id,
                job.OrthancStudyId,
                job.ModelKey,
                job.Status.ToString(),
                hangfireJobId,
                job.CreatedAtUtc,
                job.StartedAtUtc,
                job.FinishedAtUtc,
                job.ErrorMessage,
                resultUrl
            );
        }
    }
}
