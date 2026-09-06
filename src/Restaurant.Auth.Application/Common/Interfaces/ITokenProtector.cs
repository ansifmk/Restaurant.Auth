namespace Restaurant.Auth.Application.Common.Interfaces;

public interface ITokenProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedData);
}
