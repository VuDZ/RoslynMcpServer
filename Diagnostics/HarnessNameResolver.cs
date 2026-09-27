namespace RoslynMcpServer.Diagnostics;

/// <summary>
/// Maps the immediate parent process to a short harness name.
/// </summary>
/// <remarks>
/// Do not walk to the grandparent. A node harness is often launched from powershell or a shell;
/// the shell is not the caller. Do not read a non-node command line: Cursor's utility host is
/// <c>Cursor.exe</c> while its command line contains <c>node.mojom</c> and is not a harness id.
/// </remarks>
internal static class HarnessNameResolver
{
    internal static string Resolve(string? parentImagePath, string? parentCommandLine)
    {
        var stem = ImageStem(parentImagePath);
        if (IsNodeStem(stem))
        {
            return ResolveNodeCommandLine(parentCommandLine);
        }

        if (IsNamedExecutable(stem, Cursor))
        {
            return Cursor;
        }

        if (IsNamedExecutable(stem, Claude))
        {
            return Claude;
        }

        if (IsNamedExecutable(stem, Codex))
        {
            return Codex;
        }

        if (IsNamedExecutable(stem, OpenCode))
        {
            return OpenCode;
        }

        return Unknown;
    }

    internal static bool IsNodeImage(string? imagePath) => IsNodeStem(ImageStem(imagePath));

    private static string ResolveNodeCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Unknown;
        }

        var normalized = commandLine.Replace('\\', '/');
        if (normalized.Contains(DeepSeekDshPackage, StringComparison.OrdinalIgnoreCase)
            || HasExactSegment(normalized, Dsh))
        {
            return Dsh;
        }

        if (normalized.Contains(ClaudeCodePackage, StringComparison.OrdinalIgnoreCase)
            || HasHarnessSegment(normalized, Claude))
        {
            return Claude;
        }

        if (normalized.Contains(OpenAiCodexPackage, StringComparison.OrdinalIgnoreCase)
            || HasHarnessSegment(normalized, Codex))
        {
            return Codex;
        }

        if (HasHarnessSegment(normalized, OpenCode))
        {
            return OpenCode;
        }

        if (HasHarnessSegment(normalized, Cursor))
        {
            return Cursor;
        }

        return Unknown;
    }

    private static bool HasExactSegment(string normalizedCommandLine, string name)
    {
        foreach (var segment in SplitSegments(normalizedCommandLine))
        {
            if (segment.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasHarnessSegment(string normalizedCommandLine, string name)
    {
        var prefixed = name + "-";
        foreach (var segment in SplitSegments(normalizedCommandLine))
        {
            if (segment.Equals(name, StringComparison.OrdinalIgnoreCase)
                || segment.StartsWith(prefixed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] SplitSegments(string normalizedCommandLine) =>
        normalizedCommandLine.Split(SegmentSeparators, StringSplitOptions.RemoveEmptyEntries);

    private static string ImageStem(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(imagePath.Trim());
    }

    private static bool IsNodeStem(string stem) =>
        stem.Equals(NodeStem, StringComparison.OrdinalIgnoreCase)
        || stem.Equals(NodeJsStem, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamedExecutable(string stem, string name)
    {
        if (stem.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!stem.StartsWith(name, StringComparison.OrdinalIgnoreCase) || stem.Length == name.Length)
        {
            return false;
        }

        return stem[name.Length] is '-' or ' ' or '.';
    }

    internal const string Cursor = "cursor";

    internal const string Dsh = "dsh";

    internal const string Claude = "claude";

    internal const string Codex = "codex";

    internal const string OpenCode = "opencode";

    internal const string Unknown = "cli";

    private const string NodeStem = "node";

    private const string NodeJsStem = "nodejs";

    private const string DeepSeekDshPackage = "@deepseek-ai/dsh";

    private const string ClaudeCodePackage = "@anthropic-ai/claude-code";

    private const string OpenAiCodexPackage = "@openai/codex";

    private static readonly char[] SegmentSeparators = ['/', ' ', '"', '\'', '\t', '\r', '\n'];
}
