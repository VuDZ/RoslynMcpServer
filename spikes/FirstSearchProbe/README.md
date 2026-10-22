# First declaration search probe

Standalone reproduction of Roslyn text-loader retries. It uses the Roslyn binaries
from the server's Release output; it does not load an external solution.

Build `RoslynMcpServer.sln` in Release, then run:

```powershell
dotnet run --project spikes/FirstSearchProbe/FirstSearchProbe.csproj -c Release
```

The program emits JSON for a control, failing text loaders, sequential projects,
warm repeats, and an actual missing file. It asserts the symbol and retry counts.
Timings are observations, without timing thresholds. A randomly named missing file
under the system temp directory is queried; no source files are created or removed.

The project stays outside the main solution and is excluded by the host's existing
`spikes/**` compile exclusion, including its generated `obj` files.
See [findings](../../docs/workspace-load-performance/epoch-0-stage-measurement/evidence/first-search-investigation-2026-10-22.md).
This demonstrates a library mechanism; it does not establish office I/O failures.
