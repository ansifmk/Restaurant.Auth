using MediatR;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.Login;

public class LoginCommand : IRequest<Result<AuthResponse>>
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
