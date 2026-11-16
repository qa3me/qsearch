[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [string]$SourceRevision = '',

    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$packageWorkRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'work\package'))
$versionWorkRoot = [System.IO.Path]::GetFullPath((Join-Path $packageWorkRoot "$Version-slim"))
$stageRoot = Join-Path $versionWorkRoot 'stage'
$appPublish = Join-Path $versionWorkRoot 'app'
$helperPublish = Join-Path $versionWorkRoot 'helper'

function Assert-ChildPath {
    param([string]$Path, [string]$Parent)
    $parentPrefix = $Parent.TrimEnd('\') + '\'
    if (-not $Path.StartsWith($parentPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe package work path: $Path"
    }
}

Assert-ChildPath -Path $versionWorkRoot -Parent $packageWorkRoot
if (Test-Path -LiteralPath $versionWorkRoot) {
    Remove-Item -LiteralPath $versionWorkRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$dotnet = if ($env:DOTNET_ROOT -and (Test-Path (Join-Path $env:DOTNET_ROOT 'dotnet.exe'))) {
    Join-Path $env:DOTNET_ROOT 'dotnet.exe'
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$commonPublish = @(
    '-c', $Configuration,
    '-r', 'win-x64',
    '--self-contained', 'false',
    '--no-restore',
    '-m:1',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:QsearchPackageAppOnly=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:ContinuousIntegrationBuild=true',
    "-p:QsearchOutputRoot=$(Join-Path $versionWorkRoot 'build')",
    "-p:Version=$Version"
)

$appProject = Join-Path $repositoryRoot 'src\Qsearch.App\Qsearch.App.csproj'
$helperProject = Join-Path $repositoryRoot 'src\Qsearch.ElevatedIndexer\Qsearch.ElevatedIndexer.csproj'
$restoreConfig = Join-Path $repositoryRoot 'NuGet.Config'
if (-not $SkipRestore) {
    foreach ($project in $appProject, $helperProject) {
        & $dotnet restore $project `
            --configfile $restoreConfig `
            --runtime win-x64 `
            --disable-parallel `
            -p:SelfContained=false `
            -p:NuGetAudit=false `
            -v:minimal
        if ($LASTEXITCODE -ne 0) { throw "Runtime restore failed: $project" }
    }
}

& $dotnet publish $appProject @commonPublish -o $appPublish
if ($LASTEXITCODE -ne 0) { throw 'Qsearch app publish failed.' }
& $dotnet publish $helperProject @commonPublish -o $helperPublish
if ($LASTEXITCODE -ne 0) { throw 'Qsearch helper publish failed.' }

$appExe = Join-Path $appPublish 'Qsearch.exe'
$helperExe = Join-Path $helperPublish 'Qsearch.ElevatedIndexer.exe'
if (-not (Test-Path -LiteralPath $appExe)) { throw 'Published Qsearch.exe was not found.' }
if (-not (Test-Path -LiteralPath $helperExe)) { throw 'Published helper was not found.' }
Copy-Item -LiteralPath $appExe -Destination $stageRoot
Copy-Item -LiteralPath $helperExe -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets\brand\qsearch.ico') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets\brand\qsearch-dark.ico') -Destination $stageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'assets\brand\qsearch-light.ico') -Destination $stageRoot

$documents = @(
    'LICENSE',
    'ASSETS-NOTICE.md',
    'PRIVACY.md',
    'SECURITY.md',
    'THREAT-MODEL.md',
    'README.md',
    'README.fa.md'
)
foreach ($document in $documents) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $document) -Destination $stageRoot
}
& (Join-Path $PSScriptRoot 'stage-legal-files.ps1') -Destination $stageRoot

$sourceRevision = $SourceRevision
if ([string]::IsNullOrWhiteSpace($sourceRevision)) {
    $sourceRevision = (& git -C $repositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $sourceRevision = 'source-archive' }
}
elseif ($sourceRevision -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'SourceRevision must be a full 40-character Git commit SHA.'
}
$runtimeRequirement = [ordered]@{
    framework = 'Microsoft.WindowsDesktop.App'
    minimumVersion = '10.0.0'
    architecture = 'x64'
    displayName = '.NET 10 Desktop Runtime for Windows x64'
}
$metadata = [ordered]@{
    product = 'Qsearch'
    version = $Version
    runtimeIdentifier = 'win-x64'
    packageKind = 'slim'
    selfContained = $false
    singleFile = $true
    runtimeRequirement = $runtimeRequirement
    appIcon = 'qsearch.ico'
    signed = $false
    commit = $sourceRevision.Trim()
}
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stageRoot 'VERSION.json') -Encoding utf8
@'
This is an unsigned development artifact.
Install the .NET 10 Desktop Runtime for Windows x64 before running this slim package.
Verify the adjacent SHA-256 file before use. Public release signing requires the owner's certificate and is not performed by this script.
'@ | Set-Content -LiteralPath (Join-Path $stageRoot 'UNSIGNED.txt') -Encoding utf8

$privacyAudit = [ordered]@{
    runtimeNetworkClientReferences = 0
    remoteRuntimeEndpoints = 0
    telemetryPackages = 0
    evidence = 'Qsearch.Architecture.Tests'
}
$privacyAudit | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stageRoot 'PRIVACY-AUDIT.json') -Encoding utf8

$fixedTime = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
$archiveName = "Qsearch-$Version-win-x64-slim.zip"
$archivePath = Join-Path $outputRoot $archiveName
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [System.IO.File]::Open($archivePath, [System.IO.FileMode]::CreateNew)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $stream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $files = Get-ChildItem -LiteralPath $stageRoot -File -Recurse | Sort-Object FullName
        foreach ($file in $files) {
            $entryName = $file.FullName.Substring($stageRoot.TrimEnd('\').Length).TrimStart('\').Replace('\', '/')
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTime
            $entryStream = $entry.Open()
            $fileStream = $file.OpenRead()
            try { $fileStream.CopyTo($entryStream) }
            finally { $fileStream.Dispose(); $entryStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
}
finally { $stream.Dispose() }

$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$archivePath.sha256"
"$hash  $archiveName" | Set-Content -LiteralPath $hashPath -Encoding ascii
Write-Output $archivePath
Write-Output $hashPath
