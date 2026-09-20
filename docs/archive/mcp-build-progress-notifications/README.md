# MCP `notifications/progress` для `run_dotnet_build`

Дата: 2026-09-12. Статус: **S1–S4 shipped** (S1–S3 v1.3.25, review-исправления
v1.3.26, S4 v1.3.27). Process (`review/`, `response/`) — в
[_archive/](_archive/README.md).
Повод: длинный `tools/call` на build/test и вопрос, лечит ли progress
хост-таймаут Cursor (п.1). Ответ: **сам лимит хоста — нет**; progress —
UX-heartbeat, не фикс п.1.

Состояние репозитория на момент записи плана: **v1.3.19**, S5 shadow-copy
принят. Реализация первой поставки: **v1.3.25**.

## Зачем отложено

Статистика прогонов в Cursor IDE + stdio (сессия v3 S5, 2026-09-12):

| Вызов | Наблюдение |
| --- | --- |
| Unit (`Planner` / write-boundary / catalog) | секунды |
| Один AnalyzerLifecycle метод | 30–50 с |
| Класс S3 / S4 / S5 при `timeoutSeconds=600` | 2–4 мин, вызов дошёл до конца |
| Хост-abort `-32001` / client abort | не было |
| Фактические «таймауты» | внутренние 15 с (`waitDirty`) и 45 с (`host-response-timeout`) |

Cursor IDE уже держит такие вызовы. OpenCode лечится `"timeout": 600000` в
`opencode.json`, не progress. Cursor ACP/CLI (~60 с) **не сдвигает** окно по
`notifications/progress` (подтверждение команды Cursor, 2026).

Имеет смысл позже как UX: агент видит, что build ещё идёт, и не дублирует
вызов на большом репо (10+ мин). Не как обход хост-таймаута.

## Цель первой поставки

Только **`run_dotnet_build`** (`DotNetBuildProbe` → `DotNetCliRunner`):
периодический MCP progress во время живого `dotnet build` / restore.
Публичная схема тула не меняется (новых параметров нет).

`run_dotnet_test` / `run_specific_test` / `run_test_by_filter` /
`run_dotnet_run` — отдельный последующий шаг: тот же reporter, но другая
приёмка и риск шума; `run_dotnet_run` ещё и другой entrypoint runner
(`RunSeparatedAsync`). Канон — [S4](s4-test-and-run.md).

## Что это не делает

- Не поднимает таймаут `tools/call` у Cursor и не заменяет OpenCode
  `"timeout"`.
- Не лечит Cursor ACP/CLI ~60 с.
- Не заменяет `timeoutSeconds` (это бюджет процесса на стороне сервера).
- Не вводит job id / poll / фоновую очередь.
- Не шлёт полный лог MSBuild в progress (только короткие счётчики шага).
- Не меняет catalog size ради новых `[Description]`, если можно ограничиться
  README.

## Порядок работы

Каждый файл — один шаг. Выполнение последовательное.

| Шаг | Единственный результат | Зависимость |
| --- | --- | --- |
| [S1 — шов progress в CLI runner](s1-cli-runner-progress-seam.md) | Runner умеет слать progress без смены схемы тулов | Нет · **shipped v1.3.25** |
| [S2 — build probe](s2-build-probe-progress.md) | `run_dotnet_build` репортит шаги restore/build | S1 · **shipped v1.3.25** |
| [S3 — честные docs](s3-docs-and-claims.md) | README не обещает лечение хост-таймаута | S2 · **shipped v1.3.25** |
| [S4 — test/run, опционально](s4-test-and-run.md) | Progress на test/run (+ `run_test_by_filter`); watch на `RunSeparatedAsync` | S3 · **shipped v1.3.27** |

## Фиксированные решения

- Транспорт: stdio MCP, `notifications/progress` по протоколу (не Serilog
  и не `tail_tool_log`).
- Частота: не чаще чем раз в несколько секунд или на границе шага probe
  (`restore` → `build -v:minimal` → `build -v:normal`).
- Текст progress: шаг, elapsed, last exit если есть. Без путей машины и
  без полного stdout.
- Если SDK/`RequestId` не даёт progress token — no-op, build как сейчас.
- Приёмка: unit на шов + один длинный build, что вызов не ломается без
  token. Не требовать, чтобы Cursor ACP «перестал падать на 60 с».

## Результат каталога

Первая поставка (S1–S3) выполнена в **v1.3.25** и уточнена по
[review](_archive/review/README.md) в **v1.3.26** (та же фича, без новых тулов
и параметров).

- **Commit:** bump `1.3.24 → 1.3.25` (первая поставка) и `1.3.25 → 1.3.26`
  (исправления ревью); публичный MCP-контракт не менялся, поэтому отдельного
  catalog-budget ряда нет.
- **Что репортится** (только `run_dotnet_build`): на границе каждого шага probe
  (`dotnet build -v:minimal` → pinned restore → `restore -v:minimal` →
  `restore -v:detailed` → `build -v:normal` → `build -v:detailed`) и далее
  heartbeat раз в **5 с**, пока процесс жив. Текст: метка шага, `elapsed`
  **текущего шага** (на границе — `starting`, без числа), exit предыдущего шага.
  Без stdout, без путей машины, без полного лога. Числовое `Progress` — целые
  секунды шага со строгим ростом (per-step часы сбрасываются, значение — нет);
  `Total` не отправляется, поэтому это **не процент готовности** — хостам
  смотреть `Message`.
- **Транспорт:** MCP `notifications/progress` через `IProgress<ProgressNotificationValue>`,
  который SDK инжектит в метод тула (`ExcludeFromSchema`). Новых параметров
  тулов нет. Хост без progress token получает SDK-овский no-op singleton;
  `McpToolProgressReporter.TryCreate` распознаёт его и возвращает `null`, так
  что heartbeat-таймер не заводится вообще — тот же результат сборки и путь
  без таймера, что до S1. `Report` сам глотает сбои транспорта (контракт
  `ICliProgressReporter`, не надежда на `catch` у каждого call site).
- **Seam:** `Services/CliProgress.cs` (`CliProgressUpdate` / `ICliProgressReporter` /
  `CliProgressWatch`) + `DotNetCliRunner` (heartbeat-таймер только при watch) +
  `DotNetBuildProbe` (границы шагов) + `Tools/McpToolProgressReporter.cs` (адаптер).
  Для S4 переиспользуются `ICliProgressReporter` / `CliProgressWatch` /
  `TryCreate` без правок контракта `RunWithMetadataAsync`. `RunSeparatedAsync`
  получил тот же optional watch в **v1.3.27**.
- **Хосты:** проверено unit-тестом шва (fake reporter + искусственно долгий
  процесс), тестом probe на **многошаговой** эскалации (метки `-v:minimal` →
  `restore` → `-v:normal`), прямым тестом `TryCreate` против реального
  SDK-singleton `ModelContextProtocol.NullProgress`, и двумя in-process MCP
  server+client тестами на **живом** `dotnet build` — с progress token
  (уведомления есть) и без token (тот же отчёт о сборке). `SlowProject` в
  тестах разведён по OS (`ping -n 3` / `sleep 2`). Реальные хосты (Cursor IDE,
  Cursor ACP/CLI, OpenCode) не перепроверялись: прогресс не претендует на
  лечение их таймаутов.
- **Ревью:** [`_archive/review/`](_archive/review/README.md) (R-01, G-01,
  S1-01, S1-02, S2-01, S2-02, S2-03) — все приняты и исправлены в v1.3.26; сам
  каталог review не редактировался. Постатейные вердикты и новые риски —
  [`_archive/response/`](_archive/response/summary.md) (`summary.md`,
  NEW-D-01, NEW-D-02); на арбитраж ничего не вынесено. Process лежит в
  [`_archive/`](_archive/README.md), не на живой полке.

S4 выполнен в **v1.3.27**: progress на `run_dotnet_test` / `run_specific_test` /
`run_test_by_filter` / `run_dotnet_run`. Метки `dotnet build` / `dotnet test` /
`dotnet run`; heartbeat 5 с; `RunSeparatedAsync` с optional watch. Факты —
корневой README (ряд v1.3.27) и [ARCHITECTURE.md](../../ARCHITECTURE.md).
