# S1 — шов progress в `DotNetCliRunner`

Статус: **не выполнено**.
Результат шага: долгий CLI-процесс может отправить MCP progress, не меняя
публичные параметры тулов.

## Основание

`DotNetCliRunner.RunWithMetadataAsync` — единственное место, где живёт
таймаут процесса и чтение stdout/stderr. Build probe и test/run уже идут
через него. Progress на уровне тула без шва в runner размножит таймеры.

Targets: `DotNetCliRunner`, вызов из MCP tool context (progress token /
`IProgress` SDK, как даст текущий `ModelContextProtocol` пакет).

## Работа

Добавить необязательный reporter в runner: если token/callback есть —
периодически слать короткое обновление, пока процесс не завершился.
Если token нет — поведение байт-в-байт как сейчас.

Не читать analyzer files. Не менять kill-on-timeout / cancel. Не писать
progress в файл лога как замену протоколу.

Имена внутренних типов — выбор реализации. Новых MCP параметров нет.

## Приёмка

- Без token существующие тесты runner/probe не требуют правок контракта.
- С тестовым callback за время искусственно длинного процесса приходит
  ≥1 уведомление с elapsed и без полного stdout.
- Cancel/timeout по-прежнему убивают process tree.

## Результат

Не выполнено.
