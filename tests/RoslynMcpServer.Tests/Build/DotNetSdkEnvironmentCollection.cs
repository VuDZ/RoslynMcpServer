using Xunit;

namespace RoslynMcpServer.Tests.Build;

// These tests set inherited MSBuild variables that other tests' child processes can read.
[CollectionDefinition("DotNetSdkEnvironment", DisableParallelization = true)]
public sealed class DotNetSdkEnvironmentCollection
{
}
