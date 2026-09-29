namespace RoslynMcpServer.Tests.Support;

/// <summary>
/// Protocol revisions an in-process test client can request.
/// An SDK 2.x client prefers <see cref="SubscriptionProtocolVersion"/> and probes <c>server/discover</c>
/// when <see cref="ModelContextProtocol.Client.McpClientOptions.ProtocolVersion"/> is left unset.
/// A set value is both the requested revision and the minimum the client will accept.
/// </summary>
internal static class McpInitializeHandshake
{
    /// <summary>Initialize handshake production hosts still send. List changes arrive without a subscription.</summary>
    internal const string ProtocolVersion = "2025-11-25";

    /// <summary>
    /// SDK 2.2 default. <c>tools/list_changed</c> arrives only after <c>subscriptions/listen</c>
    /// with <c>toolsListChanged</c>.
    /// </summary>
    internal const string SubscriptionProtocolVersion = "2026-07-28";
}
