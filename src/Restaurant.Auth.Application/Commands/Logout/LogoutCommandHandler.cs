using MediatR;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.Logout;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokenRepository)
    {
        _refreshTokenRepository = refreshTokenRepository;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenRepository.GetByTokenAsync(request.RefreshToken);

        if (refreshToken == null)
        {
            return Result.Failure("Invalid refresh token.");
        }

        refreshToken.Revoked = DateTime.UtcNow;
        await _refreshTokenRepository.UpdateAsync(refreshToken);

        return Result.Success();
    }
}
