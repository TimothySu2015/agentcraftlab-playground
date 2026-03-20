using System.Text.Json;
using System.Threading.Channels;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Playground;

/// <summary>
/// 事件廣播器 — 將 ExecutionEvent 推送給所有 SSE 連線。
/// </summary>
public sealed class EventBroadcaster
{
    private readonly Lock _lock = new();
    private readonly List<Channel<string>> _clients = [];

    /// <summary>當前 Round 的唯一識別碼。每次執行前由 StartRound() 設定。</summary>
    public string CurrentRoundId { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>開始新一輪對話，產生唯一 RoundId。</summary>
    public string StartRound()
    {
        CurrentRoundId = Guid.NewGuid().ToString("N")[..8];
        return CurrentRoundId;
    }

    /// <summary>訂閱 SSE 串流，回傳可讀取的 ChannelReader。</summary>
    public ChannelReader<string> Subscribe()
    {
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        lock (_lock) { _clients.Add(channel); }
        return channel.Reader;
    }

    /// <summary>取消訂閱（連線斷開時呼叫）。</summary>
    public void Unsubscribe(ChannelReader<string> reader)
    {
        lock (_lock)
        {
            _clients.RemoveAll(c => c.Reader == reader);
        }
    }

    /// <summary>廣播事件給所有連線的 client。</summary>
    public void Broadcast(ExecutionEvent evt)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = evt.Type,
            agentName = evt.AgentName ?? "",
            text = evt.Text ?? "",
            inputType = evt.InputType,
            metadata = evt.Metadata,
            roundId = CurrentRoundId,
            timestamp = DateTime.Now.ToString("HH:mm:ss.fff")
        }, JsonOptions);

        lock (_lock)
        {
            foreach (var channel in _clients)
            {
                channel.Writer.TryWrite(json);
            }
        }
    }
}
