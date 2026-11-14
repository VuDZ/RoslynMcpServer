using System.Runtime.CompilerServices;

namespace AffectedTestsProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "-h" or "--help")
            {
                Usage();
                return args.Length == 0 ? 2 : 0;
            }

            if (args[0] == "precheck")
            {
                return await Precheck.RunAsync().ConfigureAwait(false);
            }

            var solution = CommandArgs.Value(args, "--solution")
                ?? throw new InvalidOperationException("Pass --solution.");
            var directory = Path.GetDirectoryName(Path.GetFullPath(solution)) ?? Environment.CurrentDirectory;
            var registered = MsBuildRegistration.Register(directory);
            Console.Error.WriteLine("MSBuild: " + registered);
            return await DispatchAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // MSBuild types are first used in this method, after the locator has registered a path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<int> DispatchAsync(string[] args) => args[0] switch
    {
        "summary" => SummaryCommand.RunAsync(args),
        "reverse" => ReverseCommand.RunAsync(args),
        "reverse-batch" => ReverseCommand.BatchAsync(args),
        "sample" => SampleCommand.RunAsync(args),
        "oracle" => OracleCommand.RunAsync(args),
        "impact" => ImpactCommand.RunAsync(args),
        _ => Task.FromResult(Unknown(args[0])),
    };

    private static int Unknown(string command)
    {
        Console.Error.WriteLine("Unknown command: " + command);
        Usage();
        return 2;
    }

    private static void Usage()
    {
        Console.WriteLine(
            """
            AffectedTestsProbe

            precheck
            summary --solution <path> [--test-project <name>]... [--configuration Debug] [--platform AnyCPU] [--out file]
            sample --solution <path> [--test-project <name>]... [--seed 1] [--out file]
            reverse --solution <path> --member <documentation-id> [--test-project <name>]... [--project <path>]
            reverse-batch --solution <path> --members <file> [--repeat 3] [--test-project <name>]...
            oracle --solution <path> --member <documentation-id> --test-project <name> --clone-ack [--configuration Debug]
            impact --solution <path> [--files <file>] [--members <file>] [--sample 20] [--seed 1] [--test-project <name>]... [--out <dir>] [--no-quality]

            oracle, summary, and sample need a public git solution that builds, such as Roslyn.slnx; record the
            commit the command prints. Mutations require a clone or worktree. impact needs only a solution that
            loads: no build, no git, no file changes.
            """);
    }
}
