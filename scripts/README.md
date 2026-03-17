# Scripts — 관리자 전용

## publish-nuget.ps1

NuGet 패키지 빌드, 코드 서명, 배포를 자동화하는 스크립트입니다.

### 사전 준비

- GlobalSign EV 코드 서명 USB 동글 연결
- NuGet.org API Key ([nuget.org/account/apikeys](https://www.nuget.org/account/apikeys)에서 발급)

### 사용법

```powershell
# pack → sign → push (전체)
.\scripts\publish-nuget.ps1 -NuGetApiKey <key>

# 환경변수로 API Key 설정 후 실행
$env:NUGET_API_KEY = '<key>'
.\scripts\publish-nuget.ps1

# pack → sign만 (배포 전 확인용)
.\scripts\publish-nuget.ps1 -SkipPush

# pack만 (빌드 확인, USB 동글 없이 가능)
.\scripts\publish-nuget.ps1 -SkipSign -SkipPush
```

### 대상 패키지

| PackageId | 프로젝트 |
|---|---|
| `FieldCure.AssistStudio.Core` | src/AssistStudio.Core |
| `FieldCure.AssistStudio.Controls.WinUI` | src/AssistStudio.Controls |

### 서명 인증서

- **발급자**: GlobalSign
- **주체**: Fieldcure Co., Ltd.
- **방식**: USB 토큰 (EV Code Signing)
- **타임스탬프**: GlobalSign TSA

### 출력

빌드된 `.nupkg` 파일은 `artifacts/` 폴더에 생성됩니다.
