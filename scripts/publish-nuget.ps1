<#
.SYNOPSIS
    Pack, sign, and push AssistStudio NuGet packages (Providers + Core + Controls).
.EXAMPLE
    .\publish-nuget.ps1                      # full: pack → sign → push
    .\publish-nuget.ps1 -SkipPush            # pack → sign only
    .\publish-nuget.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\Ai.Providers\Ai.Providers.csproj',
        'src\AssistStudio.Core\AssistStudio.Core.csproj',
        'src\AssistStudio.Controls\AssistStudio.Controls.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
