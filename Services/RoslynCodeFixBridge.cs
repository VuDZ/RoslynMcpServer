using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;

namespace RoslynMcpServer.Services;

internal static class RoslynCodeFixBridge
{
    private static readonly Type? CodeFixServiceInterfaceType = Type.GetType(
        "Microsoft.CodeAnalysis.CodeFixes.ICodeFixService, Microsoft.CodeAnalysis.Features",
        throwOnError: false);

    private static readonly MethodInfo? GetServiceMethod = typeof(HostWorkspaceServices)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "GetService" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

    internal static bool IsAvailable => CodeFixServiceInterfaceType is not null && GetServiceMethod is not null;

    internal static async Task<IReadOnlyList<(string Title, CodeAction Action)>> GetFixesAsync(
        Document document,
        TextSpan span,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return Array.Empty<(string, CodeAction)>();
        }

        var workspace = document.Project.Solution.Workspace
            ?? throw new InvalidOperationException("Document is not part of an active workspace.");

        var service = GetCodeFixService(workspace);
        if (service is null)
        {
            return Array.Empty<(string, CodeAction)>();
        }

        var streamMethod = service.GetType().GetMethod(
            "StreamFixesAsync",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(TextDocument), typeof(TextSpan), typeof(CodeActionRequestPriority?), typeof(CancellationToken)],
            modifiers: null);

        if (streamMethod is null)
        {
            return Array.Empty<(string, CodeAction)>();
        }

        var streamResult = streamMethod.Invoke(service, [document, span, null, cancellationToken]);
        if (streamResult is null)
        {
            return Array.Empty<(string, CodeAction)>();
        }

        var fixes = new List<(string Title, CodeAction Action)>();
        await foreach (var collection in EnumerateFixCollectionsAsync(streamResult, cancellationToken).ConfigureAwait(false))
        {
            var fixesProperty = collection.GetType().GetProperty("Fixes", BindingFlags.Public | BindingFlags.Instance);
            if (fixesProperty?.GetValue(collection) is not IEnumerable fixItems)
            {
                continue;
            }

            foreach (var fixItem in fixItems)
            {
                if (fixItem is null)
                {
                    continue;
                }

                var actionProperty = fixItem.GetType().GetProperty("Action", BindingFlags.Public | BindingFlags.Instance);
                if (actionProperty?.GetValue(fixItem) is not CodeAction action)
                {
                    continue;
                }

                fixes.Add((action.Title, action));
            }
        }

        return fixes;
    }

    private static async IAsyncEnumerable<object> EnumerateFixCollectionsAsync(
        object asyncEnumerable,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerableType = asyncEnumerable.GetType();
        var getEnumeratorMethod = enumerableType.GetMethod("GetAsyncEnumerator", [typeof(CancellationToken)])
            ?? enumerableType.GetMethod("GetAsyncEnumerator", Type.EmptyTypes);

        if (getEnumeratorMethod is null)
        {
            yield break;
        }

        var enumerator = getEnumeratorMethod.GetParameters().Length == 0
            ? getEnumeratorMethod.Invoke(asyncEnumerable, null)
            : getEnumeratorMethod.Invoke(asyncEnumerable, [cancellationToken]);

        if (enumerator is null)
        {
            yield break;
        }

        var moveNextMethod = enumerator.GetType().GetMethod("MoveNextAsync");
        var currentProperty = enumerator.GetType().GetProperty("Current");
        if (moveNextMethod is null || currentProperty is null)
        {
            yield break;
        }

        while (true)
        {
            var moveNextResult = moveNextMethod.Invoke(enumerator, null);
            if (moveNextResult is not ValueTask<bool> valueTask)
            {
                yield break;
            }

            if (!await valueTask.ConfigureAwait(false))
            {
                yield break;
            }

            if (currentProperty.GetValue(enumerator) is { } current)
            {
                yield return current;
            }
        }
    }

    private static object? GetCodeFixService(Workspace workspace)
    {
        if (CodeFixServiceInterfaceType is null || GetServiceMethod is null)
        {
            return null;
        }

        var genericGetService = GetServiceMethod.MakeGenericMethod(CodeFixServiceInterfaceType);
        return genericGetService.Invoke(workspace.Services, null);
    }
}
