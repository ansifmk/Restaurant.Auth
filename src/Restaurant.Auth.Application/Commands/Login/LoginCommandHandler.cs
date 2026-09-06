using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email.ToLowerInvariant());
        var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (user == null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for {Email} from IP {IP}", request.Email, clientIp);
            return Result<AuthResponse>.Failure("Invalid email or password.");
        }

        var token = _jwtTokenService.GenerateToken(user.Id, user.Email, user.Role.ToString());
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        var refreshExpirationDays = double.Parse(
            _configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");

        var familyId = Guid.NewGuid();

        await _refreshTokenRepository.AddAsync(new Domain.Entities.RefreshToken
        {
            Id = Guid.NewGuid(),
            TokenHash = Domain.Entities.TokenHasher.ComputeHash(refreshToken),
            UserId = user.Id,
            Expires = DateTime.UtcNow.AddDays(refreshExpirationDays),
            Created = DateTime.UtcNow,
            CreatedByIp = clientIp,
            FamilyId = familyId
        });

        _logger.LogInformation("Successful login for {Email} from IP {IP}", request.Email, clientIp);

        var accessExpirationMinutes = double.Parse(
            _configuration["Jwt:AccessTokenExpirationMinutes"] ?? "15");

        return Result<AuthResponse>.Success(new AuthResponse
        {
            Token = token,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(accessExpirationMinutes),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(refreshExpirationDays)
        });
    }
}
