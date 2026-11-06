# Task-11 / эпоха 1 — повторная приёмка после раунда 2

- Дата: **2026-11-06**, Europe/Moscow. Координатор: **Codex**, точный runtime model ID средой не предоставлен.
- База исправления: `45ea344`; владелец переоткрыл task-11 по [R1-001 второго рецензента](task-11-review-round-1-claude.md#r1-001--фильтр-через-run_test_by_filter-по-инструкции-идёт-на-старых-бинарниках).
- **ACCEPT повторно**: [свежий раунд 2](task-11-review-round-2.md), **2 раунда / 1 исправление**, R1-001 закрыто тем же исполнителем `/root/task11_implementer`. Task-11 вновь закрывает VSTest-scope эпохи 1 (task-01–09 и task-11). Task-10 deferred; эпохи 2–4, MTP-план и новые activation permissions не вводились.
- Исправление только в документации: README EN/RU и `AGENTS.md.sample` требуют `run_test_by_filter` с явным `noBuild: false` после сохранённых правок. Объяснено, что omit пропускает сборку и может запустить старые DLL; ручная сборка может вызвать T09-C1, как `run`. [Evidence исполнителя](task-11-review-round-2-fix.md).
- ARCHITECTURE, help и description проверены: аналогичной неверной инструкции ручного выполнения возвращённого фильтра в них нет. Справка `run_test_by_filter` уже называет default `noBuild=true`; C# строки/description/schema не менялись.
- Координатор проверил **3/3** workflow-абзаца: явный `noBuild: false`, риск stale binaries при omit и связь сборки с T09-C1 присутствуют во всех трёх местах. Сопоставлены реальные omit-константы/argument route и существующая stale-DLL regression; исторические negative controls с намеренным `noBuild: true` не переписаны.
- Сравнение `git diff --exit-code 13ded97 -- '*.cs' '*.csproj' '*.props' '*.targets' '*.sln' '*.slnx' global.json .editorconfig '.github/workflows/*'` — **PASS**: production, tests и build settings совпадают с проверенной версией. Точный inventory 16 SourceStructure limits не менялся и не ослаблялся.
- Версии **1.7.0 / 1.7.0.0 / 1.7.0.0** сохранены, bump не делался. SHA-256 `RoslynMcpServer.csproj`: `9829E84D55AF0172295F6EB0451CD93ED623AB6E0B1DBF89135DE3F5020DD8BB`.
- По явному [поручению раунда 2](../task-11-integration-and-docs-med.md#раунд-2-r1-001-решение-владельца) docs-only исправление **не требует повторных build/SourceStructure/main/Lifecycle или новой версии**. Эти прогоны **не выполнялись**. Искусственная RED-фаза не создавалась; нулевой тестовый запуск не объявляется PASS.
- Проверены локальные evidence links и whitespace diff. Статусы task, отчёта, epoch README, program README и docs index согласованы координатором после ACCEPT. Посторонняя untracked `.agents/skills/prepare-github-release/` исключена; push/release не выполнялись.

## Ранее выполненные проверки той же версии кода

[Первоначальная Windows validation](task-11-final-validation.md) на `13ded97`: Release solution build PASS, focused **145/145**, SourceStructure **123/123**, main **2004/2004**, opted-in AnalyzerLifecycle **13/13**, без failures/skips/timeouts. Полный MCP call main — **202.731 s**. Это прежние результаты неизменившегося кода, **не новый запуск раунда 2**.

[Второй рецензент раунда 1](task-11-review-round-1-claude.md) независимо выполнил Linux main **1996/1996** на чистом `13ded97`, SDK10.0.114, Release, с дополнительным исключением RequiresWindows; все passed, без skips. Координатор этот Linux-прогон не повторял.

Существующий `Run_builds_changed_sources_instead_of_passing_on_stale_binaries_and_continues_after_test_failure` уже демонстрирует риск R1-001: после правки обычный `RunTestByFilter` без `noBuild` проходит на старой DLL; `run: true` затем исполняет новый failing body. Исправление документации направляет ручной вариант в тот же существующий build-enabled route, сохраняя поведение раннеров и совместимость.

## Сохранённые ограничения

T09-C1 не исправлялось: перезаписанный сборкой mapped output оставляет owner и зависимые тестовые проекты выбранными целиком до другого solution или перезапуска сервера; revert/reset/reload не снимают расширение. Это касается и ручного фильтра с `noBuild: false`. [Owner-проба](task-11-owner-measurement.md) остаётся одним наблюдением 1/1; первый run16/16 занял5.164s. Нефильтрованный whole-project, долговременная частота/recall и другие owner solutions не измерены. Общие ограничения parser, OS termination и автоматической отправки cancel notification сохранены; docs-only правка их не меняет.
