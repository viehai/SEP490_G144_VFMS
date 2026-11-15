using Microsoft.AspNetCore.DataProtection;
using VFMS.Application.Interfaces;

namespace VFMS.API.Security;

/// <summary>
/// Encrypts face descriptors with ASP.NET Core Data Protection.
/// Keys live in the "DataProtection:KeysPath" folder (default: ./keys). Losing that folder makes every stored
/// template unreadable, so members would have to register their face again.
/// </summary>
public sealed class DataProtectionFaceTemplateProtector : IFaceTemplateProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionFaceTemplateProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("VFMS.FaceTemplate.v1");
    }

    public byte[] Protect(IReadOnlyList<float> descriptor)
    {
        var bytes = new byte[descriptor.Count * sizeof(float)];
        Buffer.BlockCopy(descriptor.ToArray(), 0, bytes, 0, bytes.Length);
        return _protector.Protect(bytes);
    }

    public float[] Unprotect(byte[] protectedTemplate)
    {
        var bytes = _protector.Unprotect(protectedTemplate);
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
        return values;
    }
}
