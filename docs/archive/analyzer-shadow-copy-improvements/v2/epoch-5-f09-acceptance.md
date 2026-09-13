# Приёмка F-09 — эскиз захвата provenance

Дата: 2026-09-12. Вердикт: **эскиз принят; P0 принят**
([epoch-5-f09-p0-acceptance.md](epoch-5-f09-p0-acceptance.md)).
Matcher и E5-S2 rollout не открыты.
Норматив: [FOLLOWUPS.md](FOLLOWUPS.md) F-09, [U-ARB-01](UNRESOLVED-v2.md),
[epoch-5-f09-capture-design.md](epoch-5-f09-capture-design.md).

| ID | Sev | Тема | Статус |
| --- | --- | --- | --- |
| F09-D1 | — | Канал / не второй eval | **закрыт** — binlog той же `Open*Async`; replay без target pass |
| F09-D2 | — | Входы / lifetime / fail-closed | **закрыт** — load/graph-stale/globals; skip без metadata ≠ foreign |
| F09-D3 | — | Matcher / Alt-2/3 / inaccessible | **закрыт** — не выбирались |
| F09-01 | Medium | Join TargetPath vs obj `AssemblyPath` | **закрыт P0** — exact `Analyzer.Identity == Project.OutputFilePath`; свойства не обязательны |
| F09-02 | Medium | Capture на каждом load, даже flag=false | **измерен P0** — always-on сохранён; telemetry обязательна |
| F09-03 | Low | Type-name `BinaryLogger` — частный контракт | **закрыт как факт 5.9.0**; pin в spike |
| F09-04 | Low | В csproj нет `Microsoft.Build` / `BinaryLogger` | **закрыт v1.3.9** ([приёмка](epoch-5-f09-production-capture-acceptance.md)) |

---

Независимая сверка (этот проход):

`MSBuildWorkspace.OpenSolutionAsync(..., ILogger?)` есть.
`MSBuildProjectLoader.LoadInfoAsync` передаёт в `BuildHostProcessManager` только
если `logger.GetType().FullName == "Microsoft.Build.Logging.BinaryLogger"`;
иначе `binaryLogPathProvider = null`. Custom `ILogger` в BuildHost не идёт.
Утверждение эскиза верное.

Эскиз отвечает Done when F-09: канал, входы, lifetime, отказ, честная цена
(диск/replay, не второй graph). `ProjectInstance` и второй `dotnet msbuild`
оценены и не взяты как production fallback.

---

ID: F09-01
Severity: Medium
Depends: E5-S1 obj vs bin

Target:
epoch-5-f09-capture-design.md § Ключ item → source

Claim:
E5-S1: Consumer analyzer = `bin\...\Generator.dll`, loaded Generator
`CompilationOutputInfo` = `obj\...\Generator.dll`. Эскиз сравнивает
`TargetPath` только с `OutputFilePath`, `IntermediateAssembly` только с
`AssemblyPath`. «Конфликт двух непустых точных сравнений» можно прочитать
как отказ, если bin≠obj при живом obj-match. На исходном redirected fixture
join может стать unconfirmed без эвристики path.

Suggested change:
P0 на fixture эпохи 1 (missing + existing path). Зафиксировать: один точный
hit достаточен; mismatch другой пары не conflict, если не указывает на
другой `ProjectId`. Не принимать E5-S2, пока join на этой fixture не зелёный.

---

ID: F09-02
Severity: Medium
Depends: U-ARB-05 sticky false→true

Target:
epoch-5-f09-capture-design.md § Lifetime («захват независимо от флага»)

Claim:
Каждый физический `load_workspace` этого сервера платит binlog I/O, даже если
overlay никогда не включат. Это закрывает cached `false→true` без второй
evaluation. Для большого solution — налог на общий путь. SLA нет.

Suggested change:
P0 измерить wall/bytes на реальном multi-project. Не выключать always-on в
эскизе без отдельного решения (ломает false→true без eval).

---

ID: F09-04
Severity: Low
Depends: F09-03

Target:
RoslynMcpServer.csproj

Claim:
Сейчас `Microsoft.Build.Framework` / `Utilities` / `Tasks`, без
`Microsoft.Build` (там `BinaryLogger` и `BinaryLogReplayEventSource`).
Spike должен добавить зависимость явно; ExcludeAssets/runtime как у остальных
MSBuild refs.

Suggested change:
Только в P0, не в этом коммите эскиза.

---

## Что не чинить сейчас

- E5-S2 / смена matcher
- Alt-2 / Alt-3 / inaccessible
- Реализация BinaryLogger в `SolutionManager`
- U-ARB-04 / U-ARB-05
