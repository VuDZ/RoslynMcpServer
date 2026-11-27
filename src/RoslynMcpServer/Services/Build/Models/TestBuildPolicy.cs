namespace RoslynMcpServer.Services.Build;

/// <summary>
/// How a DLL-route test run treats the session build.
/// <see cref="Auto"/> builds unless <see cref="SessionBuildState.EvaluateReuse"/> allows a skip.
/// The shared pull has no input content hash, so that skip is not taken.
/// </summary>
internal enum TestBuildPolicy
{
    Auto = 0,
    Always,
    Never,
}
