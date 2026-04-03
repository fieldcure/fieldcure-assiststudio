<#
.SYNOPSIS
    Pack, sign, and push FieldCure.Ai.Execution NuGet package.
.EXAMPLE
    .\publish-execution.ps1                      # full: pack → sign → push
    .\publish-execution.ps1 -SkipPush            # pack → sign only
    .\publish-execution.ps1 -SkipSign -SkipPush  # pack only (testing)
#>
param(
    [switch]$SkipSign,
    [switch]$SkipPush,
    [string]$NuGetApiKey
)

. "$PSScriptRoot\nuget-common.ps1"

Invoke-NuGetPublish `
    -Projects @(
        'src\Ai.Execution\Ai.Execution.csproj'
    ) `
    -SkipSign:$SkipSign `
    -SkipPush:$SkipPush `
    -NuGetApiKey $NuGetApiKey
