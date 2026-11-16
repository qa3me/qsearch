# Packaging and signing

Qsearch `v0.1.0-preview.1` is distributed as two deterministic, unsigned
Windows x64 ZIP packages. Both include the main application, the separately
elevated read-only helper, the Qsearch icon, legal/privacy documents,
`VERSION.json`, `UNSIGNED.txt`, `PRIVACY-AUDIT.json`, and an adjacent
SHA-256 file.

## Slim package

```powershell
$revision = git rev-parse HEAD
pwsh -NoProfile -File packaging/build-slim.ps1 -Version 0.1.0-preview.1 -OutputDirectory outputs -SourceRevision $revision
pwsh -NoProfile -File packaging/verify-package.ps1 -Package outputs/Qsearch-0.1.0-preview.1-win-x64-slim.zip -ExpectedVersion 0.1.0-preview.1 -ExpectedRevision $revision
```

Slim is framework-dependent and uses single-file hosts. It requires the
**.NET 10 Desktop Runtime for Windows x64** (`Microsoft.WindowsDesktop.App`,
minimum `10.0.0`) to be installed. Qsearch records this dependency in
`VERSION.json` and never downloads it at runtime.

## Self-contained portable package

```powershell
$revision = git rev-parse HEAD
pwsh -NoProfile -File packaging/build-portable.ps1 -Version 0.1.0-preview.1 -OutputDirectory outputs -SourceRevision $revision
pwsh -NoProfile -File packaging/verify-package.ps1 -Package outputs/Qsearch-0.1.0-preview.1-win-x64-portable.zip -ExpectedVersion 0.1.0-preview.1 -ExpectedRevision $revision
```

Portable publishes self-contained, single-file `win-x64` executables and
does not require a separately installed .NET runtime. It is larger than Slim.

Both builders accept `-Configuration`; release automation uses `Release`.
ZIP entries have a deterministic order and timestamp. The checksum changes
when binaries, documents, version, or source revision metadata changes.

The verifier checks the SHA-256 file, safe ZIP paths, runtime contract,
required documents and icon, third-party notices, absence of private Modam,
Farhang2, and Tahrir font binaries, unsigned metadata and executable certificate
tables, then runs
the packaged offline startup/search smoke.

## Unsigned preview

The preview archives are intentionally unsigned and may trigger an
unknown-publisher or SmartScreen warning. They may be published only as a
clearly labelled GitHub **Prerelease** with their SHA-256 files. They must not
be described as signed, trusted, or production-ready.

No installer is built or attached for this preview. Run Qsearch by extracting
one ZIP and launching `Qsearch.exe`.

## Future signed release

Never commit a PFX, certificate password, hardware-token credential, encoded
secret, or private key. For a future owner-approved signed release:

1. Build and verify both candidates from the final tagged revision.
2. Sign `Qsearch.exe` and `Qsearch.ElevatedIndexer.exe` with the owner's
   trusted code-signing certificate and timestamp policy.
3. Verify every signature, rebuild the ZIP/checksum without altering the
   signed binaries, and set `signed: true` only after successful verification.
4. Repeat clean-machine SmartScreen, UAC, offline, runtime-present, and
   runtime-missing checks.

The Inno Setup recipe `packaging/Qsearch.iss` remains development scaffolding
for a later signed installer and is not part of `v0.1.0-preview.1`.
