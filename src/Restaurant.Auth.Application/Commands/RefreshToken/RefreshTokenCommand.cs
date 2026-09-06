using MediatR;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.RefreshToken;

public class RefreshTokenCommand : IRequest<Result<AuthResponse>>
{
    public string RefreshToken { get; set; } = string.Empty;
}
