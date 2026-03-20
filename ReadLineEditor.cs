namespace AgentCraftLab.Autonomous.Playground;

/// <summary>
/// 互動式行編輯器 — 支援上下鍵歷史瀏覽 + 斜線指令自動完成 + @ 檔案參照。
/// </summary>
public sealed class ReadLineEditor
{
    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".git", ".vs", ".idea", "__pycache__", "dist", "build"
    };

    /// <summary>二進位/多模態檔案副檔名（應使用 /attach，不走 @ 文字注入）。</summary>
    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 圖片
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff", ".tif",
        // 文件（多模態）
        ".pdf",
        // 影音
        ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv", ".flac", ".ogg", ".webm",
        // 壓縮
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2",
        // 二進位 / 資料庫
        ".exe", ".dll", ".so", ".dylib", ".bin", ".dat",
        ".db", ".db-wal", ".db-shm", ".db-journal", ".sqlite", ".sqlite-wal", ".sqlite-shm",
        // 字型
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        // Office（二進位格式）
        ".doc", ".xls", ".ppt",
    };

    private const int MaxFileResults = 10;
    private const int MaxFileScanDepth = 4;

    private readonly List<string> _history;
    private readonly (string Cmd, string Desc)[] _commands;
    private readonly string? _workingDir;

    public ReadLineEditor(List<string> history, (string Cmd, string Desc)[]? commands = null, string? workingDir = null)
    {
        _history = history;
        _commands = commands ?? [];
        _workingDir = workingDir;
    }

    /// <summary>讀取一行輸入（阻塞式）。</summary>
    public string ReadLine(string prompt)
    {
        Console.Write(prompt);
        var buffer = new List<char>();
        var historyIndex = _history.Count;
        var cursorPos = 0;
        var cs = new CompletionState();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            // 自動完成模式下的按鍵
            if (cs.Active && cs.Matches.Count > 0)
            {
                if (HandleCompletionKey(key, prompt, buffer, ref cursorPos, cs))
                {
                    continue;
                }
            }

            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    ClearCompletions(cs);
                    Console.WriteLine();
                    var result = new string(buffer.ToArray());
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        _history.Add(result);
                    }
                    return result;

                case ConsoleKey.Backspace:
                    if (cursorPos > 0)
                    {
                        buffer.RemoveAt(cursorPos - 1);
                        cursorPos--;
                        Redraw(prompt, buffer, cursorPos);
                        UpdateCompletions(buffer, prompt, cursorPos, cs);
                    }
                    break;

                case ConsoleKey.Delete:
                    if (cursorPos < buffer.Count)
                    {
                        buffer.RemoveAt(cursorPos);
                        Redraw(prompt, buffer, cursorPos);
                        UpdateCompletions(buffer, prompt, cursorPos, cs);
                    }
                    break;

                case ConsoleKey.LeftArrow:
                    if (cursorPos > 0) { cursorPos--; Console.Write("\x1b[D"); }
                    break;

                case ConsoleKey.RightArrow:
                    if (cursorPos < buffer.Count) { cursorPos++; Console.Write("\x1b[C"); }
                    break;

                case ConsoleKey.UpArrow:
                    if (historyIndex > 0)
                    {
                        historyIndex--;
                        SetBuffer(buffer, _history[historyIndex], ref cursorPos, prompt, cs);
                    }
                    break;

                case ConsoleKey.DownArrow:
                    if (historyIndex < _history.Count - 1)
                    {
                        historyIndex++;
                        SetBuffer(buffer, _history[historyIndex], ref cursorPos, prompt, cs);
                    }
                    else if (historyIndex == _history.Count - 1)
                    {
                        historyIndex = _history.Count;
                        SetBuffer(buffer, "", ref cursorPos, prompt, cs);
                    }
                    break;

                case ConsoleKey.Home:
                    cursorPos = 0;
                    Redraw(prompt, buffer, cursorPos);
                    break;

                case ConsoleKey.End:
                    cursorPos = buffer.Count;
                    Redraw(prompt, buffer, cursorPos);
                    break;

                case ConsoleKey.Escape:
                    SetBuffer(buffer, "", ref cursorPos, prompt, cs);
                    break;

                case ConsoleKey.Tab:
                    UpdateCompletions(buffer, prompt, cursorPos, cs);
                    if (cs.Active && cs.Matches.Count > 0)
                    {
                        cs.Index = 0;
                        DrawCompletions(prompt, buffer, cursorPos, cs);
                    }
                    break;

                default:
                    if (key.KeyChar >= 32)
                    {
                        buffer.Insert(cursorPos, key.KeyChar);
                        cursorPos++;
                        Redraw(prompt, buffer, cursorPos);
                        UpdateCompletions(buffer, prompt, cursorPos, cs);
                    }
                    break;
            }
        }
    }

    /// <summary>處理自動完成模式下的按鍵。回傳 true 表示已處理。</summary>
    private bool HandleCompletionKey(
        ConsoleKeyInfo key, string prompt, List<char> buffer, ref int cursorPos, CompletionState cs)
    {
        if (key.Key is ConsoleKey.DownArrow or ConsoleKey.Tab)
        {
            cs.Index = (cs.Index + 1) % cs.Matches.Count;
            DrawCompletions(prompt, buffer, cursorPos, cs);
            return true;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            cs.Index = cs.Index <= 0 ? cs.Matches.Count - 1 : cs.Index - 1;
            DrawCompletions(prompt, buffer, cursorPos, cs);
            return true;
        }

        if (key.Key == ConsoleKey.Enter && cs.Index >= 0)
        {
            var selected = cs.Matches[cs.Index].Cmd;
            ClearCompletions(cs);

            if (cs.IsFileMode)
            {
                // @ 檔案模式：替換 @keyword 部分，保留前後文字
                var text = new string(buffer.ToArray());
                var atPos = text.LastIndexOf('@');
                if (atPos >= 0)
                {
                    buffer.RemoveRange(atPos, buffer.Count - atPos);
                    buffer.InsertRange(atPos, selected);
                    buffer.Add(' ');
                    cursorPos = buffer.Count;
                }
            }
            else
            {
                // / 指令模式：整行替換
                buffer.Clear();
                buffer.AddRange(selected);
                cursorPos = buffer.Count;
            }

            cs.Reset();
            Redraw(prompt, buffer, cursorPos);
            return true;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            ClearCompletions(cs);
            cs.Reset();
            return true;
        }

        return false;
    }

    /// <summary>設定 buffer 內容。</summary>
    private static void SetBuffer(
        List<char> buffer, string text, ref int cursorPos, string prompt, CompletionState cs)
    {
        ClearCompletions(cs);
        cs.Active = false;
        buffer.Clear();
        buffer.AddRange(text);
        cursorPos = buffer.Count;
        Redraw(prompt, buffer, cursorPos);
    }

    /// <summary>根據 buffer 內容更新自動完成狀態（/ 指令 或 @ 檔案）。</summary>
    private void UpdateCompletions(List<char> buffer, string prompt, int cursorPos, CompletionState cs)
    {
        var text = new string(buffer.ToArray());

        // / 指令自動完成
        if (text.StartsWith('/') && text.Length >= 2 && !text.Contains(' '))
        {
            var keyword = text[1..];
            cs.Matches = _commands
                .Where(c => c.Cmd[1..].Contains(keyword, StringComparison.OrdinalIgnoreCase)
                         || c.Desc.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (cs.Matches.Count > 0)
            {
                cs.Active = true;
                cs.IsFileMode = false;
                cs.Index = -1;
                DrawCompletions(prompt, buffer, cursorPos, cs);
                return;
            }
        }

        // @ 檔案自動完成（至少 2 個字元才觸發，減少無效掃描）
        if (_workingDir is not null)
        {
            var atPos = text.LastIndexOf('@');
            if (atPos >= 0)
            {
                var afterAt = text[(atPos + 1)..];
                if (afterAt.Length >= 2 && !afterAt.Contains(' '))
                {
                    var newMatches = SearchFiles(afterAt);
                    if (newMatches.Count > 0)
                    {
                        // 跳過重繪 — 如果結果跟上次完全一樣
                        var same = cs.Active && cs.IsFileMode
                            && cs.Matches.Count == newMatches.Count
                            && cs.Matches.Select(m => m.Cmd).SequenceEqual(newMatches.Select(m => m.Cmd));
                        if (same) return;

                        cs.Matches = newMatches;
                        cs.Active = true;
                        cs.IsFileMode = true;
                        cs.Index = -1;
                        DrawCompletions(prompt, buffer, cursorPos, cs);
                        return;
                    }
                }
            }
        }

        if (cs.Active)
        {
            ClearCompletions(cs);
            cs.Reset();
        }
    }

    /// <summary>搜尋 workingDir 下的檔案（模糊匹配檔名）。</summary>
    private List<(string Cmd, string Desc)> SearchFiles(string keyword)
    {
        if (_workingDir is null || !Directory.Exists(_workingDir))
        {
            return [];
        }

        var results = new List<(string Cmd, string Desc)>();
        SearchDirectory(new DirectoryInfo(_workingDir), keyword.ToLowerInvariant(), _workingDir, results, 0);
        return results;
    }

    private static void SearchDirectory(
        DirectoryInfo dir, string keyword, string rootDir,
        List<(string Cmd, string Desc)> results, int depth)
    {
        if (depth > MaxFileScanDepth || results.Count >= MaxFileResults)
        {
            return;
        }

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                if (results.Count >= MaxFileResults) break;

                if (ExcludedExtensions.Contains(file.Extension)) continue;

                if (file.Name.ToLowerInvariant().Contains(keyword))
                {
                    var relativePath = Path.GetRelativePath(rootDir, file.FullName).Replace('\\', '/');
                    var sizeKb = file.Length / 1024.0;
                    var desc = sizeKb < 1 ? $"{file.Length} B" : $"{sizeKb:F0} KB";
                    results.Add(($"@{file.FullName}", $"{relativePath}  {desc}"));
                }
            }

            foreach (var subDir in dir.EnumerateDirectories())
            {
                if (results.Count >= MaxFileResults) break;
                if (ExcludedDirs.Contains(subDir.Name)) continue;
                SearchDirectory(subDir, keyword, rootDir, results, depth + 1);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 無權限的目錄跳過
        }
    }

    // === ANSI 繪製 ===

    private static void DrawCompletions(string prompt, List<char> buffer, int cursorPos, CompletionState cs)
    {
        ClearCompletions(cs);

        var maxWidth = Math.Max(40, Console.WindowWidth - 6); // 預留左右邊距

        for (var i = 0; i < cs.Matches.Count; i++)
        {
            Console.Write("\n\x1b[2K");
            var (cmd, desc) = cs.Matches[i];

            // @ 檔案模式：只顯示 desc（相對路徑 + 大小），不顯示完整絕對路徑
            var displayText = cs.IsFileMode ? desc : $"{cmd}  {desc}";
            if (displayText.Length > maxWidth)
            {
                displayText = displayText[..maxWidth] + "...";
            }

            if (i == cs.Index)
            {
                Console.Write($"  \x1b[7m {displayText} \x1b[0m");
            }
            else
            {
                var cmdColor = cs.IsFileMode ? "33" : "36";
                Console.Write($"  \x1b[{cmdColor}m{displayText}\x1b[0m");
            }
        }

        cs.LinesDrawn = cs.Matches.Count;
        if (cs.LinesDrawn > 0)
        {
            Console.Write($"\x1b[{cs.LinesDrawn}A");
        }
        Redraw(prompt, buffer, cursorPos);
    }

    private static void ClearCompletions(CompletionState cs)
    {
        if (cs.LinesDrawn <= 0) return;
        for (var i = 0; i < cs.LinesDrawn; i++)
        {
            Console.Write("\n\x1b[2K");
        }
        Console.Write($"\x1b[{cs.LinesDrawn}A\r");
        cs.LinesDrawn = 0;
    }

    private static void Redraw(string prompt, List<char> buffer, int cursorPos)
    {
        Console.Write($"\r\x1b[2K{prompt}{new string(buffer.ToArray())}");
        var moveBack = buffer.Count - cursorPos;
        if (moveBack > 0)
        {
            Console.Write($"\x1b[{moveBack}D");
        }
    }
}
