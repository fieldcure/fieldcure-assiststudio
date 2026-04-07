<#
.SYNOPSIS
    Pack, sign, and push FieldCure.AssistStudio.Core NuGet package.
.EXAMPLE
    .\publish-core.ps1                      # full: pack → sign → push
    .\publish-core.ps1 -SkipPush            # pack → sign only
    .\publish-core.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\AssistStudio.Core\AssistStudio.Core.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
