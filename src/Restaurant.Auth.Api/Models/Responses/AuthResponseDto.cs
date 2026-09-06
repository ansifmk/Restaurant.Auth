namespace Restaurant.Auth.Api.Models.Responses;

public record AuthResponseDto(DateTime AccessTokenExpiresAt, DateTime RefreshTokenExpiresAt);
public record LogoutResponseDto(string Message);
