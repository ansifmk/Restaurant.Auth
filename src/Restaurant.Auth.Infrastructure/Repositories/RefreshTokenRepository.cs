using Microsoft.EntityFrameworkCore;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Domain.Entities;

namespace Restaurant.Auth.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly Data.AppDbContext _context;

    public RefreshTokenRepository(Data.AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        var tokenHash = TokenHasher.ComputeHash(token);
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
    }

    public async Task AddAsync(RefreshToken refreshToken)
    {
        await _context.RefreshTokens.AddAsync(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Update(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task RevokeAllByFamilyAsync(Guid familyId)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.FamilyId == familyId && rt.Revoked == null)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoked = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}
