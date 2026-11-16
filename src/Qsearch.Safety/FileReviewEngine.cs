using Qsearch.Safety.Evidence;
using Qsearch.Safety.Models;
using Qsearch.Safety.Policy;

namespace Qsearch.Safety;

public sealed class FileReviewEngine
{
    public const int MaximumHeaderBytes = 4096;
    public const int EntropySampleBytes = 64 * 1024;
    public const int MaximumEntropySamples = 3;
    public const long MinimumEntropyFileSize = 192 * 1024;
    public const long MaximumAutomaticEntropyFileSize = 256L * 1024 * 1024;
    public static readonly TimeSpan ReviewTimeout = TimeSpan.FromSeconds(3);

    private static readonly HashSet<string> ExecutableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".scr", ".com", ".ps1", ".bat", ".cmd", ".vbs", ".js", ".wsf", ".msi",
        };
    private static readonly HashSet<string> FamiliarDocumentExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png",
        };
    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

    private readonly IFileEvidenceReader _evidenceReader;
    private readonly IAuthenticodeVerifier _authenticodeVerifier;
    private readonly ReviewPolicy _policy;
    private readonly TimeProvider _timeProvider;

    public FileReviewEngine(
        IFileEvidenceReader evidenceReader,
        IAuthenticodeVerifier authenticodeVerifier,
        ReviewPolicy policy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(evidenceReader);
        ArgumentNullException.ThrowIfNull(authenticodeVerifier);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _evidenceReader = evidenceReader;
        _authenticodeVerifier = authenticodeVerifier;
        _policy = policy;
        _timeProvider = timeProvider;
    }

    public async ValueTask<FileReviewResult> ReviewAsync(
        FileReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<ReviewEvidence> evidence = [];
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + ReviewTimeout;
        using CancellationTokenSource reviewCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reviewCancellation.CancelAfter(ReviewTimeout);

        try
        {
            FileReviewMetadata before = await _evidenceReader.ReadMetadataAsync(
                request.Path,
                reviewCancellation.Token);
            ReadOnlyMemory<byte> header = await _evidenceReader.ReadHeaderAsync(
                request.Path,
                MaximumHeaderBytes,
                reviewCancellation.Token);
            FileReviewMetadata after = await _evidenceReader.ReadMetadataAsync(
                request.Path,
                reviewCancellation.Token);
            if (before.Size != after.Size || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                return Incomplete(evidence, "file-changed-during-review");
            }

            FileKind headerKind = FileHeaderClassifier.Classify(header.Span);
            FileKind claimedKind = FileHeaderClassifier.ClassifyClaimedType(before.Path);
            FileKind kind = headerKind == FileKind.Unknown ? claimedKind : headerKind;
            bool executableContext = IsExecutableContext(kind, claimedKind);

            if (ContainsBidirectionalControl(before.Name))
            {
                AddEvidence(evidence, "name.bidi-control");
            }

            if (HasDoubleExtension(before.Name))
            {
                AddEvidence(evidence, "name.double-extension");
            }

            if (HasHeaderConflict(headerKind, claimedKind))
            {
                AddEvidence(evidence, "content.header-conflict");
            }

            if (HasUnusualPresentation(before.Name))
            {
                AddEvidence(evidence, "name.unusual-presentation");
            }

            if (executableContext)
            {
                AddEvidence(evidence, "category.executable-context");
                AddLocationEvidence(evidence, before.Path);
                if ((before.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    AddEvidence(evidence, "attributes.hidden-system");
                }
            }

            MarkOfTheWeb? origin = await _evidenceReader.ReadMarkOfTheWebAsync(
                request.Path,
                reviewCancellation.Token);
            if (origin?.ZoneId >= 3)
            {
                AddEvidence(evidence, "origin.mark-of-web");
                if (claimedKind == FileKind.MacroDocument)
                {
                    AddEvidence(evidence, "document.macro-with-origin");
                }
            }

            if (claimedKind == FileKind.Shortcut)
            {
                ShortcutMetadata? shortcut = await _evidenceReader.ReadShortcutAsync(
                    request.Path,
                    reviewCancellation.Token);
                if (shortcut is not null && ShortcutInspector.TargetsScriptHost(shortcut))
                {
                    AddEvidence(evidence, "shortcut.script-host");
                }
            }

            if (kind == FileKind.PortableExecutable)
            {
                TimeSpan remaining = deadline - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return Incomplete(evidence, "review-time-budget-expired", kind);
                }

                AuthenticodeResult signature = await _authenticodeVerifier.VerifyAsync(
                    request.Path,
                    remaining,
                    reviewCancellation.Token);
                if (signature.State == AuthenticodeState.TimedOut)
                {
                    return Incomplete(evidence, "signature-check-timed-out", kind);
                }

                AddEvidence(
                    evidence,
                    signature.State == AuthenticodeState.NoEmbeddedSignature
                        ? "signature.unsigned"
                        : signature.State == AuthenticodeState.IntegrityFailure
                            ? "signature.integrity-failure"
                            : null);
            }

            if (kind == FileKind.PortableExecutable &&
                before.Size >= MinimumEntropyFileSize &&
                (before.Size <= MaximumAutomaticEntropyFileSize || request.DeepReview))
            {
                double? entropy = await _evidenceReader.ReadEntropyAsync(
                    request.Path,
                    EntropySampleBytes,
                    MaximumEntropySamples,
                    reviewCancellation.Token);
                if (entropy >= 7.5)
                {
                    AddEvidence(evidence, "content.high-entropy");
                }
            }

            int score = evidence.Sum(item => item.Score);
            return new FileReviewResult(
                _policy.MapState(score),
                score,
                kind,
                evidence,
                true);
        }
        catch (OperationCanceledException)
        {
            return Incomplete(evidence, "review-canceled");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Incomplete(evidence, "required-evidence-unavailable");
        }
    }

    private void AddLocationEvidence(ICollection<ReviewEvidence> evidence, string path)
    {
        string normalized = path.Replace('/', '\\');
        if (normalized.Contains(
                "\\Start Menu\\Programs\\Startup\\",
                StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\", StringComparison.OrdinalIgnoreCase))
        {
            AddEvidence(evidence, "location.startup");
        }

        if (normalized.Contains("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("\\Windows\\Temp\\", StringComparison.OrdinalIgnoreCase))
        {
            AddEvidence(evidence, "location.temporary");
        }

        if (normalized.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase))
        {
            AddEvidence(evidence, "location.downloads");
        }
    }

    private void AddEvidence(ICollection<ReviewEvidence> evidence, string? ruleId)
    {
        if (ruleId is null)
        {
            return;
        }

        ReviewRule rule = _policy.GetRule(ruleId);
        evidence.Add(new ReviewEvidence(rule.Id, rule.Score, rule.Explanation, rule.Guidance));
    }

    private static bool IsExecutableContext(FileKind actual, FileKind claimed)
    {
        return actual is FileKind.PortableExecutable or FileKind.Script or FileKind.Installer ||
            claimed is FileKind.PortableExecutable or FileKind.Script or FileKind.Installer;
    }

    private static bool HasHeaderConflict(FileKind header, FileKind claimed)
    {
        if (header == FileKind.Unknown || claimed == FileKind.Unknown || header == claimed)
        {
            return false;
        }

        return !(header == FileKind.Archive && claimed == FileKind.MacroDocument);
    }

    private static bool ContainsBidirectionalControl(string name)
    {
        return name.Any(character => character is >= '\u202A' and <= '\u202E' or >= '\u2066' and <= '\u2069');
    }

    private static bool HasDoubleExtension(string name)
    {
        string finalExtension = Path.GetExtension(name);
        if (!ExecutableExtensions.Contains(finalExtension))
        {
            return false;
        }

        string withoutFinal = Path.GetFileNameWithoutExtension(name);
        if (FamiliarDocumentExtensions.Contains(Path.GetExtension(withoutFinal)))
        {
            return true;
        }

        int bidiIndex = withoutFinal.IndexOfAny(['\u202A', '\u202B', '\u202C', '\u202D', '\u202E']);
        return bidiIndex >= 0 && bidiIndex < withoutFinal.Length - 1;
    }

    private static bool HasUnusualPresentation(string name)
    {
        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            return true;
        }

        string baseName = Path.GetFileNameWithoutExtension(name).TrimEnd(' ', '.');
        return ReservedNames.Contains(baseName);
    }

    private static FileReviewResult Incomplete(
        IReadOnlyList<ReviewEvidence> evidence,
        string reason,
        FileKind kind = FileKind.Unknown)
    {
        return new FileReviewResult(
            ReviewState.Incomplete,
            evidence.Sum(item => item.Score),
            kind,
            evidence,
            false,
            reason);
    }
}
