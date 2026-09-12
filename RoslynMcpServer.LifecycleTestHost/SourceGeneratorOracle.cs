using Microsoft.CodeAnalysis;

namespace RoslynMcpServer.LifecycleTestHost;

/// <summary>
/// Exact execution oracle: overlay <see cref="Project.GetCompilationAsync"/> then
/// <see cref="IFieldSymbol.ConstantValue"/>. Empty diagnostics are not a version oracle.
/// </summary>
internal static class SourceGeneratorOracle
{
    public const string GeneratedTypeMetadataName = "GeneratedMarker";
    public const string GeneratedFieldName = "Version";

    public static async Task<OracleObservation> ReadAsync(
        Project project,
        CancellationToken cancellationToken,
        string? generatedTypeMetadataName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var typeName = string.IsNullOrWhiteSpace(generatedTypeMetadataName)
            ? GeneratedTypeMetadataName
            : generatedTypeMetadataName;

        Compilation? compilation;
        try
        {
            compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return OracleObservation.Fail("compilation-exception:" + ex.GetType().Name + ":" + ex.Message);
        }

        if (compilation is null)
        {
            return OracleObservation.Fail("no-compilation");
        }

        var type = compilation.GetTypeByMetadataName(typeName);
        if (type is null)
        {
            return OracleObservation.Fail("no-type");
        }

        var field = type.GetMembers(GeneratedFieldName).OfType<IFieldSymbol>().FirstOrDefault();
        if (field is null)
        {
            return OracleObservation.Fail("no-field");
        }

        if (field.ConstantValue is not string marker || string.IsNullOrEmpty(marker))
        {
            return OracleObservation.Fail("no-constant");
        }

        string? generatedText = null;
        try
        {
            var generated = await project.GetSourceGeneratedDocumentsAsync(cancellationToken).ConfigureAwait(false);
            var hit = generated.FirstOrDefault(d =>
                d.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                generatedText = (await hit.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
            }
        }
        catch (Exception ex)
        {
            generatedText = "(generated-text-unavailable:" + ex.GetType().Name + ")";
        }

        return new OracleObservation(true, marker, null, generatedText);
    }
}

internal readonly record struct OracleObservation(
    bool Success,
    string? Marker,
    string? Failure,
    string? GeneratedText)
{
    public static OracleObservation Fail(string reason) => new(false, null, reason, null);
}
