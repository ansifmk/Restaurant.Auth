namespace Restaurant.Auth.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateToken(Guid userId, string email, string role);
    string GenerateRefreshToken();
}
