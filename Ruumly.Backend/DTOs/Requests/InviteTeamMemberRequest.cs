namespace Ruumly.Backend.DTOs.Requests;

public record InviteTeamMemberRequest
{
    public string Email { get; init; } = string.Empty;
    public string Name  { get; init; } = string.Empty;
}
