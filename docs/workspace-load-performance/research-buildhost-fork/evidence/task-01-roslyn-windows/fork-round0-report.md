# Traced load (BuildHost/task-01)

- solution: (not shown); wall 41320 ms; 434 projects, 50201 documents, 44 workspace failures; 1 traced BuildHost process(es)
- clock check: 636 requests inside their client operation, 0 unmatched, 0 beyond 1 ms; min lead 0.013 ms, min tail 0.013 ms

## Wall time by what was running

| category | ms | share |
|---|---|---|
| host: design-time build | 18018 | 43.6 % |
| host: evaluation (TFM switch) | 7918 | 19.2 % |
| host: evaluation (root) | 5788 | 14.0 % |
| host: ProjectFileInfo extraction | 5633 | 13.6 % |
| client: Resolve, no host work | 800 | 1.9 % |
| client: between loader operations | 783 | 1.9 % |
| client: after the last loader operation | 550 | 1.3 % |
| host: response send | 516 | 1.2 % |
| host: project instance | 345 | 0.8 % |
| client: Build waiting, no host work | 267 | 0.6 % |
| host: response serialization | 218 | 0.5 % |
| client: before the first loader operation | 90 | 0.2 % |
| host: RPC method, other | 87 | 0.2 % |
| host: process startup (estimate) | 51 | 0.1 % |
| host: project file read | 49 | 0.1 % |
| host: RPC queue | 46 | 0.1 % |
| host: RPC dispatch | 42 | 0.1 % |
| client: Evaluate waiting, no host work | 37 | 0.1 % |
| host: build manager start | 34 | 0.1 % |
| host: build bookkeeping | 33 | 0.1 % |
| host: waiting for the client to connect | 15 | 0.0 % |

## Projects by host time (top 15, inclusive ms)

| project | root evaluation | TFM evaluations | design-time builds | instance | extraction | response (chars) | client Evaluate / Build / Resolve |
|---|---|---|---|---|---|---|---|
| project-001.csproj | 19 (1) | 0 (0) | 3492 (1) | 1 | 28 | 2 (77004) | 20 / 3524 / 8182 |
| project-002.csproj | 149 (1) | 439 (3) | 412 (2) | 2 | 117 | 2 (217162) | 149 / 974 / 0 |
| project-003.csproj | 22 (1) | 750 (3) | 89 (2) | 9 | 29 | 30 (1414706) | 22 / 915 / 0 |
| project-004.csproj | 18 (1) | 0 (0) | 639 (1) | 1 | 17 | 1 (61094) | 19 / 661 / 1917 |
| project-005.csproj | 16 (1) | 505 (3) | 76 (2) | 9 | 23 | 34 (1230334) | 16 / 652 / 948 |
| project-006.csproj | 17 (1) | 321 (3) | 85 (2) | 7 | 19 | 22 (962065) | 18 / 469 / 210 |
| project-007.csproj | 190 (1) | 0 (0) | 189 (1) | 3 | 51 | 6 (13713) | 235 / 261 / 14931 |
| project-008.csproj | 46 (1) | 0 (0) | 270 (1) | 2 | 67 | 7 (280423) | 47 / 349 / 2773 |
| project-009.csproj | 40 (1) | 115 (3) | 192 (2) | 1 | 20 | 1 (120451) | 41 / 330 / 0 |
| project-010.csproj | 12 (1) | 0 (0) | 315 (1) | 0 | 12 | 1 (78224) | 12 / 330 / 1087 |
| project-011.csproj | 121 (1) | 0 (0) | 17 (1) | 1 | 125 | 11 (505588) | 122 / 158 / 0 |
| project-012.csproj | 130 (1) | 0 (0) | 31 (1) | 1 | 101 | 8 (498318) | 131 / 143 / 0 |
| project-013.csproj | 27 (1) | 0 (0) | 212 (1) | 1 | 23 | 2 (188999) | 28 / 239 / 762 |
| project-014.csproj | 47 (1) | 0 (0) | 140 (1) | 2 | 68 | 7 (298180) | 48 / 219 / 2473 |
| project-015.csproj | 40 (1) | 99 (3) | 114 (2) | 1 | 7 | 2 (106023) | 40 / 224 / 0 |

Totals over 318 paths: root evaluation 5788 ms, TFM evaluations 7918 ms, design-time builds 18018 ms, extraction 5633 ms, response 686 ms.

## Repeats and reuse

- paths with more than one root evaluation: 0, 0 ms after the first
- project loads served from the host's ProjectCollection: 0
- repeated design-time builds of one path and target framework: 0, 0 ms
- output path queries: 0, of them on an already loaded project: 0

## Observed load graph (graph.json)

- nodes: 475, of them built: 434; solution entries not loaded: 41
- edges, client load during Resolve: 130
- edges, evaluated ProjectReference item: 4160
- ProjectReference items with ReferenceOutputAssembly=false: 52

Host categories are self time: a span minus its child spans. Writing a child span's trace line falls into its parent's self time.
Process startup is placed from the wall-clock start time and is an estimate; every other host time is a Stopwatch span.
Client categories are loader operations with no host span under them: request transport, response parsing and the loader's own work.
