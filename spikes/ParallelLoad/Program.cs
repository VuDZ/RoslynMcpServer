using System.Runtime.CompilerServices;
using ParallelLoad;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["check-core-first"])
            {
                MsBuildRegistration.Register(Environment.CurrentDirectory);
                return VerifyCoreFirst();
            }
            if (args is ["check-refusal"])
            {
                if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("Fallback checks require Windows."); }
                MsBuildRegistration.Register(Environment.CurrentDirectory);
                return await VerifyRefusalAsync();
            }

            if (args is ["check-profile"])
            {
                MsBuildRegistration.Register(Environment.CurrentDirectory);
                return VerifyProfile();
            }

            if (args.Length > 0 && args[0] == "plan")
            {
                return ProfilePartition.Run(args);
            }
            if (args.Length > 0 && args[0] == "binlog-timings")
            {
                return BinlogTimingReplay.Run(args);
            }

            if (args.Length > 0 && args[0] == "comparison-partitions")
            {
                return ProjectPartition.Export(args);
            }

            if (args.Length > 0 && args[0] == "core-plan")
            {
                return CoreFirstPartition.Export(args);
            }

            if (args is ["sample-process", var rootId, var directory] && OperatingSystem.IsWindows())
            {
                return await ExternalSampler.RunAsync(int.Parse(rootId), directory);
            }

            if (args is ["check-merge"])
            {
                MsBuildRegistration.Register(Environment.CurrentDirectory);
                return VerifyMerge();
            }

            var options = RunOptions.Parse(args);
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("This experiment requires Windows process telemetry.");
            }
            var registration = MsBuildRegistration.Register(Path.GetDirectoryName(options.SolutionPath)!, options.LocalSdkPath);
            return await RunRegisteredAsync(options, registration);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // Keep MSBuild-dependent code behind a JIT boundary until Locator has installed resolution handlers.
    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task<int> RunRegisteredAsync(RunOptions options, string registration) =>
        options.Mode == "core-first"
            ? options.RefusalPolicy ? RefusalExperiment.RunAsync(options, registration) : CoreFirstExperiment.RunAsync(options, registration)
            : options.Mode is "profile" or "replay-profile"
            ? ProfileExperiment.RunAsync(options, registration)
            : LoadExperiment.RunAsync(options, registration);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int VerifyMerge() => MergeChecks.Run();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int VerifyProfile() => ProfileChecks.Run();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int VerifyCoreFirst() => CoreFirstChecks.Run();

    [MethodImpl(MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task<int> VerifyRefusalAsync()
    {
        BinlogTimingReplay.CheckIntervals();
        return RefusalChecks.RunAsync();
    }
}
