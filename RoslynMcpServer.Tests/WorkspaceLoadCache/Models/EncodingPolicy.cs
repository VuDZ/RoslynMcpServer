using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

internal sealed record EncodingPolicy(
    int? CodePage,
    ImmutableArray<byte> Preamble,
    string DecoderFallback,
    string? DecoderReplacement,
    string EncoderFallback,
    string? EncoderReplacement);
