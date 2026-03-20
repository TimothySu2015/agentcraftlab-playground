using System.Text;
using AgentCraftLab.Engine.Models;

namespace AgentCraftLab.Autonomous.Playground;

/// <summary>
/// 從 ExecutionEvent 列表生成 Mermaid sequence diagram。
/// </summary>
public static class FlowDiagramGenerator
{
    public static string Generate(List<ExecutionEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine("sequenceDiagram");

        var (orchestratorName, aliasMap) = WriteParticipants(sb, events);

        sb.AppendLine();
        WriteInteractions(sb, events, aliasMap);

        return sb.ToString();
    }

    private static (string OrchestratorName, Dictionary<string, string> AliasMap) WriteParticipants(
        StringBuilder sb, List<ExecutionEvent> events)
    {
        var orchestratorName = "Orchestrator";
        var subAgents = new List<string>();
        var hasTools = false;
        var hasUser = false;
        var hasAudit = false;

        foreach (var evt in events)
        {
            switch (evt.Type)
            {
                case EventTypes.AgentStarted when !string.IsNullOrEmpty(evt.AgentName):
                    orchestratorName = evt.AgentName;
                    break;
                case EventTypes.SubAgentCreated:
                {
                    var name = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    if (!string.IsNullOrEmpty(name) && !subAgents.Contains(name))
                    {
                        subAgents.Add(name);
                    }

                    break;
                }
                case EventTypes.ToolCall:
                    hasTools = true;
                    break;
                case EventTypes.WaitingForInput or EventTypes.WaitingForRiskApproval:
                    hasUser = true;
                    break;
                case EventTypes.AuditStarted:
                    hasAudit = true;
                    break;
            }
        }

        sb.AppendLine($"    participant O as {Sanitize(orchestratorName)}");
        if (hasTools)
        {
            sb.AppendLine("    participant T as Tools");
        }

        var aliasMap = new Dictionary<string, string>();
        for (var i = 0; i < subAgents.Count; i++)
        {
            var alias = $"S{i}";
            aliasMap[subAgents[i]] = alias;
            sb.AppendLine($"    participant {alias} as {Sanitize(subAgents[i])}");
        }

        if (hasUser)
        {
            sb.AppendLine("    participant U as User");
        }

        if (hasAudit)
        {
            sb.AppendLine("    participant A as Auditor");
        }

        return (orchestratorName, aliasMap);
    }

    private static void WriteInteractions(
        StringBuilder sb, List<ExecutionEvent> events, Dictionary<string, string> aliasMap)
    {
        foreach (var evt in events)
        {
            switch (evt.Type)
            {
                case EventTypes.ToolCall:
                    sb.AppendLine($"    O->>T: {Sanitize(ExtractToolName(evt.Text))}");
                    break;
                case EventTypes.ToolResult:
                    sb.AppendLine($"    T-->>O: {Sanitize(Truncate(evt.Text, 40))}");
                    break;
                case EventTypes.SubAgentCreated:
                {
                    var name = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    if (aliasMap.TryGetValue(name, out var alias))
                    {
                        var instr = evt.Metadata?.GetValueOrDefault(MetadataKeys.Instructions) ?? "";
                        var label = string.IsNullOrEmpty(instr) ? "create" : $"create: {Truncate(instr, 30)}";
                        sb.AppendLine($"    O->>+{alias}: {Sanitize(label)}");
                    }

                    break;
                }
                case EventTypes.SubAgentAsked:
                {
                    var name = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    var msg = evt.Metadata?.GetValueOrDefault(MetadataKeys.Message) ?? "";
                    if (aliasMap.TryGetValue(name, out var alias))
                    {
                        sb.AppendLine($"    O->>+{alias}: {Sanitize(Truncate(msg, 40))}");
                    }

                    break;
                }
                case EventTypes.SubAgentResponded:
                {
                    var name = evt.Metadata?.GetValueOrDefault(MetadataKeys.SubAgentName) ?? "";
                    if (aliasMap.TryGetValue(name, out var alias))
                    {
                        sb.AppendLine($"    {alias}-->>-O: {Sanitize(Truncate(evt.Text, 40))}");
                    }

                    break;
                }
                case EventTypes.WaitingForInput:
                    sb.AppendLine($"    O->>+U: {Sanitize(Truncate(evt.Text, 40))}");
                    break;
                case EventTypes.UserInputReceived:
                    sb.AppendLine($"    U-->>-O: {Sanitize(Truncate(evt.Text, 40))}");
                    break;
                case EventTypes.WaitingForRiskApproval:
                {
                    var tool = evt.Metadata?.GetValueOrDefault(MetadataKeys.ToolName) ?? "";
                    sb.AppendLine($"    O->>+U: Risk approval: {Sanitize(tool)}");
                    break;
                }
                case EventTypes.RiskApprovalResult:
                {
                    var approved = evt.Metadata?.GetValueOrDefault(MetadataKeys.Approved) ?? "";
                    sb.AppendLine($"    U-->>-O: {(approved == "True" ? "Approved" : "Rejected")}");
                    break;
                }
                case EventTypes.AuditStarted:
                    sb.AppendLine("    O->>+A: audit answer");
                    break;
                case EventTypes.AuditCompleted:
                {
                    var verdict = evt.Metadata?.GetValueOrDefault(MetadataKeys.Verdict) ?? "";
                    sb.AppendLine($"    A-->>-O: {Sanitize(verdict)}");
                    break;
                }
                case EventTypes.ReasoningStep:
                {
                    var step = evt.Metadata?.GetValueOrDefault(MetadataKeys.Step) ?? "";
                    sb.AppendLine($"    Note over O: Step {Sanitize(step)}");
                    break;
                }
            }
        }
    }

    public static string ExtractToolName(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "?";
        var parenIdx = text.IndexOf('(');
        return parenIdx > 0 ? text[..parenIdx] : text;
    }

    public static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "...";
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text
            .Replace(";", ",")
            .Replace("#", "")
            .Replace(">>", "> >")
            .Replace("\n", " ")
            .Replace("\r", "");
    }
}
