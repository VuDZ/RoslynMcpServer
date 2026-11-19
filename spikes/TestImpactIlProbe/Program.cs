namespace TestImpactIlProbe;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "measure" => await MeasureCommand.RunAsync(args).ConfigureAwait(false),
                "why" => WhyCommand.Run(args),
                "coverage" => await CoverageCommand.RunAsync(args).ConfigureAwait(false),
                "class-coverage" => await ClassCoverageCommand.RunAsync(args).ConfigureAwait(false),
                "recall" => await RecallCommand.RunAsync(args).ConfigureAwait(false),
                _ => Usage(),
            };
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            """
            Reads built assemblies (IL and portable PDB), builds a class-level dependency graph and reports which
            tests an edit of a production file affects. Build the solution in Debug first; nothing is executed.

              measure --root <repository> [--bin <directory>]... [--files <list.txt>] [--types <list.txt>] [--sample 40] [--seed 1] [--out <directory>]
              why     --root <repository> [--bin <directory>]... --test <test class> --type <type>
              coverage --root <repository> [--bin <directory>]... --coverage <cobertura.xml or directory>... [--out <directory>]
              class-coverage --root <repository> [--bin <directory>]... --out <directory> [--runner vstest|mtp|xunit3] [--run "<command line>"] [--only <text>] [--sample <n>] [--seed 1] [--limit <n>] [--timeout 900] [--path-filter <text>]
              recall  --root <repository> [--bin <directory>]... --classes <directory of class-coverage> [--out <directory>]

            --files lists edited source files, --types lists full names of top-level types; one per line.
            --coverage takes Cobertura reports of a test run over the same build; coverage counts the production
            files that tests execute and the graph selects no test for.
            class-coverage runs every test class on its own under a coverage collector; recall then tells, for
            every file, which of the test classes that execute it each layer selects.
            --root is where the sources are. --bin limits the search for assemblies to build output directories;
            without it the whole root is searched.
            """);
        return 2;
    }
}
