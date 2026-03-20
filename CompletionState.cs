namespace AgentCraftLab.Autonomous.Playground;

/// <summary>
/// 自動完成狀態（斜線指令 + @ 檔案參照共用）。
/// </summary>
public class CompletionState
{
    public bool Active;
    public bool IsFileMode;
    public List<(string Cmd, string Desc)> Matches = [];
    public int Index = -1;
    public int LinesDrawn;

    public void Reset()
    {
        Active = false;
        IsFileMode = false;
        Index = -1;
    }
}
