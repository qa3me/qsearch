using Qsearch.Windows.Volumes;

namespace Qsearch.Windows.Tests;

public sealed class FixedVolumeDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsOnlyReadyFixedNtfsVolumesInStableOrder()
    {
        VolumeProbe[] probes =
        [
            new(@"D:\", "Data", DriveType.Fixed, true, "ntfs"),
            new(@"Z:\", "Share", DriveType.Network, true, "NTFS"),
            new(@"E:\", "USB", DriveType.Removable, true, "NTFS"),
            new(@"F:\", "Not ready", DriveType.Fixed, false, "NTFS"),
            new(@"C:\", "Windows", DriveType.Fixed, true, "NTFS"),
            new(@"G:\", "Archive", DriveType.Fixed, true, "ReFS"),
        ];
        FixedVolumeDiscovery discovery = new(new StubProbeProvider(probes));

        IReadOnlyList<IndexableVolume> volumes = discovery.Discover();

        Assert.Collection(
            volumes,
            volume =>
            {
                Assert.Equal(@"C:\", volume.RootPath);
                Assert.Equal("Windows", volume.VolumeName);
                Assert.Equal("NTFS", volume.FileSystem);
            },
            volume =>
            {
                Assert.Equal(@"D:\", volume.RootPath);
                Assert.Equal("Data", volume.VolumeName);
                Assert.Equal("NTFS", volume.FileSystem);
            });
    }

    private sealed class StubProbeProvider(IReadOnlyList<VolumeProbe> probes) : IVolumeProbeProvider
    {
        public IReadOnlyList<VolumeProbe> GetVolumes() => probes;
    }
}
