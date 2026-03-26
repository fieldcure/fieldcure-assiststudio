<#
.SYNOPSIS
    Shared NuGet pack/sign/push logic. Dot-sourced by publish scripts.
.DESCRIPTION
    Provides Invoke-NuGetPublish function that handles:
    1. dotnet pack (Release)
    2. nuget sign with GlobalSign EV certificate
    3. nuget push to nuget.org
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Shared Config ---
$script:RepoRoot = Split-Path $PSScriptRoot -Parent
$script:CertFingerprint = 'FB343073EF0D477E64595A66FFB87AC631278C4B43D2CC89C56BCDF3B5BF8826'
$script:TimestampUrl = 'http://timestamp.globalsign.com/tsa/r6advanced1'
$script:NuGetSource = 'https://api.nuget.org/v3/index.json'

function Invoke-NuGetPublish {
    param(
        [string[]]$Projects,
        [switch]$SkipSign,
        [switch]$SkipPush,
        [string]$NuGetApiKey
    )

    $OutputDir = Join-Path $script:RepoRoot 'artifacts'

    # --- Clean artifacts ---
    if (Test-Path $OutputDir) {
        Remove-Item $OutputDir -Recurse -Force
    }
    New-Item $OutputDir -ItemType Directory | Out-Null

    # --- 1. Clean + Pack ---
    Write-Host "`n=== Packing (clean build) ===" -ForegroundColor Cyan
    foreach ($proj in $Projects) {
        $projPath = Join-Path $script:RepoRoot $proj
        Write-Host "  Cleaning $proj ..."
        dotnet clean $projPath -c Release --nologo -v q
        Write-Host "  Packing $proj ..."
        dotnet pack $projPath -c Release -o $OutputDir
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Pack failed for $proj"
        }
    }

    $packages = Get-ChildItem $OutputDir -Filter '*.nupkg'
    Write-Host "`n  Packages created:" -ForegroundColor Green
    $packages | ForEach-Object { Write-Host "    $_" }

    # --- 2. Sign ---
    if (-not $SkipSign) {
        Write-Host "`n=== Signing (USB dongle must be plugged in) ===" -ForegroundColor Cyan

        $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
            $_.Subject -like '*Fieldcure Co., Ltd.*' -and $_.NotAfter -gt (Get-Date)
        }
        if (-not $cert) {
            Write-Error "Fieldcure code signing certificate not found. Is the USB dongle plugged in?"
        }
        Write-Host "  Using certificate: $($cert.Subject)"

        foreach ($pkg in $packages) {
            Write-Host "  Signing $($pkg.Name) ..."
            dotnet nuget sign $pkg.FullName `
                --certificate-fingerprint $script:CertFingerprint `
                --timestamper $script:TimestampUrl `
                --overwrite
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Signing failed for $($pkg.Name)"
            }
        }
        Write-Host "  All packages signed." -ForegroundColor Green
    }

    # --- 3. Push ---
    if (-not $SkipPush) {
        Write-Host "`n=== Pushing to nuget.org ===" -ForegroundColor Cyan

        if (-not $NuGetApiKey) {
            $NuGetApiKey = $env:NUGET_API_KEY
        }
        if (-not $NuGetApiKey) {
            Write-Error "NuGet API key required. Pass -NuGetApiKey or set NUGET_API_KEY env var."
        }

        foreach ($pkg in $packages) {
            Write-Host "  Pushing $($pkg.Name) ..."
            dotnet nuget push $pkg.FullName `
                --api-key $NuGetApiKey `
                --source $script:NuGetSource `
                --skip-duplicate
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Push failed for $($pkg.Name)"
            }
        }
        Write-Host "  All packages pushed." -ForegroundColor Green
    }

    Write-Host "`n=== Done ===" -ForegroundColor Cyan
    Get-ChildItem $OutputDir -Filter '*.nupkg' | ForEach-Object {
        Write-Host "  $($_.Name)  ($([math]::Round($_.Length / 1KB, 1)) KB)"
    }
}
