using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Restaurant.Auth.Api.Models.Requests;
using Restaurant.Auth.Api.Models.Responses;
using Restaurant.Auth.Application.Commands.Login;
using Restaurant.Auth.Application.Commands.Logout;
using Restaurant.Auth.Application.Commands.RefreshToken;
using Restaurant.Auth.Application.Commands.Register;
using Restaurant.Auth.Application.Common.Interfaces;

namespace Restaurant.Auth.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IConfiguration _configuration;
    private readonly ITokenProtector _protector;

    public AuthController(IMediator mediator, IConfiguration configuration, ITokenProtector protector)
    {
        _mediator = mediator;
        _configuration = configuration;
        _protector = protector;
    }

    private string AccessTokenName => _configuration["Cookie:AccessTokenName"] ?? "accessToken";
    private string RefreshTokenName => _configuration["Cookie:RefreshTokenName"] ?? "refreshToken";
    private string RefreshTokenPath => _configuration["Cookie:RefreshTokenPath"] ?? "/api/auth/refresh";
    private int CookieMaxAgeDays => int.Parse(_configuration["Cookie:MaxAgeDays"] ?? "7");

    private void SetTokenCookie(string name, string value, string path, int maxAgeDays)
    {
        var encryptedValue = _protector.Protect(value);
        Response.Cookies.Append(name, encryptedValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = path,
            MaxAge = TimeSpan.FromDays(maxAgeDays),
            IsEssential = true
        });
    }

    private void ClearCookie(string name, string path)
    {
        Response.Cookies.Delete(name, new CookieOptions
        {
            Path = path,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth.register")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var command = new RegisterCommand
        {
            Email = request.Email,
            Password = request.Password,
            ConfirmPassword = request.ConfirmPassword,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            var errors = new List<ApiError>
            {
                new() { Code = "REGISTRATION_FAILED", Message = result.Error ?? "Registration could not be completed." }
            };
            return BadRequest(ApiResponse<object>.Fail(result.Error ?? "Registration could not be completed.", errors));
        }

        SetTokenCookie(AccessTokenName, result.Data!.Token, "/", 1);
        SetTokenCookie(RefreshTokenName, result.Data.RefreshToken, RefreshTokenPath, CookieMaxAgeDays);

        var response = ApiResponse<AuthResponseDto>.Ok(
            new AuthResponseDto(result.Data.ExpiresAt, result.Data.RefreshTokenExpiresAt),
            "Registration successful.");

        return CreatedAtAction(nameof(Register), response);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth.login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var command = new LoginCommand
        {
            Email = request.Email,
            Password = request.Password
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            var errors = new List<ApiError>
            {
                new() { Code = "AUTHENTICATION_FAILED", Message = result.Error ?? "Invalid email or password." }
            };
            return Unauthorized(ApiResponse<object>.Fail(result.Error ?? "Invalid email or password.", errors));
        }

        SetTokenCookie(AccessTokenName, result.Data!.Token, "/", 1);
        SetTokenCookie(RefreshTokenName, result.Data.RefreshToken, RefreshTokenPath, CookieMaxAgeDays);

        var response = ApiResponse<AuthResponseDto>.Ok(
            new AuthResponseDto(result.Data.ExpiresAt, result.Data.RefreshTokenExpiresAt),
            "Login successful.");

        return Ok(response);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth.refresh")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenName, out var encryptedRefreshToken) || string.IsNullOrEmpty(encryptedRefreshToken))
        {
            var errors = new List<ApiError>
            {
                new() { Code = "REFRESH_TOKEN_MISSING", Message = "Refresh token cookie not found." }
            };
            return BadRequest(ApiResponse<object>.Fail("Refresh token cookie not found.", errors));
        }

        var refreshToken = _protector.Unprotect(encryptedRefreshToken);

        var command = new RefreshTokenCommand
        {
            RefreshToken = refreshToken
        };

        var result = await _mediator.Send(command);

        if (!result.IsSuccess)
        {
            ClearCookie(AccessTokenName, "/");
            ClearCookie(RefreshTokenName, RefreshTokenPath);

            var errors = new List<ApiError>
            {
                new() { Code = "REFRESH_FAILED", Message = result.Error ?? "Token refresh failed." }
            };
            return BadRequest(ApiResponse<object>.Fail(result.Error ?? "Token refresh failed.", errors));
        }

        SetTokenCookie(AccessTokenName, result.Data!.Token, "/", 1);
        SetTokenCookie(RefreshTokenName, result.Data.RefreshToken, RefreshTokenPath, CookieMaxAgeDays);

        var response = ApiResponse<AuthResponseDto>.Ok(
            new AuthResponseDto(result.Data.ExpiresAt, result.Data.RefreshTokenExpiresAt),
            "Token refreshed successfully.");

        return Ok(response);
    }

    [HttpPost("logout")]
    [EnableRateLimiting("auth.logout")]
    [ProducesResponseType(typeof(ApiResponse<LogoutResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenName, out var encryptedRefreshToken) || string.IsNullOrEmpty(encryptedRefreshToken))
        {
            ClearCookie(AccessTokenName, "/");
            ClearCookie(RefreshTokenName, "/");
            ClearCookie(RefreshTokenName, "/api/auth/refresh");

            var errors = new List<ApiError>
            {
                new() { Code = "REFRESH_TOKEN_MISSING", Message = "Refresh token cookie not found." }
            };
            return BadRequest(ApiResponse<object>.Fail("Refresh token cookie not found.", errors));
        }

        var refreshToken = _protector.Unprotect(encryptedRefreshToken);

        var command = new LogoutCommand
        {
            RefreshToken = refreshToken
        };

        var result = await _mediator.Send(command);

        ClearCookie(AccessTokenName, "/");
        ClearCookie(RefreshTokenName, RefreshTokenPath);

        if (!result.IsSuccess)
        {
            var errors = new List<ApiError>
            {
                new() { Code = "LOGOUT_FAILED", Message = result.Error ?? "Logout could not be completed." }
            };
            return BadRequest(ApiResponse<object>.Fail(result.Error ?? "Logout could not be completed.", errors));
        }

        var response = ApiResponse<LogoutResponseDto>.Ok(
            new LogoutResponseDto("Logged out successfully."),
            "Logged out successfully.");

        return Ok(response);
    }
}
