<p align="right">
  <a href="README.md">English</a> · <strong>한국어</strong>
</p>

# FieldCure AssistStudio

<picture>
  <source srcset="docs/hero-ko.webp" type="image/webp">
  <img src="docs/hero-ko.png" alt="AssistStudio — AI는 갈아끼워도, 도구와 작업은 그대로." width="100%">
</picture>

**AI는 갈아끼워도, 도구는 멈추지 않습니다.**

[![NuGet Providers](https://img.shields.io/nuget/v/FieldCure.Ai.Providers?label=Ai.Providers)](https://www.nuget.org/packages/FieldCure.Ai.Providers)
[![NuGet Core](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Core?label=Core)](https://www.nuget.org/packages/FieldCure.AssistStudio.Core)
[![NuGet Controls](https://img.shields.io/nuget/v/FieldCure.AssistStudio.Controls.WinUI?label=Controls.WinUI)](https://www.nuget.org/packages/FieldCure.AssistStudio.Controls.WinUI)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%2B-purple)](https://dotnet.microsoft.com/)

---

## AssistStudio란?

Claude · GPT · Gemini · Ollama 등 어떤 AI 모델을 쓰든 **같은 도구·지식베이스·작업 파일이 따라오는** Windows 네이티브 (WinUI 3) AI 워크스페이스 앱입니다. 대화는 사용자 PC에 `.astx` 파일로 영속되고, 5개의 MCP 도구 서버가 즉시 동작하며, 여러 specialist 서브 에이전트를 병렬로 부려 답을 깊게 만듭니다.

| 기존 | AssistStudio |
|---|---|
| ChatGPT / Claude.ai — 클라우드 종속, 단일 provider | 멀티 provider, 로컬 영속 |
| Claude Desktop — MCP 풍부하지만 Anthropic 전용 | 모든 provider에서 같은 MCP 도구 |
| LangChain / LiteLLM — 개발자 SDK | 일반 사용자용 데스크톱 앱 |

---

## 핵심 가치 — Foundation + 3 Pillars

### Foundation: Provider 추상화 *(사용자가 의식 안 해도 됨)*

> *"어떤 AI든 같은 방식으로 부린다."*

- **통합 대화 스키마** — Claude로 시작한 대화를 GPT·Gemini·Ollama에 그대로 이어 붙여도, 히스토리·도구 호출 기록·첨부가 형태에 맞게 재구성됩니다.
- **MCP 호환 레이어** — Anthropic tool_use, OpenAI function calling, Gemini function declarations 등 각 provider 규격에 자동 매핑. MCP를 native 지원하지 않는 GPT·Gemini·Ollama에서도 같은 도구 생태계 사용 가능.

### ② 즉시 작업 가능 (Ready-to-Work Capability)

> *"지금 바로 일 시킬 수 있다."*

- **Built-in MCP Suite (5종)** — Filesystem · RAG · Essentials · Outbox · Runner. NuGet에서 자동 fetch & 마이너 버전 자동 업데이트.
- **Capability-Aware UX** — provider별 PDF · 오디오 · thinking 등 지원 기능을 자동 감지하여 UI가 적응. 모델을 바꾸면 첨부 가능한 파일 타입과 토글이 즉시 변합니다.

### ③ 병렬 에이전트 워크플로 (Parallel Agentic Workflow)

> *"AI가 어떻게 일하는지 보이고, 통제할 수 있다."*

- **Role-Specialized Sub-Agents** — `delegate_task`로 specialist 여러 명을 병렬 fan-out. 각 sub-agent는 컨텍스트 격리 상태로 독립 실행.
- **Tool Approval / Elicitation** — 도구 실행 전 inline 승인 패널. MCP 서버가 추가 입력을 요청하면 동적 입력 폼이 자동 생성됩니다.

### ④ 영속 워크스페이스 (Persistent Workspace)

> *"내 작업이 내 자산이다."*

- **`.astx` 아카이브** — 대화·미디어·매니페스트를 묶은 zip 패키지. 이메일·USB로 옮길 수 있는 대화 자산.
- **분기 대화 (Branching)** — 한 대화에서 여러 갈래 탐색. 메시지를 편집하면 새 분기로 갈라지고, 트리는 그대로 보존됩니다.
- **대화별 워크스페이스 폴더** — AI가 만들거나 편집하는 작업 파일이 대화에 묶임.
- **RAG + Memory** — 멀티 지식베이스(BM25 + 벡터 하이브리드)와 영속 메모리.

---

## 빠른 시작

### 1. 패키지 설치

```bash
dotnet add package FieldCure.AssistStudio.Core
dotnet add package FieldCure.AssistStudio.Controls.WinUI
```

### 2. Provider 생성 + ChatPanel 연결

```csharp
using FieldCure.Ai.Providers;

// BYOK — 사용자가 자기 API 키 입력
var provider = new ClaudeProvider(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-6");
```

```xml
<Page xmlns:assist="using:FieldCure.AssistStudio.Controls">
    <assist:ChatPanel x:Name="Chat"
                      Placeholder="무엇이든 물어보세요..."
                      Theme="System" />
</Page>
```

```csharp
// Code-behind
Chat.Provider = provider;
```

스트리밍 · 마크다운 · 코드 하이라이팅 · thinking · 대화 분기까지 모두 작동하는 AI 채팅이 한 번에 완성됩니다.

---

## 워크스페이스 앱 실행

소스에서 빌드:

```bash
dotnet run --project src/AssistStudio
```

또는 Microsoft Store 출시 진행 중 — v1.0 정식 출시 후 링크 제공 예정.

---

## 더 알아보기

전체 기술 문서 · 아키텍처 · API 레퍼런스 · 모든 패키지 목록은 [English README](README.md)를 참고하세요. 이 한국어 README는 핵심 가치와 빠른 시작에 집중되어 있습니다.

주요 문서:

- [English README](README.md) — 전체 기술 문서 (영문)
- [Custom Provider 작성 가이드](docs/CustomProvider.md) (영문)
- [의존성 그래프](docs/dependencies.md) (영문)
- [ADR-001 MCP 자격증명 정책](docs/ADR-001-MCP-Credential-Management.md) (영문)

---

## 라이선스

[MIT](LICENSE) — Copyright (c) 2026 FieldCure Co., Ltd.
