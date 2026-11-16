[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination,

    [switch]$IncludeRuntimeNotices,

    [string]$DotnetPath = 'dotnet',

    [string]$RuntimeConfigPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destinationRoot = [System.IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null

function Copy-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$RelativeDestination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Required legal file was not found: $Source"
    }

    $target = Join-Path $destinationRoot $RelativeDestination
    $targetDirectory = Split-Path -Parent $target
    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $target -Force
    if ((Get-Item -LiteralPath $target).Length -eq 0) {
        throw "Required legal file is empty: $target"
    }
}

$commonFiles = @(
    @('THIRD-PARTY-NOTICES.md', 'THIRD-PARTY-NOTICES.md'),
    @('licenses\Microsoft.Data.Sqlite\MIT.txt', 'licenses\Microsoft.Data.Sqlite\MIT.txt'),
    @('licenses\SQLitePCLRaw\Apache-2.0.txt', 'licenses\SQLitePCLRaw\Apache-2.0.txt'),
    @('assets\fontawesome\LICENSE.txt', 'licenses\FontAwesome-Free\LICENSE.txt'),
    @('assets\fontawesome\ATTRIBUTION.md', 'licenses\FontAwesome-Free\ATTRIBUTION.md')
)
foreach ($mapping in $commonFiles) {
    Copy-RequiredFile -Source (Join-Path $repositoryRoot $mapping[0]) -RelativeDestination $mapping[1]
}

if ($IncludeRuntimeNotices) {
    if ([string]::IsNullOrWhiteSpace($RuntimeConfigPath) -or
        -not (Test-Path -LiteralPath $RuntimeConfigPath -PathType Leaf)) {
        throw 'A published runtimeconfig.json is required when staging runtime notices.'
    }

    $nugetRoot = if ($env:NUGET_PACKAGES) {
        [System.IO.Path]::GetFullPath($env:NUGET_PACKAGES)
    } else {
        $line = (& $DotnetPath nuget locals global-packages --list | Select-Object -First 1)
        if ($LASTEXITCODE -ne 0 -or $line -notmatch ':\s*(.+)$') {
            throw 'Unable to resolve the NuGet global-packages directory.'
        }
        [System.IO.Path]::GetFullPath($Matches[1].Trim())
    }

    $runtimeConfig = Get-Content -LiteralPath $RuntimeConfigPath -Raw | ConvertFrom-Json
    $includedFrameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    function Get-IncludedFrameworkVersion {
        param([Parameter(Mandatory = $true)][string]$Name)

        $framework = @($includedFrameworks | Where-Object { $_.name -eq $Name })
        if ($framework.Count -ne 1 -or [string]::IsNullOrWhiteSpace($framework[0].version)) {
            throw "Published runtime config does not identify exactly one $Name version."
        }
        return [string]$framework[0].version
    }

    $netCoreVersion = Get-IncludedFrameworkVersion -Name 'Microsoft.NETCore.App'
    $windowsDesktopVersion = Get-IncludedFrameworkVersion -Name 'Microsoft.WindowsDesktop.App'
    $netCoreRuntime = Join-Path $nugetRoot "microsoft.netcore.app.runtime.win-x64\$($netCoreVersion.ToLowerInvariant())"
    $windowsDesktopRuntime = Join-Path $nugetRoot "microsoft.windowsdesktop.app.runtime.win-x64\$($windowsDesktopVersion.ToLowerInvariant())"
    Copy-RequiredFile -Source (Join-Path $netCoreRuntime 'LICENSE.TXT') -RelativeDestination 'licenses\dotnet-runtime\LICENSE.TXT'
    Copy-RequiredFile -Source (Join-Path $netCoreRuntime 'THIRD-PARTY-NOTICES.TXT') -RelativeDestination 'licenses\dotnet-runtime\THIRD-PARTY-NOTICES.TXT'
    Copy-RequiredFile -Source (Join-Path $windowsDesktopRuntime 'LICENSE') -RelativeDestination 'licenses\windows-desktop-runtime\LICENSE'
}
