# FluentView.AI 구현 계획서

> 최종 갱신: 2026-03-12
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

### Phase 2 완료 (2026-03-11)
- marked.js 15.x + highlight.js 11.x 인라인 번들 (임베디드 리소스)
- Markdown 렌더링 (finalizeMessage → marked.parse + hljs)
- 코드블록 복사 버튼 + 클립보드 복사 (DataPackage)
- GitHub-style 구문 하이라이팅 토큰 색상 (라이트/다크)
- 스트리밍 중 instant 스크롤, finalize 후 smooth 스크롤

### Phase 3 완료 (2026-03-11)
- SseReader 공용 SSE 파서 구현
- ClaudeProvider (Anthropic Messages API, SSE 스트리밍)
- OpenAiProvider (Chat Completions API, SSE 스트리밍)
- OllamaProvider (Ollama Chat API, NDJSON 스트리밍)
- SampleApp Provider 선택 UI (ComboBox + API Key + Model + Apply)

### Phase 4 완료 (2026-03-12)
- AttachmentPreviewBar 구현 (이미지 썸네일/파일 아이콘 + 제거 버튼)
- InputContainer 확장 (FileOpenPicker 연동, 드래그앤드롭 지원)
- MessageSentEventArgs (텍스트 + 첨부 파일 포함)
- chat.html 이미지 인라인 렌더링 (appendUserMessage attachments 파라미터)
- WebViewChatRenderer attachments 직렬화 및 전달
- ClaudeProvider Vision API 지원 (image content block)
- OpenAiProvider Vision API 지원 (image_url content)
- OllamaProvider Vision API 지원 (images 필드)
- WindowHelper 공용 클래스 (FileOpenPicker HWND 연동)

### 미구현 항목
- Control 품질 향상 + 로컬라이제이션 보완 — Phase 6
- SampleApp 완성 + NuGet 배포 + CI/CD — Phase 7

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

### Phase 2: 렌더링 완성 ✅ 완료
Markdown 렌더링, 코드 하이라이팅, 코드블록 복사, 다크/라이트 테마 CSS 확장, 스크롤 동작 개선

### Phase 3: Provider 구현 ✅ 완료
ClaudeProvider (SSE), OpenAiProvider, OllamaProvider, SseReader 공용 유틸

### Phase 4: 첨부 & UX ✅ 완료
이미지 첨부, FilePicker 연동, AttachmentPreviewBar, Vision API 지원

### Phase 4.5 완료 (2026-03-12)
토큰 사용량 추적 (Usage), 자동 히스토리 요약 (컨텍스트 압축), 모델 목록 API 연동 (Claude/OpenAI/Ollama/Gemini), IModelManager + OllamaModelManager (모델 다운로드/삭제), GeminiProvider (무료 티어), API 연결 검증, SampleApp 모델 선택 UI

### Phase 4.6: 문서 첨부 지원 (PDF/DOCX) — 진행 중
- [x] PdfPig + DocumentFormat.OpenXml NuGet 패키지 추가
- [x] `AttachmentType.Document` enum 추가 (향후 multimodal 문서 지원용)
- [x] InputContainer에서 PDF/DOCX 텍스트 추출 → TextFile로 변환
- [ ] 향후: `AttachmentType.Document`를 활용한 multimodal 문서 전송 (Claude/Gemini PDF Vision)

### Phase 5: UI/UX/사용성 + 로컬 모델 자동 관리
Core: Provider 뱃지, 메시지 복사, 요약 버튼, 테마 시스템, 로컬라이즈, ChatMessage.ProviderName
SampleApp: 멀티 Provider preset, Ollama 자동 설치/시작, 하드웨어 감지 + 모델 추천, First Run Experience

### Phase 6: Control 품질 향상 & 로컬라이제이션 보완
XML Summary 태그 정비, Control UI/UX 리터치 (멀티 프로바이더 대화 UX), Ctrl+V 확장 (파일/이미지 붙여넣기), 로컬라이제이션 누락 점검

### Phase 7: 샘플앱 완성 & 문서 & 배포
SampleApp 완성, README, NuGet 배포, CI/CD

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
- [x] 스트리밍 완료 후 Markdown이 올바르게 렌더링 (헤더, 리스트, 볼드, 코드블록 등)
- [x] 코드블록에 언어별 구문 하이라이팅 적용
- [x] 코드블록 복사 버튼 클릭 시 클립보드에 코드 복사
- [x] 다크/라이트 테마 전환 시 코드 하이라이팅 테마도 함께 전환
- [x] 스트리밍 중 스크롤이 끊기지 않고 자연스럽게 동작
- [x] 솔루션 빌드 성공 (경고 0, 오류 0)

---

## 5. Phase 2 (완료)

Phase 2 완료 기준 — 모두 달성. 상세는 섹션 4.5 참조.

---

## 6. Phase 3 상세 구현 계획 (Provider 구현)

### 목표
**3개 LLM Provider (Claude, OpenAI, Ollama) 구현 + SampleApp Provider 선택 UI**

`IAiProvider` 인터페이스의 `StreamAsync()` → `IAsyncEnumerable<string>` 패턴을 활용하여
각 Provider의 스트리밍 API를 통일된 토큰 스트림으로 변환.

### 6.1 핵심 설계 결정

#### HttpClient 관리
- 각 Provider는 내부에 `HttpClient` 인스턴스를 소유 (DI 미사용, 라이브러리 특성상)
- 소비자가 원할 경우 외부에서 `HttpClient`를 주입할 수 있도록 생성자 오버로드 제공
- `IDisposable` 구현하여 HttpClient 정리

#### SSE 파싱 공용화
- Claude API와 OpenAI API 모두 SSE(Server-Sent Events) 프로토콜 사용
- `SseReader` 유틸: `Stream` → `IAsyncEnumerable<SseEvent>` 변환
- 각 Provider는 `SseReader`로 이벤트를 수신 후, 자체 JSON 구조에서 토큰 추출

#### Ollama는 NDJSON
- SSE가 아닌 줄 단위 JSON (Newline-Delimited JSON)
- `SseReader`와 별도로 `StreamReader.ReadLineAsync()` + `JsonDocument.Parse()` 처리

#### 모델 ID 기본값
- Claude: `claude-sonnet-4-20250514`
- OpenAI: `gpt-4o`
- Ollama: `llama3.1`

### 6.2 파일 목록 및 상세

#### 1) `src/FluentView.AI/Providers/SseReader.cs` — 공용 SSE 파서

```csharp
namespace FluentView.AI.Providers;

internal record SseEvent(string EventType, string Data);

internal static class SseReader
{
    // SSE 스펙: "event: xxx\ndata: yyy\n\n" 패턴
    // 빈 줄(\n\n)이 이벤트 경계
    // data가 여러 줄일 수 있음 (data: ...\ndata: ... → 개행으로 합산)
    static async IAsyncEnumerable<SseEvent> ReadEventsAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct);
}
```

핵심 구현:
- `StreamReader`로 줄 단위 읽기
- `event:` 라인 → 이벤트 타입 저장
- `data:` 라인 → 데이터 축적
- 빈 줄 → `SseEvent` yield 후 리셋
- `data:` prefix 제거 주의 (공백 1개 포함)

#### 2) `src/FluentView.AI/Providers/ClaudeProvider.cs` — Anthropic Messages API

```
API: POST https://api.anthropic.com/v1/messages
Headers:
  x-api-key: {apiKey}
  anthropic-version: 2023-06-01
  content-type: application/json

Request body:
  { "model": "...", "max_tokens": N, "temperature": T,
    "system": "...",
    "messages": [{"role":"user","content":"..."}, ...],
    "stream": true }

SSE 이벤트 흐름:
  event: message_start        → 무시
  event: content_block_start  → 무시
  event: content_block_delta  → data.delta.text 추출 (★토큰)
  event: content_block_stop   → 무시
  event: message_delta        → 무시
  event: message_stop         → 종료

ChatRole 매핑: User → "user", Assistant → "assistant", System → system param
```

주요 구현 포인트:
- `ChatRole.System` 메시지는 `messages` 배열이 아닌 `system` 파라미터로 전달
- `content_block_delta` 이벤트의 `data` JSON: `{"type":"content_block_delta","delta":{"type":"text_delta","text":"토큰"}}`
- `CompleteAsync`: `stream: false`로 호출, `content[0].text` 추출
- 생성자: `ClaudeProvider(string apiKey, string model = "claude-sonnet-4-20250514")`
- HttpClient 주입 오버로드: `ClaudeProvider(HttpClient httpClient, string apiKey, string model)`

#### 3) `src/FluentView.AI/Providers/OpenAiProvider.cs` — OpenAI Chat Completions

```
API: POST https://api.openai.com/v1/chat/completions
Headers:
  Authorization: Bearer {apiKey}
  content-type: application/json

Request body:
  { "model": "...", "max_tokens": N, "temperature": T,
    "messages": [{"role":"system","content":"..."}, {"role":"user","content":"..."}, ...],
    "stream": true }

SSE 이벤트:
  data: {"choices":[{"delta":{"content":"토큰"}}]}
  data: [DONE]  → 종료

ChatRole 매핑: User → "user", Assistant → "assistant", System → "system"
```

주요 구현 포인트:
- `ChatRole.System`을 `messages` 배열에 `"role": "system"`으로 포함 (Claude와 차이점)
- `data: [DONE]` 수신 시 스트림 종료
- `delta.content`가 null일 수 있음 (role-only delta 등) — null 체크 필요
- 생성자: `OpenAiProvider(string apiKey, string model = "gpt-4o")`

#### 4) `src/FluentView.AI/Providers/OllamaProvider.cs` — Ollama Local API

```
API: POST {baseUrl}/api/chat
Headers: content-type: application/json

Request body:
  { "model": "...", "messages": [...], "stream": true,
    "options": { "temperature": T, "num_predict": N } }

NDJSON 스트리밍 (줄 단위):
  {"message":{"role":"assistant","content":"토큰"},"done":false}
  {"message":{"role":"assistant","content":""},"done":true}  → 종료

ChatRole 매핑: User → "user", Assistant → "assistant", System → "system"
API 키 불필요 (로컬)
```

주요 구현 포인트:
- SSE가 아닌 NDJSON → `StreamReader.ReadLineAsync()` 사용
- `done: true` 수신 시 스트림 종료
- `baseUrl` 커스터마이징 가능 (Docker 등 포트 변경 시)
- 생성자: `OllamaProvider(string model = "llama3.1", string baseUrl = "http://localhost:11434")`

#### 5) `src/FluentView.AI.SampleApp/MainWindow.xaml` — Provider 선택 UI

```
상단 바: ComboBox(Provider 선택) + TextBox(API Key) + TextBox(Model) + Button(Apply)
하단: ChatPanel (기존)
```

- Provider 선택 → API Key 필드 표시/숨김 (Mock, Ollama는 불필요)
- Apply 버튼 → Provider 인스턴스 생성 → ChatPanel.Provider에 할당
- 이전 Provider가 IDisposable이면 Dispose 호출

### 6.3 구현 순서

```
1. SseReader.cs (공용 유틸, 의존성 없음)
2. ClaudeProvider.cs (SseReader 의존)
3. OpenAiProvider.cs (SseReader 의존)
4. OllamaProvider.cs (독립, NDJSON)
5. SampleApp MainWindow 업데이트 (Provider 선택 UI + 생성 로직)
6. 빌드 및 테스트
```

### 6.4 JSON 직렬화 전략

- `System.Text.Json` 사용 (추가 패키지 불필요)
- 요청 body: `JsonSerializer.Serialize()` + anonymous object 또는 `JsonObject`
- 응답 파싱: `JsonDocument.Parse()` + `RootElement.GetProperty()` 패턴
- 별도 DTO 클래스 없이 `JsonDocument`로 직접 탐색 (외부 API 스키마 변경 대응 유연)

### 6.5 에러 처리

- HTTP 4xx/5xx → `HttpRequestException` throw (메시지에 상태 코드 + 응답 body 포함)
- 네트워크 오류 → `HttpRequestException` 전파
- JSON 파싱 실패 → `JsonException` 전파
- `ChatPanel.OnMessageSent`에서 이미 catch하여 `[Error: ...]` 메시지 표시

### 6.6 Phase 3 완료 기준
- [ ] SseReader가 SSE 스트림을 올바르게 파싱
- [ ] ClaudeProvider로 실제 API 호출 + 스트리밍 응답 수신
- [ ] OpenAiProvider로 실제 API 호출 + 스트리밍 응답 수신
- [ ] OllamaProvider로 로컬 Ollama 호출 + 스트리밍 응답 수신
- [ ] SampleApp에서 Provider 선택 + API Key 입력 후 대화 가능
- [ ] Provider 전환 시 이전 Provider가 올바르게 Dispose
- [ ] 솔루션 빌드 성공 (경고 0, 오류 0)

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

## 7.5 Phase 4.5 상세 구현 계획 (토큰 관리 + 모델 목록 + 로컬 모델 관리)

### 목표
1. **토큰 사용량 추적**: API 응답의 usage 정보를 파싱하여 소비자에게 노출
2. **자동 히스토리 요약**: 토큰 임계값 초과 시 오래된 대화를 자동 요약하여 컨텍스트 압축
3. **모델 목록 API 연동**: Claude/OpenAI/Ollama 모델 목록을 조회하여 ComboBox로 선택
4. **로컬 모델 관리**: Ollama 모델 다운로드/삭제 + SampleApp 모델 선택 UI
5. **API 연결 검증**: API Key 유효성 + 사용자/조직 정보 표시
6. **GeminiProvider 추가**: Google Gemini API 연동 (무료 티어 활용 가능)

### 7.5.1 아키텍처 설계

#### 레이어 분리
```
Core 라이브러리 (FluentView.AI)
├── Models/
│   ├── TokenUsage.cs                  — 토큰 사용량 정보 (NEW)
│   ├── AiModel.cs                     — 모델 메타데이터 (NEW, 통합)
│   └── LocalModel.cs                  — 로컬 모델 확장 정보 (NEW)
├── Providers/
│   ├── IAiProvider.cs                 — ListModelsAsync 추가 (MODIFY)
│   ├── IModelManager.cs              — 로컬 모델 관리 인터페이스 (NEW)
│   ├── ClaudeProvider.cs             — usage 파싱 + ListModels (MODIFY)
│   ├── OpenAiProvider.cs             — usage 파싱 + ListModels (MODIFY)
│   ├── OllamaProvider.cs             — usage 파싱 + ListModels (MODIFY)
│   ├── GeminiProvider.cs             — Google Gemini API (NEW)
│   └── OllamaModelManager.cs         — Ollama 모델 관리 구현 (NEW)
├── Controls/
│   └── ChatPanel.xaml.cs             — 자동 요약 로직 (MODIFY)
│
SampleApp (FluentView.AI.SampleApp)
├── MainWindow.xaml(.cs)              — Model ComboBox + Browse (MODIFY)
├── ModelSelectionDialog.xaml(.cs)     — Ollama 모델 선택/다운로드 (NEW)
```

### 7.5.2 Feature A: 토큰 사용량 추적 (Token Usage Tracking)

#### `src/FluentView.AI/Models/TokenUsage.cs`
```csharp
namespace FluentView.AI.Models;

public record TokenUsage(
    int InputTokens,
    int OutputTokens
)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
```

#### API별 usage 위치

**Claude API** — `message_start` 이벤트 또는 최종 `message_delta` 이벤트:
```json
"usage": { "input_tokens": 1250, "output_tokens": 340 }
```

**OpenAI API** — 스트리밍 시 마지막 chunk 또는 `stream_options: {"include_usage": true}`:
```json
"usage": { "prompt_tokens": 1250, "completion_tokens": 340, "total_tokens": 1590 }
```

**Ollama API** — 스트리밍 완료 시 (`done: true` 응답):
```json
{ "done": true, "prompt_eval_count": 1250, "eval_count": 340 }
```

#### IAiProvider 변경
```csharp
public interface IAiProvider
{
    // 기존
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct = default);

    // 추가
    TokenUsage? LastUsage { get; }  // 마지막 요청의 토큰 사용량
}
```

#### 각 Provider 구현 변경
- `StreamAsync()` 내에서 스트리밍 완료 시 usage JSON을 파싱하여 `LastUsage` 프로퍼티에 저장
- `CompleteAsync()`에서도 동일하게 파싱
- Claude: `message_delta` 이벤트의 `usage` 필드
- OpenAI: `stream_options: {"include_usage": true}` 요청 옵션 추가, 마지막 chunk에서 파싱
- Ollama: `done: true` 응답의 `prompt_eval_count`, `eval_count`

### 7.5.3 Feature B: 자동 히스토리 요약 (Auto-Summarization)

#### 동작 흐름
```
ChatPanel.OnMessageSent():
    1. Provider.StreamAsync() 완료
    2. Provider.LastUsage.InputTokens 확인
    3. InputTokens > MaxInputTokens 이면:
       a. 오래된 메시지 (처음 ~ N-RecentTurns)를 추출
       b. Provider.CompleteAsync("다음 대화를 간결하게 요약해줘: ...")
       c. 요약 결과를 System 메시지로 교체
       d. 오래된 메시지 제거, [System: 요약] + [최근 메시지] 유지
```

#### ChatPanel 옵션 추가
```csharp
// ChatPanel.xaml.cs
public int MaxInputTokens { get; set; } = 0;           // 0 = 비활성화 (기본값: 전체 전송)
public int RecentTurnsToKeep { get; set; } = 10;        // 요약 시 유지할 최근 턴 수
public bool AutoSummarize { get; set; } = false;        // 자동 요약 활성화
```

#### 요약 프롬프트 (내부)
```
"다음은 이전 대화의 내용입니다. 핵심 맥락과 결정 사항을 유지하면서 간결하게 요약해주세요:

[이전 대화 내용]"
```

#### 요약 후 메시지 구조
```
messages = [
    { role: System, content: "[이전 대화 요약]\n{요약 내용}" },
    { role: User, content: "최근 메시지 1" },
    { role: Assistant, content: "최근 응답 1" },
    ...최근 N턴
]
```

### 7.5.4 Feature C: 모델 목록 API 연동 (Model List API)

#### 각 API 엔드포인트

**Claude**: `GET https://api.anthropic.com/v1/models`
```json
{
  "data": [
    { "id": "claude-sonnet-4-20250514", "display_name": "Claude Sonnet 4", "created_at": "..." },
    { "id": "claude-opus-4-20250514", "display_name": "Claude Opus 4", "created_at": "..." }
  ]
}
```

**OpenAI**: `GET https://api.openai.com/v1/models`
```json
{
  "data": [
    { "id": "gpt-4o", "owned_by": "openai", "created": 1234567890 },
    { "id": "gpt-4o-mini", "owned_by": "openai", "created": 1234567890 }
  ]
}
```

**Ollama**: `GET http://localhost:11434/api/tags`
```json
{
  "models": [
    { "name": "llama3.1:latest", "size": 4661224676, "modified_at": "..." }
  ]
}
```

#### IAiProvider 추가 메서드
```csharp
public interface IAiProvider
{
    // 기존 ...
    TokenUsage? LastUsage { get; }

    // 추가: 사용 가능한 모델 목록 조회
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
}
```

#### `src/FluentView.AI/Models/AiModel.cs`
```csharp
namespace FluentView.AI.Models;

public record AiModel(
    string Id,                // "claude-sonnet-4-20250514", "gpt-4o", "llama3.1:latest"
    string? DisplayName,      // "Claude Sonnet 4" (null이면 Id 표시)
    string? OwnedBy           // "anthropic", "openai", null
);
```

#### SampleApp UI 변경
```
기존: [Provider ▼] [API Key ●●●] [Model (text)] [Apply]
변경: [Provider ▼] [API Key ●●●] [Model ▼ (ComboBox)] [Apply]

Apply 클릭 시:
  1. Provider 인스턴스 생성
  2. Provider.ListModelsAsync() 호출
  3. 결과를 Model ComboBox에 바인딩
  4. 기본 모델 자동 선택
  5. 사용자가 모델 변경 시 Provider 재생성

Ollama 선택 시: Model ComboBox 옆에 [Browse...] 버튼 추가
  → ModelSelectionDialog 열림 (모델 다운로드/삭제 가능)
```

### 7.5.5 Feature D: 로컬 모델 관리 (Ollama Model Manager)

#### `src/FluentView.AI/Models/LocalModel.cs`
```csharp
namespace FluentView.AI.Models;

public class LocalModel : AiModel
{
    public LocalModel(string id, string? displayName, string? ownedBy)
        : base(id, displayName, ownedBy) { }

    public long SizeBytes { get; init; }
    public string? Family { get; init; }              // "llama", "phi", "gemma" 등
    public string? ParameterSize { get; init; }       // "8B", "70B" 등
    public string? QuantizationLevel { get; init; }   // "Q4_0", "Q8_0" 등
    public DateTime? ModifiedAt { get; init; }
    public bool IsDownloaded { get; init; }
}
```

#### `src/FluentView.AI/Providers/IModelManager.cs`
```csharp
namespace FluentView.AI.Providers;

public interface IModelManager
{
    Task<IReadOnlyList<LocalModel>> ListLocalModelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LocalModel>> SearchAvailableModelsAsync(
        string? query = null, CancellationToken ct = default);
    Task DownloadModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
}

public record ModelDownloadProgress(
    string Status,       // "pulling manifest", "downloading", "verifying", "success"
    double Percent,      // 0.0 ~ 1.0
    long? TotalBytes,
    long? CompletedBytes
);
```

#### `src/FluentView.AI/Providers/OllamaModelManager.cs`
```csharp
namespace FluentView.AI.Providers;

public class OllamaModelManager : IModelManager, IDisposable
{
    // Ollama REST API:
    //   GET    /api/tags    → 로컬 모델 목록
    //   POST   /api/pull    → 모델 다운로드 (NDJSON 스트리밍 진행률)
    //   DELETE  /api/delete  → 모델 삭제
    //
    // SearchAvailableModelsAsync:
    //   Ollama 자체에는 검색 API가 없으므로 추천 모델 목록을 내장한다.
}
```

추천 모델 목록 (내장):
```
llama3.1       — Meta Llama 3.1 (8B, 범용)
llama3.1:70b   — Meta Llama 3.1 (70B, 고성능)
phi4           — Microsoft Phi-4 (14B, 소형 고성능)
gemma2         — Google Gemma 2 (9B)
qwen2.5        — Alibaba Qwen 2.5 (7B)
mistral        — Mistral 7B
deepseek-r1    — DeepSeek R1 (추론 특화)
codellama      — Code Llama (코드 생성)
llava          — LLaVA (Vision 지원)
```

### 7.5.6 Feature E: API 연결 검증 (Connection Validation)

#### 목적
API Key 입력 후 Apply 시 키 유효성을 검증하고, 가능한 경우 사용자/조직 정보를 표시한다.

#### Provider별 검증 방법

**OpenAI** — 사용자/조직 정보 조회 가능:
```
GET https://api.openai.com/v1/models
→ 성공 (200): 키 유효, 응답 헤더에서 organization 정보 확인
→ 실패 (401): 키 무효
```
OpenAI는 응답 헤더 `openai-organization`에 조직 ID가 포함됨.

**Claude** — 제한적:
```
GET https://api.anthropic.com/v1/models
→ 성공 (200): 키 유효
→ 실패 (401): 키 무효
```
Anthropic 일반 API 키로는 사용자/조직 정보 조회 불가 (Admin API 필요).
키 유효성 검증만 수행.

**Ollama** — 서버 연결 확인:
```
GET http://localhost:11434/api/tags
→ 성공 (200): Ollama 서버 실행 중
→ 실패 (연결 거부): 서버 미실행
```
API 키 없음, 서버 가용성만 확인.

#### IAiProvider 추가
```csharp
public interface IAiProvider
{
    // 기존 ...

    // 추가: 연결 검증 + 정보 조회
    Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default);
}

public record ConnectionInfo(
    bool IsValid,
    string? OrganizationId,     // OpenAI: org ID
    string? OrganizationName,   // 표시용 이름 (가능한 경우)
    string? ErrorMessage        // 실패 시 에러 메시지
);
```

#### SampleApp UI 표시
```
Apply 클릭 시:
  1. Provider 인스턴스 생성
  2. ValidateConnectionAsync() 호출
  3. 성공 시:
     ✅ Connected (org: "ZFlow Inc.")  — 또는 키 유효 확인만
     → Model ComboBox 활성화 + ListModelsAsync() 호출
  4. 실패 시:
     ❌ Invalid API Key / Server not running
     → Model ComboBox 비활성화
```

상단 바 StatusText 영역에 연결 상태를 표시:
```
[Provider ▼] [API Key ●●●] [Model ▼] [Apply] [Clear]
✅ Connected: OpenAI (org: org-xxxx)     ← 상태 텍스트
```

### 7.5.7 Feature F: GeminiProvider (Google Gemini API)

#### API 개요
```
엔드포인트: POST https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?key={apiKey}&alt=sse
인증: API Key (URL query parameter)
스트리밍: SSE
무료 티어: 분당 15 요청, 일 1,500 요청 (gemini-2.0-flash)
```

#### 요청 형식
```json
{
  "contents": [
    {
      "role": "user",
      "parts": [{ "text": "Hello" }]
    },
    {
      "role": "model",
      "parts": [{ "text": "Hi there!" }]
    }
  ],
  "systemInstruction": {
    "parts": [{ "text": "You are a helpful assistant." }]
  },
  "generationConfig": {
    "temperature": 0.7,
    "maxOutputTokens": 4096
  }
}
```

#### ChatRole 매핑
```
User      → "user"
Assistant → "model"    (⚠ OpenAI/Claude의 "assistant"와 다름)
System    → systemInstruction 파라미터 (messages 배열 밖)
```

#### SSE 스트리밍 응답
```json
data: {"candidates":[{"content":{"parts":[{"text":"토큰"}]}}]}
data: {"candidates":[{"content":{"parts":[{"text":""}]}}],
       "usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":50,"totalTokenCount":60}}
```

#### Vision 지원
```json
{
  "contents": [{
    "role": "user",
    "parts": [
      { "text": "이 이미지에 무엇이 보이나요?" },
      { "inlineData": { "mimeType": "image/png", "data": "base64..." } }
    ]
  }]
}
```

#### 모델 목록 API
```
GET https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}
→ { "models": [{ "name": "models/gemini-2.0-flash", "displayName": "Gemini 2.0 Flash", ... }] }
```

#### Usage 파싱
스트리밍 마지막 chunk의 `usageMetadata`:
```json
{ "promptTokenCount": 1250, "candidatesTokenCount": 340, "totalTokenCount": 1590 }
```

#### 구현 파일
```csharp
// src/FluentView.AI/Providers/GeminiProvider.cs
namespace FluentView.AI.Providers;

public class GeminiProvider : IAiProvider, IDisposable
{
    // 생성자: GeminiProvider(string apiKey, string model = "gemini-2.0-flash")
    // SseReader 재활용 가능 (SSE 프로토콜 동일)
    // 기본 모델: gemini-2.0-flash (무료 티어)
}
```

#### 연결 검증
```
GET https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}
→ 성공 (200): 키 유효
→ 실패 (400/403): 키 무효
```

### 7.5.8 SampleApp UI 설계

#### MainWindow 상단 바 변경
```
기존: [Provider ▼] [API Key ●●●]          [Model (TextBox)]    [Apply] [Clear]
변경: [Provider ▼] [API Key ●●●] [Refresh] [Model ▼ (ComboBox)] [Apply] [Clear]
                                                      ↑
                                         Ollama일 때: [Browse...] 버튼 추가
```

#### 모델 선택 다이얼로그 (`ModelSelectionDialog.xaml`) — Ollama 전용
```
┌─────────────────────────────────────────────┐
│  Local Models                               │
│  ┌─────────────────────────────────────────┐ │
│  │ ✅ llama3.1:latest    4.7 GB   [Select] │ │
│  │ ✅ phi4:latest        8.5 GB   [Select] │ │
│  │ ✅ gemma2:latest      5.4 GB   [Select] │ │
│  └─────────────────────────────────────────┘ │
│                                             │
│  Available Models                           │
│  ┌─────────────────────────────────────────┐ │
│  │ ⬇ mistral             4.1 GB  [Pull]   │ │
│  │ ⬇ codellama           3.8 GB  [Pull]   │ │
│  │ ⬇ deepseek-r1         4.7 GB  [Pull]   │ │
│  └─────────────────────────────────────────┘ │
│                                             │
│  ┌──────────────────────┐                   │
│  │ Custom: [________]  [Pull]              │ │
│  └──────────────────────┘                   │
│                                             │
│  [Cancel]                                   │
└─────────────────────────────────────────────┘
```

### 7.5.9 구현 순서

```
Feature A: 토큰 사용량 추적
  1. TokenUsage.cs 모델 생성
  2. IAiProvider에 LastUsage 프로퍼티 추가
  3. ClaudeProvider usage 파싱 구현
  4. OpenAiProvider usage 파싱 구현 (stream_options 추가)
  5. OllamaProvider usage 파싱 구현
  6. MockProvider LastUsage 스텁 구현

Feature F: GeminiProvider
  7. GeminiProvider.cs 구현 (SSE 스트리밍, SseReader 재활용)
  8. Gemini Vision 지원 (inlineData)
  9. Gemini usage 파싱 (usageMetadata)
  10. SampleApp Provider ComboBox에 Gemini 추가

Feature B: 자동 히스토리 요약
  11. ChatPanel에 MaxInputTokens, AutoSummarize 옵션 추가
  12. 요약 로직 구현 (임계값 초과 시 자동 트리거)
  13. 요약 결과를 System 메시지로 삽입하는 로직

Feature C: 모델 목록 API
  14. AiModel.cs 모델 생성
  15. IAiProvider에 ListModelsAsync() 추가
  16. ClaudeProvider.ListModelsAsync() (GET /v1/models)
  17. OpenAiProvider.ListModelsAsync() (GET /v1/models)
  18. OllamaProvider.ListModelsAsync() (GET /api/tags)
  19. GeminiProvider.ListModelsAsync() (GET /v1beta/models)
  20. MockProvider.ListModelsAsync() 스텁
  21. SampleApp MainWindow Model ComboBox 변경

Feature D: 로컬 모델 관리
  22. LocalModel.cs 모델 생성
  23. IModelManager.cs 인터페이스
  24. OllamaModelManager.cs 구현
  25. ModelSelectionDialog.xaml + .cs (SampleApp)
  26. MainWindow Browse Models 버튼 연동

Feature E: API 연결 검증
  27. ConnectionInfo record 생성
  28. IAiProvider에 ValidateConnectionAsync() 추가
  29. ClaudeProvider 연결 검증 (GET /v1/models → 200 여부)
  30. OpenAiProvider 연결 검증 (GET /v1/models + organization 헤더)
  31. GeminiProvider 연결 검증 (GET /v1beta/models → 200 여부)
  32. OllamaProvider 연결 검증 (GET /api/tags → 서버 가용성)
  33. MockProvider 스텁 (항상 유효)
  34. SampleApp 상태 텍스트 표시 (✅ Connected / ❌ Invalid)

통합 테스트:
  35. 빌드 및 전체 테스트
```

### 7.5.9 향후 확장 (ONNX Runtime GenAI)

Phase 4.5에서는 Ollama만 지원하지만, `IModelManager` 인터페이스를 통해 향후 확장 가능:

```
OnnxModelManager : IModelManager
  - HuggingFace에서 ONNX 모델 검색/다운로드
  - Microsoft.ML.OnnxRuntimeGenAI로 로컬 추론
  - OnnxProvider : IAiProvider 추가
  - GPU/NPU 가속 지원 (DirectML, QNN)
```

이 확장은 `Microsoft.ML.OnnxRuntimeGenAI` 패키지 의존성이 필요하며,
별도 NuGet 패키지 (`FluentView.AI.Onnx`)로 분리하는 것이 좋다.

### 7.5.11 Phase 4.5 완료 기준
- [x] TokenUsage 모델 + IAiProvider.LastUsage 구현
- [x] 5개 Provider 모두 usage 파싱 동작 (Claude, OpenAI, Ollama, Gemini, Mock)
- [x] GeminiProvider 스트리밍 + Vision 동작
- [x] AutoSummarize 옵션 활성화 시 자동 요약 동작
- [x] AiModel 모델 + IAiProvider.ListModelsAsync() 구현
- [x] SampleApp에서 Model ComboBox로 모델 선택 가능
- [x] IModelManager 인터페이스 정의
- [x] OllamaModelManager로 로컬 모델 목록 조회
- [x] OllamaModelManager로 모델 다운로드 (진행률 표시)
- [x] OllamaModelManager로 모델 삭제
- [x] SampleApp ModelSelectionDialog 동작
- [x] 다이얼로그에서 선택한 모델이 OllamaProvider에 적용
- [x] ValidateConnectionAsync() 연결 검증 동작
- [x] SampleApp에서 연결 상태 + 조직 정보 표시
- [x] 솔루션 빌드 성공 (경고 0, 오류 0)

---

## 8. Phase 5 상세 계획 (UI/UX/사용성 + 로컬 모델 자동 관리)

### Core 라이브러리 (FluentView.AI) 변경

#### 5.1 Provider 뱃지 표시
- `ChatMessage`에 `ProviderName` 속성 추가
- `chat.html`: Assistant 말풍선 위에 Provider/모델명 뱃지 렌더링
- `WebViewChatRenderer`: 메시지 생성 시 provider 정보 전달

#### 5.2 메시지 복사 버튼
- `chat.html`: 메시지 호버 시 복사 아이콘 오버레이 표시
  - User 메시지: 프롬프트 텍스트 복사
  - Assistant 메시지: 답변 전체 텍스트 복사 (Markdown 원문)
- `WebViewChatRenderer`: `copy:message:{id}` 웹 메시지 처리 → 클립보드

#### 5.3 대화 요약 버튼
- `ChatPanel`: 수동 요약 트리거 메서드 (기존 AutoSummarize 로직 재활용)
- 요약 후 [System 요약 메시지 + 최근 N턴]으로 히스토리 압축
- 요약 시점 이전 메시지는 렌더링에서 접기/숨기기 처리

#### 5.4 테마 시스템
- `ChatPanel`에 테마 속성 노출 (Light/Dark/System)
- `chat.html` CSS 변수 기반 테마 전환 확장
- WinUI 3 `ActualThemeChanged` 이벤트 연동

#### 5.5 로컬라이즈 (i18n)
- UI 문자열 리소스화 (x:Uid 패턴 또는 .resw)
- `chat.html` 내 고정 문자열 (Copy, Continue 등) 외부 주입
- 초기 지원 언어: en, ko

### SampleApp 변경

#### 5.6 SampleApp 구조 재설계 (TabView + NavigationView)
- `TabView` 기반 멀티 탭 — 각 탭이 독립 `ChatPanel` + 독립 대화 세션
- `NavigationView` (SplitView 스타일) 좌측 패널:
  - Chat (탭 뷰)
  - Settings 페이지
- 새 탭 생성/닫기, 탭 간 독립 히스토리

#### 5.7 Settings 페이지
- Provider preset 관리 (3~4 slot 설정/저장)
- API Key 입력 → `PasswordVault` 암호화 저장
- 테마 선택 (Light/Dark/System)
- 언어 선택 (en/ko)
- Ollama 서버 상태 표시/관리

#### 5.8 멀티 Provider 즉석 전환
- 입력 영역에 Provider 선택 드롭다운 (preset에서 선택)
- 모델 변경 시 대화 히스토리 유지 (clear 안 함)
- 답변에 Provider 뱃지로 누가 답했는지 구분

#### 5.9 API Key 보안 저장
- `Windows.Security.Credentials.PasswordVault` 사용
- Provider별 API Key 암호화 저장/로드
- 앱 재시작 시 자동 복원

#### 5.10 Ollama 자동 설치/시작
- Ollama 설치 여부 감지 (`ollama` 프로세스 또는 경로 확인)
- 미설치 시: 설치 안내 다이얼로그 + 공식 설치 페이지 연결
- Ollama 서버 미실행 시: `Process.Start("ollama", "serve")` 자동 시작
- 서버 준비 대기 (healthcheck: `GET http://localhost:11434`)

#### 5.11 하드웨어 감지 + 모델 추천
- DXGI API로 GPU VRAM 확인
- 시스템 RAM 확인
- 모델 크기 대비 VRAM/RAM 호환성 판단:
  - Compatible: VRAM >= 모델 크기 + 2GB
  - NotRecommended: VRAM >= 모델 크기 + 1GB
  - NotCompatible: VRAM < 모델 크기 + 1GB (CPU fallback 경고)
- ModelSelectionDialog에 호환성 상태 표시

#### 5.12 First Run Experience
- 첫 실행 시 Ollama 자동 감지 → 모델 없으면 추천 모델 다운로드 제안
- 하드웨어 사양에 맞는 모델 자동 추천
- 다운로드 진행률 표시 (기존 OllamaModelManager.DownloadModelAsync 활용)

#### 5.13 기타 UX 개선
- System Prompt 설정 UI (Settings에 포함)
- 대화 저장/불러오기 (탭 단위)

---

## 9. Phase 6 상세 계획 (Control 품질 향상 & 로컬라이제이션 보완)

### 6.1 XML Summary 태그 정비
- Core 라이브러리(FluentView.AI)의 모든 public 클래스/인터페이스/메서드에 `<summary>` 태그 추가
- NuGet 배포 시 IntelliSense 지원을 위한 필수 작업
- 대상: Models/, Providers/, Controls/, Helpers/, Rendering/ 하위 모든 public API

### 6.2 Control UI/UX 리터치 — 멀티 프로바이더 대화 UX
- 현재 구조: 하나의 대화에서 Provider를 자유롭게 변경 가능 (대화 히스토리 유지)
- Provider 뱃지로 각 응답이 어떤 모델에서 온 건지 이미 표시됨
- 구체적 UI 목업은 추후 확정 예정 (사용자 제공)

### 6.3 Ctrl+V 기능 확대
- 현재: 텍스트 붙여넣기만 지원
- 확장: 클립보드 이미지 붙여넣기 (스크린샷 등)
- 확장: 클립보드 파일 참조 붙여넣기 (탐색기에서 복사한 파일)
- InputContainer에서 클립보드 DataPackageView 분석 → 이미지/파일 자동 첨부

### 6.4 로컬라이제이션 누락 점검
- Core 라이브러리(chat.html) 내 하드코딩 문자열 확인
- SampleApp 전체 UI 문자열 중 x:Uid 미적용 항목 점검
- en-US / ko-KR 리소스 파일 누락 키 확인

---

## 10. Phase 7 상세 계획 (샘플앱 완성 & 배포)

### SampleApp 완성
- 전체 UI 마무리
- README 작성

### NuGet 배포
- `FluentView.AI.csproj`에 `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`
- GitHub Actions `publish.yml`: tag push 시 `dotnet pack` -> `nuget push`

### CI
- `.github/workflows/ci.yml`: PR 빌드 검증
  - `dotnet restore` -> `dotnet build` -> (향후 테스트)

---

## 11. 파일 전체 목록 (최종 구조)

```
src/FluentView.AI/
├── Controls/
│   ├── ChatPanel.xaml
│   ├── ChatPanel.xaml.cs
│   ├── InputContainer.xaml
│   ├── InputContainer.xaml.cs
│   ├── AttachmentPreviewBar.xaml
│   ├── AttachmentPreviewBar.xaml.cs
│   └── WindowHelper.cs
├── Helpers/
│   ├── ConversationManager.cs
│   ├── HardwareInfo.cs
│   ├── ModelCompatibility.cs
│   ├── OllamaHelper.cs
│   └── PasswordVaultHelper.cs
├── Models/
│   ├── ChatMessage.cs
│   ├── ChatAttachment.cs
│   ├── AiRequest.cs
│   ├── TokenUsage.cs
│   ├── AiModel.cs
│   ├── LocalModel.cs
│   └── ProviderPreset.cs
├── Providers/
│   ├── IAiProvider.cs
│   ├── IModelManager.cs
│   ├── ClaudeProvider.cs
│   ├── OpenAiProvider.cs
│   ├── OllamaProvider.cs
│   ├── GeminiProvider.cs
│   ├── OllamaModelManager.cs
│   ├── MockProvider.cs
│   ├── ProviderFactory.cs
│   └── SseReader.cs
├── Rendering/
│   ├── WebViewChatRenderer.cs
│   └── Resources/
│       └── chat.html
└── FluentView.AI.csproj

src/FluentView.AI.SampleApp/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── SettingsPanel.xaml
├── SettingsPanel.xaml.cs
├── Dialogs/
│   ├── FirstRunDialog.xaml(.cs)
│   └── ModelSelectionDialog.xaml(.cs)
├── Helpers/
│   └── AppSettings.cs
├── Settings/
│   ├── AboutPage.xaml(.cs)
│   ├── AdvancedPage.xaml(.cs)
│   ├── ModelsPage.xaml(.cs)
│   ├── PersonalizationPage.xaml(.cs)
│   └── PromptPage.xaml(.cs)
├── Strings/
│   ├── en-US/Resources.resw
│   └── ko-KR/Resources.resw
└── FluentView.AI.SampleApp.csproj
```
