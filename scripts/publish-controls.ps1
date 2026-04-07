<#
.SYNOPSIS
    Pack, sign, and push FieldCure.AssistStudio.Controls.WinUI NuGet package.
.EXAMPLE
    .\publish-controls.ps1                      # full: pack → sign → push
    .\publish-controls.ps1 -SkipPush            # pack → sign only
    .\publish-controls.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\AssistStudio.Controls\AssistStudio.Controls.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
