using System.ComponentModel.DataAnnotations.Schema;

namespace Restaurant.Auth.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; }

    [NotMapped]
    public string Token { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTime Expires { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Revoked { get; set; }
    public string CreatedByIp { get; set; } = string.Empty;
    public Guid FamilyId { get; set; }

    public User User { get; set; } = null!;

    public bool IsExpired => DateTime.UtcNow >= Expires;
    public bool IsActive => Revoked == null && !IsExpired;
}
