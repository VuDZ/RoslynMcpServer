# План реализации переноса из форка

Статус: **proposal, runtime не меняется**. Канон сравнения —
[../README.md](../README.md). Здесь только ship: порядок, зависимости, файлы,
тесты. Подробный разбор симптомов — в sibling-файлах каталога, не дублировать.

Каждый пункт — **отдельный патч-релиз** (или явный «тот же commit, если
независимы и ревьюер согласен»). Не смешивать VSTest-парсер с sanitizer и
platform.

## Порядок

```text
10 → 1 → 4 → 2 → 5 → 6 → 3 → 7a → 11
                              ↘ 8 (серия S1…)
                                 12 (P3, не блокер)
```

| Шаг | Пункт | Pri | Файл | Зависит от |
|---|---|---|---|---|
| 1 | 10 `get_test_list` FQN | P1 | [10-test-list-fqn.md](10-test-list-fqn.md) | — |
| 2 | 1 CLI locale | P1 | [01-cli-locale.md](01-cli-locale.md) | — |
| 3 | 4 analyzer sanitizer | P1 | [04-analyzer-sanitizer.md](04-analyzer-sanitizer.md) | — |
| 4 | 2 агрегация VSTest | P1 | [02-vstest-aggregation.md](02-vstest-aggregation.md) | желателен 1 |
| 5 | 5 ненулевой exit | P2 | [05-nonzero-exit.md](05-nonzero-exit.md) | **2** |
| 6 | 6 multi-root watcher | P2 | [06-multi-root-watcher.md](06-multi-root-watcher.md) | — |
| 7 | 3 platform target-aware | P1 `.sln` | [03-platform-target-aware.md](03-platform-target-aware.md) | — |
| 8 | 7a mixed non-C# | P2 | [07a-mixed-non-csharp.md](07a-mixed-non-csharp.md) | — |
| 9 | 11 search scope | P2 | [11-search-scope.md](11-search-scope.md) | **6** |
| позже | 8 навигация | P2 | [08-navigation.md](08-navigation.md) | 4 желателен |
| опц. | 12 lossless log | P3 | [12-lossless-output.md](12-lossless-output.md) | не 5 |
| не ship | 9 shadow ALC | research | [09-analyzer-shadow-research.md](09-analyzer-shadow-research.md) | — |
| не ship | 7b missing C# | DEFER | [07b-missing-csharp-project.md](07b-missing-csharp-project.md) | 7a |

10, 1, 4 независимы и могут идти тремя patch подряд. 3 и 7a независимы от 2/6,
но не вперемешку с парсером в одном diff.

## Общие правила каждого патча

- Минимальный диф; не копировать файлы форка целиком.
- Не трогать: `MSBUILDDISABLENODEREUSE=1`, `-p:Configuration=` на probe,
  disk-sync без `AddDocument`, `TestAttributeMatcher` base-chain, overlay
  publication, tool-groups, lazy `RoslynMcp.jsonc`.
- После зелёного `run_dotnet_build` / `dotnet test`: bump **patch** в
  `RoslynMcpServer.csproj` (`Version` / `AssemblyVersion` / `FileVersion`),
  строка в README «Agent tools by version». `AGENTS.md.sample` — только если
  меняется session policy. `PackageVersion` не трогать.
- Навигация (8) — **minor**, если появляются новые параметры/поведение тулов
  (см. `roslyn-mcp-version-bump.mdc`).
