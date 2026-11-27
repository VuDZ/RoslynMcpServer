using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace RoslynMcpServer.Tests.SourceStructure.DeadlockWitnessHost;

/// <summary>
/// Process that runs one operation of an isolated witness assembly so the parent can bound it from the
/// outside. The host never applies a timeout of its own: the failure under test is a synchronous hang
/// before the entry returns its task, and only a parent that can kill the process tree recovers from it.
/// <para>
/// Protocol, one line per message on stdout with <c>\n</c> endings:
/// <c>ready pid=&lt;pid&gt;</c> once the host is listening, then the command
/// <c>&lt;assemblyPath&gt;|&lt;typeName&gt;|&lt;methodName&gt;</c> read from stdin, then exactly one of
/// <c>result:&lt;value&gt;</c> (exit code 0) or <c>error:&lt;type&gt;:&lt;message&gt;</c> (exit code 3).
/// The assembly is loaded from a stream, so the parent can delete the file while the host still runs.
/// </para>
/// </summary>
internal static class Program
{
    private const int ErrorExitCode = 3;
    private const int ProtocolExitCode = 2;

    private static async Task<int> Main(string[] args)
    {
        var protocol = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        await protocol.WriteLineAsync($"ready pid={Environment.ProcessId}").ConfigureAwait(false);
        var command = await Console.In.ReadLineAsync().ConfigureAwait(false);
        var parts = command?.Split('|') ?? Array.Empty<string>();
        if (parts.Length != 3)
        {
            await protocol.WriteLineAsync($"error:protocol:{command ?? "<no command>"}").ConfigureAwait(false);
            return ProtocolExitCode;
        }

        try
        {
            var value = await InvokeAsync(parts[0], parts[1], parts[2]).ConfigureAwait(false);
            await protocol.WriteLineAsync($"result:{value}").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await protocol.WriteLineAsync($"error:{ex.GetType().Name}:{ex.Message}").ConfigureAwait(false);
            return ErrorExitCode;
        }
    }

    /// <summary>
    /// Invokes the entry and awaits the task it returns. Awaiting is what makes the deadlock visible: the
    /// synchronous part of an async entry runs on this thread before the call returns at all.
    /// </summary>
    private static async Task<object?> InvokeAsync(string assemblyPath, string typeName, string methodName)
    {
        var bytes = await File.ReadAllBytesAsync(assemblyPath).ConfigureAwait(false);
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
        var type = assembly.GetType(typeName, throwOnError: true)!;
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{typeName}.{methodName} was not found in {assemblyPath}");

        var returned = method.Invoke(null, null);
        if (returned is not Task task)
        {
            return returned;
        }

        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
