# Scripts — Maintainers Only

## publish-nuget.ps1

Automates NuGet package build, code signing, and publishing.

### Prerequisites

- GlobalSign EV code signing USB dongle connected
- NuGet.org API Key (issue one at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys))

### Usage

```powershell
# pack → sign → push (full pipeline)
.\scripts\publish-nuget.ps1 -NuGetApiKey <key>

# Set API key via environment variable
$env:NUGET_API_KEY = '<key>'
.\scripts\publish-nuget.ps1

# pack → sign only (pre-publish verification)
.\scripts\publish-nuget.ps1 -SkipPush

# pack only (build check, no USB dongle needed)
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush
```

### Target Packages

| PackageId | Project |
|---|---|
| `FieldCure.AssistStudio.Core` | src/AssistStudio.Core |
| `FieldCure.AssistStudio.Controls.WinUI` | src/AssistStudio.Controls |
| `FieldCure.DocumentParsers` | src/DocumentParsers |

### Signing Certificate

- **Issuer**: GlobalSign
- **Subject**: Fieldcure Co., Ltd.
- **Method**: USB token (EV Code Signing)
- **Timestamp**: GlobalSign TSA

### Output

Built `.nupkg` files are placed in the `artifacts/` folder.
