# TestImpactIlProbe

Answers "which tests does an edit of this production file affect" from build output: it reads IL and portable
PDBs of the built assemblies, builds a dependency graph between top-level types and takes, for every test class,
the set of types it depends on. Nothing is loaded for execution, and neither MSBuild nor Roslyn is involved.

It is a measurement tool for [rapid TIA 2](../../docs/rapid-tia-2/README.md): what the graph costs, and how
much smaller the answer is than level 0 (every test assembly that references the edited one).

## Run

Build the target solution in Debug first, so that every test project has its assemblies and PDBs on disk.

```powershell
dotnet run --project spikes/TestImpactIlProbe/TestImpactIlProbe.csproj -c Release -- measure --root <repository> --bin <repository>\artifacts\bin --out il-run
```

- `--root` is the directory with the sources. `--bin` (may be repeated) limits the search for assemblies to
  build output; without it the whole root is searched. Directories named `obj` are skipped.
- `--sample 40 --seed 1` picks production files at random. `--files list.txt` and `--types list.txt` add
  files (paths relative to the root) and top-level types (full names) to ask about.
- The run writes `summary.json` (counts only, can be shared) and `raw.json` (paths and type names, private).

```powershell
dotnet run --project spikes/TestImpactIlProbe/TestImpactIlProbe.csproj -c Release -- why --root <repository> --bin <dir> --test OrderTests --type Shop.OrderService
```

`why` prints the chain of types through which a test class depends on a type, for each layer.

## Everything on one solution

```powershell
.\spikes\TestImpactIlProbe\Measure-Solution.ps1 LOCAL_ROOT/Shop.sln
```

The script takes the path to a solution and does the rest: builds the probe and the solution (Debug, portable
PDBs), runs `measure`, collects coverage of the whole test run and runs `coverage`, picks 400 test classes at
random, finds a way to run one class (`vstest`, `xunit3`, `mtp`), runs `class-coverage` (with the idle run of
every test assembly, described below) and `recall`. It
installs `dotnet-coverage` into the results directory when the tool is missing and falls back to the collector
built into VSTest when it cannot.

- Results go to `il-probe-runs\<solution>-<time>` in the user profile. Only its `share` directory may leave
  the machine: `summary.json`, `coverage-summary.json`, `recall-summary.json` and `run-info.json` hold counts,
  durations and versions, no paths and no names. Raw files, logs and reports per class name files, types and
  tests.
- `-SkipBuild` uses the Debug output already on disk, for a solution that `dotnet build` cannot build.
  `-SkipTests` reads the graph only. `-ClassSample 0` runs every test class. `-Root`, `-Out`, `-Runner` and
  `-Collector` override what the script finds by itself; `Get-Help .\Measure-Solution.ps1 -Full` lists them.
- The sources are taken to be the git repository of the solution. Every assembly built from them that lies
  under that directory enters the graph, including the output of other solutions of the repository.

Checked on Linux with PowerShell 7 and the collector built into VSTest. Not checked: Windows PowerShell 5.1 and
the `dotnet-coverage` path of the script.

## Check against coverage

An empty answer means either "no test uses this file" or "the graph does not see the dependency". Coverage of
one whole test run tells them apart: a production file that a test executes and that a layer selects no test
for is a miss.

```powershell
dotnet test <solution> -c Debug --no-build --collect "Code Coverage;Format=cobertura" --results-directory <coverage>
dotnet run --project spikes/TestImpactIlProbe/TestImpactIlProbe.csproj -c Release -- coverage --root <repository> --bin <dir> --coverage <coverage> --out il-coverage
```

- `--coverage` (may be repeated) is a Cobertura report or a directory searched for `*.cobertura.xml`. Any
  collector that writes Cobertura will do; reports of several test projects are united.
- Collect coverage from the build the graph is read from, and run the probe after the test run has ended: a
  collector may rewrite the assemblies on disk while the tests run.
- The run writes `coverage-summary.json` (counts only, can be shared) and `coverage-raw.json` (the missed
  files by assembly, private).

For each layer the files level 0 has tests for are divided into empty and non-empty answers, and each group
into executed by tests, not executed, and not in the reports (no executable lines, or the assembly was never
loaded). `missedShareOfCovered` is the share of executed files with an empty answer. Files of assemblies that
no test assembly references are counted apart, with the same division by layer: if tests execute them, level 0
misses them, and only the layers that follow loads by name can select a test for them.

What this check cannot say:

- Whether a non-empty answer holds the right tests. That needs coverage per test class, below.
- Anything about a test that failed or did not run: its files look not executed. A miss is certain, a
  confirmed empty answer is only as good as the test run.

## Check against coverage per test class

Coverage of a whole run says that some test executes a file. To judge an answer, the reference has to say
which test classes do. `class-coverage` runs every test class of the graph on its own under a collector and
keeps the files it executed; `recall` compares that with the answers.

```powershell
dotnet run --project spikes/TestImpactIlProbe/TestImpactIlProbe.csproj -c Release -- class-coverage --root <repository> --bin <dir> --out <classes> --runner vstest
dotnet run --project spikes/TestImpactIlProbe/TestImpactIlProbe.csproj -c Release -- recall --root <repository> --bin <dir> --classes <classes> --out il-recall
```

- `--runner vstest` (the default) runs `dotnet-coverage collect ... -- dotnet test Tests.dll --filter ...`;
  `--runner xunit3` runs the test executable of xUnit v3 with `-class`, `--runner mtp` runs it with the
  Microsoft.Testing.Platform option `--filter-class`. Which one an executable takes shows in the log of a
  trial run: OrchardCore runs through Microsoft.Testing.Platform under `dotnet test`, and its executable
  takes only `-class`. `--run "<command line>"` replaces all three: `{assembly}`, `{exe}`, `{class}`, `{filter}` and
  `{report}` are filled in, and `{report}` may be written as a file or as a directory of Cobertura files.
  `--run @file` reads the line from a file, for a shell that spoils quotes inside an argument.
- Try `--limit 3` first and read `<classes>/<Assembly>/<Class>.log`: a wrong command line shows there.
  `--sample 400 --seed 1` runs a random part of the test classes: an answer is compared only with the
  measured classes, so a part still tells which share of executing classes a layer keeps.
  `--only <text>` limits the run to matching `Assembly/Class` names, `--path-filter net10.0` picks an output
  directory when a test project has several, `--timeout` (seconds, 900) stops a hanging class.
- A class that already has a report is skipped, so an interrupted run continues and a failed class is retried
  by running the command again. A full Cobertura report per class is large; only the names of the executed
  files are kept, as `<classes>/<Assembly>/<Class>.cobertura.xml`.
- `recall` writes `recall-summary.json` (counts only, can be shared) and `recall-raw.json` (the lost test
  classes and files, private).

For level 0 and every layer `recall` counts pairs of a file and a test class that executes it. `recall` is the
share of pairs whose class is in the answer for the file; `filesLosing` is the number of files that miss at
least one executing class; `precision` is the share of selected classes that execute the file. An answer is
cut down to the measured classes, so a class without a report costs nothing and proves nothing.

### The idle run

A run of one class starts with a search for the tests of the whole assembly, and the search executes code.
xUnit under VSTest computes `MemberData` and `ClassData` of every theory of the assembly, so a constructor
called in the theory data of one class is in the coverage of every other class of that assembly. Such a pair
looks lost by every layer, and no rule over the IL of the class can keep it: the class does not use the file.

`class-coverage` therefore runs each test assembly once more with a class name that no test has, and keeps the
files that run executed as `<classes>/<Assembly>/idle-run.xml`, with `idle-run.log` next to it. An assembly
that has the file is skipped, so running the command again on an old `<classes>` directory adds only the idle
runs. A pair is outside the idle run when the test assembly of the class does not execute the file with no
test selected. `recall` reports both counts:

- `pairs`, `keptPairs`, `recall`, `filesLosing` are over all pairs, the search for tests included;
- `pairsOutsideIdleRun`, `keptPairsOutsideIdleRun`, `recallOutsideIdleRun`, `filesLosingOutsideIdleRun` leave
  out every pair of the idle run. That also drops the classes that do call a file of the idle run, so these
  counts describe fewer pairs, each of them certain;
- `classes.testAssembliesWithIdleRun` says how many test assemblies of the measured classes have the report,
  `testAssembliesIdleRunExecutes` how many of those execute a production file with no test selected. Pairs of
  an assembly without the report all count as outside the idle run;
- in `recall-raw.json` the files and classes lost outside the idle run come first, with `lostOutsideIdleRun`
  and `filesOutsideIdleRun` next to the totals.

Checked on this probe with VSTest and xUnit v2: the theory data of `RecallComparisonTests` puts
`SelectorRecall.cs` into the coverage of the other 13 classes, the idle run names that file, and `discovery`
keeps 83.3% of 78 pairs and all 64 pairs outside the idle run. Not checked: the `xunit3` and `mtp` runners,
NUnit and MSTest.

Limits:

- A class is run alone, so state shared between classes in a full run is not there. Code that only runs
  because another class ran first is not counted.
- The VSTest filter is "the test name contains `Namespace.Class.`": a class with the same name under a longer
  namespace in the same assembly runs too. Whether the `mtp` and `xunit3` runners run test classes nested in the named one has not been checked.
- Coverage comes from one target framework; the graph merges all of them.

## What is in the graph

- **Assemblies**: those with a portable PDB (next to the file or embedded) that names at least one source
  file present on disk. Everything else is a package or the framework and is left out. All builds of one
  assembly (target frameworks, configurations) are merged.
- **Nodes**: top-level types. Nested types, lambdas and state machines belong to their outermost type.
- **Edges**: base types and interfaces, signatures of fields and methods, generic constraints, attributes and
  their `typeof` arguments, local variables, catch clauses and every type, method and field token in IL.
- **Files**: taken from the PDB, including types without method bodies.
- **Test classes**: non-abstract types with methods marked by an xUnit, NUnit or MSTest attribute (recognized
  by name, including attributes derived from them in the solution); inherited test methods count.

## Layers

A dependency injection container or MEF finds types that no code names. The probe records code that hands a
whole assembly to reflection: `typeof(X).Assembly`, `Assembly.GetExecutingAssembly()`, generic methods such as
`FromAssemblyOf<T>()`, `UseStartup<T>()` and `WebApplicationFactory<T>`. The answer is reported in layers:

| Layer | A test class depends on |
| --- | --- |
| `references` | types reachable through the edges above |
| `discovery` | also, in each assembly handed to reflection: types that implement or derive from a reached type, and types a framework finds by itself (a base type, interface or attribute from a package) |
| `wholeAssembly` | also every type of each assembly handed to reflection |
| `referencesWithLoads` | `references`, and for a test class that reaches a load by name: every type of every assembly lying next to its test assembly |
| `discoveryWithLoads` | `discovery` with the same rule for loads by name |
| `discoveryWithNamedLoads` | `discoveryWithLoads`, except that `AppDomain.GetAssemblies` counts as reflection over the assemblies the test class already reaches |

A plugin host loads assemblies by a name that only a string holds: `Assembly.Load*`, `AssemblyLoadContext.Load*`,
`AppDomain.GetAssemblies`. No rule about types can follow that, so the layers with loads give such a test class
everything its process can load: the assemblies the build copied into the directory of the test assembly. A
test class that reaches no such call keeps its narrow answer. `loadsByName` in the summary counts, for each of
the three calls, the types that make it and the test classes that reach them.

`AppDomain.GetAssemblies` names nothing and loads nothing: it returns what the process has loaded. The last
layer therefore treats it as a scan, by the `discovery` rule, of every assembly that holds a type the test
class reaches, and keeps "everything next to the test assembly" for the calls that take a name.

These layers can select tests for a file level 0 has none for, and more tests than level 0 has: level 0 here
follows references in metadata, and a module loaded by name is only copied next to the test assembly.

An explicit registration such as `AddScoped<IService, Service>()` names the implementation, so it is already
in `references` for every test that reaches the registration code.

## Known limits

- A constant is copied into its user by the compiler: an edited `const` or enum value leaves no edge.
  `nameof` leaves none either.
- Another process is not followed: a test that starts an executable depends on nothing inside it.
- Types found by name in a string or a configuration file are not seen.
- `x.GetType().Assembly` is taken as the assembly of the calling code, and `Assembly.GetEntryAssembly()` is
  ignored. Both are counted in `opaqueAssemblyLoads` together with the loads by name.
- A type found by `Type.GetType("Name, Assembly")` or a directory scanned for plugins is not a load by name
  for the layers above.
- The graph describes the last build. An edit that makes existing code bind to another member (a new overload,
  a new extension method) is not visible until the next build.
- A Windows PDB cannot be read; such assemblies are counted in `unreadablePdbAssemblies` and are missing.
- Level 0 here follows assembly references in metadata. The compiler drops unused references, so it can be
  smaller than level 0 by project references.

## Tests

```powershell
dotnet test spikes/TestImpactIlProbe/TestImpactIlProbe.Tests/TestImpactIlProbe.Tests.csproj
```

The tests read the test assembly itself: its `Fixtures` directory holds small types for each rule.
