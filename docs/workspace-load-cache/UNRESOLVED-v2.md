# Unresolved arbitration issues

Статус: **normative open gates**. U-ARB-01 решён: запись поколения до ответа
`load_workspace`. U-ARB-02 для живой сессии решён: доверие watcher между
подтверждениями. U-ARB-04 решён повторной проверкой DLL и новым session id.
U-ARB-05 решён: hydrate host — `AdhocWorkspace`, запись только `.cs`.
U-ARB-06 решён профилем `sdk-project-v1`. У U-ARB-03 выбран baseline
обычной загрузки офисного ПК. Hit-rate и miss-overhead по-прежнему не выбраны.

## U-ARB-01 — Capture scheduling и durable cadence

Findings: E2-05 — UNRESOLVED; E3-06 — ACCEPT WITH MODIFICATION. Решение
владельца 2026-10-02: **запись поколения до ответа `load_workspace`**. Текст
выбора — в [epoch-2](epoch-2-conservative-disk-cache/spec.md#выбранный-capture-schedule).

Тот же вызов после полной обычной загрузки публикует указатель и возвращает
`written` или `failed`. Успех загрузки от записи не зависит. Поколение после
каждого edit не пишется.

Решение не задаёт численный budget и не включает disk-hit.

## U-ARB-02 — Live freshness, lost events и availability

Findings: E3-02, E3-04. Решение владельца 2026-10-01: **доверие watcher между
подтверждениями**. Текст выбора и три не выбранных варианта (строгая проверка
на каждый вызов, периодическое подтверждение, последний снимок с
`freshness=unknown`) — в
[epoch-3](epoch-3-live-consistency/spec.md#выбранный-контракт-чтения).

Это политика уже работающей живой сессии. Она не активирует дисковый кэш,
не закрывает измерения U-ARB-03 и не ослабляет A-WRITE и `Banned`/`Unavailable`.
Тихо потерянное событие остаётся принятой дырой до следующего `load_workspace`.

## U-ARB-03 — Репрезентативная нагрузка и численный budget

Findings: R-06, E0-02, E0-04, E2-04, V-03, V-04.

До activation владелец фиксирует target solution/size, SDK, TFM/instances,
generators/imports/tasks, overlay/metadata, workload mix, median/p95,
miss-overhead, hit-rate и resource budgets. 70% не является восстановленным
требованием. Unsupported обязательная target feature означает revise для цели.

Решение владельца 2026-10-03: baseline обычной загрузки — отчёт
`20260922-120221-bondarev` (хост `bondarev`, 6 процессоров, 65318 МБ, сервер
`1.4.14.0`). Текст и таблицы Warm median — в
[epoch-0](epoch-0-feasibility/spec.md#выбранный-baseline-офисного-пк).
Сценарий отчёта — новый процесс и первый символ на неизменном дереве, без
overlay. Hit-rate и miss-overhead в отчёте нет, нулями они не заполняются.
Public activation этим не открывается. Оба корпуса вне текущего профиля.

Уточнение владельца 2026-10-03: OrchardCore/Roslyn выбраны случайно и не
являются обязательными target. Для проверки нового кандидата выбран
BTCPayServer `v2.4.3`, commit `f78d6c20f0aa184f61eb4533bcf1dee20e366579`,
scope `BTCPayServer/BTCPayServer.csproj` со всеми его транзитивными ссылками.
Runbook — [baseline benchmark](baseline-benchmark.md). Прогон кандидата не
заменяет выбранный отчёт офисного ПК и не утверждает его admission,
достаточность размера, hit-rate, miss-overhead или resource budgets.
Результат — [BTCPayServer candidate](btcpay-candidate.md): 10 успешных warm
попыток на `VuDZ-PC`, SDK `10.0.300`, сервер `1.5.4.0`. Внешний NuGet source
и пакетные Target вне SDK не допускаются текущим профилем. U-ARB-03 не закрыт.

Решение владельца 2026-10-03: вернуться к выбору workload и оставшихся budgets
после полной технической реализации кеша. Порядок работы —
[E4/task-13](epoch-4-measured-optimizations/task-13-workload-budget-and-series-gate-Astra.md):
[E4/task-16](epoch-4-measured-optimizations/task-16-workload-budget-and-performance-Sol.md)
готовит workload/budgets и выполняет измерения после утверждения владельцем;
task-13 независимо принимает evidence. Budget approval предшествует первым
cache-hit performance results.
Этот пункт не блокирует реализацию и функциональные fixtures, но остаётся gate
public activation.

## U-ARB-04 — Portable analyzer provenance и cache trust

Findings: R-04, C-05, C-01; risks N2/N3/N7. Решение владельца 2026-10-01:
**повторная проверка DLL и новый `LoadSessionId`**. Текст выбора — в
[epoch-0](epoch-0-feasibility/spec.md#выбранное-admission-overlay).

Disk hydrate не подставляет в gate snapshot с диска и не грузит analyzer DLL
только по DTO. Совпали путь проекта, TFM, путь DLL, хеш содержимого и каталог
pack — процесс выдаёт новый session id и пускает существующий gate. Иначе
base graph без overlay.

Решение не выбирает hydrate host и не включает disk-hit.

## U-ARB-05 — Production hydrate host и mutating contract

Findings: R-01, E0-01, E1-01. Решение владельца 2026-10-01: **`AdhocWorkspace`
и writer только для `.cs`**. Текст выбора — в
[epoch-0](epoch-0-feasibility/spec.md#выбранный-hydrate-host).

`MSBuildWorkspace` 5.9.0 публично наполняется только через `Open*`. Adhoc
принимает `ProjectInfo` и при `TryApplyChanges` файл не пишет. Проектный файл
из hydrate-сессии не меняется. Явный `<Compile Include>` без глобов —
`unsupported`.

Решение не включает disk-hit и не заменяет сравнение со свежим
`MSBuildWorkspace`.

## U-ARB-06 — Dependency-closure evidence

Findings: R-02, C-01, E0-02, E2-01. Решение владельца 2026-10-01: профиль
**`sdk-project-v1`**. Текст выбора — в
[epoch-0](epoch-0-feasibility/spec.md#выбранный-admission-profile).

Spike на SDK 10.0.300 и Roslyn `Workspaces.MSBuild` 5.9.0 показал, какой
источник закрывает какую категорию. Design-time binlog с `ProjectImports=None`
даёт positive-импорты и отсутствующий `Exists`. Glob-регион берётся из XML
проекта, не из событий лога. Пользовательский target — `unknown`. Каталог SDK
опознаётся по пути `Sdk.props`, не по строке TFM.

Профиль не выбирает hydrate host и не
включает disk-hit. Текущий replay provenance по-прежнему читает только item
`Analyzer`.

2026-10-03 владелец расширил тот же профиль проектами Razor и Web. Текст — в
[epoch-0](epoch-0-feasibility/spec.md#расширение-профиля-razor-и-web). Расширение
не закрывает U-ARB-03 и не включает disk-hit.
