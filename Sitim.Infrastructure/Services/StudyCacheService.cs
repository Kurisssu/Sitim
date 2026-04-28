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
        private readonly IOrthancClientFactory _orthancFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ITenantContext _tenantContext;

        public StudyCacheService(
            AppDbContext db,
            IOrthancClientFactory orthancFactory,
            IServiceScopeFactory scopeFactory,
            ITenantContext tenantContext)
        {
            _db = db;
            _orthancFactory = orthancFactory;
            _scopeFactory = scopeFactory;
            _tenantContext = tenantContext;
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

        public async Task<ImagingStudy?> GetStudyEntityAsync(string orthancStudyId, CancellationToken ct)
        {
            // For SuperAdmin: need to bypass query filters to access any institution's study
            var query = _tenantContext.IsSuperAdmin 
                ? _db.ImagingStudies.IgnoreQueryFilters() 
                : _db.ImagingStudies;

            return await query
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.OrthancStudyId == orthancStudyId, ct);
        }

        public async Task<StudyDetails?> SyncFromOrthancAsync(string orthancStudyId, CancellationToken ct)
        {
            var orthanc = await _orthancFactory.CreateClientForCurrentTenantAsync(ct);
            if (orthanc is null)
                return null;

            var d = await orthanc.GetStudyAsync(orthancStudyId, ct);

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

            // 1) Patient upsert (best-effort) — IgnoreQueryFilters to avoid cross-tenant duplicates.
            Patient? patient = null;
            if (!string.IsNullOrWhiteSpace(d.PatientId))
            {
                patient = await _db.Patients
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.PatientId == d.PatientId, ct);
                if (patient is null)
                {
                    patient = new Patient
                    {
                        Id = Guid.NewGuid(),
                        PatientId = d.PatientId,
                        PatientName = d.PatientName,
                        InstitutionId = _tenantContext.InstitutionId,
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

            // 2) Study upsert — use IgnoreQueryFilters to find the record across ALL institutions,
            // preventing duplicates when a study already exists under a different institution.
            var study = await _db.ImagingStudies
                .IgnoreQueryFilters()
                .Include(x => x.Series)
                .FirstOrDefaultAsync(x => x.OrthancStudyId == d.OrthancStudyId, ct);

            if (study is not null
                && study.InstitutionId.HasValue
                && study.InstitutionId != _tenantContext.InstitutionId
                && !_tenantContext.IsSuperAdmin)
            {
                // Study belongs to a different institution — do not touch it.
                return await GetLocalAsync(d.OrthancStudyId, ct);
            }

            if (study is null)
            {
                study = new ImagingStudy
                {
                    Id = Guid.NewGuid(),
                    OrthancStudyId = d.OrthancStudyId,
                    InstitutionId = _tenantContext.InstitutionId,
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
            var orthanc = await _orthancFactory.CreateClientForCurrentTenantAsync(ct);
            if (orthanc is null)
                return 0;

            IReadOnlyList<string> remoteIds;

            if (!_tenantContext.IsSuperAdmin && _tenantContext.InstitutionId.HasValue)
            {
                // In multi-Orthanc architecture, each institution has its own Orthanc instance.
                // No filtering needed - all studies in this Orthanc belong to this institution.
                remoteIds = await orthanc.GetStudyIdsAsync(ct);
            }
            else
            {
                // SuperAdmin: sync every study in Orthanc (legacy single-Orthanc mode).
                // NOTE: In multi-Orthanc, SuperAdmin may need to iterate through all institutions.
                remoteIds = await orthanc.GetStudyIdsAsync(ct);
            }

            // localIds is already scoped by the Global Query Filter (tenant or all for SuperAdmin).
            var localIds = await _db.ImagingStudies.Select(x => x.OrthancStudyId).ToListAsync(ct);

            // 1. Remove local studies that no longer exist in Orthanc (for this institution's scope).
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

            // 2. Sync existing/new studies with limited parallelism.
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
            SeriesOrthancIds: s.Series.Select(x => x.OrthancSeriesId).ToList(),
            DbStudyId: s.Id  // Include DB ID
        );

        public async Task<bool> DeleteStudyAsync(string orthancStudyId, CancellationToken ct)
        {
            var orthanc = await _orthancFactory.CreateClientForCurrentTenantAsync(ct);
            if (orthanc is null)
                return false;

            // 1. Delete from Orthanc first — if this fails, we stop (DB stays consistent).
            var deleted = await orthanc.DeleteStudyAsync(orthancStudyId, ct);
            if (!deleted)
                return false;

            // 2. Remove from local DB (IgnoreQueryFilters so SuperAdmin can delete any study).
            var study = await _db.ImagingStudies
                .IgnoreQueryFilters()
                .Include(x => x.Patient)
                .FirstOrDefaultAsync(x => x.OrthancStudyId == orthancStudyId, ct);

            if (study is null)
                return true; // Already gone from DB — that's fine.

            var patientDbId = study.PatientDbId;
            _db.ImagingStudies.Remove(study);
            await _db.SaveChangesAsync(ct);

            // 3. Remove patient if it has no remaining studies.
            if (patientDbId.HasValue)
            {
                var hasOtherStudies = await _db.ImagingStudies
                    .IgnoreQueryFilters()
                    .AnyAsync(x => x.PatientDbId == patientDbId, ct);

                if (!hasOtherStudies)
                {
                    var patient = await _db.Patients
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(x => x.Id == patientDbId, ct);

                    if (patient is not null)
                    {
                        _db.Patients.Remove(patient);
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }

            return true;
        }
    }
}
