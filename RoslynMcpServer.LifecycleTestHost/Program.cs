using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using RoslynMcpServer.Diagnostics;
using RoslynMcpServer.LifecycleTestHost;

Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
var protocol = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true,
    NewLine = "\n",
};
Console.SetOut(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
{
    AutoFlush = true,
});

MsBuildBootstrapper.Register();

try
{
    _ = typeof(CSharpFormattingOptions).Assembly;
    _ = typeof(SyntaxFactory).Assembly;
    _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.Features"));
    _ = Assembly.Load(new AssemblyName("Microsoft.CodeAnalysis.CSharp.Features"));
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[LifecycleTestHost] Failed to force-load C# services: {ex.Message}");
}

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

void WriteProtocol(HostResponse response)
{
    protocol.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
}

var bootstrap = MsBuildEnvironmentInfo.RegistrationSummary ?? string.Empty;
if (bootstrap.StartsWith("Failed", StringComparison.OrdinalIgnoreCase))
{
    WriteProtocol(new HostResponse
    {
        Ok = false,
        Skip = true,
        Op = "env",
        Error = bootstrap,
        Environment = new EnvironmentDto
        {
            Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Bootstrap = bootstrap,
        },
    });
    return 2;
}

var session = new HostSession();
string? line;
while ((line = await Console.In.ReadLineAsync()) is not null)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    HostCommand? command;
    try
    {
        command = JsonSerializer.Deserialize<HostCommand>(line, jsonOptions);
    }
    catch (Exception ex)
    {
        WriteProtocol(new HostResponse { Ok = false, Error = "bad-json:" + ex.Message });
        continue;
    }

    if (command is null || string.IsNullOrWhiteSpace(command.Op))
    {
        WriteProtocol(new HostResponse { Ok = false, Error = "missing-op" });
        continue;
    }

    if (string.Equals(command.Op, "exit", StringComparison.OrdinalIgnoreCase))
    {
        WriteProtocol(new HostResponse { Ok = true, Op = "exit" });
        break;
    }

    var response = await session.ExecuteAsync(command, CancellationToken.None).ConfigureAwait(false);
    WriteProtocol(response);
}

return 0;
