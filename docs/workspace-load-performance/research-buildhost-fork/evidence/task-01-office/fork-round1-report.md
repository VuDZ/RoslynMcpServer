# Traced load (BuildHost/task-01)

- solution: (not shown); wall 43379 ms; 142 projects, 18877 documents, 1674 workspace failures; 1 traced BuildHost process(es)
- clock check: 274 requests inside their client operation, 10 unmatched, 0 beyond 1 ms; min lead 0.02 ms, min tail -0.036 ms

## Wall time by what was running

| category | ms | share |
|---|---|---|
| host: design-time build | 13740 | 31.7 % |
| host: evaluation (root) | 10267 | 23.7 % |
| host: ProjectFileInfo extraction | 10046 | 23.2 % |
| client: Build waiting, no host work | 3721 | 8.6 % |
| client: Evaluate waiting, no host work | 1552 | 3.6 % |
| client: between loader operations | 1143 | 2.6 % |
| client: Resolve, no host work | 970 | 2.2 % |
| client: after the last loader operation | 550 | 1.3 % |
| host: response send | 302 | 0.7 % |
| host: project instance | 225 | 0.5 % |
| client: before the first loader operation | 200 | 0.5 % |
| host: response serialization | 171 | 0.4 % |
| host: process startup (estimate) | 119 | 0.3 % |
| host: RPC method, other | 92 | 0.2 % |
| host: project file read | 86 | 0.2 % |
| host: build manager start | 55 | 0.1 % |
| host: RPC queue | 53 | 0.1 % |
| host: RPC dispatch | 42 | 0.1 % |
| host: build bookkeeping | 24 | 0.1 % |
| host: waiting for the client to connect | 21 | 0.0 % |
| several host spans at once | 0 | 0.0 % |

## Projects by host time (top 15, inclusive ms)

| project | root evaluation | TFM evaluations | design-time builds | instance | extraction | response (chars) | client Evaluate / Build / Resolve |
|---|---|---|---|---|---|---|---|
| project-001.csproj | 532 (1) | 0 (0) | 1794 (1) | 6 | 239 | 35 (400782) | 609 / 2104 / 7186 |
| project-002.csproj | 547 (1) | 0 (0) | 1455 (1) | 8 | 538 | 45 (1248933) | 548 / 2072 / 2636 |
| project-003.csproj | 195 (1) | 0 (0) | 379 (1) | 3 | 213 | 15 (425040) | 196 / 614 / 891 |
| project-004.csproj | 146 (1) | 0 (0) | 461 (1) | 6 | 154 | 10 (238914) | 147 / 633 / 886 |
| project-005.csproj | 164 (1) | 0 (0) | 434 (1) | 1 | 161 | 8 (279388) | 165 / 607 / 1300 |
| project-006.csproj | 102 (1) | 0 (0) | 557 (1) | 2 | 99 | 5 (161576) | 103 / 666 / 3656 |
| project-007.csproj | 137 (1) | 0 (0) | 448 (1) | 1 | 145 | 4 (135308) | 138 / 600 / 1400 |
| project-008.csproj | 237 (1) | 0 (0) | 138 (1) | 22 | 247 | 15 (420129) | 238 / 427 / 1333 |
| project-009.csproj | 65 (1) | 0 (0) | 484 (1) | 1 | 66 | 1 (62277) | 66 / 552 / 1593 |
| project-010.csproj | 191 (1) | 0 (0) | 215 (1) | 2 | 197 | 8 (422002) | 192 / 425 / 0 |
| project-011.csproj | 228 (1) | 0 (0) | 133 (1) | 1 | 233 | 11 (372112) | 229 / 382 / 3 |
| project-012.csproj | 211 (1) | 0 (0) | 121 (1) | 1 | 207 | 10 (336716) | 212 / 346 / 208 |
| project-013.csproj | 145 (1) | 0 (0) | 234 (1) | 2 | 138 | 5 (141838) | 146 / 382 / 927 |
| project-014.csproj | 121 (1) | 0 (0) | 251 (1) | 2 | 115 | 6 (168493) | 122 / 375 / 2119 |
| project-015.csproj | 220 (1) | 0 (0) | 20 (1) | 2 | 207 | 2 (57850) | 221 / 232 / 0 |

Totals over 142 paths: root evaluation 10267 ms, TFM evaluations 0 ms, design-time builds 13740 ms, extraction 10046 ms, response 381 ms.

## Repeats and reuse

- paths with more than one root evaluation: 0, 0 ms after the first
- project loads served from the host's ProjectCollection: 0
- repeated design-time builds of one path and target framework: 0, 0 ms
- output path queries: 0, of them on an already loaded project: 0

## Observed load graph (graph.json)

- nodes: 232, of them built: 137; solution entries not loaded: 90
- edges, client load during Resolve: 101
- edges, evaluated ProjectReference item: 1435
- ProjectReference items with ReferenceOutputAssembly=false: 356

Host categories are self time: a span minus its child spans. Writing a child span's trace line falls into its parent's self time.
Process startup is placed from the wall-clock start time and is an estimate; every other host time is a Stopwatch span.
Client categories are loader operations with no host span under them: request transport, response parsing and the loader's own work.
