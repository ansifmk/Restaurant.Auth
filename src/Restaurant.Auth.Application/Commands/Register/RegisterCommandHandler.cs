using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Restaurant.Auth.Application.Common.Interfaces;
using Restaurant.Auth.Application.Common.Models;

namespace Restaurant.Auth.Application.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<RegisterCommandHandler> _logger;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        ILogger<RegisterCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        if (await _userRepository.ExistsByEmailAsync(request.Email))
        {
            _logger.LogWarning("Registration attempt with existing email: {Email}", request.Email);
            return Result<AuthResponse>.Failure("Email already exists.");
        }

        var user = new Domain.Entities.User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = _passwordHasher.Hash(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Role = Domain.Enums.Role.Staff,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);

        var token = _jwtTokenService.GenerateToken(user.Id, user.Email, user.Role.ToString());
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        var refreshExpirationDays = double.Parse(
            _configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");

        var familyId = Guid.NewGuid();
        var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

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

        _logger.LogInformation("User registered successfully: {Email}", request.Email);

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
