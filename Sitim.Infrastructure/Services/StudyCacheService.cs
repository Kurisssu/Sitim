using Microsoft.EntityFrameworkCore;
using Sitim.Core.Entities;
using Sitim.Core.Models;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Sitim.Infrastructure.Services
{
    /// <summary>
    /// Simple "cache" layer:
    /// - Reads from PostgreSQL for UI lists (fast).
    /// - Can sync (upsert) a study from Orthanc into PostgreSQL.
    /// </summary>
    public sealed class StudyCacheService : IStudyCacheService
    {
        private readonly AppDbContext _db;
        private readonly IOrthancClient _orthanc;
        private readonly IServiceScopeFactory _scopeFactory;

        public StudyCacheService(AppDbContext db, IOrthancClient orthanc, IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _orthanc = orthanc;
            _scopeFactory = scopeFactory;
        }

        public async Task<IReadOnlyList<StudySummary>> ListLocalAsync(CancellationToken ct)
        {
            var studies = await _db.ImagingStudies
                .AsNoTracking()
                .Include(s => s.Patient)
                .OrderByDescending(s => s.StudyDate)
                .ThenByDescending(s => s.UpdatedAtUtc)
                .ToListAsync(ct);

            return studies.Select(ToSummary).ToList();
        }

        public async Task<StudyDetails?> GetLocalAsync(string orthancStudyId, CancellationToken ct)
        {
            var s = await _db.ImagingStudies
                .AsNoTracking()
                .Include(x => x.Patient)
                .Include(x => x.Series)
                .FirstOrDefaultAsync(x => x.OrthancStudyId == orthancStudyId, ct);

            return s is null ? null : ToDetails(s);
        }

        public async Task<StudyDetails?> SyncFromOrthancAsync(string orthancStudyId, CancellationToken ct)
        {
            var d = await _orthanc.GetStudyAsync(orthancStudyId, ct);

            // If study is missing in Orthanc (null), ensure it is removed from local DB
            if (d is null)
            {
                var existing = await _db.ImagingStudies
                    .FirstOrDefaultAsync(x => x.OrthancStudyId == orthancStudyId, ct);
                
                if (existing is not null)
                {
                    _db.ImagingStudies.Remove(existing);
                    await _db.SaveChangesAsync(ct);
                }
                return null;
            }

            // 1) Patient upsert (best-effort)
            Patient? patient = null;
            if (!string.IsNullOrWhiteSpace(d.PatientId))
            {
                patient = await _db.Patients.FirstOrDefaultAsync(p => p.PatientId == d.PatientId, ct);
                if (patient is null)
                {
                    patient = new Patient
                    {
                        Id = Guid.NewGuid(),
                        PatientId = d.PatientId,
                        PatientName = d.PatientName,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    _db.Patients.Add(patient);
                }
                else
                {
                    // Update name if it changed / was missing
                    if (!string.IsNullOrWhiteSpace(d.PatientName) && patient.PatientName != d.PatientName)
                        patient.PatientName = d.PatientName;
                    patient.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            // 2) Study upsert
            var study = await _db.ImagingStudies
                .Include(x => x.Series)
                .FirstOrDefaultAsync(x => x.OrthancStudyId == d.OrthancStudyId, ct);

            if (study is null)
            {
                study = new ImagingStudy
                {
                    Id = Guid.NewGuid(),
                    OrthancStudyId = d.OrthancStudyId,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                };
                _db.ImagingStudies.Add(study);
            }

            study.StudyInstanceUid = d.StudyInstanceUid;
            study.StudyDate = d.StudyDate;
            study.ModalitiesInStudy = d.ModalitiesInStudy.ToArray();
            study.Patient = patient; // can be null
            study.PatientDbId = patient?.Id;
            study.UpdatedAtUtc = DateTime.UtcNow;

            // 3) Series upsert (minimal)
            var existingSeriesIds = study.Series.Select(x => x.OrthancSeriesId).ToHashSet(StringComparer.Ordinal);
            foreach (var sid in d.SeriesOrthancIds)
            {
                if (existingSeriesIds.Contains(sid))
                    continue;

                study.Series.Add(new ImagingSeries
                {
                    Id = Guid.NewGuid(),
                    StudyDbId = study.Id,
                    OrthancSeriesId = sid,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);

            // Reload as no-tracking for response consistency
            var saved = await _db.ImagingStudies
                .AsNoTracking()
                .Include(x => x.Patient)
                .Include(x => x.Series)
                .FirstAsync(x => x.OrthancStudyId == orthancStudyId, ct);

            return ToDetails(saved);
        }


        public async Task<int> SyncAllFromOrthancAsync(CancellationToken ct)
        {
            var remoteIds = await _orthanc.GetStudyIdsAsync(ct);
            var localIds = await _db.ImagingStudies.Select(x => x.OrthancStudyId).ToListAsync(ct);

            // 1. Identify studies present locally but missing in Orthanc (deleted)
            var idsToDelete = localIds.Except(remoteIds).ToList();
            if (idsToDelete.Count > 0)
            {
                var studiesToDelete = await _db.ImagingStudies
                    .Where(x => idsToDelete.Contains(x.OrthancStudyId))
                    .ToListAsync(ct);
                
                _db.ImagingStudies.RemoveRange(studiesToDelete);
                await _db.SaveChangesAsync(ct);
            }

            if (remoteIds.Count == 0) return 0;

            // 2. Sync existing/new studies from Orthanc
            // Limited parallelism with separate scopes to avoid DbContext concurrency issues
            var gate = new SemaphoreSlim(3);
            var tasks = remoteIds.Select(async id =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scopedService = scope.ServiceProvider.GetRequiredService<IStudyCacheService>();
                    await scopedService.SyncFromOrthancAsync(id, ct);
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();

            await Task.WhenAll(tasks);
            return remoteIds.Count;
        }

        private static StudySummary ToSummary(ImagingStudy s) => new(
                OrthancStudyId: s.OrthancStudyId,
                StudyInstanceUid: s.StudyInstanceUid,
                PatientId: s.Patient?.PatientId,
                PatientName: s.Patient?.PatientName,
                StudyDate: s.StudyDate,
                ModalitiesInStudy: s.ModalitiesInStudy
            );

        private static StudyDetails ToDetails(ImagingStudy s) => new(
            OrthancStudyId: s.OrthancStudyId,
            StudyInstanceUid: s.StudyInstanceUid,
            PatientId: s.Patient?.PatientId,
            PatientName: s.Patient?.PatientName,
            StudyDate: s.StudyDate,
            ModalitiesInStudy: s.ModalitiesInStudy,
            SeriesOrthancIds: s.Series.Select(x => x.OrthancSeriesId).ToList()
        );
    }
}
