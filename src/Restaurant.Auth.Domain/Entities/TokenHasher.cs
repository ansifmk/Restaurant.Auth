using System.Security.Cryptography;
using System.Text;

namespace Restaurant.Auth.Domain.Entities;

public static class TokenHasher
{
    public static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }
}
