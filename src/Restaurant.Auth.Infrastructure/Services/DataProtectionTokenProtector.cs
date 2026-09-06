using Microsoft.AspNetCore.DataProtection;
using Restaurant.Auth.Application.Common.Interfaces;

namespace Restaurant.Auth.Infrastructure.Services;

public class DataProtectionTokenProtector : ITokenProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("RestaurantAuth.TokenProtector");
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedData) => _protector.Unprotect(protectedData);
}
