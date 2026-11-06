using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using VFMS.API.Extensions;
using VFMS.Application.Interfaces;

namespace VFMS.IntegrationTests;

public sealed class FaceTemplateProtectionTests
{
    [Fact]
    public void FaceTemplateRoundTripsAndKeyRingIsEncryptedOnWindows()   // SRS 2.3: only encrypted templates at rest
    {
        var keysPath = Path.Combine(Path.GetTempPath(), "vfms-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = keysPath })
                .Build();
            var environment = new HostingEnvironment { ContentRootPath = Path.GetTempPath(), EnvironmentName = Environments.Development };
            using var provider = new ServiceCollection().AddLogging().AddCheckInModule(configuration, environment).BuildServiceProvider();
            var protector = provider.GetRequiredService<IFaceTemplateProtector>();
            var descriptor = Enumerable.Range(0, 128).Select(i => i / 100f).ToArray();

            var stored = protector.Protect(descriptor);

            Assert.Equal(descriptor, protector.Unprotect(stored));
            var keyXml = File.ReadAllText(Directory.GetFiles(keysPath, "key-*.xml").Single());
            if (OperatingSystem.IsWindows())
                Assert.Contains("encryptedSecret", keyXml);
        }
        finally
        {
            if (Directory.Exists(keysPath))
                Directory.Delete(keysPath, true);
        }
    }
}
