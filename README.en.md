**English** | [繁體中文](README.md)

# AgentCraftLab Autonomous Playground

An interactive console for autonomous AI agents — built on the ReAct loop architecture with Sub-agent parallel collaboration, tool calling, and real-time visualization.

![Autonomous Agent Console](docs/agent-craft-lab.png)

![Live Events Dashboard — Real-time event streaming + Mermaid flow diagram](docs/live-events.png)

## Features

- **ReAct Loop** — AI autonomously plans, executes, and reflects, up to 25 reasoning steps
- **Sub-agent Parallelism** — Automatically decomposes tasks and delegates to multiple Sub-agents running in parallel (up to 5)
- **10 Built-in Tools** — Azure Web Search, DuckDuckGo, Wikipedia, URL Fetch, Calculator, JSON Parser, Code Explorer (list_directory / read_file / search_code)
- **Live Events Dashboard** — Real-time streaming of all events to a browser, with search filtering, Round grouping, and Mermaid flow diagrams
- **Context Compaction** — 3-layer compression strategy (truncate tool results → local compression → LLM summary), with model-aware dynamic thresholds
- **Cross-session Memory** — Reflexion mode that learns from past execution experiences
- **Multi-turn Conversations** — Accumulated context across rounds, with `/compact` for manual summarization
- **@ File Reference** — Type `@` to search and reference local file contents

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource) — Deploy at least one GPT model (gpt-4o-mini or gpt-4o recommended)

## Quick Start

### 1. Configure Credentials

Edit `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "ApiKey": "your-azure-openai-api-key",
    "Endpoint": "https://your-resource.openai.azure.com/",
    "Model": "gpt-4o-mini"
  }
}
```

Or use environment variables:

```bash
export AZURE_OPENAI_KEY="your-key"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export ORCHESTRATOR_MODEL="gpt-4o-mini"  # optional, defaults to gpt-4o-mini
```

### 2. Run

```bash
dotnet run
```

### 3. Start a Conversation

```
Goal > Research and compare NVIDIA, Tesla, and Apple's latest stock trends and major news
```

## Commands

| Command | Description |
|---------|-------------|
| `/help` | Show all commands |
| `/web` | Open Live Events dashboard (http://localhost:5199) |
| `/compact` | Compress multi-turn context (LLM summary) |
| `/flow` | Toggle Execution Flow diagram display |
| `/status` | Show session statistics |
| `/attach <path>` | Attach image/file (multimodal support) |
| `/memory` | View cross-session execution memory |
| `/reset` | Clear conversation context |
| `/clear` | Clear screen |
| `/test` | Automated test scenarios |
| `/debate <topic>` | Debate mode |
| `quit` | Exit |

## Live Events Dashboard

A web server starts automatically at `http://localhost:5199` on launch.

- **Real-time Streaming** — All events (tool calls, Sub-agent interactions, Plan, Audit) streamed unfiltered
- **Round Grouping** — Each conversation round has alternating background colors and a unique UUID
- **Search & Filter** — Text search + Round dropdown + Hide TextChunk toggle
- **Flow Diagram** — Click the "Flow" button on any Round header to open a Mermaid sequence diagram panel
- **Export JSON** — Export structured event records

## Architecture

```
User Input
  → TaskPlanner (auto-generates execution plan for complex goals)
  → SystemPromptBuilder (dynamic system prompt + memory injection)
  → ReactExecutor (ReAct loop, up to 25 steps)
    → MetaToolFactory (8 meta-tools: Sub-agent collaboration + peer review)
    → FunctionInvokingChatClient (tool calling)
    → HybridHistoryManager (3-layer Context Compaction)
    → ConvergenceDetector (convergence detection for early termination)
    → AuditorReflectionEngine (independent LLM auditing final answers)
  → ExecutionMemoryService (Reflexion + experience storage)
  → IAsyncEnumerable<ExecutionEvent> (streaming events → Console + SSE)
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `AZURE_OPENAI_KEY` | Azure OpenAI API Key | — |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI endpoint | — |
| `ORCHESTRATOR_MODEL` | Model for the main Agent | `gpt-4o-mini` |
| `WORKING_DIR` | Root directory for code explorer tools | Current directory |

## License

Copyright 2026 AgentCraftLab

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) for details.
