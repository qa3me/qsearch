using System.Globalization;
using System.IO;
using System.Text.Json;
using Qsearch.Core.Settings;

namespace Qsearch.App.Services;

public interface ILocalSettingsStore
{
    ValueTask<QsearchSettings> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(QsearchSettings settings, CancellationToken cancellationToken);
}

public sealed class LocalSettingsStore : ILocalSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly TimeProvider _timeProvider;

    public LocalSettingsStore(string settingsPath, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _settingsPath = Path.GetFullPath(settingsPath);
        _timeProvider = timeProvider;
    }

    public async ValueTask<QsearchSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return QsearchSettings.Default;
        }

        try
        {
            await using FileStream stream = new(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            QsearchSettings? settings = await JsonSerializer.DeserializeAsync<QsearchSettings>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (settings is null || (!settings.IsValid() && !settings.CanUpgrade()))
            {
                throw new JsonException("The settings document was invalid.");
            }

            return settings.Upgrade();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            PreserveCorruptFile();
            return QsearchSettings.Default;
        }
    }

    public async ValueTask SaveAsync(
        QsearchSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.IsValid())
        {
            throw new ArgumentException("Settings values are invalid.", nameof(settings));
        }

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _settingsPath + ".tmp";
        try
        {
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void PreserveCorruptFile()
    {
        string directory = Path.GetDirectoryName(_settingsPath) ?? string.Empty;
        string timestamp = _timeProvider.GetUtcNow().ToString(
            "yyyyMMddHHmmssfff",
            CultureInfo.InvariantCulture);
        string preservedPath = Path.Combine(directory, $"settings.corrupt-{timestamp}.json");
        if (File.Exists(preservedPath))
        {
            preservedPath = Path.Combine(
                directory,
                $"settings.corrupt-{timestamp}-{Guid.NewGuid():N}.json");
        }

        File.Move(_settingsPath, preservedPath);
    }
}
