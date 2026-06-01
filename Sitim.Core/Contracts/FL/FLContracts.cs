namespace Sitim.Core.Contracts.FL;

/// <summary>Body for POST /api/fl/sessions (start new FL session).</summary>
public sealed record StartFLSessionRequest(
    string ModelKey,
    int TotalRounds,
    List<Guid> InstitutionIds);

/// <summary>Compact FL session row — listing on the Federated Learning dashboard.</summary>
public sealed record FLSessionDto(
    Guid Id,
    string ModelKey,
    string Status,
    int TotalRounds,
    int CurrentRound,
    int ParticipantsCount,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc);

/// <summary>Participant entry in an FL session — one row per invited institution.</summary>
public sealed record FLParticipantDto(
    Guid InstitutionId,
    string InstitutionName,
    string Status,
    DateTime? LastHeartbeatUtc);

/// <summary>Connected FL client (heartbeat from a Flower fl-client container).</summary>
public sealed record FLConnectedClientDto(
    Guid InstitutionId,
    string ClientId,
    string Status,
    DateTime? LastHeartbeatUtc,
    bool IsOnline);

/// <summary>One round of FL aggregation — metrics produced by Flower's aggregate_evaluate.</summary>
public sealed record FLRoundDto(
    int RoundNumber,
    decimal? AggregatedLoss,
    decimal? AggregatedAccuracy,
    DateTime? CompletedAtUtc);

/// <summary>Detailed FL session view — includes participants + per-round metrics.</summary>
public sealed record FLSessionDetailsDto(
    Guid Id,
    string ModelKey,
    string Status,
    int TotalRounds,
    int CurrentRound,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? LastError,
    string? OutputModelPath,
    List<FLParticipantDto> Participants,
    List<FLRoundDto> Rounds);

/// <summary>FL session's published model in the AI model registry (post-completion).</summary>
public sealed record FLPublishedModelDto(
    Guid ModelId,
    string Name,
    string Task,
    string Version,
    string StorageFileName,
    bool IsActive);
