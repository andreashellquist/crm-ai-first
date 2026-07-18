namespace CrmApi.Models;

// Dev-only password auth; replace with real OAuth per auth-security-expert
// before shipping to real users.
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? Name { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<WorkspaceMember> Memberships { get; set; } = [];
}
