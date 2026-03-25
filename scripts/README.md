# Scripts — Maintainers Only

## Publish Scripts

Two separate scripts for independent release cycles. Both share common logic from `nuget-common.ps1`.

### publish-nuget.ps1 — AssistStudio (Core + Controls)

```powershell
.\scripts\publish-nuget.ps1 -NuGetApiKey <key>   # pack → sign → push
.\scripts\publish-nuget.ps1 -SkipPush             # pack → sign only
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush   # pack only
```

| PackageId | Project |
|---|---|
| `FieldCure.AssistStudio.Core` | src/AssistStudio.Core |
| `FieldCure.AssistStudio.Controls.WinUI` | src/AssistStudio.Controls |

### publish-parsers.ps1 — DocumentParsers

```powershell
.\scripts\publish-parsers.ps1 -NuGetApiKey <key>   # pack → sign → push
.\scripts\publish-parsers.ps1 -SkipPush             # pack → sign only
.\scripts\publish-parsers.ps1 -SkipSign -SkipPush   # pack only
```

| PackageId | Project |
|---|---|
| `FieldCure.DocumentParsers` | src/DocumentParsers |
| `FieldCure.DocumentParsers.Pdf` | src/DocumentParsers.Pdf |

### Prerequisites

- GlobalSign EV code signing USB dongle connected
- NuGet.org API Key ([nuget.org/account/apikeys](https://www.nuget.org/account/apikeys))
- Alternatively, set `$env:NUGET_API_KEY` instead of passing `-NuGetApiKey`

### Signing Certificate

- **Issuer**: GlobalSign
- **Subject**: Fieldcure Co., Ltd.
- **Method**: USB token (EV Code Signing)
- **Timestamp**: GlobalSign TSA

### Output

Built `.nupkg` files are placed in the `artifacts/` folder.
