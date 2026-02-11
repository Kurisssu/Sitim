namespace Sitim.Core.Models
{
    public sealed record CreateAnalysisRequest(
        string OrthancStudyId,
        string? ModelKey
    );

    public sealed record AnalysisJobDto(
        Guid Id,
        string OrthancStudyId,
        string ModelKey,
        string Status,
        string? HangfireJobId,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? FinishedAtUtc,
        string? ErrorMessage,
        string? ResultUrl
    );
}
