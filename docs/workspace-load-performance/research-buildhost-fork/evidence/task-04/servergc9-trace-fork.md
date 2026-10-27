# Traced load (BuildHost/task-01)

- solution: Roslyn.slnx; wall 35995 ms; 434 projects, 50201 documents, 44 workspace failures; 1 traced BuildHost process(es)
- trace: complete
- clock check: 636 requests inside their client operation, 0 operation(s) whose requests lie outside them, 0 request(s) outside every operation, 0 beyond 1 ms; min lead 0.009 ms, min tail 0.026 ms
- operations served by no traced host: 0

## Wall time by what was running

| category | ms | share |
|---|---|---|
| host: design-time build | 9738 | 27.1 % |
| host: evaluation (TFM switch) | 9460 | 26.3 % |
| host: extraction / GetAllGlobs | 8232 | 22.9 % |
| several host spans at once | 3366 | 9.4 % |
| host: project file read | 980 | 2.7 % |
| client: Resolve, no host work | 932 | 2.6 % |
| client: between loader operations | 808 | 2.2 % |
| host: response send | 512 | 1.4 % |
| client: after the last loader operation | 436 | 1.2 % |
| client: Build waiting, no host work | 374 | 1.0 % |
| host: project instance | 327 | 0.9 % |
| host: response serialization | 228 | 0.6 % |
| host: extraction / documents and metadata | 95 | 0.3 % |
| client: before the first loader operation | 88 | 0.2 % |
| host: RPC method, other | 88 | 0.2 % |
| host: process startup (estimate) | 67 | 0.2 % |
| host: RPC queue | 43 | 0.1 % |
| host: build manager start | 37 | 0.1 % |
| host: RPC dispatch | 36 | 0.1 % |
| host: build bookkeeping | 25 | 0.1 % |
| host: evaluate-ahead-start | 24 | 0.1 % |
| client: Evaluate waiting, no host work | 23 | 0.1 % |
| host: ProjectFileInfo extraction | 22 | 0.1 % |
| host: extraction / additional and editorconfig documents | 13 | 0.0 % |
| host: extraction / other items and properties | 12 | 0.0 % |
| host: extraction / compiler arguments | 6 | 0.0 % |
| host: waiting for the client to connect | 5 | 0.0 % |
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
| src/Workspaces/Core/Portable/Microsoft.CodeAnalysis.Workspaces.csproj | 34 (1) | 1014 (3) | 72 (2) | 10 | 41 | 39 (1389452) | 0 / 110 / 0 |
| src/Razor/src/Compiler/Microsoft.AspNetCore.Razor.Language/test/Microsoft.AspNetCore.Razor.Language.UnitTests.csproj | 152 (1) | 360 (3) | 320 (2) | 2 | 101 | 3 (191808) | 0 / 786 / 0 |
| src/Features/Core/Portable/Microsoft.CodeAnalysis.Features.csproj | 22 (1) | 476 (3) | 105 (2) | 9 | 34 | 33 (1205366) | 0 / 84 / 128 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/Microsoft.CodeAnalysis.BannedApiAnalyzers.csproj | 375 (1) | 0 (0) | 22 (1) | 2 | 134 | 10 (492852) | 0 / 175 / 0 |
| src/Features/CSharp/Portable/Microsoft.CodeAnalysis.CSharp.Features.csproj | 31 (1) | 351 (3) | 85 (2) | 5 | 42 | 25 (936952) | 0 / 84 / 56 |
| src/Compilers/Core/Portable/Microsoft.CodeAnalysis.csproj | 26 (1) | 232 (3) | 171 (2) | 8 | 26 | 25 (705000) | 0 / 59 / 2 |
| src/Razor/src/Compiler/Microsoft.AspNetCore.Razor.Language/legacyTest/Microsoft.AspNetCore.Razor.Language.Legacy.UnitTests.csproj | 50 (1) | 130 (3) | 150 (2) | 1 | 37 | 1 (95097) | 0 / 321 / 0 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.AnalyzerUtilities/Microsoft.CodeAnalysis.AnalyzerUtilities.csproj | 157 (1) | 0 (0) | 24 (1) | 3 | 172 | 12 (468019) | 0 / 192 / 0 |
| src/RoslynAnalyzers/Tools/GenerateDocumentationAndConfigFiles/GenerateDocumentationAndConfigFiles.csproj | 188 (1) | 0 (0) | 18 (1) | 1 | 145 | 9 (459222) | 0 / 174 / 0 |
| src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/Core/Microsoft.CodeAnalysis.Analyzers.csproj | 180 (1) | 0 (0) | 19 (1) | 2 | 138 | 13 (505540) | 0 / 175 / 0 |
| src/Features/VisualBasic/Portable/Microsoft.CodeAnalysis.VisualBasic.Features.vbproj | 21 (1) | 197 (3) | 88 (2) | 4 | 25 | 10 (473948) | 0 / 45 / 44 |
| src/VisualStudio/Core/Def/Microsoft.VisualStudio.LanguageServices.csproj | 52 (1) | 0 (0) | 213 (1) | 2 | 50 | 6 (280375) | 0 / 60 / 567 |
| src/RoslynAnalyzers/PerformanceSensitiveAnalyzers/Core/Microsoft.CodeAnalysis.PerformanceSensitiveAnalyzers.csproj | 183 (1) | 0 (0) | 17 (1) | 2 | 111 | 9 (496077) | 0 / 122 / 0 |
| src/EditorFeatures/Core/Microsoft.CodeAnalysis.EditorFeatures.csproj | 51 (1) | 0 (0) | 203 (1) | 3 | 51 | 7 (298132) | 0 / 62 / 471 |
| src/RoslynAnalyzers/Text.Analyzers/Core/Text.Analyzers.csproj | 173 (1) | 0 (0) | 23 (1) | 1 | 97 | 10 (485257) | 0 / 108 / 0 |

Totals over 318 paths: root evaluation 12593 ms, TFM evaluations 11557 ms, design-time builds 10635 ms, extraction 8389 ms, response 704 ms.

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
