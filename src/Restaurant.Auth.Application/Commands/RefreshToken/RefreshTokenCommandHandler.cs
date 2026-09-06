using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.RefreshToken;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponse>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var storedRefreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);
        var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (storedRefreshToken == null)
        {
            _logger.LogWarning("Invalid refresh token used from IP {IP}", clientIp);
            return Result<AuthResponse>.Failure("Invalid refresh token.");
        }

        if (storedRefreshToken.IsExpired)
        {
            _logger.LogWarning("Expired refresh token used from IP {IP} for user {UserId}", clientIp, storedRefreshToken.UserId);
            return Result<AuthResponse>.Failure("Refresh token has expired.");
        }

        if (storedRefreshToken.Revoked != null)
        {
            // Token reuse detected — revoke entire family
            await _refreshTokenRepository.RevokeAllByFamilyAsync(storedRefreshToken.FamilyId);
            _logger.LogCritical("Refresh token reuse detected from IP {IP} for family {FamilyId}. All tokens revoked.", clientIp, storedRefreshToken.FamilyId);
            return Result<AuthResponse>.Failure("Refresh token reuse detected. All tokens in this family have been revoked.");
        }

        // Log IP mismatch (audit trail, not blocking)
        if (storedRefreshToken.CreatedByIp != clientIp)
        {
            _logger.LogWarning("Refresh token IP mismatch: original {OriginalIP}, current {CurrentIP} for user {UserId}",
                storedRefreshToken.CreatedByIp, clientIp, storedRefreshToken.UserId);
        }

        var user = await _userRepository.GetByIdAsync(storedRefreshToken.UserId);

        if (user == null)
        {
            _logger.LogWarning("User not found for refresh token: {UserId}", storedRefreshToken.UserId);
            return Result<AuthResponse>.Failure("Invalid refresh token.");
        }

        // Revoke old token
        storedRefreshToken.Revoked = DateTime.UtcNow;
        await _refreshTokenRepository.UpdateAsync(storedRefreshToken);

        // Generate new tokens
        var newAccessToken = _jwtTokenService.GenerateToken(user.Id, user.Email, user.Role.ToString());
        var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

        // Save new refresh token to DB with same FamilyId
        var refreshExpirationDays = double.Parse(
            _configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");

        await _refreshTokenRepository.AddAsync(new Domain.Entities.RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Domain.Entities.TokenHasher.ComputeHash(newRefreshToken),
            UserId = user.Id,
            Expires = DateTime.UtcNow.AddDays(refreshExpirationDays),
            Created = DateTime.UtcNow,
            CreatedByIp = clientIp,
            FamilyId = storedRefreshToken.FamilyId
        });

        _logger.LogInformation("Token refreshed for user {UserId} from IP {IP}", user.Id, clientIp);

        var accessExpirationMinutes = double.Parse(
            _configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15");

        return Result<AuthResponse>.Success(new AuthResponse
        {
            Token = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(accessExpirationMinutes),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(refreshExpirationDays)
        });
    }
}
