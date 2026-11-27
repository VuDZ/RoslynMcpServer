using Xunit;

namespace RoslynMcpServer.Tests.Support;

internal sealed class WpfFactAttribute : FactAttribute
{
    public WpfFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "A Windows host with the WPF SDK is required.";
        }
    }
}
