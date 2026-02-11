using Hangfire;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.HangfireJobs;
using Sitim.Core.Entities;
using Sitim.Infrastructure.Data;

namespace Sitim.Api.Services
{
    public class AnalysisJobScheduler : IAnalysisJobScheduler
    {
        private readonly IBackgroundJobClient _jobs;
        private readonly AppDbContext _db;

        public AnalysisJobScheduler(IBackgroundJobClient jobs, AppDbContext db)
        {
            _jobs = jobs;
            _db = db;
        }

        public async Task<string> EnqueueAsync(Guid analysisJobId, CancellationToken ct)
        {
            var job = await _db.AnalysisJobs.FirstOrDefaultAsync(x => x.Id == analysisJobId, ct);
            if (job is null)
                throw new InvalidOperationException($"Analysis job not found: {analysisJobId}");

            // Always enqueue a fresh Hangfire job id (you can improve this later with deduplication).
            var hangfireJobId = _jobs.Enqueue<AnalysisHangfireJob>(x => x.RunAsync(analysisJobId));

            job.HangfireJobId = hangfireJobId;
            job.Status = AnalysisStatus.Queued;
            job.ErrorMessage = null;
            job.StartedAtUtc = null;
            job.FinishedAtUtc = null;

            await _db.SaveChangesAsync(ct);

            return hangfireJobId;
        }
    }
}
