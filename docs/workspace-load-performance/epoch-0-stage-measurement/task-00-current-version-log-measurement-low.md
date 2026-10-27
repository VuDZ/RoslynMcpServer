# D/E0/task-00 — Замер текущей версии по существующим логам

- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: DeepSeek 4.1 Flash.
- Рекомендуемый reasoning: GPT: medium.
- Примерная сложность: 2/5; класс `low`.
- Риск ошибки: низкий — кода нет; неверная интерпретация меток лога может направить E1 не туда.
- Необходимые способности: `анализ_производительности`, `проектирование_проверок`.
- Статус: **accepted in the requested scope, 2026-10-22** — публичные прокси и
  офисный baseline по существующим логам получены. Scope: измерение, без
  утверждения внутренней причины MSBuild open; независимого ревью не было.
  [Офисные результаты](evidence/task-00-current-version.md#офисный-замер-получен-2026-10-22),
  [checkpoint validation](evidence/commit-2026-10-22.md).
- Depends on: —.
- Модель, реализовавшая задачу: Claude Opus 5.5 (прокси-часть).
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [протокол](evidence/task-00-protocol.md), [результаты прокси](evidence/task-00-current-version.md).

## Цель

Снять первую грубую разбивку времени `load_workspace` на целевом решении коллег
без изменения кода, по строкам, которые сервер пишет уже сейчас.

## Что уже есть в `logs/mcp-*.log`

Каждая строка имеет метку времени с миллисекундами и harness session id.

1. Входящий `tools/call` с именем инструмента. Пишется по умолчанию
   (`McpInboundProtocolLogger`; `MCP_LOG_INCOMING_RPC=0` выключает). Это начало вызова.
2. `Analyzer provenance capture finished: Status=…, Files=…, Bytes=…, ReplayMs=…`
   (`AnalyzerProvenanceCaptureService.OpenAndCaptureAsync`). Это конец MSBuild open
   и replay binlog.
3. `Loaded Roslyn workspace from …` (`SolutionManager.LoadCoreAsync`). Это конец
   execution gate, публикации, сбора diagnostics, построения input map и старта watchers.
4. Строка telemetry ответа инструмента (`ToolTelemetry`). Это конец вызова.
5. Строки `MSBuildWorkspace Failure/Warning`, если были.

Производные оценки:

- MSBuild open ≈ (2) − (1) − `ReplayMs`; включает ожидание manager lock, старт
  BuildHost, evaluation, design-time build и IPC;
- после open ≈ (3) − (2);
- хвост ответа ≈ (4) − (3);
- первый семантический запрос ≈ (4) − (1) для этого вызова.

Это оценки по меткам лога, а не измеренные spans. В отчёте они так и называются.

## Протокол для коллег

Результат задачи — `evidence/task-00-protocol.md` с этим протоколом в готовом
для пересылки виде и `evidence/task-00-current-version.md` с данными.

Шаги 2–5 выполняет [`scripts/Measure-WorkspaceLoad.ps1`](../../../scripts/Measure-WorkspaceLoad.ps1):
новый процесс сервера на каждый прогон, время каждого вызова на стороне клиента,
разбор строк лога этой сессии и процессорное время сервера и BuildHost. Скрипт —
инструмент замера, а не код сервера: build, тесты и version bump он не требует.
Если между прогонами нет Reload MCP, это не нарушает шаг 2: каждый прогон — новый PID.

1. Записать `get_mcp_server_info` (версия сервера), SDK, ОС, CPU, RAM.
2. Новый PID: Reload MCP. Вызвать `load_workspace` на решение с одними и теми же
   аргументами, затем один `find_symbol_definition` известного типа из решения.
3. Повторить шаг 2 не менее трёх раз; первую попытку пометить warmup.
4. Отдельно один RAM hit: повторный `load_workspace` без Reload MCP.
5. Прислать вырезку лога по этим вызовам и сводку ответа `load_workspace`
   (число проектов, документов, diagnostics). Корень репозитория заменить на
   `<repo>`, профиль пользователя — на `<user>`. Environment, токены и
   содержимое файлов не присылать.

Опционально, отдельным прогоном, — трасса BuildHost. BuildHost-netcore является
.NET-процессом и наследует environment сервера, поэтому переменные EventPipe
(`DOTNET_EnableEventPipe`, `DOTNET_EventPipeOutputPath`, `DOTNET_EventPipeConfig`)
в `env` MCP-сервера дают трассы обоих процессов. Перед прогоном проверить, что
каждый процесс пишет свой файл. Трассировка добавляет overhead, поэтому её
времена в baseline не входят. BuildHost-net472 (non-SDK проекты) так не трассируется.

## Проверки

- Каждое число ссылается на сырые строки лога.
- Warmup, новые PID и RAM hit разделены; указаны n, ошибки и таймауты.
- Hotspot не объявляется: open содержит несколько стадий, их разделят task-01 и task-02.
- Пропущенная строка лога делает оценку недоступной, а не нулевой.

Docs-only результат: link и diff checks. Build и тесты не требуются.

## Записанный результат, 2026-10-22

На сервере 1.5.7 офисный пакет 20:50:57 содержит логи успешной загрузки
142 проектов: load 48,40 с, первый MainViewModel 36,87 с (медианы).
Оценка MSBuild open около 44,3 с; второй binlog ещё не читался.
Серии 1.5.8/1.5.9 относятся к последующим изменениям и записаны в
[task-08](task-08-source-search-and-office-results-med.md), а не подменяют baseline.
Отдельные стадии open требуют task-01/task-02; они этим замером не закрыты.
