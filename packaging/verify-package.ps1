[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Package,

    [string]$ExpectedVersion = '',

    [string]$ExpectedRevision = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packagePath = [System.IO.Path]::GetFullPath($Package)
$hashPath = "$packagePath.sha256"
if (-not (Test-Path -LiteralPath $packagePath)) { throw "Package not found: $packagePath" }
if (-not (Test-Path -LiteralPath $hashPath)) { throw "Checksum not found: $hashPath" }

$expectedHash = ((Get-Content -LiteralPath $hashPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) { throw 'SHA-256 verification failed.' }

$temporaryRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ("Qsearch-verify-" + [guid]::NewGuid().ToString('N'))))
$expectedPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\Qsearch-verify-'
if (-not $temporaryRoot.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe verification path: $temporaryRoot"
}

New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        foreach ($entry in $archive.Entries) {
            $destination = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot $entry.FullName))
            if (-not $destination.StartsWith($temporaryRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe ZIP entry: $($entry.FullName)"
            }
        }
    }
    finally { $archive.Dispose() }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $temporaryRoot)

    $required = @(
        'Qsearch.exe',
        'Qsearch.ElevatedIndexer.exe',
        'qsearch.ico',
        'qsearch-dark.ico',
        'qsearch-light.ico',
        'LICENSE',
        'THIRD-PARTY-NOTICES.md',
        'licenses\Microsoft.Data.Sqlite\MIT.txt',
        'licenses\SQLitePCLRaw\Apache-2.0.txt',
        'licenses\FontAwesome-Free\LICENSE.txt',
        'licenses\FontAwesome-Free\ATTRIBUTION.md',
        'PRIVACY.md',
        'THREAT-MODEL.md',
        'VERSION.json',
        'UNSIGNED.txt',
        'PRIVACY-AUDIT.json'
    )
    foreach ($file in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $temporaryRoot $file))) {
            throw "Required package file is missing: $file"
        }
        if ((Get-Item -LiteralPath (Join-Path $temporaryRoot $file)).Length -eq 0) {
            throw "Required package file is empty: $file"
        }
    }

    $notices = Get-Content -LiteralPath (Join-Path $temporaryRoot 'THIRD-PARTY-NOTICES.md') -Raw
    foreach ($component in 'Microsoft.Data.Sqlite 10.0.15', 'SQLitePCLRaw 2.1.13', 'Font Awesome Free 7.3.1') {
        if (-not $notices.Contains($component, [System.StringComparison]::Ordinal)) {
            throw "Third-party notice is missing: $component"
        }
    }

    $bundledFont = Get-ChildItem -LiteralPath $temporaryRoot -File -Recurse |
        Where-Object { $_.Name -match 'Modam|Tahrir|Farhang2' -and $_.Extension -in '.ttf', '.otf' }
    if ($bundledFont) { throw 'A proprietary font binary was bundled without release approval.' }

    $metadata = Get-Content -LiteralPath (Join-Path $temporaryRoot 'VERSION.json') -Raw | ConvertFrom-Json
    if ($metadata.product -ne 'Qsearch') { throw 'Unexpected product metadata.' }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
        $metadata.version -ne $ExpectedVersion) {
        throw "Package version metadata does not match the expected version: $ExpectedVersion"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRevision)) {
        if ($ExpectedRevision -notmatch '^[0-9a-fA-F]{40}$') {
            throw 'ExpectedRevision must be a full 40-character Git commit SHA.'
        }
        if ($metadata.commit -ne $ExpectedRevision) {
            throw "Package source revision does not match the expected revision: $ExpectedRevision"
        }
    }
    if ($metadata.runtimeIdentifier -ne 'win-x64') { throw 'Unexpected runtime identifier.' }
    if ($metadata.singleFile -ne $true) { throw 'Package metadata must state singleFile=true.' }
    if ($metadata.selfContained -isnot [bool]) { throw 'Package metadata must declare selfContained.' }
    if ($metadata.signed -ne $false) { throw 'Development package metadata must state signed=false.' }
    if ($metadata.appIcon -ne 'qsearch.ico') { throw 'Package metadata must identify qsearch.ico.' }
    if ($metadata.selfContained -eq $false) {
        if ($metadata.packageKind -ne 'slim') { throw 'Framework-dependent metadata must state packageKind=slim.' }
        if ($metadata.runtimeRequirement.framework -ne 'Microsoft.WindowsDesktop.App') {
            throw 'Slim package must require Microsoft.WindowsDesktop.App.'
        }
        if ([string]::IsNullOrWhiteSpace($metadata.runtimeRequirement.minimumVersion)) {
            throw 'Slim package must declare a minimum desktop runtime version.'
        }
    } else {
        if ($null -ne $metadata.runtimeRequirement) {
            throw 'A self-contained package must not declare an external runtimeRequirement.'
        }
        foreach ($file in @(
            'licenses\dotnet-runtime\LICENSE.TXT',
            'licenses\dotnet-runtime\THIRD-PARTY-NOTICES.TXT',
            'licenses\windows-desktop-runtime\LICENSE')) {
            $path = Join-Path $temporaryRoot $file
            if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
                throw "Required runtime legal file is missing or empty: $file"
            }
        }
    }
    Add-Type -AssemblyName System.Reflection.Metadata
    foreach ($executable in 'Qsearch.exe', 'Qsearch.ElevatedIndexer.exe') {
        $fileStream = [System.IO.File]::OpenRead((Join-Path $temporaryRoot $executable))
        try {
            $peReader = [System.Reflection.PortableExecutable.PEReader]::new($fileStream)
            try {
                if ($peReader.PEHeaders.PEHeader.CertificateTableDirectory.Size -ne 0) {
                    throw "Unexpected embedded signature table in ${executable}."
                }
            }
            finally { $peReader.Dispose() }
        }
        finally { $fileStream.Dispose() }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new(
        (Join-Path $temporaryRoot 'Qsearch.exe'))
    $startInfo.ArgumentList.Add('--diagnostics-offline-smoke')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit(15000)) {
            $process.Kill($true)
            throw 'Offline startup smoke timed out.'
        }
        if ($process.ExitCode -ne 0) {
            throw "Offline startup smoke failed with exit code $($process.ExitCode)."
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Output "SHA-256: valid ($actualHash)"
    Write-Output 'Required files: valid'
    if ($metadata.selfContained) {
        Write-Output 'Runtime contract: self-contained portable'
    } else {
        Write-Output "Runtime contract: $($metadata.runtimeRequirement.framework) >= $($metadata.runtimeRequirement.minimumVersion) ($($metadata.runtimeRequirement.architecture))"
    }
    Write-Output 'Runtime network surface: 0 audited clients/endpoints; offline startup passed'
    Write-Output 'Font policy: no proprietary font binary bundled'
    Write-Output 'Signing status: unsigned development artifact'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
