using System.Text;

namespace Qsearch.Safety.Evidence;

public static class ShortcutInspector
{
    private static readonly string[] ScriptHosts =
    [
        "powershell.exe",
        "pwsh.exe",
        "cmd.exe",
        "wscript.exe",
        "cscript.exe",
        "mshta.exe",
    ];

    public static ShortcutMetadata? Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 76)
        {
            return null;
        }

        string unicode = Encoding.Unicode.GetString(bytes);
        string ascii = Encoding.Latin1.GetString(bytes);
        string? host = ScriptHosts.FirstOrDefault(
            candidate => unicode.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                ascii.Contains(candidate, StringComparison.OrdinalIgnoreCase));
        return host is null ? null : new ShortcutMetadata(host, null);
    }

    public static bool TargetsScriptHost(ShortcutMetadata metadata)
    {
        return metadata.Target is not null && ScriptHosts.Any(
            host => metadata.Target.EndsWith(host, StringComparison.OrdinalIgnoreCase));
    }
}
