<#
.SYNOPSIS
    Pack, sign, and push DocumentParsers NuGet package.
.EXAMPLE
    .\publish-parsers.ps1                      # full: pack → sign → push
    .\publish-parsers.ps1 -SkipPush            # pack → sign only
    .\publish-parsers.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\DocumentParsers\DocumentParsers.csproj',
        'src\DocumentParsers.Pdf\DocumentParsers.Pdf.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
