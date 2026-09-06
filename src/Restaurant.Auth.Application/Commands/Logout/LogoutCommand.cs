using MediatR;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.Logout;

public class LogoutCommand : IRequest<Result>
{
    public string RefreshToken { get; set; } = string.Empty;
}
