# FieldCure.AssistStudio 구현 계획서

> 최종 갱신: 2026-03-14

---

## 1. 프로젝트 개요

WinUI 3 기반 AI Chat 컨트롤 라이브러리. 2개의 NuGet 패키지 + 1개의 데모 앱으로 구성.

### 솔루션 구조

```
FieldCure.AssistStudio.slnx
src/
├── AssistStudio.Core/           → NuGet: FieldCure.AssistStudio.Core (net8.0)
│   ├── Models/                  (FieldCure.AssistStudio.Models)
│   ├── Providers/               (FieldCure.AssistStudio.Providers)
│   └── Helpers/                 (FieldCure.AssistStudio.Helpers)
│
├── AssistStudio.Controls/       → NuGet: FieldCure.AssistStudio.Controls.WinUI
│   │                             (net8.0-windows10.0.19041.0 + net9.0-windows10.0.19041.0)
│   ├── Controls/                (FieldCure.AssistStudio.Controls) — TemplatedControls
│   ├── Helpers/                 (FieldCure.AssistStudio.Helpers)
│   ├── Rendering/               (FieldCure.AssistStudio.Rendering)
│   ├── Themes/Generic.xaml
│   └── Strings/                 (en-US, ko-KR)
│
├── AssistStudio/                → App (namespace: AssistStudio)
│   │                             (net9.0-windows10.0.19041.0)
│   ├── Dialogs/
│   ├── Modules/Helpers/         (PasswordVaultHelper — WinRT 전용)
│   ├── Settings/
│   └── Strings/                 (en-US, ko-KR)
│
└── AssistStudio.Core.Tests/     → Unit Tests (net8.0)
```

### 아키텍처 핵심 결정

- **단일 WebView2 렌더링**: ChatPanel 내부에 WebView2 하나, JS DOM append로 메시지 추가
- **스트리밍 전략**: 스트리밍 중 raw text + 커서(▌), 완료 시 marked.js로 HTML 변환 1회
- **C#↔JS Interop**: C#→JS는 ExecuteScriptAsync, JS→C#은 WebMessageReceived
- **Provider 추상화**: IAiProvider 인터페이스, StreamAsync → IAsyncEnumerable<string>
- **Core는 플랫폼 비종속**: net8.0, Windows 전용 API는 [SupportedOSPlatform("windows")] 표시
- **Controls는 TemplatedControl**: Generic.xaml 기반, PART_ 컨벤션

---

## 2. 완료된 Phase 요약

| Phase | 내용 | 완료일 |
|-------|------|--------|
| 1 | Core 골격 (Models, IAiProvider, ChatPanel, WebView2) | 2026-03-11 |
| 2 | 렌더링 (Markdown, 코드 하이라이팅, 복사, 스크롤) | 2026-03-11 |
| 3 | Provider 구현 (Claude, OpenAI, Ollama SSE/NDJSON) | 2026-03-11 |
| 4 | 첨부 & UX (이미지, FilePicker, Vision API) | 2026-03-12 |
| 4.5 | 토큰 추적, 자동 요약, 모델 목록, Ollama 관리, GeminiProvider, 연결 검증 | 2026-03-12 |
| 4.6 | 문서 첨부 (PDF/DOCX 텍스트 추출) | 2026-03-12 |
| 5 | UI/UX (TabView, NavigationView, Settings, 프리셋, PasswordVault, 하드웨어 감지) | 2026-03-12 |
| 6 | Chat UI 개선, 대화 관리, 프로필 시스템, Input UX, 로컬라이제이션 | 2026-03-13 |
| 7-partial | 3 프로젝트 분리, TemplatedControl 전환, XML Summary, #region | 2026-03-13 |
| 7-cont | Authors 변경, Multi-target, API 키 보안 안내, Ollama Fit Policy | 2026-03-14 |
| 7-cont | Core → net8.0 전환, PasswordVaultHelper → App 이동, [SupportedOSPlatform] | 2026-03-14 |
| 7-cont | 솔루션 이름 변경 (FieldCure.AssistStudio), MVP Plan docx 삭제 | 2026-03-14 |

---

## 3. 현재 상태

- 솔루션 빌드: 0 errors, 0 warnings
- 테스트: 8/8 통과 (OllamaFitPolicy)
- Core TFM: `net8.0` (플랫폼 비종속)
- Controls TFM: `net8.0-windows10.0.19041.0` + `net9.0-windows10.0.19041.0`
- 3개 컨트롤 모두 TemplatedControl 전환 완료

---

## 4. Next Steps

### Phase 8: 배포 준비 & CI/CD

#### 8-1. README.md 작성
- 프로젝트 소개, 설치 방법 (NuGet), Quick Start 코드
- 스크린샷, 지원 Provider 목록, API 문서 링크

#### 8-2. NuGet 패키지 설정
- `AssistStudio.Core.csproj`: `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`
- `AssistStudio.Controls.csproj`: 동일
- PackageIcon, PackageReadme, RepositoryUrl 등 메타데이터

#### 8-3. CI/CD (GitHub Actions)
- `ci.yml`: PR 빌드 검증 (dotnet restore → build → test)
- `publish.yml`: tag push 시 dotnet pack → nuget push

#### 8-4. Repository 이름 변경
- GitHub repo 이름: `FluentView.AI` → `AssistStudio` (또는 유지)
- 로컬 디렉토리명 변경 고려

### Phase 9: 추가 기능 (후순위)

- Multimodal 문서 전송 (Claude/Gemini PDF Vision)
- 프로필 변수 바인딩 ({{lang}}, {{style}})
- 프로바이더별 시스템 프롬프트 오버라이드
- 메시지 편집 분기(branching) 버전
- ONNX Runtime GenAI 지원 (별도 NuGet 패키지)
