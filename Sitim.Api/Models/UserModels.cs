namespace Sitim.Api.Models
{
    public sealed record UserResult(
        Guid Id,
        string Email,
        string? FullName,
        string Role,
        Guid? InstitutionId,
        string? InstitutionName,
        bool IsActive,
        DateTime CreatedAtUtc);

    public sealed record InviteUserRequest(
        string Email,
        string? FullName,
        string Role,
        // Only used by SuperAdmin. Admin-created users inherit the Admin's institution.
        Guid? InstitutionId);

    public sealed record InviteUserResponse(
        Guid UserId,
        string Email,
        string InviteLink);

    public sealed record UpdateUserRequest(
        string? FullName,
        string? Role,
        bool? IsActive);

    public sealed record SetPasswordRequest(
        Guid UserId,
        string Token,
        string NewPassword);
}
