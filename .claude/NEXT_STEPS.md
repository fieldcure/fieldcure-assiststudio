# FluentView.AI 구현 계획서

> 최종 갱신: 2026-03-11
> MVP Plan v0.1 기반

---

## 1. 현재 상태 요약

### Phase 1 완료 (2026-03-11)
- Models: `ChatMessage`, `ChatAttachment`, `AiRequest` 구현
- Providers: `IAiProvider` 인터페이스 정의 (CompleteAsync, StreamAsync)
- Rendering: `chat.html` 임베디드 리소스 + `WebViewChatRenderer` (C#↔JS 브릿지)
- Controls: `ChatPanel`, `InputContainer`, `AttachmentPreviewBar` (스텁) 구현
- SampleApp: `MockProvider` (에코) + `ChatPanel` 연결 완료
- 빌드 성공, 기본 채팅 흐름 동작 확인

### 미구현 항목
- Markdown 렌더링 (marked.js) — Phase 2
- 코드 구문 하이라이팅 (highlight.js) — Phase 2
- 코드블록 복사 버튼 — Phase 2
- 다크/라이트 테마 CSS 확장 — Phase 2
- Provider 구현 (Claude, OpenAI, Ollama) — Phase 3
- 이미지/파일 첨부 — Phase 4
- SampleApp 완성 + NuGet 배포 — Phase 5
- CI/CD 워크플로우 — Phase 5

---

## 2. 아키텍처 핵심 결정 사항

### 2.1 단일 WebView2 렌더링 패턴
- 메시지마다 WebView2를 생성하지 않는다
- `ChatPanel` 내부에 WebView2 하나를 두고, JavaScript DOM append로 메시지 추가
- 읽기 전용 렌더링만 WebView2가 담당, 입력은 WinUI 3 네이티브

### 2.2 스트리밍 렌더링 전략
- 스트리밍 중: raw text 축적 + 커서(▌) 표시만
- 스트리밍 완료(finalize): `marked.js`로 HTML 변환 1회 실행
- 불완전한 마크다운 깜빡임 방지

### 2.3 C# <-> JavaScript Interop 패턴
- **C# -> JS**: `WebView2.ExecuteScriptAsync()` 호출로 JS 함수 실행
- **JS -> C#**: `WebView2.WebMessageReceived` 이벤트 + `window.chrome.webview.postMessage()`
- 모든 JS 함수는 `chat.html` 내부에 정의

### 2.4 Provider 추상화
- `IAiProvider` 인터페이스로 모든 LLM Provider 추상화
- `StreamAsync()` -> `IAsyncEnumerable<string>` 토큰 단위 반환
- `ChatPanel`은 `IAiProvider`만 의존, 구체 Provider 무관

---

## 3. 구현 Phase 전체 로드맵

### Phase 1: Core 골격 ✅ 완료
Models + IAiProvider 인터페이스 + ChatPanel XAML 기본 구조 + WebView2 초기화

### Phase 2: 렌더링 완성 (현재 단계)
Markdown 렌더링, 코드 하이라이팅, 코드블록 복사, 다크/라이트 테마 CSS 확장, 스크롤 동작 개선

### Phase 3: Provider 구현
ClaudeProvider (SSE), OpenAiProvider, OllamaProvider

### Phase 4: 첨부 & UX
이미지 첨부, FilePicker 연동, AttachmentPreviewBar

### Phase 5: 샘플앱 & 문서 & 배포
SampleApp 완성, README, NuGet 배포

---

## 4. Phase 1 (완료)

Phase 1 완료 기준 — 모두 달성:
- [x] 솔루션 빌드 성공 (경고 0, 오류 0)
- [x] SampleApp 실행 시 WebView2가 초기화되어 빈 채팅 화면 표시
- [x] InputContainer에 텍스트 입력 후 Enter로 전송 가능
- [x] 전송된 메시지가 WebView2에 사용자 말풍선으로 표시
- [x] MockProvider가 에코 응답을 스트리밍으로 반환
- [x] 스트리밍 토큰이 WebView2에 실시간 표시 (raw text, 커서 ▌)
- [x] 스트리밍 완료 시 말풍선이 확정 (커서 제거)

---

## 4.5 Phase 2 상세 구현 계획

### 목표
**Markdown 렌더링 + 코드 하이라이팅 + 코드 복사 + 테마 + 스크롤 완성**

MockProvider의 에코 응답 대신 Markdown이 포함된 응답이 올 때, 스트리밍 중에는 raw text로 표시하다가
스트리밍 완료 시 `marked.js`로 파싱하여 리치 HTML로 교체하는 것이 핵심.

### 4.5.1 핵심 설계 결정

#### 라이브러리 번들링 전략: **인라인 번들 (CDN 불가)**
- `chat.html`은 `NavigateToString()`으로 로드되므로 CDN `<script src>` 사용 불가
- `marked.min.js` (약 50KB)와 `highlight.min.js` + 언어팩 (약 80KB)를 HTML 내 `<script>` 태그에 인라인 삽입
- NuGet 패키지 크기 영향 미미 (임베디드 리소스에 ~130KB 추가)

#### 언어팩 선정 (highlight.js)
번들 크기 최적화를 위해 주요 언어만 포함:
- 기본 포함: `javascript`, `typescript`, `python`, `csharp`, `json`, `xml/html`, `css`, `bash`, `sql`, `yaml`, `markdown`, `plaintext`
- highlight.js의 `common` 서브셋을 기반으로 하되, `csharp`은 반드시 포함 (WinUI 라이브러리 특성)

#### 테마 매핑
- 라이트 모드: highlight.js `github` 테마 계열 CSS
- 다크 모드: highlight.js `github-dark` 테마 계열 CSS
- CSS 변수 + `[data-theme="dark"]` 선택자로 전환

### 4.5.2 변경 파일 목록

#### 1) `src/FluentView.AI/Rendering/Resources/chat.html` — 대규모 확장

**CSS 추가 사항:**
```
- 코드블록 스타일 (.message-bubble pre, .message-bubble code)
  - 인라인 코드: 배경 구분, 패딩, 둥근 모서리
  - 블록 코드: 전체 너비, 오버플로우 스크롤, 언어 레이블
- 코드블록 복사 버튼 (.code-header)
  - 언어명 표시 + 복사 버튼 (우측 상단)
  - 호버/클릭 피드백
- highlight.js 테마 CSS 인라인 삽입
  - :root {} 에 라이트 테마 코드 색상
  - [data-theme="dark"] {} 에 다크 테마 코드 색상
- Markdown 요소 스타일
  - h1~h6, ul/ol, blockquote, hr, table, a, img
  - white-space를 pre-wrap에서 normal로 전환 (finalize 시)
```

**JS 추가 사항:**
```
- marked.min.js 인라인 (9.x)
- highlight.min.js + 언어팩 인라인
- marked 설정:
  marked.setOptions({
    highlight: function(code, lang) {
      if (lang && hljs.getLanguage(lang)) {
        return hljs.highlight(code, { language: lang }).value;
      }
      return hljs.highlightAuto(code).value;
    },
    breaks: true,        // 줄바꿈 -> <br>
    gfm: true,           // GitHub Flavored Markdown
  });
- finalizeMessage() 변경:
  기존: escapeHtml(fullText)
  변경: marked.parse(fullText) -> innerHTML 교체
       -> 코드블록에 .code-header 삽입 (언어명 + 복사 버튼)
       -> white-space를 normal로 전환
- addCopyButtons() 헬퍼:
  finalize 후 모든 <pre><code> 블록에 복사 버튼 삽입
  클릭 시 window.chrome.webview.postMessage('copy:' + codeText)
```

#### 2) `src/FluentView.AI/Rendering/WebViewChatRenderer.cs` — 소규모 확장

```
- CopyRequested 이벤트 발생 시 -> 클립보드에 복사하는 로직 추가
  Windows.ApplicationModel.DataTransfer.DataPackage 사용
- 기존 메서드는 변경 없음 (JS 쪽에서 처리)
```

#### 3) `src/FluentView.AI/Controls/ChatPanel.xaml.cs` — 소규모 수정

```
- _renderer.CopyRequested 이벤트 구독 -> 클립보드 복사 수행
- OnLoaded에서 CopyRequested 핸들러 연결
```

#### 4) `src/FluentView.AI.SampleApp/MockProvider.cs` — Markdown 응답 추가

```
- 에코 대신 Markdown 샘플 응답 반환하도록 변경
- 헤더, 볼드, 리스트, 코드블록(C#, Python), 인라인 코드 등 포함
- 토큰 단위 스트리밍 유지
```

### 4.5.3 구현 순서

```
1. marked.min.js, highlight.min.js + 언어팩 다운로드 및 인라인 준비
2. chat.html CSS 확장 (Markdown 요소 + 코드블록 + 복사 버튼 + highlight 테마)
3. chat.html JS 확장 (marked 설정, finalizeMessage 변경, addCopyButtons)
4. WebViewChatRenderer.cs 클립보드 복사 구현
5. ChatPanel.xaml.cs CopyRequested 구독
6. MockProvider.cs Markdown 응답으로 변경
7. 빌드 및 테스트
```

### 4.5.4 기술 상세

#### marked.js 설정
```javascript
marked.setOptions({
    breaks: true,
    gfm: true,
    highlight: function(code, lang) {
        if (lang && hljs.getLanguage(lang)) {
            return hljs.highlight(code, { language: lang }).value;
        }
        return hljs.highlightAuto(code).value;
    }
});
```

#### finalizeMessage 변경 흐름
```
1. fullText (raw markdown string) 수신
2. marked.parse(fullText) -> HTML 문자열 생성
3. bubble.innerHTML = parsedHtml (white-space: normal)
4. addCopyButtons(bubble) -> 각 <pre><code>에 헤더 삽입
5. scrollToBottom()
```

#### 코드블록 복사 버튼 구조
```html
<div class="code-block">
  <div class="code-header">
    <span class="code-lang">python</span>
    <button class="copy-btn" onclick="copyCode(this)">Copy</button>
  </div>
  <pre><code class="hljs language-python">...</code></pre>
</div>
```

#### 클립보드 복사 (C# 측)
```csharp
// WebViewChatRenderer.OnWebMessageReceived에서 "copy:" 프리픽스 처리
// -> DataPackage로 클립보드에 텍스트 설정
var dp = new DataPackage();
dp.SetText(codeText);
Clipboard.SetContent(dp);
```

#### 스크롤 동작 개선
```
- 현재: window.scrollTo({ behavior: 'smooth' })
- 개선: 스트리밍 중 appendToken 시 smooth 대신 instant 사용 (잦은 호출로 인한 끊김 방지)
- finalizeMessage 후에만 smooth 사용
```

### 4.5.5 Phase 2 완료 기준
- [ ] 스트리밍 완료 후 Markdown이 올바르게 렌더링 (헤더, 리스트, 볼드, 코드블록 등)
- [ ] 코드블록에 언어별 구문 하이라이팅 적용
- [ ] 코드블록 복사 버튼 클릭 시 클립보드에 코드 복사
- [ ] 다크/라이트 테마 전환 시 코드 하이라이팅 테마도 함께 전환
- [ ] 스트리밍 중 스크롤이 끊기지 않고 자연스럽게 동작
- [ ] 솔루션 빌드 성공 (경고 0, 오류 0)

---

## 5. Phase 2 요약 (렌더링 완성)

> 상세 구현 계획은 위 섹션 4.5 참조.
> 목표: Markdown 렌더링 + 코드 하이라이팅 + 코드 복사 + 테마 + 스크롤 완성
- [ ] 스크롤 동작이 자연스러움

---

## 6. Phase 3 상세 계획 (Provider 구현)

### 파일 목록

#### `src/FluentView.AI/Providers/ClaudeProvider.cs` (P0)
```csharp
namespace FluentView.AI.Providers;

public class ClaudeProvider : IAiProvider
{
    public ClaudeProvider(string apiKey, string model = "claude-sonnet-4-20250514");

    // Anthropic Messages API: POST https://api.anthropic.com/v1/messages
    // SSE 스트리밍: content_block_delta 이벤트에서 토큰 추출
    // 헤더: x-api-key, anthropic-version
}
```

#### `src/FluentView.AI/Providers/OpenAiProvider.cs` (P1)
```csharp
// OpenAI Chat Completions API: POST https://api.openai.com/v1/chat/completions
// SSE 스트리밍: data: {"choices":[{"delta":{"content":"..."}}]}
```

#### `src/FluentView.AI/Providers/OllamaProvider.cs` (P1)
```csharp
// Ollama API: POST http://localhost:11434/api/chat
// NDJSON 스트리밍: {"message":{"content":"..."}}
```

### 공통 유틸

#### `src/FluentView.AI/Providers/SseReader.cs`
```csharp
// SSE 스트림(StreamReader)을 읽어 IAsyncEnumerable<string>으로 변환하는 헬퍼
// Claude, OpenAI 공용
```

### Phase 3 완료 기준
- [ ] ClaudeProvider로 실제 API 호출 + 스트리밍 응답 수신
- [ ] OpenAiProvider로 실제 API 호출 + 스트리밍 응답 수신
- [ ] OllamaProvider로 로컬 Ollama 호출 + 스트리밍 응답 수신
- [ ] SampleApp에서 Provider 선택 가능

---

## 7. Phase 4 상세 계획 (첨부 & UX)

### 파일 목록

#### `src/FluentView.AI/Controls/AttachmentPreviewBar.xaml(.cs)` 구현
- 첨부된 파일 미리보기 (이미지 썸네일, 파일명)
- 제거 버튼

#### `src/FluentView.AI/Controls/InputContainer.xaml(.cs)` 확장
- 첨부 버튼 -> `FileOpenPicker` 연동
- 드래그앤드롭 지원 (`DragOver`, `Drop` 이벤트)
- 이미지: PNG/JPG 파일 읽기 -> `ChatAttachment` 생성
- 텍스트 파일: TXT/CSV 읽기 -> `ChatAttachment` 생성

#### `src/FluentView.AI/Rendering/Resources/chat.html` 확장
- `appendImageAttachment(id, base64, fileName)` 구현
- 이미지 인라인 렌더링

#### Provider 확장
- `AiRequest`에 첨부 파일 포함 시 Vision API 형식으로 변환
- ClaudeProvider: `image` content block 지원
- OpenAiProvider: `image_url` content 지원

---

## 8. Phase 5 상세 계획 (샘플앱 & 배포)

### SampleApp 완성
- Provider 선택 UI (ComboBox: Claude / OpenAI / Ollama)
- API Key 입력 필드
- System Prompt 설정
- 대화 클리어 버튼

### NuGet 배포
- `FluentView.AI.csproj`에 `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`
- GitHub Actions `publish.yml`: tag push 시 `dotnet pack` -> `nuget push`

### CI
- `.github/workflows/ci.yml`: PR 빌드 검증
  - `dotnet restore` -> `dotnet build` -> (향후 테스트)

---

## 9. 파일 전체 목록 (최종 구조)

```
src/FluentView.AI/
├── Controls/
│   ├── ChatPanel.xaml
│   ├── ChatPanel.xaml.cs
│   ├── InputContainer.xaml
│   ├── InputContainer.xaml.cs
│   ├── AttachmentPreviewBar.xaml
│   └── AttachmentPreviewBar.xaml.cs
├── Models/
│   ├── ChatMessage.cs
│   ├── ChatAttachment.cs
│   └── AiRequest.cs
├── Providers/
│   ├── IAiProvider.cs
│   ├── ClaudeProvider.cs
│   ├── OpenAiProvider.cs
│   ├── OllamaProvider.cs
│   └── SseReader.cs
├── Rendering/
│   ├── WebViewChatRenderer.cs
│   └── Resources/
│       └── chat.html
└── FluentView.AI.csproj

src/FluentView.AI.SampleApp/
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── MockProvider.cs          (Phase 1 테스트용)
├── App.xaml
├── App.xaml.cs
└── FluentView.AI.SampleApp.csproj
```
