using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sitim.Core.Entities;
using Sitim.Core.Services;
using Sitim.Infrastructure.Identity;

namespace Sitim.Infrastructure.Data
{
    public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        private readonly ITenantContext _tenantContext;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public DbSet<Institution> Institutions => Set<Institution>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<ImagingStudy> ImagingStudies => Set<ImagingStudy>();
        public DbSet<ImagingSeries> ImagingSeries => Set<ImagingSeries>();
        public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Institution>(b =>
            {
                b.ToTable("institutions");
                b.HasKey(x => x.Id);
                b.Property(x => x.Name).HasMaxLength(256).IsRequired();
                b.Property(x => x.Slug).HasMaxLength(64).IsRequired();
                b.HasIndex(x => x.Slug).IsUnique();
                b.Property(x => x.OrthancLabel).HasMaxLength(64).IsRequired();
                b.HasIndex(x => x.OrthancLabel).IsUnique();
                b.Property(x => x.IsActive).HasDefaultValue(true);
                b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            });

            modelBuilder.Entity<Patient>(b =>
            {
                b.ToTable("patients");
                b.HasKey(x => x.Id);
                b.Property(x => x.PatientId).HasMaxLength(128);
                b.Property(x => x.PatientName).HasMaxLength(256);
                b.HasIndex(x => x.PatientId);
                b.Property(x => x.InstitutionId).HasColumnName("institution_id");
                b.HasIndex(x => x.InstitutionId);
                b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
                b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

                // Global Query Filter: scope to current institution.
                // When InstitutionId is null (SuperAdmin / background jobs), all records are visible.
                b.HasQueryFilter(p =>
                    _tenantContext.InstitutionId == null ||
                    p.InstitutionId == _tenantContext.InstitutionId);
            });

            modelBuilder.Entity<ImagingStudy>(b =>
            {
                b.ToTable("imaging_studies");
                b.HasKey(x => x.Id);

                b.Property(x => x.OrthancStudyId).HasColumnName("orthanc_study_id").HasMaxLength(64).IsRequired();
                b.HasIndex(x => x.OrthancStudyId).IsUnique();

                b.Property(x => x.StudyInstanceUid).HasColumnName("study_instance_uid").HasMaxLength(128);
                b.HasIndex(x => x.StudyInstanceUid);

                b.Property(x => x.StudyDate).HasColumnName("study_date").HasMaxLength(16);

                // PostgreSQL text[] mapping via Npgsql
                b.Property(x => x.ModalitiesInStudy).HasColumnName("modalities_in_study");

                b.Property(x => x.InstitutionId).HasColumnName("institution_id");
                b.HasIndex(x => x.InstitutionId);

                b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
                b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

                b.HasOne(x => x.Patient)
                    .WithMany(p => p.Studies)
                    .HasForeignKey(x => x.PatientDbId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Global Query Filter
                b.HasQueryFilter(s =>
                    _tenantContext.InstitutionId == null ||
                    s.InstitutionId == _tenantContext.InstitutionId);
            });

            modelBuilder.Entity<ImagingSeries>(b =>
            {
                b.ToTable("imaging_series");
                b.HasKey(x => x.Id);

                b.Property(x => x.OrthancSeriesId).HasColumnName("orthanc_series_id").HasMaxLength(64).IsRequired();
                b.HasIndex(x => new { x.StudyDbId, x.OrthancSeriesId }).IsUnique();

                b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");

                b.HasOne(x => x.Study)
                    .WithMany(s => s.Series)
                    .HasForeignKey(x => x.StudyDbId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AnalysisJob>(b =>
            {
                b.ToTable("analysis_jobs");
                b.HasKey(x => x.Id);

                b.Property(x => x.OrthancStudyId).HasColumnName("orthanc_study_id").HasMaxLength(64).IsRequired();
                b.HasIndex(x => x.OrthancStudyId);

                b.Property(x => x.ModelKey).HasColumnName("model_key").HasMaxLength(128).IsRequired();

                b.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);

                b.Property(x => x.HangfireJobId).HasColumnName("hangfire_job_id").HasMaxLength(64);
                b.Property(x => x.ErrorMessage).HasColumnName("error_message");

                b.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
                b.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
                b.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc");

                b.Property(x => x.StudyArchivePath).HasColumnName("study_archive_path");
                b.Property(x => x.ResultJsonPath).HasColumnName("result_json_path");

                b.Property(x => x.InstitutionId).HasColumnName("institution_id");
                b.HasIndex(x => x.InstitutionId);

                b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");

                // Global Query Filter
                b.HasQueryFilter(j =>
                    _tenantContext.InstitutionId == null ||
                    j.InstitutionId == _tenantContext.InstitutionId);
            });
        }
    }
}
