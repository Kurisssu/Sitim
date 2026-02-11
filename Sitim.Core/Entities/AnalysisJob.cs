using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Sitim.Core.Entities
{
    /// <summary>
    /// Represents a single analysis run for a given Orthanc Study.
    /// </summary>
    public sealed class AnalysisJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Orthanc internal Study id (REST id).</summary>
        public string OrthancStudyId { get; set; } = default!;

        /// <summary>Logical identifier of the model/pipeline to run (e.g. "demo", "lung-nodules-v1").</summary>
        public string ModelKey { get; set; } = "demo";

        public AnalysisStatus Status { get; set; } = AnalysisStatus.Queued;

        /// <summary>Hangfire background job id (string), if scheduled.</summary>
        public string? HangfireJobId { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }

        /// <summary>Path on local disk where the study archive (zip) is stored.</summary>
        public string? StudyArchivePath { get; set; }

        /// <summary>Path on local disk where the analysis result JSON is stored.</summary>
        public string? ResultJsonPath { get; set; }
    }
}
