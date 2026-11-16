using System.Reflection;
using Qsearch.Safety.Evidence;

namespace Qsearch.Safety.Tests;

public sealed class AuthenticodeMappingTests
{
    [Theory]
    [InlineData(0, AuthenticodeState.Valid)]
    [InlineData(unchecked((int)0x800B0100), AuthenticodeState.NoEmbeddedSignature)]
    [InlineData(unchecked((int)0x80096010), AuthenticodeState.IntegrityFailure)]
    public void NativeStatus_IsMappedWithoutLosingExactValue(int status, AuthenticodeState expected)
    {
        AuthenticodeResult result = AuthenticodeVerifier.MapStatus(status);

        Assert.Equal(expected, result.State);
        Assert.Equal(status, result.NativeStatus);
    }

    [Fact]
    public void SafetyAssembly_HasNoMutationProcessOrNetworkSurface()
    {
        Assembly assembly = typeof(FileReviewEngine).Assembly;
        string[] forbiddenAssemblies = ["System.Diagnostics.Process", "System.Net.Http"];
        Assert.DoesNotContain(
            assembly.GetReferencedAssemblies(),
            reference => forbiddenAssemblies.Contains(reference.Name, StringComparer.Ordinal));
        Assert.DoesNotContain(
            assembly.GetExportedTypes().SelectMany(type => type.GetMethods()),
            method => method.Name is "Delete" or "Move" or "Start" or "Quarantine");
    }
}
