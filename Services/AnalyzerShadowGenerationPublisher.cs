using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RoslynMcpServer.Services;

internal sealed record AnalyzerShadowPublishResult(
    bool Success,
    bool ReusedExisting,
    string? GenerationId,
    string? GenerationDirectory,
    string? MainShadowPath,
    string? FailureReason,
    long GenerationBytes,
    IReadOnlyList<(string RelativePath, string Sha256)> RequiredHashes);

/// <summary>
/// Immutable main-only generation publisher: unique staging, content hashes, manifest-last,
/// same-volume no-replace move, full reuse verification. Does not migrate timestamp layouts.
/// </summary>
internal static class AnalyzerShadowGenerationPublisher
{
    internal const int DiskFullHResult = unchecked((int)0x80070070);

    internal static int AnalyzerFileIoCount;
    internal static int RemainingForcedCopyFailures;
    internal static int RemainingForcedAccessFailures;
    internal static int RemainingForcedMoveFailures;
    internal static int RemainingForcedDiskFullFailures;
    internal static string? ForcedDiskFullStage;
    internal static int RemainingForcedPdbCopyFailures;
    internal static string? BeforeMoveGatePath;
    internal static TimeSpan BeforeMoveGateTimeout = TimeSpan.FromSeconds(30);
    internal static Action<string>? MutateSourceAfterFirstHash;
    internal static Action<string>? AfterRequiredCopyBeforeManifest;
    internal static Action<string, string>? BeforeMove;

    internal static void ResetTestHooks()
    {
        AnalyzerFileIoCount = 0;
        RemainingForcedCopyFailures = 0;
        RemainingForcedAccessFailures = 0;
        RemainingForcedMoveFailures = 0;
        RemainingForcedDiskFullFailures = 0;
        ForcedDiskFullStage = null;
        RemainingForcedPdbCopyFailures = 0;
        BeforeMoveGatePath = null;
        BeforeMoveGateTimeout = TimeSpan.FromSeconds(30);
        MutateSourceAfterFirstHash = null;
        AfterRequiredCopyBeforeManifest = null;
        BeforeMove = null;
    }

    public static AnalyzerShadowPublishResult PublishMainOnly(
        string sourceAssemblyPath,
        string shadowRootDirectory,
        string? matchedProjectName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAssemblyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shadowRootDirectory);

        string sourceFull;
        string rootFull;
        try
        {
            sourceFull = Path.GetFullPath(sourceAssemblyPath);
            rootFull = Path.GetFullPath(shadowRootDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Fail("unresolvable-path: " + ex.Message);
        }

        if (!File.Exists(sourceFull))
        {
            return Fail("source-missing");
        }

        var policyRoot = AnalyzerShadowPolicy.GetPolicyRoot(rootFull);
        Directory.CreateDirectory(policyRoot);

        var stagingName = $".staging-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var stagingDirectory = Path.Combine(policyRoot, stagingName);
        if (!AnalyzerShadowPathSafety.IsContained(stagingDirectory, policyRoot))
        {
            return Fail("staging-outside-policy-root");
        }

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            ThrowIfForcedDiskFull("staging-create");

            var fileName = Path.GetFileName(sourceFull);
            var relativePath = fileName;
            var safety = AnalyzerShadowPathSafety.ValidateRelativePath(relativePath, stagingDirectory);
            if (!safety.Safe)
            {
                return Fail("unsafe-relative-path: " + safety.Reason);
            }

            string stagedMain;
            string mainHash;
            try
            {
                (stagedMain, mainHash) = CopyRequiredWithStability(sourceFull, Path.Combine(stagingDirectory, fileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(FormatIoFailure(ex));
            }

            TryCopyOptionalPdb(sourceFull, stagedMain);

            AfterRequiredCopyBeforeManifest?.Invoke(stagingDirectory);

            var required = new List<AnalyzerShadowManifestFile>
            {
                new() { RelativePath = relativePath, Sha256 = mainHash },
            };
            var generationId = ComputeGenerationId(required);
            var destDirectory = Path.Combine(policyRoot, generationId);
            if (!AnalyzerShadowPathSafety.IsContained(destDirectory, policyRoot))
            {
                return Fail("destination-outside-policy-root");
            }

            var optional = CollectOptionalSidecars(stagingDirectory, relativePath);
            var manifest = new AnalyzerShadowManifest
            {
                FormatVersion = AnalyzerShadowPolicy.FormatVersion,
                Policy = AnalyzerShadowPolicy.PolicyName,
                Required = required,
                Optional = optional,
                Source = new AnalyzerShadowManifestSource
                {
                    MatchedProjectName = matchedProjectName,
                    SourcePath = sourceFull,
                },
            };

            try
            {
                ThrowIfForcedDiskFull("manifest");
                var manifestPath = Path.Combine(stagingDirectory, AnalyzerShadowPolicy.ManifestFileName);
                File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, AnalyzerShadowManifest.JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(FormatIoFailure(ex));
            }

            if (Directory.Exists(destDirectory))
            {
                var reuse = TryVerifyPublishedGeneration(destDirectory, generationId, required);
                if (reuse.Success)
                {
                    return reuse;
                }

                return Fail("existing-generation-unfit: " + (reuse.FailureReason ?? "unverified"));
            }

            WaitForMoveGate();
            BeforeMove?.Invoke(stagingDirectory, destDirectory);

            try
            {
                if (RemainingForcedMoveFailures > 0)
                {
                    RemainingForcedMoveFailures--;
                    throw new IOException("Forced move failure for epoch-2 publication.");
                }

                ThrowIfForcedDiskFull("move");
                Directory.Move(stagingDirectory, destDirectory);
            }
            catch (IOException ex) when (Directory.Exists(destDirectory))
            {
                var reuse = TryVerifyPublishedGeneration(destDirectory, generationId, required);
                if (reuse.Success)
                {
                    return reuse;
                }

                return Fail("move-race-unfit: " + (reuse.FailureReason ?? ex.Message));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(FormatIoFailure(ex));
            }

            var verified = TryVerifyPublishedGeneration(destDirectory, generationId, required);
            if (!verified.Success)
            {
                return Fail("post-move-verify-failed: " + (verified.FailureReason ?? "unverified"));
            }

            return verified with { ReusedExisting = false };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(FormatIoFailure(ex));
        }
        finally
        {
            TryDeleteOwnStaging(stagingDirectory, policyRoot);
        }
    }

    public static AnalyzerShadowPublishResult TryVerifyPublishedGeneration(
        string generationDirectory,
        string expectedGenerationId,
        IReadOnlyList<AnalyzerShadowManifestFile> expectedRequired)
    {
        if (!Directory.Exists(generationDirectory))
        {
            return Fail("generation-missing");
        }

        var manifestPath = Path.Combine(generationDirectory, AnalyzerShadowPolicy.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Fail("manifest-missing");
        }

        AnalyzerShadowManifest? manifest;
        try
        {
            AnalyzerFileIoCount++;
            manifest = JsonSerializer.Deserialize<AnalyzerShadowManifest>(
                File.ReadAllText(manifestPath),
                AnalyzerShadowManifest.JsonOptions);
        }
        catch (Exception ex)
        {
            return Fail("manifest-corrupt: " + ex.Message);
        }

        if (manifest is null)
        {
            return Fail("manifest-empty");
        }

        if (manifest.FormatVersion != AnalyzerShadowPolicy.FormatVersion
            || !string.Equals(manifest.Policy, AnalyzerShadowPolicy.PolicyName, StringComparison.Ordinal))
        {
            return Fail("policy-or-version-mismatch");
        }

        if (manifest.Required is null || manifest.Required.Count == 0)
        {
            return Fail("required-set-empty");
        }

        foreach (var file in manifest.Required.Concat(manifest.Optional ?? new List<AnalyzerShadowManifestFile>()))
        {
            var safety = AnalyzerShadowPathSafety.ValidateRelativePath(file.RelativePath, generationDirectory);
            if (!safety.Safe)
            {
                return Fail("unsafe-manifest-path: " + safety.Reason);
            }
        }

        if (!RequiredSetsEqual(manifest.Required, expectedRequired))
        {
            return Fail("required-set-mismatch");
        }

        foreach (var required in expectedRequired)
        {
            var safety = AnalyzerShadowPathSafety.ValidateRelativePath(required.RelativePath, generationDirectory);
            if (!safety.Safe)
            {
                return Fail("unsafe-expected-path: " + safety.Reason);
            }

            var fullPath = Path.GetFullPath(Path.Combine(generationDirectory, required.RelativePath));
            if (!File.Exists(fullPath))
            {
                return Fail("required-file-missing: " + required.RelativePath);
            }

            string actualHash;
            try
            {
                actualHash = HashFile(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail("required-file-unreadable: " + ex.Message);
            }

            if (!string.Equals(actualHash, required.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("required-hash-mismatch: " + required.RelativePath);
            }
        }

        var computedId = ComputeGenerationId(expectedRequired);
        if (!string.Equals(computedId, expectedGenerationId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("generation-identity-mismatch");
        }

        var mainRelative = expectedRequired[0].RelativePath;
        var mainPath = Path.GetFullPath(Path.Combine(generationDirectory, mainRelative));
        return new AnalyzerShadowPublishResult(
            Success: true,
            ReusedExisting: true,
            GenerationId: computedId,
            GenerationDirectory: generationDirectory,
            MainShadowPath: mainPath,
            FailureReason: null,
            GenerationBytes: MeasureDirectoryBytes(generationDirectory),
            RequiredHashes: expectedRequired.Select(r => (r.RelativePath, r.Sha256)).ToList());
    }

    public static string ComputeGenerationId(IReadOnlyList<AnalyzerShadowManifestFile> required)
    {
        var ordered = required
            .OrderBy(r => NormalizeRelative(r.RelativePath), StringComparer.Ordinal)
            .ToList();
        var sb = new StringBuilder();
        sb.Append("format=").Append(AnalyzerShadowPolicy.FormatVersion).Append('\n');
        sb.Append("policy=").Append(AnalyzerShadowPolicy.PolicyName).Append('\n');
        foreach (var file in ordered)
        {
            sb.Append(NormalizeRelative(file.RelativePath)).Append(':')
                .Append(file.Sha256.ToUpperInvariant()).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static long MeasureDirectoryBytes(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Measurement is best-effort.
            }
        }

        return total;
    }

    internal static string HashFile(string path)
    {
        AnalyzerFileIoCount++;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static (string StagedPath, string Hash) CopyRequiredWithStability(string sourcePath, string stagedPath)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < AnalyzerShadowPolicy.SourceStabilityRetries; attempt++)
        {
            var firstHash = HashFile(sourcePath);
            MutateSourceAfterFirstHash?.Invoke(sourcePath);
            CopyFile(sourcePath, stagedPath);
            var stagedHash = HashFile(stagedPath);
            var secondHash = HashFile(sourcePath);
            if (string.Equals(firstHash, secondHash, StringComparison.OrdinalIgnoreCase)
                && string.Equals(firstHash, stagedHash, StringComparison.OrdinalIgnoreCase))
            {
                return (stagedPath, stagedHash);
            }

            last = new IOException("source-unstable");
        }

        throw last ?? new IOException("source-unstable");
    }

    private static void CopyFile(string sourcePath, string destinationPath)
    {
        if (RemainingForcedAccessFailures > 0)
        {
            RemainingForcedAccessFailures--;
            throw new UnauthorizedAccessException("Forced analyzer source access failure.");
        }

        if (RemainingForcedCopyFailures > 0)
        {
            RemainingForcedCopyFailures--;
            throw new IOException("Forced copy failure for epoch-1 overlay reapply baseline.");
        }

        ThrowIfForcedDiskFull("copy");
        AnalyzerFileIoCount++;
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
    }

    private static void TryCopyOptionalPdb(string sourceAssemblyPath, string stagedMainPath)
    {
        var sourcePdb = Path.ChangeExtension(sourceAssemblyPath, ".pdb");
        if (!File.Exists(sourcePdb))
        {
            return;
        }

        var stagedPdb = Path.ChangeExtension(stagedMainPath, ".pdb");
        try
        {
            if (RemainingForcedPdbCopyFailures > 0)
            {
                RemainingForcedPdbCopyFailures--;
                throw new IOException("Forced PDB copy failure.");
            }

            CopyFile(sourcePdb, stagedPdb);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (File.Exists(stagedPdb))
                {
                    File.Delete(stagedPdb);
                }
            }
            catch (Exception deleteEx) when (deleteEx is IOException or UnauthorizedAccessException)
            {
                // Optional sidecar must not remain half-written if we are dropping it.
            }
        }
    }

    private static List<AnalyzerShadowManifestFile> CollectOptionalSidecars(string stagingDirectory, string mainRelativePath)
    {
        var optional = new List<AnalyzerShadowManifestFile>();
        var pdbName = Path.ChangeExtension(mainRelativePath, ".pdb");
        if (string.IsNullOrWhiteSpace(pdbName))
        {
            return optional;
        }

        var pdbPath = Path.Combine(stagingDirectory, pdbName);
        if (!File.Exists(pdbPath))
        {
            return optional;
        }

        optional.Add(new AnalyzerShadowManifestFile
        {
            RelativePath = pdbName,
            Sha256 = HashFile(pdbPath),
        });
        return optional;
    }

    private static bool RequiredSetsEqual(
        IReadOnlyList<AnalyzerShadowManifestFile> actual,
        IReadOnlyList<AnalyzerShadowManifestFile> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var actualMap = actual.ToDictionary(f => NormalizeRelative(f.RelativePath), f => f.Sha256, comparison);
        foreach (var file in expected)
        {
            if (!actualMap.TryGetValue(NormalizeRelative(file.RelativePath), out var hash)
                || !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeRelative(string relativePath) =>
        relativePath.Replace('\\', '/').TrimStart('/');

    private static void WaitForMoveGate()
    {
        var gate = BeforeMoveGatePath;
        if (string.IsNullOrWhiteSpace(gate))
        {
            return;
        }

        var deadline = DateTime.UtcNow + BeforeMoveGateTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(gate))
            {
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void ThrowIfForcedDiskFull(string stage)
    {
        if (!string.IsNullOrWhiteSpace(ForcedDiskFullStage)
            && string.Equals(ForcedDiskFullStage, stage, StringComparison.Ordinal))
        {
            ForcedDiskFullStage = null;
            throw new IOException($"Forced disk-full during {stage}.", DiskFullHResult);
        }

        if (RemainingForcedDiskFullFailures > 0)
        {
            RemainingForcedDiskFullFailures--;
            throw new IOException($"Forced disk-full during {stage}.", DiskFullHResult);
        }
    }

    private static string FormatIoFailure(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return "access-failure: " + ex.Message;
        }

        if (ex is IOException io && io.HResult == DiskFullHResult)
        {
            return "disk-full: " + io.Message;
        }

        return "io-failure: " + ex.Message;
    }

    private static void TryDeleteOwnStaging(string stagingDirectory, string policyRoot)
    {
        if (!Directory.Exists(stagingDirectory))
        {
            return;
        }

        if (!AnalyzerShadowPathSafety.IsContained(stagingDirectory, policyRoot))
        {
            return;
        }

        var name = Path.GetFileName(stagingDirectory);
        var prefix = $".staging-{Environment.ProcessId}-";
        if (!name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Own incomplete staging only; a leftover is not a published generation.
        }
    }

    private static AnalyzerShadowPublishResult Fail(string reason) =>
        new(
            Success: false,
            ReusedExisting: false,
            GenerationId: null,
            GenerationDirectory: null,
            MainShadowPath: null,
            FailureReason: reason,
            GenerationBytes: 0,
            RequiredHashes: Array.Empty<(string, string)>());
}
