using System.Globalization;
using System.Windows.Media;
using Qsearch.App.Services;

namespace Qsearch.App.ViewModels;

public sealed class FilePreviewViewModel
{
    public FilePreviewViewModel(FilePreviewData preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        Name = preview.Name;
        FullPath = preview.FullPath;
        ParentPath = preview.ParentPath;
        Extension = string.IsNullOrWhiteSpace(preview.Extension)
            ? "—"
            : preview.Extension.TrimStart('.').ToUpperInvariant();
        IsDirectory = preview.IsDirectory;
        SizeText = preview.IsDirectory ? "—" : FormatSize(preview.Size);
        CreatedText = preview.CreatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        ModifiedText = preview.ModifiedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        AccessedText = preview.AccessedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
        AttributesText = preview.Attributes.ToString().Replace(", ", " · ", StringComparison.Ordinal);
        PreviewKind = preview.PreviewKind;
        TextPreview = preview.TextPreview;
        ImagePreview = preview.ImagePreview;
        IsTruncated = preview.IsTruncated;
        PreviewMessage = preview.PreviewMessage;
    }

    public string Name { get; }
    public string FullPath { get; }
    public string ParentPath { get; }
    public string Extension { get; }
    public bool IsDirectory { get; }
    public string SizeText { get; }
    public string CreatedText { get; }
    public string ModifiedText { get; }
    public string AccessedText { get; }
    public string AttributesText { get; }
    public LocalPreviewKind PreviewKind { get; }
    public string? TextPreview { get; }
    public ImageSource? ImagePreview { get; }
    public bool IsTruncated { get; }
    public string? PreviewMessage { get; }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{value:0} {units[unit]}")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }
}
