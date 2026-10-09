using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMcpServer.Services.Workspace.StateCache;
using RoslynMcpServer.Services.Workspace.StateCache.Models;

namespace RoslynMcpServer.Tests.WorkspaceStateCache;

/// <summary>
/// One new-process capture or isolated restore of an envelope already produced by ordinary capture.
/// The runner starts a fresh process per role. This type does not open the production session,
/// does not call <c>SolutionManager</c>, and does not admit a disk cache.
/// </summary>
internal static class IsolatedRestoreAttempt
{
    public const string DirectoryVariable = "ROSLYN_TASK07_DIRECTORY";

    public const string RoleVariable = "ROSLYN_TASK07_ROLE";

    public const string WorkspaceVariable = "ROSLYN_TASK07_WORKSPACE";

    public const string RunIdVariable = "ROSLYN_TASK07_RUN_ID";

    public const string ExpectFullNameVariable = "ROSLYN_TASK07_EXPECT_FULL_NAME";

    public const string ExpectFileVariable = "ROSLYN_TASK07_EXPECT_FILE";

    public static async Task RunAsync(string? directory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("A measured attempt needs the runner directory.");
        }

        var role = Environment.GetEnvironmentVariable(RoleVariable);
        if (role is not "capture" and not "restore" and not "large")
        {
            throw new InvalidOperationException("The runner role is missing or unknown.");
        }

        try
        {
            if (role == "restore")
            {
                await RestoreAsync(directory, cancellationToken).ConfigureAwait(false);
                return;
            }

            await CaptureAsync(directory, role, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            await WriteCrashAsync(directory, role, exception).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task CaptureAsync(string directory, string role, CancellationToken cancellationToken)
    {
        var workspacePath = Environment.GetEnvironmentVariable(WorkspaceVariable);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new InvalidOperationException("Capture needs the workspace path.");
        }

        // CaptureAsync does not open a project. The ordinary load that feeds it is a separate stage,
        // then added into overhead, so neither number is dropped to flatter the restore.
        // MSBuild is registered by the caller before this method is compiled: a Register call in
        // this method would run only after the CLR had already failed to load MSBuild.Framework.
        var clock = Stopwatch.StartNew();
        using var workspace = new AdhocWorkspace();
        var descriptors = await new MSBuildProjectLoader(workspace)
            .LoadProjectInfoAsync(workspacePath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        workspace.AddProjects(descriptors);
        var ordinaryLoadMs = Milliseconds(clock);
        var capture = await OrdinaryWorkspaceCapture.CaptureAsync(
            workspace.CurrentSolution,
            descriptors,
            workspacePath,
            new SnapshotLoadProperties(new SnapshotPropertySet(null, null, null), new SnapshotPropertySet(null, null, null)),
            originalAnalyzerPathsProven: true,
            cancellationToken).ConfigureAwait(false);
        var captureMs = Milliseconds(clock);
        double? writeMs = null;
        long? envelopeBytes = null;
        string outcome;
        if (capture.UnsupportedReason is not null || capture.Snapshot is null)
        {
            outcome = role == "large" ? "unsupported" : "error";
            var stale = Path.Combine(directory, "snapshot.bin");
            if (File.Exists(stale))
            {
                File.Delete(stale);
            }
        }
        else
        {
            var encoded = WorkspaceSnapshotCodec.Encode(capture.Snapshot);
            await File.WriteAllBytesAsync(Path.Combine(directory, "snapshot.bin"), encoded, cancellationToken)
                .ConfigureAwait(false);
            writeMs = Milliseconds(clock);
            envelopeBytes = encoded.LongLength;
            outcome = role == "large" ? "unexpected-success" : "success";
        }

        await WriteAsync(directory, role, new
        {
            role,
            pid = Environment.ProcessId,
            outcome,
            ordinaryLoadMs,
            captureMs,
            writeMs,
            captureOverheadMs = Round(ordinaryLoadMs + captureMs + (writeMs ?? 0)),
            diskEnvelopeBytes = envelopeBytes,
            projectCount = descriptors.Count(),
            unsupportedReason = capture.UnsupportedReason,
            peakWorkingSetBytes = PeakWorkingSet(),
            peakPrivateBytes = PeakPrivate(),
            error = capture.UnsupportedReason is null ? null : "capture refused: " + capture.UnsupportedReason,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RestoreAsync(string directory, CancellationToken cancellationToken)
    {
        var envelope = Path.Combine(directory, "snapshot.bin");
        if (!File.Exists(envelope))
        {
            await WriteAsync(directory, "restore", Failed("restore", "error", "The capture envelope is missing."), cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var clock = Stopwatch.StartNew();
        var bytes = await File.ReadAllBytesAsync(envelope, cancellationToken).ConfigureAwait(false);
        var readMs = Milliseconds(clock);
        var snapshot = WorkspaceSnapshotCodec.Decode(bytes);
        var decodeMs = Milliseconds(clock);
        // HydrateAsync encodes and decodes again before it touches the disk. That round-trip stays
        // inside hydrate: pulling it into the earlier decode stage would time a different program.
        using var session = await new IsolatedWorkspaceHydrator().HydrateAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
        var hydrateMs = Milliseconds(clock);
        QueryHit hit;
        try
        {
            hit = Query(session, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            hit = new QueryHit("error", null, exception.GetType().Name + ": " + exception.Message);
        }

        var queryMs = Milliseconds(clock);
        await WriteAsync(directory, "restore", new
        {
            role = "restore",
            pid = Environment.ProcessId,
            outcome = hit.Outcome,
            readMs,
            decodeMs,
            hydrateMs,
            queryMs,
            usefulMs = Round(readMs + decodeMs + hydrateMs + queryMs),
            diskEnvelopeBytes = bytes.LongLength,
            declaration = hit.Declaration,
            queryMissDetail = hit.Detail,
            hydrateIncludesInternalCodecRoundTrip = true,
            peakWorkingSetBytes = PeakWorkingSet(),
            peakPrivateBytes = PeakPrivate(),
            error = hit.Outcome == "success" ? null : hit.Detail,
        }, cancellationToken).ConfigureAwait(false);
    }

    private static QueryHit Query(IsolatedHydratedSession session, CancellationToken cancellationToken)
    {
        // A hardcoded SamplePlugin success would count a different requested symbol as this control.
        // The runner also refuses any other query before it starts a timer.
        var expectedName = Environment.GetEnvironmentVariable(ExpectFullNameVariable);
        var expectedFile = Environment.GetEnvironmentVariable(ExpectFileVariable);
        if (expectedName is null || expectedFile is null || !IsAcceptedControl(expectedName, expectedFile))
        {
            return new QueryHit("error", null, "The expected declaration is outside the accepted SamplePlugin control.");
        }

        var pluginId = session.Projects.Single(pair =>
            pair.Key.ProjectPath.EndsWith("RoslynMcpPlugin.csproj", StringComparison.OrdinalIgnoreCase)).Value;
        var plugin = session.BaseCompilations[pluginId];
        var sample = plugin.GetTypeByMetadataName(expectedName) as INamedTypeSymbol;
        var file = sample?.Locations.FirstOrDefault()?.SourceTree?.FilePath;
        var fullName = sample?.ToDisplayString();
        var declaration = new { fullName, file };
        if (sample is null || fullName != expectedName || !IsPluginSampleFile(file))
        {
            return new QueryHit("miss", declaration, "expected declaration was not in the hydrated compilation");
        }

        var implementsPlugin = sample.AllInterfaces.Any(symbol =>
            symbol.ToDisplayString() == "RoslynMcpServer.Plugins.IRoslynMcpPlugin"
            && symbol.ContainingAssembly.Name == "RoslynMcpServer");
        var errors = plugin.GetDiagnostics(cancellationToken).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (!implementsPlugin || errors)
        {
            return new QueryHit("miss", declaration, !implementsPlugin
                ? "expected interface was not on the declaration"
                : "hydrated compilation has compiler errors");
        }

        return new QueryHit("success", declaration, null);
    }

    private const string AcceptedFullName = "RoslynMcpPlugin.SamplePlugin";

    private static bool IsAcceptedControl(string? fullName, string? file) =>
        fullName == AcceptedFullName && IsPluginSampleFile(file);

    private static bool IsPluginSampleFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        var suffix = Path.Combine("RoslynMcpPlugin", "SamplePlugin.cs");
        return full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteCrashAsync(string directory, string role, Exception exception)
    {
        try
        {
            await WriteAsync(directory, role, Failed(role, "error", exception.GetType().Name + ": " + exception.Message), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The attempt record is already lost if the handoff directory cannot be written.
        }
    }

    private static object Failed(string role, string outcome, string error) => new
    {
        role,
        pid = Environment.ProcessId,
        outcome,
        peakWorkingSetBytes = PeakWorkingSet(),
        peakPrivateBytes = PeakPrivate(),
        error,
    };

    private static async Task WriteAsync(string directory, string role, object receipt, CancellationToken cancellationToken)
    {
        // The runner deletes the previous receipt and accepts this file only when runId matches the launch.
        // A leftover stages file from an earlier process must not become this attempt's success.
        var node = JsonSerializer.SerializeToNode(receipt, JsonOptions) as JsonObject
            ?? throw new InvalidOperationException("The attempt receipt is not a JSON object.");
        node["runId"] = Environment.GetEnvironmentVariable(RunIdVariable);
        var json = node.ToJsonString(JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(directory, role + "-stages.json"), json, cancellationToken)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(directory, role + ".pid"), Environment.ProcessId.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }

    private static double Milliseconds(Stopwatch clock)
    {
        var value = Round(clock.Elapsed.TotalMilliseconds);
        clock.Restart();
        return value;
    }

    private static double Round(double value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static long PeakWorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PeakWorkingSet64;
    }

    private static long PeakPrivate()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64;
    }

    private sealed record QueryHit(string Outcome, object? Declaration, string? Detail);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
