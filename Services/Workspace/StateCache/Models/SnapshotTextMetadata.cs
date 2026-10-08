namespace RoslynMcpServer.Services.Workspace.StateCache.Models;

/// <summary>Null code page explicitly represents absent encoding, with checksum bytes independent of source text.</summary>
public sealed record SnapshotTextMetadata(
    int? EncodingCodePage,
    byte[] EncodingPreamble,
    string ChecksumAlgorithm,
    byte[] Checksum);
