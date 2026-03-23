using Andy.CodeIndex.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace Andy.CodeIndex.Infrastructure.Services;

public class EncryptionService : IEncryptionService
{
    private readonly IDataProtector _protector;

    public EncryptionService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Andy.CodeIndex.ApiKeys");
    }

    public string Encrypt(string plainText) => _protector.Protect(plainText);

    public string Decrypt(string cipherText)
    {
        try { return _protector.Unprotect(cipherText); }
        catch { return ""; }
    }
}
