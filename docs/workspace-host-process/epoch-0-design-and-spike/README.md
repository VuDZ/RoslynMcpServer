# C/E0 — Инвентаризация, дизайн и spike: карта выполнения

- Статус плана: **draft; независимое ревью 2026-10-23 завершено без существенных
  findings; принято только в scope исследовательского плана, design не принят;
  после ревью добавлены task-05 и сравнение моделей без повторного ревью**.
- Статус исполнения: **in progress; task-00 completed, 2026-10-26, docs-only;
  task-01 in progress, 2026-10-29, docs-only; task-02/task-06 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28; task-05 completed, 2026-10-28, isolated probe;
  production-код темы не начат**. Принятый scope task-00 — инвентаризация
  состояния и этапов инструментов; [результат и validation](evidence/task-00-state-inventory.md).
- Актуализация task-00: **completed, 2026-10-28**; база main `4e5e9f9` / 1.5.17,
  S01–S42 и прежние 54 tools; [влияние изменений и validation](evidence/task-00-impact-2026-10-28.md).
  Task-01 in progress, 2026-10-29; task-02/task-06 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28; task-05 completed, production host/proxy не реализован.
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
- [task-01 — Архитектурное решение](task-01-architecture-decision-hi.md) — `hi`, **in progress, 2026-10-29, docs-only; O-05 выбран: небольшой key/private fresh/reuse-only output; редакция перепроверена в раундах 8–9, O-01…O-04 отложены**; [design](../design.md), [отчёт](evidence/task-01-architecture-decision.md); Depends on: [task-00](task-00-process-state-inventory-med.md), [task-03](task-03-pipe-relay-spike-med.md), [task-04](task-04-proxy-routing-and-recovery-spike-hi.md), [task-05](task-05-applicability-checks-med.md), до окончательного выбора — [task-06](task-06-environment-and-deployment-preflight-med.md)
- [task-02 — Интеграционный spike прокси и host](task-02-bridge-spike-hi.md) — `hi`, **planned**; Depends on: [task-01](task-01-architecture-decision-hi.md) с оставшимися решениями владельца после [task-06](task-06-environment-and-deployment-preflight-med.md); O-05 выбран, O-01…O-04 отложены; [D/E0/task-01](../../workspace-load-performance/epoch-0-stage-measurement/task-01-load-stage-timings-med.md)
- [task-03 — Spike транспорта и MCP-фасада](task-03-pipe-relay-spike-med.md) — `med`, **completed, 2026-10-28**; standalone SDK-фасад без Roslyn, обе модели host, две версии MCP, Windows/WSL; [результат и validation](evidence/task-03-pipe-relay-spike.md); Depends on: —
- [task-04 — Spike переключения и восстановления](task-04-proxy-routing-and-recovery-spike-hi.md) — `hi`, **completed, 2026-10-28**; принят standalone spike выделенного host без Roslyn, Windows/WSL, две версии MCP; [результат, ревью и validation](evidence/task-04-routing-and-recovery-spike.md); Depends on: [task-03](task-03-pipe-relay-spike-med.md), [task-05](task-05-applicability-checks-med.md) с решением владельца
- [task-05 — Проверки применимости](task-05-applicability-checks-med.md) — `med`, **completed, 2026-10-28, в уточнённом владельцем исследовательском scope**; память/GC/cohort и ограничения запуска host; [результат](evidence/task-05-applicability.md), [scope и validation](../evidence/2026-10-28-task-05-scope.md); Depends on: —
- [task-06 — Preflight среды и развёртывания](task-06-environment-and-deployment-preflight-med.md) — `med`, **planned, 2026-10-29; проверка выбранного O-05 до окончательного O-01**; Depends on: [task-04](task-04-proxy-routing-and-recovery-spike-hi.md), [task-05](task-05-applicability-checks-med.md)

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
инвентаризация; task-01 in progress, 2026-10-29; task-02/task-06 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28, task-05 completed. Завершение task-00 не заменяет
решение владельца после task-05 и не запускает зависимые spike.

Уточнение после [раунда 5](../review-task-01-round-5-2026-10-29/result.md): task-06
идёт **до окончательного выбора** task-01. Он использует proposed design как вход,
а не зависит от завершённого task-01; цикла зависимостей нет. Позже 2026-10-29 O-05 выбран владельцем:
небольшой key/private fresh/reuse-only output. O-02 ещё не выбран;
[решение и validation](../evidence/2026-10-29-task-01-owner-context.md). Task-06 planned, task-02 по-прежнему не начат.

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
Раунд 3 — `claude-opus-5-5`, 2026-10-28: 8 замечаний (2 major), 7 закрыты в документах,
F-01/F-08 переданы task-01; [result](../review-task-04-round-3-2026-10-28/result.md).
Task-01 начат 2026-10-29; приёмка design/эпохи и production activation не объявлены.

## Выполнение task-01, 2026-10-29

**In progress, docs-only; выбор владельца отложен до отдельных ревью.** Подготовлен [проект design](../design.md),
[основания и validation](evidence/task-01-architecture-decision.md).
Research-task-review в сабагенте Codex `/root/task01_research_review`, раунды 1–2:
3 подтверждённых исправления;
[result](../review-task-01-2026-10-29/result.md).
Раунд 3 — [независимый Grok 4.7](../review-task-01-independent-2026-10-29/result.md)
на базе `d54a93a`: blocker/major/minor. Все три приняты и исправлены в design;
[ответ автора](../evidence/2026-10-29-task-01-independent-review-response.md).
Раунд 4 — Codex подтвердил ещё 3 исправления, новых must-fix по diff нет.
Раунд 5 — `claude-opus-5-5`: 3 major, minor и suggestion;
[исходное ревью](../review-task-01-round-5-2026-10-29/result.md).
Раунд 6 — Codex подтвердил пять r5/F-* fixes, но выявил R6-N01 major про поздний
identity pin; раунд 7 подтвердил исправление prelaunch boundary.
**До решения O-05: 7 раундов / 12 исправлений.**
После выбора владельца раунд 8 нашёл R8-F01/R8-F02 major: private intent терялся при
reset/unbind и не был определён новый shared creator context при recovery. Оба
исправления подтверждены в раунде 9; новых must-fix в затронутом slice нет.
**Итого task-01: 9 раундов / 14 исправлений.**
[Ответ и validation раундов 5–7](../evidence/2026-10-29-task-01-round-5-response.md).
После fixes применён research-task-authoring. 2026-10-29 владелец отложил O-01…O-04;
O-05 выбран владельцем, редакция перепроверена в раундах 8–9;
[решение и validation](../evidence/2026-10-29-task-01-owner-context.md). Task-06 planned до выбора O-01, task-02 не начат, приёмки эпохи нет.

## Статистика ревью задач

- Охват: task-00–task-06; task-00 completed, 2026-10-26, docs-only;
  task-05 completed, 2026-10-28; task-01 in progress, 2026-10-29; task-02/task-06 planned; task-04 completed, 2026-10-28; task-03 completed, 2026-10-28.
- Фактические исполнители: task-00 — Codex, точный ID модели недоступен.
  Task-05 — `gpt-6.1-sol` (Codex; metadata harness).
  Task-03 и task-04 — Codex, точные model ID запусков недоступны.
  Task-01 — GPT Sol-6.1 (`gpt-6.1-sol`), Codex; уточнено владельцем 2026-10-29, docs-only проект.
- Фактические рецензенты: task-00, task-03, task-05 — `claude-opus-5-5` (Claude Code),
  2026-10-28; task-03 и task-05 — одним совместным ревью.
  Task-04 — отдельная сессия Codex `/root/task04_research_review`, точный model ID недоступен (раунды 1–2);
  раунд 3 — `claude-opus-5-5`, 2026-10-28.
  Task-01 — Codex `/root/task01_research_review`, точный model ID недоступен,
  2026-10-29, раунды 1–2, 4, 6–9; Grok 4.7, session ID недоступен, раунд 3;
  `claude-opus-5-5`, Claude Code, раунд 5. Task-02/task-06 не имеют ревью результата.
- Сумма раундов независимого ревью задач: 16 (по одному на task-00, task-03, task-05; три на task-04; девять на task-01).
- Исправленные замечания независимого ревью задач: 39 (task-00 — 5; task-05 — 4,
  F-01–F-04; task-03 — 5, F-05–F-09; task-04 — 4, F-01–F-04 раундов 1–2, и 7 раунда 3;
  task-01 — 14: Codex/F-01–F-03, independent/F-01–F-03, r5/F-01–F-05, R6-N01, R8-F01/R8-F02).
- Отчёты: [task-00 — инвентаризация и validation](evidence/task-00-state-inventory.md).
  Актуализация 2026-10-28 — тот же исполнитель;
  [влияние новой базы и validation](evidence/task-00-impact-2026-10-28.md).
  [Ревью task-00](../review-task-00-2026-10-28/result.md),
  [validation исправлений](../evidence/2026-10-28-task-00-review-fixes.md).
  [Ревью task-03 и task-05](../review-task-03-05-2026-10-28/result.md), [validation исправлений](../evidence/2026-10-28-task-03-05-review-fixes.md).
  [Task-04: результат и validation](evidence/task-04-routing-and-recovery-spike.md), [ревью](../review-task-04-2026-10-28/result.md), [ревью раунда 3](../review-task-04-round-3-2026-10-28/result.md), [validation исправлений](../evidence/2026-10-28-task-04-review-fixes.md).
  [Task-01: проект, review и validation](evidence/task-01-architecture-decision.md),
  [ревью Codex](../review-task-01-2026-10-29/result.md),
  [независимое ревью Grok](../review-task-01-independent-2026-10-29/result.md),
  [ответ и validation раунда 3](../evidence/2026-10-29-task-01-independent-review-response.md),
  [раунд 5](../review-task-01-round-5-2026-10-29/result.md),
  [ответ и validation раундов 5–7](../evidence/2026-10-29-task-01-round-5-response.md),
  [O-05 и validation раундов 8–9](../evidence/2026-10-29-task-01-owner-context.md).

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
