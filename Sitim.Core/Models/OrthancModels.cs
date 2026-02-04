namespace Sitim.Core.Models;

/// <summary>
/// Minimal data we need from Orthanc for MVP: listing studies + building an OHIF deep-link.
/// </summary>
public sealed record OrthancStudyDetails(
    string OrthancStudyId,
    string? StudyInstanceUid,
    string? StudyDate,
    string? PatientId,
    string? PatientName,
    IReadOnlyList<string> ModalitiesInStudy,
    IReadOnlyList<string> SeriesOrthancIds
);

public sealed record StudySummary(
    string OrthancStudyId,
    string? StudyInstanceUid,
    string? PatientId,
    string? PatientName,
    string? StudyDate,
    IReadOnlyList<string> ModalitiesInStudy
);

public sealed record StudyDetails(
    string OrthancStudyId,
    string? StudyInstanceUid,
    string? PatientId,
    string? PatientName,
    string? StudyDate,
    IReadOnlyList<string> ModalitiesInStudy,
    IReadOnlyList<string> SeriesOrthancIds
);
