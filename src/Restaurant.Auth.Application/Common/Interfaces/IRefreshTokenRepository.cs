using Restaurant.Auth.Domain.Entities;

namespace Restaurant.Auth.Application.Common.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task AddAsync(RefreshToken refreshToken);
    Task UpdateAsync(RefreshToken refreshToken);
    Task RevokeAllByFamilyAsync(Guid familyId);
}
