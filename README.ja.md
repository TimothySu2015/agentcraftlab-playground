[English](README.en.md) | [繁體中文](README.md) | **日本語**

# AgentCraftLab Autonomous Playground

自律型AIエージェントのインタラクティブコンソール — ReActループアーキテクチャに基づき、Sub-agentの並列協調、ツール呼び出し、リアルタイム可視化をサポート。

![Autonomous Agent Console](docs/agent-craft-lab.png)

![Live Events ダッシュボード — リアルタイムイベントストリーミング + Mermaidフロー図](docs/live-events.png)

## 機能

- **ReActループ** — AIが自律的に計画・実行・反省、最大25ステップの推論
- **Sub-agent並列処理** — タスクを自動分解し、複数のSub-agentに並列委譲（最大5つ）
- **10個の組み込みツール** — Azure Web Search、DuckDuckGo、Wikipedia、URL Fetch、Calculator、JSON Parser、コードエクスプローラー（list_directory / read_file / search_code）
- **Live Eventsダッシュボード** — すべてのイベントをブラウザにリアルタイムストリーミング、検索フィルタリング、ラウンドグループ化、Mermaidフロー図対応
- **Context Compaction** — 3層圧縮戦略（ツール結果の切り詰め → ローカル圧縮 → LLM要約）、モデルのコンテキストウィンドウに応じた動的閾値
- **セッション間メモリ** — Reflexionモード、過去の実行経験から学習
- **マルチターン会話** — ラウンド間でコンテキストを蓄積、`/compact`で手動要約圧縮
- **@ファイル参照** — `@`を入力してローカルファイルを検索・内容を参照

## 前提条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure OpenAI Service](https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource) — GPTモデルを少なくとも1つデプロイ（gpt-4o-miniまたはgpt-4o推奨）
- [Grounding with Bing Search](https://learn.microsoft.com/azure/ai-services/openai/how-to/search?tabs=bing-grounding) — `azure_web_search`ツールの使用にはAzure AI FoundryでGrounding with Bing Searchリソースの接続が必要（任意 — `web_search`はDuckDuckGo経由で設定不要）

## クイックスタート

### 1. 認証情報の設定

`appsettings.json`を編集：

```json
{
  "AzureOpenAI": {
    "ApiKey": "your-azure-openai-api-key",
    "Endpoint": "https://your-resource.openai.azure.com/",
    "Model": "gpt-4o-mini"
  }
}
```

または環境変数を使用：

```bash
export AZURE_OPENAI_KEY="your-key"
export AZURE_OPENAI_ENDPOINT="https://your-resource.openai.azure.com/"
export ORCHESTRATOR_MODEL="gpt-4o-mini"  # 任意、デフォルト gpt-4o-mini
```

### 2. 実行

```bash
dotnet run
```

### 3. 会話を開始

```
Goal > NVIDIAとTeslaの最新株価動向と主要ニュースを調査・比較し、強気・弱気要因を分析
```

## コマンド一覧

| コマンド | 説明 |
|----------|------|
| `/help` | すべてのコマンドを表示 |
| `/web` | Live Eventsダッシュボードを開く（http://localhost:5199） |
| `/compact` | マルチターンコンテキストを圧縮（LLM要約） |
| `/flow` | Execution Flowフロー図の表示を切り替え |
| `/status` | セッション統計を表示 |
| `/attach <path>` | 画像/ファイルを添付（マルチモーダル対応） |
| `/memory` | セッション間実行メモリを表示 |
| `/reset` | 会話コンテキストをクリア |
| `/clear` | 画面をクリア |
| `/test` | 自動テストシナリオ |
| `/debate <topic>` | ディベートモード |
| `quit` | 終了 |

## Live Eventsダッシュボード

起動時に`http://localhost:5199`でWebサーバーが自動起動します。

- **リアルタイムストリーミング** — すべてのイベント（ツール呼び出し、Sub-agent対話、Plan、Audit）をフィルタなしでストリーミング
- **ラウンドグループ化** — 各会話ラウンドは交互の背景色とユニークなUUIDで区別
- **検索・フィルタ** — テキスト検索 + ラウンドドロップダウン + Hide TextChunkトグル
- **フロー図** — ラウンドヘッダーの「Flow」ボタンをクリックして、Mermaidシーケンス図パネルを表示
- **JSON出力** — 構造化イベント記録をエクスポート

## アーキテクチャ

```
ユーザー入力
  → TaskPlanner（複雑な目標の実行計画を自動生成）
  → SystemPromptBuilder（動的システムプロンプト + メモリ注入）
  → ReactExecutor（ReActループ、最大25ステップ）
    → MetaToolFactory（8つのメタツール：Sub-agent協調 + ピアレビュー）
    → FunctionInvokingChatClient（ツール呼び出し）
    → HybridHistoryManager（3層Context Compaction）
    → ConvergenceDetector（収束検出による早期終了）
    → AuditorReflectionEngine（独立LLMによる最終回答の監査）
  → ExecutionMemoryService（Reflexion反省 + 経験保存）
  → IAsyncEnumerable<ExecutionEvent>（イベントストリーミング → Console + SSE）
```

## 環境変数

| 変数 | 説明 | デフォルト |
|------|------|-----------|
| `AZURE_OPENAI_KEY` | Azure OpenAI APIキー | — |
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAIエンドポイント | — |
| `ORCHESTRATOR_MODEL` | メインAgentが使用するモデル | `gpt-4o-mini` |
| `WORKING_DIR` | コードエクスプローラーツールのルートディレクトリ | カレントディレクトリ |

## ライセンス

Copyright 2026 AgentCraftLab

Apache License, Version 2.0に基づきライセンスされています。詳細は[LICENSE](LICENSE)を参照してください。
