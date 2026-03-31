<#
.SYNOPSIS
    Pack, sign, and push FieldCure.Ai.Providers NuGet package.
.EXAMPLE
    .\publish-providers.ps1                      # full: pack → sign → push
    .\publish-providers.ps1 -SkipPush            # pack → sign only
    .\publish-providers.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\Ai.Providers\Ai.Providers.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
