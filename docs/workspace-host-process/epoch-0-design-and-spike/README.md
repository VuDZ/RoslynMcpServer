# C/E0 — Инвентаризация, дизайн и spike: карта выполнения

- Статус плана: **draft; независимое ревью 2026-10-23 завершено без существенных
  findings; принято только в scope исследовательского плана, design не принят;
  после ревью добавлены task-05 и сравнение моделей без повторного ревью**.
- Статус исполнения: **in progress; task-00 completed, 2026-10-26, docs-only;
  task-01/task-02 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28; task-05 completed, 2026-10-28, isolated probe;
  production-код темы не начат**. Принятый scope task-00 — инвентаризация
  состояния и этапов инструментов; [результат и validation](evidence/task-00-state-inventory.md).
- Актуализация task-00: **completed, 2026-10-28**; база main `4e5e9f9` / 1.5.17,
  S01–S42 и прежние 54 tools; [влияние изменений и validation](evidence/task-00-impact-2026-10-28.md).
  Task-01/task-02 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28; task-05 completed, production host/proxy не реализован.
  [Task-05: memory/GC, cohort и ограничения запуска](evidence/task-05-applicability.md)
  завершён в уточнённом владельцем исследовательском scope;
  [основание и validation](../evidence/2026-10-28-task-05-scope.md).
  UI-матрица и число MCP на чат/окно исключены из критериев завершения.
- Зависимость начала эпохи: нет. Интеграционный task-02 использует
  [D/E0/task-01](../../workspace-load-performance/epoch-0-stage-measurement/task-01-load-stage-timings-med.md).
- Результат при продолжении shared-режима: принятая владельцем архитектура и
  проверенные spike выбранной модели: схлопывание дубликатов workspace, смена
  привязки внутри живой MCP-сессии, согласованное восстановление после падения
  без массовых ordinary-загрузок; для модели ролей — также передача роли host.
  RAM hit и память проверены с настоящим Roslyn.
- Канон темы: [README](../README.md).
- Обновление документации: **выполнено, 2026-10-23**; принятый scope — фиксация
  обсуждения, пересборка плана и TDD-подход, без кода и приёмки задач;
  ветка `research/workspace-host-process` от `main` (`22b9924`);
  [отчёт и validation](../evidence/2026-10-23-plan-update.md).
- Дополнение плана после ревью: **выполнено, 2026-10-23**; принятый scope —
  task-05, сравнение моделей, зависимости task-01/task-04, без кода и приёмки задач;
  [отчёт и validation](../evidence/2026-10-23-plan-revision.md).
- Согласование дополнений: **выполнено, 2026-10-23**; принятый scope —
  применимость сценариев по моделям, контроль без B, этапы build/test и условие
  Reload, без выбора модели и приёмки задач;
  [отчёт и validation](../evidence/2026-10-23-plan-consistency.md).

## Задачи

- [task-00 — Инвентаризация состояния процесса](task-00-process-state-inventory-med.md) — `med`, **completed, 2026-10-26; актуализировано 2026-10-28, docs-only**; S01–S42 и этапы 54 tools, [исходный отчёт](evidence/task-00-state-inventory.md), [поправки и validation](evidence/task-00-impact-2026-10-28.md); Depends on: —
- [task-01 — Архитектурное решение](task-01-architecture-decision-hi.md) — `hi`, **planned**; Depends on: [task-00](task-00-process-state-inventory-med.md), [task-03](task-03-pipe-relay-spike-med.md), [task-04](task-04-proxy-routing-and-recovery-spike-hi.md), [task-05](task-05-applicability-checks-med.md)
- [task-02 — Интеграционный spike прокси и host](task-02-bridge-spike-hi.md) — `hi`, **planned**; Depends on: [task-01](task-01-architecture-decision-hi.md), [D/E0/task-01](../../workspace-load-performance/epoch-0-stage-measurement/task-01-load-stage-timings-med.md)
- [task-03 — Spike транспорта и MCP-фасада](task-03-pipe-relay-spike-med.md) — `med`, **completed, 2026-10-28**; standalone SDK-фасад без Roslyn, обе модели host, две версии MCP, Windows/WSL; [результат и validation](evidence/task-03-pipe-relay-spike.md); Depends on: —
- [task-04 — Spike переключения и восстановления](task-04-proxy-routing-and-recovery-spike-hi.md) — `hi`, **completed, 2026-10-28**; принят standalone spike выделенного host без Roslyn, Windows/WSL, две версии MCP; [результат, ревью и validation](evidence/task-04-routing-and-recovery-spike.md); Depends on: [task-03](task-03-pipe-relay-spike-med.md), [task-05](task-05-applicability-checks-med.md) с решением владельца
- [task-05 — Проверки применимости](task-05-applicability-checks-med.md) — `med`, **completed, 2026-10-28, в уточнённом владельцем исследовательском scope**; память/GC/cohort и ограничения запуска host; [результат](evidence/task-05-applicability.md), [scope и validation](../evidence/2026-10-28-task-05-scope.md); Depends on: —

Task-05, task-00 и task-03 независимы; task-05 рекомендуется первым. После
task-05 владелец решает, продолжать ли тему и какие модели проверять в task-04.
**Решено 2026-10-28:** task-04 проверяет общий механизм и выделенный host;
совмещение ролей отложено как оптимизация ([README](../README.md#сравниваемые-модели-и-проверки-применимости-2026-10-23)).
Task-04 идёт после task-03 и этого решения; результаты всех четырёх входят в
task-01. Только после design, независимого ревью и выбора владельца
по U-C-01…U-C-08 начинается task-02. Task-03/task-04 исследуют кандидаты механизмов
и не принимают production design заранее. Их выполнение требует отдельного
перехода к реализации: обновление плана 2026-10-23 кода не запускает.
Если владелец выбирает нулевой вариант, task-02 и E1 shared-режима не запускаются;
невыполненные задачи получают статус deferred с причиной и ссылкой на решение.
Это не приёмка реализации shared-режима. Task-00 завершён как docs-only
инвентаризация; task-01/task-02 остаются planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28, task-05 completed. Завершение task-00 не заменяет
решение владельца после task-05 и не запускает зависимые spike.

## Сквозные критерии

- Модель процессов выбрана сравнением ролей в MCP, отдельного host и нулевого
  варианта на данных task-05. Цель «пережить Reload» снята владельцем 2026-10-28:
  процессы не переживают клиентов ([README](../README.md#зачем)).

Следующие критерии относятся к обеим shared-моделям, если не указано иное.
Для нулевого варианта сравниваются текущий режим и отдельно оценка idle unload;
перспективные выгоды B не считаются измеренными без его проверки.

- N совместимых клиентов разделяют один тяжёлый workspace; разные проекты и
  несовместимые настройки изолированы. Память учитывает все host и прокси.
  Proxy — обычный серверный процесс без загруженного workspace; второго workspace
  в нём нет ([решение владельца 2026-10-28](../README.md#сравниваемые-модели-и-проверки-применимости-2026-10-23)).
- Клиент чат2 переходит с проекта1 на проект2; чат1 продолжает проект1. При отказе
  proxy-перехода прежняя живая привязка сохраняется. В модели ролей, если проект
  меняет сам `mcp1`-host чат1, он освобождает роль проекта1, а один из оставшихся
  proxy становится новым host проекта1. В модели отдельного host смена проекта
  любого чата не вызывает handoff или повторную загрузку прежнего workspace.
  Клиенты не получают ответы от неверного проекта.
- MCP-сессия, активация групп, progress, cancellation и подписки принадлежат
  клиенту; смена host не требует перезапуска harness.
- Одновременный старт/восстановление десяти прокси даёт одного владельца ключа
  до тяжёлой инициализации и одну общую попытку загрузки. По ключу координируется
  promotion одного proxy в модели ролей либо запуск отдельного host в другой
  модели, без P2P-сети и fallback на каждого клиента.
- Проверены crash loop, смерть кандидата/запускающего процесса до readiness,
  поколения host, бюджеты ожидания и отсутствие автоматического replay операций
  записи; плановый handoff проверен только для модели ролей.
- Размещение этапов build/test и их поведение при смене поколения/падении host
  определены design; отдельный запуск CLI в proxy не отменяет проверок workspace
  и build proofs.
- Принудительный restart при обновлении analyzer, reset/stop, idle grace,
  публикация и безопасность локального канала имеют явный контракт в design.

## TDD и порядок проверок

Для task-03/task-04/task-02 каждый новый поведенческий контракт сначала
закрепляется запускаемым тестом с наблюдаемым red, затем минимальным green
и refactor при сохранении проверок. В отчётах — команда, ожидаемый контрпример
и фактический результат до/после; zero-test, ошибка сборки и timeout среды
не считаются red контрактного теста или его успешным green.

Сначала лёгкий стенд и управляемые interleavings без Roslyn; затем реальные
multi-process проверки владения/падений; после design — Roslyn integration.
Проверки конкуренции наблюдают владельцев, load attempts и побочные эффекты,
а не детали внутренней реализации. Короткая разведка SDK API допустима до
тестов. Замеры памяти/latency и platform ACL не заменяются fake/unit тестами.

## Ревью плана эпохи

- Модели, проводившие ревью: точный ID недоступен; отдельная сессия Codex
  `/root/independent_plan_review`, без истории автора.
- Даты ревью: 2026-10-23.
- Количество раундов ревью: 1 (та же проверка, что в сводке темы).
- Количество исправлений после ревью: 0; findings всех severity — 0.
- Итог: принято в scope draft-плана исследования. Это не принятие design,
  задач, реализации эпохи или production activation. Ревью охватывало версию
  `dc1b1c5`; дополнения того же дня (task-05, сравнение моделей) его не проходили.
- Отчёты: [result](../review-independent-2026-10-23/result.md),
  [coverage](../review-independent-2026-10-23/coverage.md),
  [учёт и validation](../evidence/2026-10-23-independent-review.md).

## Выполнение task-03, 2026-10-28

Scope результата — standalone транспорт/MCP-фасад без Roslyn: обе модели host,
две версии протокола на Windows/WSL, ACL, TDD, задержки и память.
[Результат, C03-01/C03-02 и validation](evidence/task-03-pipe-relay-spike.md).
Release build solution и main suite 1432/1432 прошли; production не менялся.
Task-04 completed, 2026-10-28, standalone spike выделенного host; [результат](evidence/task-04-routing-and-recovery-spike.md).
Независимое ревью task-03 и task-05 — 2026-10-28, раунд 1, 9 исправлений;
[результат](../review-task-03-05-2026-10-28/result.md).

## Выполнение task-04, 2026-10-28

**Completed, принят standalone spike выделенного host без Roslyn.** Windows/WSL,
MCP 2025-11-25 и 2026-07-28: по 10 cold/recovery повторов на версию и ОС,
управляемые startup/closing races, switch, cancellation и no replay; cleanup проверен.
[Результат и validation](evidence/task-04-routing-and-recovery-spike.md).
Release build и main suite 1432/1432 прошли. Независимое ревью — отдельный
Codex `/root/task04_research_review`, точный model ID недоступен; 2 раунда,
4 исправления подтверждены; [result](../review-task-04-2026-10-28/result.md).
Следующий шаг — task-01; приёмка design/эпохи и production activation не объявлены.

## Статистика ревью задач

- Охват: task-00–task-05; task-00 completed, 2026-10-26, docs-only;
  task-05 completed, 2026-10-28; task-01/task-02 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28.
- Фактические исполнители: task-00 — Codex, точный ID модели недоступен.
  Task-05 — `gpt-6.1-sol` (Codex; metadata harness).
  Task-03 и task-04 — Codex, точные model ID запусков недоступны.
- Фактические рецензенты: task-00, task-03, task-05 — `claude-opus-5-5` (Claude Code),
  2026-10-28; task-03 и task-05 — одним совместным ревью.
  Task-04 — отдельная сессия Codex `/root/task04_research_review`, точный model ID недоступен.
- Сумма раундов независимого ревью задач: 5 (по одному на task-00, task-03, task-05; два на task-04).
- Исправленные замечания независимого ревью задач: 18 (task-00 — 5; task-05 — 4,
  F-01–F-04; task-03 — 5, F-05–F-09; task-04 — 4, F-01–F-04).
- Отчёты: [task-00 — инвентаризация и validation](evidence/task-00-state-inventory.md).
  Актуализация 2026-10-28 — тот же исполнитель;
  [влияние новой базы и validation](evidence/task-00-impact-2026-10-28.md).
  [Ревью task-00](../review-task-00-2026-10-28/result.md),
  [validation исправлений](../evidence/2026-10-28-task-00-review-fixes.md).
  [Ревью task-03 и task-05](../review-task-03-05-2026-10-28/result.md), [validation исправлений](../evidence/2026-10-28-task-03-05-review-fixes.md).
  [Task-04: результат и validation](evidence/task-04-routing-and-recovery-spike.md), [ревью](../review-task-04-2026-10-28/result.md).

## Приёмка реализации эпохи

- Модели, проводившие ревью: —.
- Даты ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: приёмки эпохи нет; task-00 завершён в scope docs-only инвентаризации,
  production-код и интеграционный spike не начаты; task-05 завершён в уточнённом
  исследовательском scope, [результат и validation](evidence/task-05-applicability.md).
  Task-03 завершён как standalone spike; [результат и validation](evidence/task-03-pipe-relay-spike.md).
  Task-04 завершён и принят как standalone spike выделенного host;
  [результат, ревью и validation](evidence/task-04-routing-and-recovery-spike.md).
  [Проверки task-00](evidence/task-00-state-inventory.md)
  не являются приёмкой design или реализации эпохи.
- Принятый scope и дата: —.
- Финальная validation и замеры: —.
- Ограничения: —.
- Отчёты: —.
