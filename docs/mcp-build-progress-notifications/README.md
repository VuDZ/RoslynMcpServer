# MCP `notifications/progress` для `run_dotnet_build`

Дата: 2026-09-12. Статус: **план; реализация не начата**.
Повод: длинный `tools/call` на build/test и вопрос, лечит ли progress
хост-таймаут Cursor (п.1). Ответ: **сам лимит хоста — нет**; progress —
отложенный UX, не фикс п.1.

Этот каталог — напоминание и границы работы. Не начинать, пока нет явного
запроса на реализацию. Состояние репозитория на момент записи: **v1.3.19**,
S5 shadow-copy принят.

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

`run_dotnet_test` / `run_specific_test` / `run_dotnet_run` — отдельный
последующий шаг: тот же CLI runner, но другая приёмка и риск шума.

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
| [S1 — шов progress в CLI runner](s1-cli-runner-progress-seam.md) | Runner умеет слать progress без смены схемы тулов | Нет |
| [S2 — build probe](s2-build-probe-progress.md) | `run_dotnet_build` репортит шаги restore/build | S1 |
| [S3 — честные docs](s3-docs-and-claims.md) | README не обещает лечение хост-таймаута | S2 |
| [S4 — test/run, опционально](s4-test-and-run.md) | Тот же шов на test/run, если S1–S3 уже shipped | S3 |

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

Не выполнено. После реализации — handoff в этом README (версия, commit,
что именно репортится, какие хосты проверены).
