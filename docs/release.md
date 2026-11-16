# Release checklist and GitHub handoff

## Preview release

The current public candidate is `v0.1.0-preview.1`. It is an unsigned Windows
x64 prerelease for evaluation, not a trusted or signed production build. The
release contains only extracted ZIP packages; no installer is published.

Canonical repository: `https://github.com/qa3me/qsearch`
Release tag: `v0.1.0-preview.1`
Default branch: `main`

## Candidate verification

Run the complete source and package checks from the final public revision:

```powershell
dotnet restore Qsearch.slnx
dotnet format Qsearch.slnx --verify-no-changes --no-restore
dotnet build Qsearch.slnx -c Release --no-restore -warnaserror
dotnet test Qsearch.slnx -c Release --no-build
dotnet run --project src/Qsearch.Benchmarks/Qsearch.Benchmarks.csproj -c Release -- --smoke --output work/benchmarks/smoke.json

$revision = git rev-parse HEAD
pwsh -NoProfile -File packaging/build-portable.ps1 -Version 0.1.0-preview.1 -OutputDirectory outputs -SourceRevision $revision
pwsh -NoProfile -File packaging/verify-package.ps1 -Package outputs/Qsearch-0.1.0-preview.1-win-x64-portable.zip -ExpectedVersion 0.1.0-preview.1 -ExpectedRevision $revision
pwsh -NoProfile -File packaging/build-slim.ps1 -Version 0.1.0-preview.1 -OutputDirectory outputs -SourceRevision $revision
pwsh -NoProfile -File packaging/verify-package.ps1 -Package outputs/Qsearch-0.1.0-preview.1-win-x64-slim.zip -ExpectedVersion 0.1.0-preview.1 -ExpectedRevision $revision

$env:QSEARCH_TEST_PACKAGE = (Resolve-Path outputs/Qsearch-0.1.0-preview.1-win-x64-portable.zip)
$env:QSEARCH_TEST_SLIM_PACKAGE = (Resolve-Path outputs/Qsearch-0.1.0-preview.1-win-x64-slim.zip)
dotnet test tests/Qsearch.Architecture.Tests/Qsearch.Architecture.Tests.csproj -c Release --no-build
```

Confirm that both adjacent `.sha256` files match their ZIPs and that every
package contains `UNSIGNED.txt`, `VERSION.json`, `PRIVACY-AUDIT.json`, the
privacy/security documents, and all required third-party notices. Private
Modam, Farhang2, and Tahrir font binaries must never be present.

## Manual preview matrix

Before promoting a later build beyond preview status, complete Windows 10
22H2 and current Windows 11 checks at 100/150/200% scaling, English/Persian,
keyboard and high-contrast use, tray and shortcut conflicts, compact mode,
same-account and alternate-administrator UAC, offline operation, corrupt-index
recovery, and clean-machine SmartScreen behavior.

The preview may be published while this matrix is incomplete only when the
release remains marked **Prerelease** and its unsigned status and limitations
are stated plainly.

## GitHub release contract

Publish exactly these four assets:

- `Qsearch-0.1.0-preview.1-win-x64-portable.zip`
- `Qsearch-0.1.0-preview.1-win-x64-portable.zip.sha256`
- `Qsearch-0.1.0-preview.1-win-x64-slim.zip`
- `Qsearch-0.1.0-preview.1-win-x64-slim.zip.sha256`

Use `docs/releases/v0.1.0-preview.1.md` as the bilingual release notes. Keep
the GitHub release marked as a prerelease. Do not attach an installer, private
fonts, debug symbols, source planning files, credentials, or signing material.
