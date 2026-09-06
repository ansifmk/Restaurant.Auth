namespace Restaurant.Auth.Api.Models.Responses;

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string? Field { get; set; }
    public string Message { get; set; } = string.Empty;
}
