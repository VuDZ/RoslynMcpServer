namespace RoslynMcpServer.Services.Testing.Il.Models;

/// <summary>
/// The outcome of reading one assembly file: facts or the reason there are none, never both. The value
/// stands on its own, so a caller that keeps it rereads only the files that changed and builds the next
/// graph from the kept values of the rest.
/// </summary>
internal sealed class IlAssemblyReadResult
{
    /// <summary>The path exactly as the caller gave it; the neighbor map refers to assemblies by it.</summary>
    public string Path { get; }

    public IlAssemblyFacts? Facts { get; }

    public IlUnreadAssembly? Failure { get; }

    private IlAssemblyReadResult(string path, IlAssemblyFacts? facts, IlUnreadAssembly? failure)
    {
        Path = path;
        Facts = facts;
        Failure = failure;
    }

    public static IlAssemblyReadResult Read(string path, IlAssemblyFacts facts) => new(path, facts, failure: null);

    public static IlAssemblyReadResult Unread(IlUnreadAssembly failure) => new(failure.Path, facts: null, failure);
}
