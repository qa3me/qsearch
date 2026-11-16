namespace Qsearch.Windows.Volumes;

public sealed record VolumeProbe(
    string RootPath,
    string VolumeName,
    DriveType DriveType,
    bool IsReady,
    string FileSystem);

public sealed record IndexableVolume(
    string RootPath,
    string VolumeName,
    DriveType DriveType,
    string FileSystem);

public interface IVolumeProbeProvider
{
    IReadOnlyList<VolumeProbe> GetVolumes();
}

public interface IFixedVolumeDiscovery
{
    IReadOnlyList<IndexableVolume> Discover();
}

public sealed class FixedVolumeDiscovery : IFixedVolumeDiscovery
{
    private readonly IVolumeProbeProvider _provider;

    public FixedVolumeDiscovery(IVolumeProbeProvider? provider = null)
    {
        _provider = provider ?? new DriveInfoVolumeProbeProvider();
    }

    public IReadOnlyList<IndexableVolume> Discover()
    {
        return _provider.GetVolumes()
            .Where(volume =>
                volume.IsReady &&
                volume.DriveType == DriveType.Fixed &&
                string.Equals(volume.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
            .Select(volume => new IndexableVolume(
                NormalizeRoot(volume.RootPath),
                volume.VolumeName,
                volume.DriveType,
                "NTFS"))
            .OrderBy(volume => volume.RootPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath) ?? fullPath;
        return root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
    }

    private sealed class DriveInfoVolumeProbeProvider : IVolumeProbeProvider
    {
        public IReadOnlyList<VolumeProbe> GetVolumes()
        {
            List<VolumeProbe> volumes = [];
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    bool ready = drive.IsReady;
                    volumes.Add(new VolumeProbe(
                        drive.RootDirectory.FullName,
                        ready ? drive.VolumeLabel : string.Empty,
                        drive.DriveType,
                        ready,
                        ready ? drive.DriveFormat : string.Empty));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    volumes.Add(new VolumeProbe(
                        drive.RootDirectory.FullName,
                        string.Empty,
                        drive.DriveType,
                        false,
                        string.Empty));
                }
            }

            return volumes;
        }
    }
}
