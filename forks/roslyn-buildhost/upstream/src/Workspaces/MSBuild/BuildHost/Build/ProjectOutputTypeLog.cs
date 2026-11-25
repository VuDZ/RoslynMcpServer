// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Microsoft.CodeAnalysis.MSBuild;

internal sealed class ProjectOutputTypeLog(string path)
{
    private readonly List<Entry> _entries = [];

    public static ProjectOutputTypeLog? TryCreate(string? binlogPath)
    {
        if (binlogPath is null)
            return null;

        // The client can forward BinaryLogger.Parameters before the logger has parsed its file name.
        var parameterSeparator = binlogPath.IndexOf(';');
        var filePath = parameterSeparator < 0 ? binlogPath : binlogPath[..parameterSeparator];

        // RoslynMcpServer gives each BuildHost a separate binlog in the same private load directory.
        return Path.GetFileName(filePath).StartsWith("roslyn-mcp-workspace", StringComparison.Ordinal)
            ? new ProjectOutputTypeLog(filePath + ".projects.json")
            : null;
    }

    public void Add(string projectFile, string? targetFramework, string outputType, int codePage)
    {
        lock (_entries)
        {
            _entries.Add(new(projectFile, targetFramework, outputType, codePage));
        }
    }

    public void Save()
    {
        lock (_entries)
        {
            if (_entries.Count == 0)
                return;

            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                JsonSerializer.Serialize(stream, _entries);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The workspace can load without this optional metadata.
            }
        }
    }

    private sealed record Entry(string ProjectFile, string? TargetFramework, string OutputType, int CodePage);
}
