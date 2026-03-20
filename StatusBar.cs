namespace AgentCraftLab.Autonomous.Playground;

/// <summary>
/// 狀態列階段常數 — 控制 spinner 顏色。
/// </summary>
public static class Phases
{
    public const string Thinking = "thinking";
    public const string Tool = "tool";
    public const string SubAgent = "sub-agent";
    public const string Audit = "audit";
    public const string User = "user";
    public const string Risk = "risk";
}

/// <summary>
/// 底部固定狀態列 — Spinner 動畫 + ANSI 彩色即時狀態。
/// </summary>
public sealed class StatusBar : IDisposable
{
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private bool _active;
    private Timer? _timer;
    private int _frame;

    private int _steps;
    private int _tools;
    private long _tokens;
    private TimeSpan _elapsed;
    private string _action = "";
    private string _phase = "";
    private readonly Lock _lock = new();

    public void Start()
    {
        lock (_lock)
        {
            _active = true;
            _frame = 0;
        }

        var height = Console.WindowHeight;
        Console.Write($"\x1b[{height};1H\x1b[2K");
        Console.Write($"\x1b[1;{height - 1}r");
        Console.Write($"\x1b[{height - 1};1H\x1b[2K");
        _timer = new Timer(_ => Tick(), null, 0, 80);
    }

    public void Update(int steps, int tools, long tokens, TimeSpan elapsed, string action, string phase = "")
    {
        lock (_lock)
        {
            _steps = steps;
            _tools = tools;
            _tokens = tokens;
            _elapsed = elapsed;
            _action = action;
            if (!string.IsNullOrEmpty(phase))
            {
                _phase = phase;
            }
        }
    }

    private void Tick()
    {
        string status;
        lock (_lock)
        {
            if (!_active) return;
            var spinner = SpinnerFrames[_frame % SpinnerFrames.Length];
            _frame++;

            var phaseColor = _phase switch
            {
                Phases.Thinking => "36",
                Phases.Tool => "33",
                Phases.SubAgent => "34",
                Phases.Audit => "35",
                Phases.User => "32",
                Phases.Risk => "31",
                _ => "37"
            };

            var actionDisplay = string.IsNullOrEmpty(_action) ? "" : _action;
            if (actionDisplay.Length > 45)
            {
                actionDisplay = actionDisplay[..45] + "...";
            }

            status = $" \x1b[{phaseColor}m{spinner}\x1b[0m" +
                     $" \x1b[90mStep\x1b[0m {_steps}" +
                     $" \x1b[90m│\x1b[0m \x1b[90mTools\x1b[0m {_tools}" +
                     $" \x1b[90m│\x1b[0m \x1b[90mTokens\x1b[0m {_tokens:N0}" +
                     $" \x1b[90m│\x1b[0m {_elapsed.TotalSeconds:F1}s" +
                     $" \x1b[90m│\x1b[0m \x1b[{phaseColor}m{actionDisplay}\x1b[0m";
        }

        WriteStatusLineRaw(status);
    }

    public void ShowSessionLine(int rounds, long tokens, int tools, TimeSpan elapsed)
    {
        var status = rounds == 0
            ? " \x1b[32m●\x1b[0m \x1b[90mReady │ Waiting for input...\x1b[0m"
            : $" \x1b[36m●\x1b[0m \x1b[90mSession │\x1b[0m Rounds {rounds} \x1b[90m│\x1b[0m Tokens {tokens:N0} \x1b[90m│\x1b[0m Tools {tools} \x1b[90m│\x1b[0m {elapsed.TotalSeconds:F0}s";
        WriteStatusLineRaw(status);
    }

    private static void WriteStatusLineRaw(string status)
    {
        var height = Console.WindowHeight;
        Console.Write($"\x1b[s\x1b[{height};1H\x1b[2K\x1b[7m{status}\x1b[0m\x1b[u");
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_active) return;
            _active = false;
        }

        using var done = new ManualResetEvent(false);
        if (_timer is not null)
        {
            _timer.Dispose(done);
            done.WaitOne(500);
        }
        _timer = null;

        Console.Write($"\x1b[1;{Console.WindowHeight}r");
        ClearStatusLine();
        Console.Write($"\x1b[{Console.WindowHeight - 1};1H");
    }

    private static void ClearStatusLine()
    {
        var height = Console.WindowHeight;
        Console.Write($"\x1b[s\x1b[{height};1H\x1b[2K\x1b[u");
    }

    public void Dispose()
    {
        Stop();
        _timer?.Dispose();
    }
}
