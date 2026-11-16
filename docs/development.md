# Development

## Requirements

- Windows 10/11 x64
- .NET SDK 10
- PowerShell 7+
- Git

## Build

```powershell
dotnet restore Qsearch.slnx
dotnet build Qsearch.slnx -c Debug --no-restore
dotnet test Qsearch.slnx -c Debug --no-build
```

For release checks:

```powershell
dotnet format Qsearch.slnx --verify-no-changes
dotnet build Qsearch.slnx -c Release --no-restore -warnaserror
dotnet test Qsearch.slnx -c Release --no-build
```

Run the app with `dotnet run --project src/Qsearch.App/Qsearch.App.csproj`. No folder is indexed until one is selected in the UI. Development data is under `%LOCALAPPDATA%\Qsearch`.

## Tests

- Core: normalization and query contracts.
- Index: migration, atomic batches/checkpoints, ranking, filters, recovery.
- Windows: crawl, USN parsing, helper protocol, single-instance and hotkey lifecycle.
- Safety: exact policy thresholds, bounded reads, incomplete states, no mutation/network surface.
- App: cancellation, localization parity, hostile names, virtualization, widget sharing.
- Architecture: offline boundary, project direction, recovery, docs and packaging.

The native NTFS integration test is opt-in because it needs a compatible local NTFS volume. Set `QSEARCH_RUN_USN_INTEGRATION=1` only on a test machine; it performs read-only journal calls.

## Repository rules

Runtime code must not add network or telemetry dependencies. Keep every Persian/English resource key and placeholder in parity. Do not commit `bin`, `obj`, `work`, `outputs`, databases, packages, certificates, keys, or proprietary font binaries.
