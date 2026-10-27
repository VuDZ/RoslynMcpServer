# Traced load (BuildHost/task-01)

- solution: Roslyn.slnx; wall 39199 ms; 434 projects, 50201 documents, 44 workspace failures; 1 traced BuildHost process(es)
- trace: complete
- clock check: 636 requests inside their client operation, 0 operation(s) whose requests lie outside them, 0 request(s) outside every operation, 0 beyond 1 ms; min lead 0.01 ms, min tail 0.026 ms
- operations served by no traced host: 0

## Wall time by what was running

| category | ms | share |
|---|---|---|
| host: design-time build | 11130 | 28.4 % |
| host: evaluation (TFM switch) | 9854 | 25.1 % |
| host: extraction / GetAllGlobs | 8663 | 22.1 % |
| several host spans at once | 4190 | 10.7 % |
| host: project file read | 1024 | 2.6 % |
| client: Resolve, no host work | 935 | 2.4 % |
| client: between loader operations | 799 | 2.0 % |
| host: response send | 504 | 1.3 % |
| host: project instance | 464 | 1.2 % |
| client: after the last loader operation | 421 | 1.1 % |
| client: Build waiting, no host work | 376 | 1.0 % |
| host: response serialization | 222 | 0.6 % |
| host: extraction / documents and metadata | 111 | 0.3 % |
| host: RPC method, other | 107 | 0.3 % |
| client: before the first loader operation | 84 | 0.2 % |
| host: process startup (estimate) | 51 | 0.1 % |
| host: RPC queue | 41 | 0.1 % |
| host: RPC dispatch | 37 | 0.1 % |
| host: build manager start | 36 | 0.1 % |
| host: build bookkeeping | 26 | 0.1 % |
| host: ProjectFileInfo extraction | 23 | 0.1 % |
| client: Evaluate waiting, no host work | 23 | 0.1 % |
| host: evaluate-ahead-start | 22 | 0.1 % |
| host: extraction / other items and properties | 13 | 0.0 % |
| host: extraction / additional and editorconfig documents | 12 | 0.0 % |
| host: waiting for the client to connect | 9 | 0.0 % |
| host: extraction / compiler arguments | 6 | 0.0 % |
| host: upstream-log | 4 | 0.0 % |
| host: extraction / properties | 4 | 0.0 % |
| host: extraction / result and project references | 3 | 0.0 % |
| host: build-ahead | 2 | 0.0 % |
| host: prepared-build | 2 | 0.0 % |
| host: extraction / fallback arguments | 2 | 0.0 % |
| host: evaluation (root) | 0 | 0.0 % |
| host: evaluate-ahead | 0 | 0.0 % |

## Projects by host time of the columns below (top 15, inclusive ms)

| project | root evaluation | TFM evaluations | design-time builds | instance | extraction | response (chars) | client Evaluate / Build / Resolve |
|---|---|---|---|---|---|---|---|
| src/Workspaces/Core/Portable/Microsoft.CodeAnalysis.Workspaces.csproj | 48 (1) | 1228 (3) | 73 (2) | 26 | 47 | 31 (1389452) | 0 / 95 / 0 |
| src/Razor/src/Compiler/Microsoft.AspNetCore.Razor.Language/test/Microsoft.AspNetCore.Razor.Language.UnitTests.csproj | 219 (1) | 384 (3) | 361 (2) | 2 | 113 | 2 (191808) | 0 / 864 / 0 |
| src/Features/Core/Portable/Microsoft.CodeAnalysis.Features.csproj | 44 (1) | 476 (3) | 121 (2) | 9 | 32 | 26 (1205366) | 0 / 71 / 114 |
| src/Compilers/Core/Portable/Microsoft.CodeAnalysis.csproj | 22 (1) | 297 (3) | 214 (2) | 21 | 40 | 25 (705000) | 0 / 73 / 2 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/Microsoft.CodeAnalysis.BannedApiAnalyzers.csproj | 374 (1) | 0 (0) | 37 (1) | 2 | 106 | 12 (492852) | 0 / 165 / 0 |
| src/Features/CSharp/Portable/Microsoft.CodeAnalysis.CSharp.Features.csproj | 22 (1) | 343 (3) | 105 (2) | 8 | 31 | 23 (936952) | 1 / 75 / 58 |
| src/Razor/src/Compiler/Microsoft.AspNetCore.Razor.Language/legacyTest/Microsoft.AspNetCore.Razor.Language.Legacy.UnitTests.csproj | 76 (1) | 156 (3) | 182 (2) | 2 | 28 | 1 (95097) | 0 / 369 / 0 |
| src/RoslynAnalyzers/PerformanceSensitiveAnalyzers/Core/Microsoft.CodeAnalysis.PerformanceSensitiveAnalyzers.csproj | 275 (1) | 0 (0) | 16 (1) | 1 | 127 | 10 (496077) | 0 / 139 / 0 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/Core/Microsoft.CodeAnalysis.Analyzers.csproj | 251 (1) | 0 (0) | 20 (1) | 2 | 110 | 12 (505540) | 0 / 147 / 0 |
| src/LanguageServer/ProtocolUnitTests/Microsoft.CodeAnalysis.LanguageServer.Protocol.UnitTests.csproj | 23 (1) | 107 (3) | 199 (2) | 1 | 53 | 2 (150712) | 0 / 364 / 1 |
| src/RoslynAnalyzers/Roslyn.Diagnostics.Analyzers/Core/Roslyn.Diagnostics.Analyzers.csproj | 220 (1) | 0 (0) | 17 (1) | 1 | 136 | 10 (498270) | 0 / 148 / 0 |
| src/LanguageServer/Protocol/Microsoft.CodeAnalysis.LanguageServer.Protocol.csproj | 23 (1) | 127 (3) | 181 (2) | 5 | 26 | 18 (621992) | 0 / 51 / 1 |
| src/EditorFeatures/Core/Microsoft.CodeAnalysis.EditorFeatures.csproj | 78 (1) | 0 (0) | 203 (1) | 17 | 63 | 6 (298132) | 0 / 73 / 445 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.AnalyzerUtilities/Microsoft.CodeAnalysis.AnalyzerUtilities.csproj | 190 (1) | 0 (0) | 25 (1) | 3 | 131 | 11 (468019) | 0 / 150 / 0 |
| src/RoslynAnalyzers/Text.Analyzers/Core/Text.Analyzers.csproj | 225 (1) | 0 (0) | 17 (1) | 1 | 109 | 9 (485257) | 0 / 119 / 0 |

Totals over 318 paths: root evaluation 15676 ms, TFM evaluations 12531 ms, design-time builds 12245 ms, extraction 8837 ms, response 688 ms.

## Repeats and reuse

- paths with more than one root evaluation: 0, 0 ms after the first
- project loads served from the host's ProjectCollection: 1869
- repeated design-time builds of one path and target framework: 0, 0 ms
- output path queries: 0, of them on an already loaded project: 0

## Observed load graph (graph.json)

- nodes: 475, of them built: 434; solution entries not loaded: 41
- edges, client load during Resolve: 130
- edges, evaluated ProjectReference item: 4160
- ProjectReference items with ReferenceOutputAssembly=false: 52

Host categories are self time: a span minus its child spans. Writing a child span's trace line falls into its parent's self time.
RPC queue runs from reading the request line to the start of its handling: parsing the request and waiting for a thread pool thread.
Process startup is placed from the wall-clock start time and is an estimate; every other host time is a Stopwatch span.
Client categories are loader operations with no host span under them: request transport, response parsing and the loader's own work.
Placeholder names (project-NNN) are numbered in the order this report names paths, projects table first, and hold for this report only.
