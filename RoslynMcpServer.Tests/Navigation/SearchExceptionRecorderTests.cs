using System.Text.Json;
using RoslynMcpServer.Services.Navigation;
using Xunit;

namespace RoslynMcpServer.Tests.Navigation;

public sealed class SearchExceptionRecorderTests
{
    [Fact]
    public void Exception_of_the_current_project_flow_is_grouped_by_type_project_and_file()
    {
        using var recorder = new SearchExceptionRecorder();
        recorder.CurrentProject = "App.csproj";

        ThrowAndCatch(new FileNotFoundException("missing", "Gone.cs"));
        ThrowAndCatch(new FileNotFoundException("missing", "Gone.cs"));

        using var json = JsonDocument.Parse(recorder.ToJson(Guid.Empty));
        var group = Assert.Single(
            json.RootElement.GetProperty("groups").EnumerateArray(),
            group => group.GetProperty("project").GetString() == "App.csproj");
        Assert.Equal(typeof(FileNotFoundException).FullName, group.GetProperty("type").GetString());
        Assert.Equal("Gone.cs", group.GetProperty("detail").GetString());
        Assert.Equal(2, group.GetProperty("count").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("attributed").GetInt32());
    }

    [Fact]
    public void Exception_without_a_project_is_not_attributed()
    {
        using var recorder = new SearchExceptionRecorder();

        ThrowAndCatch(new InvalidOperationException("unrelated"));

        using var json = JsonDocument.Parse(recorder.ToJson(Guid.Empty));
        Assert.Equal(0, json.RootElement.GetProperty("attributed").GetInt32());
        Assert.True(json.RootElement.GetProperty("unattributed").GetInt32() >= 1);
        var group = Assert.Single(
            json.RootElement.GetProperty("groups").EnumerateArray(),
            group => group.GetProperty("type").GetString() == typeof(InvalidOperationException).FullName);
        Assert.Equal(JsonValueKind.Null, group.GetProperty("detail").ValueKind);
    }

    [Fact]
    public async Task Flow_that_does_not_inherit_the_project_is_not_attributed()
    {
        using var recorder = new SearchExceptionRecorder();
        recorder.CurrentProject = "App.csproj";

        Task other;
        using (ExecutionContext.SuppressFlow())
        {
            other = Task.Run(() => ThrowAndCatch(new IOException("elsewhere")));
        }

        await other;

        using var json = JsonDocument.Parse(recorder.ToJson(Guid.Empty));
        Assert.Equal(0, json.RootElement.GetProperty("attributed").GetInt32());
    }

    [Fact]
    public void Disposed_recorder_stops_counting()
    {
        var recorder = new SearchExceptionRecorder();
        recorder.CurrentProject = "App.csproj";
        recorder.Dispose();

        ThrowAndCatch(new IOException("after the search"));

        using var json = JsonDocument.Parse(recorder.ToJson(Guid.Empty));
        Assert.Equal(0, json.RootElement.GetProperty("attributed").GetInt32());
    }

    private static void ThrowAndCatch(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught) when (ReferenceEquals(caught, exception))
        {
        }
    }
}
