<#
.SYNOPSIS
    Pack, sign, and push FieldCure.AssistStudio.Anthropic NuGet package.
.EXAMPLE
    .\publish-anthropic.ps1                      # full: pack -> sign -> push
    .\publish-anthropic.ps1 -SkipPush            # pack -> sign only
    .\publish-anthropic.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\AssistStudio.Anthropic\AssistStudio.Anthropic.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
