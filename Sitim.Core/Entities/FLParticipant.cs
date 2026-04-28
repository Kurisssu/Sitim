namespace Sitim.Core.Entities;

/// <summary>
/// One institution participating in a federated learning session.
/// </summary>
public sealed class FLParticipant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }
    public FLSession Session { get; set; } = null!;

    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    public FLParticipantStatus Status { get; set; } = FLParticipantStatus.Invited;
    public DateTime? LastHeartbeatUtc { get; set; }
}

public enum FLParticipantStatus
{
    Invited = 0,
    Accepted = 1,
    Training = 2,
    Completed = 3,
    Failed = 4
}
