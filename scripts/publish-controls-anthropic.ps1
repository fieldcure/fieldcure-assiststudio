<#
.SYNOPSIS
    Pack, sign, and push FieldCure.AssistStudio.Controls.WinUI.Anthropic NuGet package.
.EXAMPLE
    .\publish-controls-anthropic.ps1                      # full: pack -> sign -> push
    .\publish-controls-anthropic.ps1 -SkipPush            # pack -> sign only
    .\publish-controls-anthropic.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\AssistStudio.Controls.Anthropic\AssistStudio.Controls.Anthropic.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
