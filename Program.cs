using AgentCraftLab.Autonomous.Extensions;
using AgentCraftLab.Autonomous.Models;
using AgentCraftLab.Autonomous.Playground;
using AgentCraftLab.Autonomous.Services;
using AgentCraftLab.Engine.Data;
using AgentCraftLab.Engine.Extensions;
using AgentCraftLab.Engine.Models;
using AgentCraftLab.Engine.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// ============================================================
// AgentCraftLab Autonomous Agent — Spectre.Console 互動式控制台
// ============================================================

// 確保終端正確顯示 UTF-8（中文、Unicode 符號）
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// 靜音 HttpClient / Hosting / EF Core / ASP.NET Core 的 info log，只保留 Warning 以上
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("AgentCraftLab", LogLevel.Warning);

// 抑制 ASP.NET Core ConsoleLifetime 的 "Press Ctrl+C to shut down" 訊息，避免干擾 console 輸入
builder.Services.Configure<Microsoft.Extensions.Hosting.ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);

var workingDir = Environment.GetEnvironmentVariable("WORKING_DIR") ?? Directory.GetCurrentDirectory();
builder.Services.AddAgentCraftEngine(workingDir: workingDir);
builder.Services.AddAutonomousAgent();
builder.Services.AddSingleton<EventBroadcaster>();

var app = builder.Build();

// Live Events — SSE 端點
app.MapGet("/events", async (EventBroadcaster broadcaster, HttpContext ctx, CancellationToken ct) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var reader = broadcaster.Subscribe();
    try
    {
        await foreach (var json in reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { }
    finally
    {
        broadcaster.Unsubscribe(reader);
    }
});

app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// 背景啟動 Web Server（不阻塞 console 主迴圈）
const int webPort = 5199;
app.Urls.Add($"http://localhost:{webPort}");
await app.StartAsync();

var host = app;

// 初始化 SQLite 資料庫（建立 Data/ 目錄 + 補建新表）
await host.Services.InitializeDatabaseAsync();

// 從 appsettings.json 讀取 Azure OpenAI 設定（環境變數可覆蓋）
var llmConfig = builder.Configuration.GetSection("AzureOpenAI");
var apiKey = llmConfig["ApiKey"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "";
var azureEndpoint = llmConfig["Endpoint"] ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
var orchestratorModel = llmConfig["Model"] ?? Environment.GetEnvironmentVariable("ORCHESTRATOR_MODEL") ?? "gpt-4o-mini";
const string provider = "azure-openai";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(azureEndpoint))
{
    AnsiConsole.MarkupLine("[red]Error: 請在 appsettings.json 設定 Azure OpenAI 憑證[/]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.MarkupLine("[yellow]appsettings.json 設定範例：[/]");
    AnsiConsole.MarkupLine("[dim]{[/]");
    AnsiConsole.MarkupLine("[dim]  \"AzureOpenAI\": {[/]");
    AnsiConsole.MarkupLine("[dim]    \"ApiKey\": \"your-api-key\",[/]");
    AnsiConsole.MarkupLine("[dim]    \"Endpoint\": \"https://your-resource.openai.azure.com/\",[/]");
    AnsiConsole.MarkupLine("[dim]    \"Model\": \"gpt-4o-mini\"[/]");
    AnsiConsole.MarkupLine("[dim]  }[/]");
    AnsiConsole.MarkupLine("[dim]}[/]");
    AnsiConsole.MarkupLine("");
    AnsiConsole.MarkupLine("[cyan]申請 Azure OpenAI Service：[/]");
    AnsiConsole.MarkupLine("[dim]https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource[/]");
    return;
}

var credentials = new Dictionary<string, ProviderCredential>
{
    [provider] = new() { ApiKey = apiKey, Endpoint = azureEndpoint, Model = orchestratorModel }
};

// 斜線指令定義（用於自動完成）
var slashCommands = new (string Cmd, string Desc)[]
{
    ("/help", "顯示所有指令"),
    ("/attach", "附加圖片/檔案"),
    ("/clear", "清除畫面"),
    ("/status", "顯示 Session 統計"),
    ("/compact", "壓縮 Multi-turn 上下文"),
    ("/flow", "切換 Execution Flow 圖表"),
    ("/web", "開啟 Live Events 網頁"),
    ("/reset", "清除對話上下文"),
    ("/memory", "查看執行記憶"),
    ("/test", "自動化測試場景"),
    ("/debate", "辯論模式"),
};

// 可用工具清單（唯一定義點）
var availableTools = new[] { "azure_web_search", "web_search", "wikipedia", "calculator", "get_datetime", "url_fetch", "json_parser", "list_directory", "read_file", "search_code" };

// Banner
var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
AnsiConsole.Write(new FigletText("AgentCraftLab").Color(Color.Cyan1));
AnsiConsole.Write(new Rule($"[cyan]Autonomous Agent Console[/] [dim]v{version}[/]").RuleStyle("grey"));

var infoTable = new Table().NoBorder().HideHeaders().AddColumn("").AddColumn("");
infoTable.AddRow("[grey]Provider[/]", $"[white]{provider}[/]");
infoTable.AddRow("[grey]Model[/]", $"[white]{orchestratorModel}[/]");
infoTable.AddRow("[grey]Tools[/]", $"[white]{string.Join(", ", availableTools)}[/]");
infoTable.AddRow("[grey]Live Events[/]", $"[dim]http://localhost:{webPort} — /web 開啟瀏覽器[/]");
infoTable.AddRow("[grey]Hints[/]", "[dim]/help — 指令清單 | /test — 測試 | /debate — 辯論 | quit — 離開[/]");
AnsiConsole.Write(infoTable);

// 把游標推到終端底部，讓輸入框貼底
var bannerLines = Console.GetCursorPosition().Top;
var padding = Console.WindowHeight - bannerLines - 2; // -2 留給 prompt
for (var i = 0; i < padding; i++) AnsiConsole.WriteLine();

await using var scope = host.Services.CreateAsyncScope();
var executor = scope.ServiceProvider.GetRequiredService<ReactExecutor>();
var humanBridge = scope.ServiceProvider.GetRequiredService<HumanInputBridge>();
string? previousResult = null;
FileAttachment? pendingAttachment = null; // /attach 暫存的檔案附件
const int maxToolResultsContextChars = 4000;

// /compact 用的 LLM client（lazy 建立，避免重複建立）
Microsoft.Extensions.AI.IChatClient? compactClient = null;

// === Session 累計 + 狀態列 ===
using var statusBar = new StatusBar();
var sessionTokens = 0L;
var sessionToolCalls = 0;
var sessionSteps = 0;
var sessionRounds = 0;
var sessionSw = System.Diagnostics.Stopwatch.StartNew();

// 輸入編輯器（歷史瀏覽 + 斜線指令自動完成）
var inputHistory = new List<string>();
var editor = new ReadLineEditor(inputHistory, slashCommands, workingDir);
var showFlowDiagram = false;
var broadcaster = host.Services.GetRequiredService<EventBroadcaster>();

// 顯示初始狀態列（不設 scroll region，只寫最後一行）
statusBar.ShowSessionLine(sessionRounds, sessionTokens, sessionToolCalls, sessionSw.Elapsed);

while (true)
{
    // 確保游標在狀態列上方（倒數第 2 行）再顯示 prompt
    Console.Write($"\x1b[{Console.WindowHeight - 1};1H");
    var input = editor.ReadLine("\x1b[32mGoal > \x1b[0m");

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    // 自動偵測貼上的檔案路徑 → 自動 /attach（圖片/PDF/二進位檔）
    var trimmedInput = input.Trim().Trim('"');
    if (!trimmedInput.StartsWith('/') && File.Exists(trimmedInput))
    {
        var ext = Path.GetExtension(trimmedInput).ToLowerInvariant();
        var autoAttachExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg",
            ".pdf", ".ico", ".tiff", ".tif"
        };

        if (autoAttachExtensions.Contains(ext))
        {
            // 模擬 /attach 行為
            input = $"/attach {trimmedInput}";
        }
    }

    // 斜線指令處理
    if (input.StartsWith('/'))
    {
        var cmd = input.Split(' ', 2)[0].ToLowerInvariant();

        switch (cmd)
        {
            case "/help":
            {
                var helpTable = new Table().NoBorder().AddColumn("指令").AddColumn("說明");
                foreach (var (slashCmd, desc) in slashCommands)
                {
                    helpTable.AddRow($"[cyan]{slashCmd}[/]", desc);
                }
                helpTable.AddRow("[cyan]quit[/]", "離開");
                AnsiConsole.Write(helpTable);
                continue;
            }

            case "/clear":
            {
                Console.Clear();
                Console.Write("\x1b[H\x1b[2J"); // 完全清畫面
                // 重畫 banner
                AnsiConsole.Write(new Rule("[cyan]Autonomous Agent Console[/]").RuleStyle("grey"));
                statusBar.ShowSessionLine(sessionRounds, sessionTokens, sessionToolCalls, sessionSw.Elapsed);
                continue;
            }

            case "/status":
            {
                var statusTable = new Table().NoBorder().AddColumn("").AddColumn("");
                statusTable.AddRow("[grey]Rounds[/]", $"[white]{sessionRounds}[/]");
                statusTable.AddRow("[grey]Total Steps[/]", $"[white]{sessionSteps}[/]");
                statusTable.AddRow("[grey]Total Tools[/]", $"[white]{sessionToolCalls}[/]");
                statusTable.AddRow("[grey]Total Tokens[/]", $"[white]{sessionTokens:N0}[/]");
                statusTable.AddRow("[grey]Session Time[/]", $"[white]{sessionSw.Elapsed.TotalMinutes:F1} min[/]");
                statusTable.AddRow("[grey]Multi-turn[/]", previousResult is not null ? "[green]Active[/]" : "[dim]None[/]");
                AnsiConsole.Write(statusTable);
                continue;
            }

            case "/flow":
            {
                showFlowDiagram = !showFlowDiagram;
                AnsiConsole.MarkupLine(showFlowDiagram
                    ? "[green]Execution Flow 圖表已開啟[/]"
                    : "[yellow]Execution Flow 圖表已關閉[/]");
                continue;
            }

            case "/web":
            {
                var url = $"http://localhost:{webPort}";
                AnsiConsole.MarkupLine($"[green]Opening {url}[/]");
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    AnsiConsole.MarkupLine($"[yellow]無法自動開啟瀏覽器，請手動打開: {url}[/]");
                }
                continue;
            }

            case "/compact":
            {
                if (previousResult is null)
                {
                    AnsiConsole.MarkupLine("[dim]沒有上下文可壓縮[/]");
                    continue;
                }

                var beforeLen = previousResult.Length;
                AnsiConsole.MarkupLine($"[cyan]壓縮中... ({beforeLen:N0} chars)[/]");

                try
                {
                    compactClient ??= AgentCraftLab.Engine.Strategies.AgentContextBuilder.CreateChatClient(
                        provider, apiKey, azureEndpoint, orchestratorModel);
                    var compactMessages = new List<Microsoft.Extensions.AI.ChatMessage>
                    {
                        new(Microsoft.Extensions.AI.ChatRole.System,
                            """
                            Summarize the following AI agent execution result into a concise progress report.
                            Preserve: key findings, data points, numbers, decisions, tool results.
                            Remove: verbose explanations, repeated information, formatting boilerplate.
                            Use bullet points. Be brief but preserve all important facts.
                            Output in the same language as the content.
                            """),
                        new(Microsoft.Extensions.AI.ChatRole.User, previousResult)
                    };
                    var compactResponse = await compactClient.GetResponseAsync(compactMessages);
                    var summary = compactResponse.Text ?? previousResult;

                    previousResult = summary;
                    var afterLen = previousResult.Length;
                    var ratio = (1.0 - (double)afterLen / beforeLen) * 100;
                    AnsiConsole.MarkupLine($"[green]壓縮完成：{beforeLen:N0} → {afterLen:N0} chars（減少 {ratio:F0}%）[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]壓縮失敗：{Markup.Escape(ex.Message)}[/]");
                }
                continue;
            }

            case "/reset":
            {
                previousResult = null;
                pendingAttachment = null;
                AnsiConsole.MarkupLine("[yellow]Multi-turn 上下文已清除[/]");
                continue;
            }

            case "/attach":
            {
                var pathArg = input.Length > 8 ? input[8..].Trim().Trim('"') : "";
                if (string.IsNullOrWhiteSpace(pathArg))
                {
                    if (pendingAttachment is not null)
                    {
                        AnsiConsole.MarkupLine($"[dim]目前附件：{Markup.Escape(pendingAttachment.FileName)}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]用法：/attach <檔案路徑>（支援圖片、PDF）[/]");
                    }
                    continue;
                }

                if (!File.Exists(pathArg))
                {
                    AnsiConsole.MarkupLine($"[red]檔案不存在：{Markup.Escape(pathArg)}[/]");
                    continue;
                }

                try
                {
                    var fileBytes = await File.ReadAllBytesAsync(pathArg);
                    var ext = Path.GetExtension(pathArg).ToLowerInvariant();
                    var mimeType = ext switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".bmp" => "image/bmp",
                        ".pdf" => "application/pdf",
                        ".svg" => "image/svg+xml",
                        _ => "application/octet-stream"
                    };

                    pendingAttachment = new FileAttachment
                    {
                        FileName = Path.GetFileName(pathArg),
                        MimeType = mimeType,
                        Data = fileBytes
                    };

                    var sizeKb = fileBytes.Length / 1024.0;
                    AnsiConsole.MarkupLine(
                        $"[green]✓ 已附加：[/][white]{Markup.Escape(pendingAttachment.FileName)}[/] " +
                        $"[dim]({sizeKb:F0} KB, {mimeType})[/]");
                    AnsiConsole.MarkupLine("[dim]  下次送出訊息時會一併傳給 AI。輸入 /reset 可清除。[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]讀取失敗：{Markup.Escape(ex.Message)}[/]");
                }
                continue;
            }

            case "/memory":
            {
                using var memScope = host.Services.CreateScope();
                var memStore = memScope.ServiceProvider.GetService<IExecutionMemoryStore>();
                if (memStore is null)
                {
                    AnsiConsole.MarkupLine("[yellow]記憶系統未啟用[/]");
                }
                else
                {
                    var memories = await memStore.SearchAsync("local", "", limit: 5);
                    if (memories.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[dim]尚無執行記憶[/]");
                    }
                    else
                    {
                        var memTable = new Table()
                            .Border(TableBorder.Rounded)
                            .AddColumn("時間")
                            .AddColumn("關鍵字")
                            .AddColumn("狀態")
                            .AddColumn("步驟")
                            .AddColumn("Tokens");
                        foreach (var m in memories)
                        {
                            var status = m.Succeeded ? "[green]成功[/]" : "[red]失敗[/]";
                            memTable.AddRow(
                                $"[dim]{m.CreatedAt:MM-dd HH:mm}[/]",
                                Markup.Escape(m.GoalKeywords.Length > 30 ? m.GoalKeywords[..30] + "..." : m.GoalKeywords),
                                status,
                                m.StepCount.ToString(),
                                m.TokensUsed.ToString("N0"));
                        }
                        AnsiConsole.Write(memTable);
                    }
                }
                continue;
            }

            case "/test" or "/debate":
                break; // 不處理，讓後面的邏輯接手

            default:
                AnsiConsole.MarkupLine($"[yellow]未知指令：{Markup.Escape(cmd)}。輸入 /help 查看可用指令。[/]");
                continue;
        }
    }

    // 測試模式覆寫
    int? testMaxIterations = null;
    long? testMaxTokens = null;

    // /test 指令：自動化測試場景
    if (input.Equals("/test", StringComparison.OrdinalIgnoreCase))
    {
        var testCases = new (string Label, string Goal, int MaxIter, long MaxTokens)[]
        {
            ("簡單問答（應 1-2 步完成）", "現在幾點", 5, 30_000),
            ("收斂偵測（應提前終止）", "搜尋 xyznotexist98765 這個不存在的東西", 10, 50_000),
            ("複雜任務+計劃（應有 Planning + Sub-agent）", "比較 Python 和 Rust 在 AI 開發上的優缺點", 15, 80_000),
            ("記憶測試（跑兩次相似任務）", "查詢 AI Agent 框架的最新發展趨勢", 10, 50_000),
        };

        AnsiConsole.WriteLine();
        for (var ti = 0; ti < testCases.Length; ti++)
        {
            AnsiConsole.MarkupLine($"  [cyan]{ti + 1}[/] {Markup.Escape(testCases[ti].Label)}");
        }

        var choice = AnsiConsole.Prompt(
            new TextPrompt<int>("[green]選擇場景 (1-4) >[/] ")
                .Validate(v => v >= 1 && v <= testCases.Length
                    ? ValidationResult.Success()
                    : ValidationResult.Error("請輸入 1-4")));

        var (label, testGoal, maxIter, maxTokens) = testCases[choice - 1];
        AnsiConsole.MarkupLine($"\n[cyan]▶ 測試：{Markup.Escape(label)}[/]");
        AnsiConsole.MarkupLine($"[dim]  Goal: {Markup.Escape(testGoal)} | MaxIter: {maxIter} | MaxTokens: {maxTokens:N0}[/]");

        input = testGoal;
        testMaxIterations = maxIter;
        testMaxTokens = maxTokens;
    }

    // 指令解析
    var skills = new List<string>();
    var goal = input;
    if (input.StartsWith("/debate ", StringComparison.OrdinalIgnoreCase))
    {
        goal = input[8..].Trim();
        skills.Add("debate_council");
    }

    // @ 檔案參照解析：讀取檔案內容注入 goal
    if (goal.Contains("@"))
    {
        goal = ResolveFileReferences(goal);
    }

    // Multi-turn：帶上前一輪結果
    if (previousResult is not null)
    {
        goal = $"[Previous result]\n{previousResult}\n\n[New request]\n{goal}";
    }

    // 附件提示
    if (pendingAttachment is not null)
    {
        AnsiConsole.MarkupLine($"[dim]📎 {Markup.Escape(pendingAttachment.FileName)}[/]");
    }

    var request = new AutonomousRequest
    {
        Goal = goal,
        Credentials = credentials,
        Provider = provider,
        Model = orchestratorModel,
        Attachment = pendingAttachment,
        AvailableTools = availableTools.ToList(),
        AvailableSkills = skills,
        Budget = new TokenBudget { MaxTotalTokens = testMaxTokens ?? 200_000 },
        ToolLimits = new ToolCallLimits { MaxTotalCalls = 40, DefaultPerToolLimit = 15 },
        MaxIterations = testMaxIterations ?? 25
    };

    // 送出後清除附件（一次性使用）
    pendingAttachment = null;

    AnsiConsole.WriteLine();

    var roundId = broadcaster.StartRound();

    using var cts = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cts.Cancel(); };
    Console.CancelKeyPress += cancelHandler;

    var responseText = new System.Text.StringBuilder();
    var toolResultsContext = new System.Text.StringBuilder(); // 收集工具結果，供多輪上下文使用
    var flowEvents = new List<ExecutionEvent>();
    var toolCallCount = 0;
    var totalTokens = 0L;
    var stepCount = 0;
    var currentAction = "";
    var sw = System.Diagnostics.Stopwatch.StartNew();

    // 啟動底部狀態列
    statusBar.Start();

    try
    {
        await foreach (var evt in executor.ExecuteAsync(request, cts.Token))
        {
            broadcaster.Broadcast(evt);

            if (evt.Type is not EventTypes.TextChunk)
            {
                flowEvents.Add(evt);
            }

            switch (evt.Type)
            {
                case EventTypes.AgentStarted:
                    currentAction = "Initializing...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Thinking);
                    AnsiConsole.MarkupLine($"[yellow]▶ {Markup.Escape(evt.AgentName ?? "")} started[/]");
                    break;

                case EventTypes.TextChunk:
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, "Generating response...", Phases.Thinking);
                    Console.Write(evt.Text);
                    responseText.Append(evt.Text);
                    break;

                case EventTypes.ToolCall:
                    toolCallCount++;
                    currentAction = $"Calling {FlowDiagramGenerator.Truncate(FlowDiagramGenerator.ExtractToolName(evt.Text), 30)}()...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Tool);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  [cyan]◆ Tool:[/] [white]{Markup.Escape(Truncate(evt.Text, 80))}[/]");
                    break;

                case EventTypes.ToolResult:
                    currentAction = "Processing result...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Tool);
                    AnsiConsole.MarkupLine($"  [dim]← {Markup.Escape(Truncate(evt.Text, 120))}[/]");
                    // 收集工具結果到多輪上下文（上限 4000 字元，避免 prompt 膨脹）
                    if (toolResultsContext.Length < maxToolResultsContextChars && !string.IsNullOrWhiteSpace(evt.Text))
                    {
                        toolResultsContext.AppendLine(evt.Text);
                    }
                    break;

                case EventTypes.PlanGenerated:
                case EventTypes.PlanRevised:
                {
                    var isPlanRevised = evt.Type == EventTypes.PlanRevised;
                    currentAction = isPlanRevised ? "Re-planning..." : "Planning...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Thinking);
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine(isPlanRevised
                        ? "  [darkorange]🔄 Plan Revised[/]"
                        : "  [mediumpurple1]📋 Execution Plan[/]");
                    foreach (var line in (evt.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(line)}[/]");
                    }
                    break;
                }

                case EventTypes.SubAgentCreated:
                    var subName = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    var subInstructions = evt.Metadata?.GetValueOrDefault(MetadataKeys.Instructions) ?? "";
                    currentAction = $"Created [{subName}]";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.SubAgent);
                    AnsiConsole.MarkupLine($"  [blue]⊕ Sub-agent created:[/] [white]{Markup.Escape(subName)}[/]");
                    if (!string.IsNullOrWhiteSpace(subInstructions))
                    {
                        AnsiConsole.MarkupLine($"    [deepskyblue1]💡 Instructions:[/] [grey]{Markup.Escape(subInstructions)}[/]");
                    }
                    break;

                case EventTypes.SubAgentAsked:
                    var askName = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    var askMsg = evt.Metadata?.GetValueOrDefault(MetadataKeys.Message) ?? "";
                    currentAction = $"Asking [{askName}]...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.SubAgent);
                    AnsiConsole.MarkupLine($"  [blue]→ Ask {Markup.Escape(askName)}:[/] [dim]{Markup.Escape(Truncate(askMsg, 80))}[/]");
                    break;

                case EventTypes.SubAgentResponded:
                    var respName = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    currentAction = $"[{respName}] responded";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.SubAgent);
                    AnsiConsole.MarkupLine($"  [blue]← {Markup.Escape(respName)}:[/] [dim]{Markup.Escape(Truncate(evt.Text, 100))}[/]");
                    // 收集 sub-agent 結果到多輪上下文
                    if (toolResultsContext.Length < maxToolResultsContextChars && !string.IsNullOrWhiteSpace(evt.Text))
                    {
                        toolResultsContext.AppendLine($"[{respName}] {evt.Text}");
                    }
                    break;

                case EventTypes.ReasoningStep:
                    stepCount++;
                    var tokens = evt.Metadata?.GetValueOrDefault(MetadataKeys.Tokens) ?? "0";
                    if (long.TryParse(tokens, out var t)) totalTokens += t;
                    currentAction = $"Thinking... (step {stepCount})";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Thinking);
                    break;

                case EventTypes.WaitingForInput:
                    statusBar.Stop();
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[magenta]❓ {Markup.Escape(evt.Text ?? "")}[/]");
                    if (!string.IsNullOrWhiteSpace(evt.Choices))
                    {
                        AnsiConsole.MarkupLine($"[dim]   Options: {Markup.Escape(evt.Choices)}[/]");
                    }
                    var answer = editor.ReadLine("\x1b[32mAnswer > \x1b[0m");
                    humanBridge.SubmitInput(answer);
                    statusBar.Start();
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, "Resuming...", Phases.Thinking);
                    break;

                case EventTypes.WaitingForRiskApproval:
                    var riskTool = evt.Metadata?.GetValueOrDefault(MetadataKeys.ToolName) ?? "";
                    var riskLevel = evt.Metadata?.GetValueOrDefault(MetadataKeys.RiskLevel) ?? "";
                    statusBar.Stop();
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[red]⚠ Risk approval required:[/] [white]{Markup.Escape(riskTool)}[/] [dim](level: {Markup.Escape(riskLevel)})[/]");
                    AnsiConsole.MarkupLine("[dim]   Type 'approve' or 'reject'[/]");
                    var approval = editor.ReadLine("\x1b[33mApprove/Reject > \x1b[0m");
                    humanBridge.SubmitInput(string.IsNullOrWhiteSpace(approval) ? "reject" : approval);
                    statusBar.Start();
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, "Resuming...", Phases.Thinking);
                    break;

                case EventTypes.RiskApprovalResult:
                    var approved = evt.Metadata?.GetValueOrDefault(MetadataKeys.Approved) ?? "";
                    AnsiConsole.MarkupLine(approved == "True"
                        ? "  [green]✓ Approved[/]"
                        : "  [red]✗ Rejected[/]");
                    break;

                case EventTypes.AuditStarted:
                    currentAction = "Auditing answer...";
                    statusBar.Update(stepCount, toolCallCount, totalTokens, sw.Elapsed, currentAction, Phases.Audit);
                    break;

                case EventTypes.AuditCompleted:
                    var verdict = evt.Metadata?.GetValueOrDefault(MetadataKeys.Verdict) ?? "";
                    var verdictColor = verdict == "Pass" ? "green" : "yellow";
                    AnsiConsole.MarkupLine($"  [{verdictColor}]Audit: {Markup.Escape(verdict)}[/]");
                    break;

                case EventTypes.UserInputReceived:
                case EventTypes.WorkflowCompleted:
                    break;

                case EventTypes.AgentCompleted:
                    // 累計到 session
                    sessionTokens += totalTokens;
                    sessionToolCalls += toolCallCount;
                    sessionSteps += stepCount;
                    sessionRounds++;

                    // 停止 scroll region（session line 在迴圈尾端統一畫）
                    statusBar.Stop();
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[green]Completed[/]").RuleStyle("grey"));

                    AnsiConsole.MarkupLine(
                        $"[grey]Steps[/] [white]{stepCount}[/][dim]({sessionSteps})[/] │ " +
                        $"[grey]Tools[/] [white]{toolCallCount}[/][dim]({sessionToolCalls})[/] │ " +
                        $"[grey]Tokens[/] [white]{totalTokens:N0}[/][dim]({sessionTokens:N0})[/] │ " +
                        $"[grey]Time[/] [white]{sw.Elapsed.TotalSeconds:F1}s[/] [dim](round {sessionRounds})[/]");

                    // 輸出執行流程圖（/flow 開啟時才顯示）
                    if (showFlowDiagram)
                    {
                        var diagram = FlowDiagramGenerator.Generate(flowEvents);
                        if (!string.IsNullOrWhiteSpace(diagram))
                        {
                            AnsiConsole.WriteLine();
                            AnsiConsole.Write(new Rule("[cyan]Execution Flow[/]").RuleStyle("grey"));
                            AnsiConsole.Write(new Panel(new Text(diagram))
                                .Header("[cyan]Mermaid Sequence Diagram[/]")
                                .BorderColor(Color.Grey)
                                .Expand());
                        }
                    }
                    break;

                case EventTypes.Error:
                    AnsiConsole.MarkupLine($"[red]✖ {Markup.Escape(evt.Text ?? "")}[/]");
                    break;

                default:
                    AnsiConsole.MarkupLine($"  [dim]\\[{Markup.Escape(evt.Type)}] {Markup.Escape(Truncate(evt.Text, 100))}[/]");
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        statusBar.Stop();
        AnsiConsole.MarkupLine("\n[yellow]\\[Cancelled][/]");
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }

    if (responseText.Length > 0)
    {
        // 多輪上下文：同時保留工具原始結果 + AI 最終回應，避免下一輪丟失結構化資料
        if (toolResultsContext.Length > 0)
        {
            previousResult = $"[Tool/search results from this round]\n{toolResultsContext}\n\n[AI response]\n{responseText}";
        }
        else
        {
            previousResult = responseText.ToString();
        }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule().RuleStyle("grey"));
    AnsiConsole.WriteLine();

    // 所有輸出結束後，重新畫 session line（確保不被捲掉）
    statusBar.ShowSessionLine(sessionRounds, sessionTokens, sessionToolCalls, sessionSw.Elapsed);
}

statusBar.Stop();
await app.StopAsync();
AnsiConsole.MarkupLine("[dim]Bye![/]");
return;

// Truncate 委派給 FlowDiagramGenerator（避免重複定義）
static string Truncate(string? text, int max) => FlowDiagramGenerator.Truncate(text, max);

/// <summary>
/// 解析輸入中的 @filepath 參照，讀取檔案內容注入文字。
/// 格式：@C:\path\to\file.cs 或 @/path/to/file.cs
/// </summary>
static string ResolveFileReferences(string input)
{
    // 找出所有 @filepath（@ 後面跟著有效路徑字元直到空格或結尾）
    var result = input;
    var fileContents = new System.Text.StringBuilder();
    var resolved = new List<string>();

    var parts = input.Split(' ');
    var cleanParts = new List<string>();

    foreach (var part in parts)
    {
        if (part.StartsWith('@') && part.Length > 1)
        {
            var filePath = part[1..];
            if (File.Exists(filePath))
            {
                try
                {
                    var content = File.ReadAllText(filePath);
                    var fileName = Path.GetFileName(filePath);

                    // 截斷超長檔案（保護 context）
                    if (content.Length > 10000)
                    {
                        content = content[..10000] + $"\n... [{content.Length - 10000:N0} chars truncated]";
                    }

                    fileContents.AppendLine($"\n[File: {fileName}]");
                    fileContents.AppendLine("```");
                    fileContents.AppendLine(content);
                    fileContents.AppendLine("```");
                    resolved.Add(fileName);
                    AnsiConsole.MarkupLine($"[dim]📄 {Markup.Escape(fileName)} ({new FileInfo(filePath).Length / 1024.0:F0} KB)[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]無法讀取 {Markup.Escape(filePath)}: {Markup.Escape(ex.Message)}[/]");
                    cleanParts.Add(part);
                }
            }
            else
            {
                cleanParts.Add(part); // 不是有效檔案路徑，保留原文
            }
        }
        else
        {
            cleanParts.Add(part);
        }
    }

    var cleanInput = string.Join(' ', cleanParts).Trim();

    if (fileContents.Length > 0)
    {
        return $"{cleanInput}\n\n[Referenced files]{fileContents}";
    }

    return cleanInput;
}
