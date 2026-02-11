using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sitim.Core.Entities;
using Sitim.Core.Options;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;

namespace Sitim.Infrastructure.Services
{
    /// <summary>
    /// Placeholder analysis runner:
    /// - downloads study ZIP from Orthanc
    /// - writes a result.json
    ///
    /// Later need to replace the "result generation" with a call to the AI microservice.
    /// </summary>
    public sealed class AnalysisService : IAnalysisService
    {
        private readonly AppDbContext _db;
        private readonly IOrthancClient _orthanc;
        private readonly StorageOptions _storage;

        public AnalysisService(AppDbContext db, IOrthancClient orthanc, IOptions<StorageOptions> storage)
        {
            _db = db;
            _orthanc = orthanc;
            _storage = storage.Value;
        }

        public async Task RunAsync(Guid analysisJobId, CancellationToken ct)
        {
            var job = await _db.AnalysisJobs.FirstOrDefaultAsync(x => x.Id == analysisJobId, ct);
            if (job is null)
                return;

            // Set Running
            job.Status = AnalysisStatus.Running;
            job.StartedAtUtc = DateTime.UtcNow;
            job.FinishedAtUtc = null;
            job.ErrorMessage = null;
            await _db.SaveChangesAsync(ct);

            var jobDir = Path.Combine(_storage.BasePath, "analyses", job.Id.ToString("N"));
            Directory.CreateDirectory(jobDir);

            var archivePath = Path.Combine(jobDir, "study.zip");
            var resultPath = Path.Combine(jobDir, "result.json");

            try
            {
                // Download archive if missing
                if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
                {
                    await using var fs = File.Create(archivePath);
                    await _orthanc.DownloadStudyArchiveAsync(job.OrthancStudyId, fs, ct);
                }

                // Placeholder "analysis" output
                var output = new
                {
                    jobId = job.Id,
                    orthancStudyId = job.OrthancStudyId,
                    modelKey = job.ModelKey,
                    createdAtUtc = job.CreatedAtUtc,
                    startedAtUtc = job.StartedAtUtc,
                    finishedAtUtc = DateTime.UtcNow,
                    findings = Array.Empty<object>()
                };

                var json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(resultPath, json, ct);

                job.StudyArchivePath = archivePath;
                job.ResultJsonPath = resultPath;
                job.Status = AnalysisStatus.Succeeded;
                job.FinishedAtUtc = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                job.Status = AnalysisStatus.Failed;
                job.ErrorMessage = ex.Message;
                job.FinishedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                // Re-throw so Hangfire can retry (AutomaticRetry).
                throw;
            }
        }
    }
}
