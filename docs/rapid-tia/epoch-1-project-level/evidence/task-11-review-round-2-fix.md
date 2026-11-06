# Task-11 — исправление R1-001 перед раундом 2

- Дата: **2026-11-06**. База: `45ea344`, версия **1.7.0**.
- Исполнитель: **Codex**, прежний `/root/task11_implementer`; точный runtime model ID средой не предоставлен.
- Основание: [MUST-FIX второго рецензента раунда 1](task-11-review-round-1-claude.md#r1-001--фильтр-через-run_test_by_filter-по-инструкции-идёт-на-старых-бинарниках), [поручение раунда 2](../task-11-integration-and-docs-med.md#раунд-2-r1-001-решение-владельца). Это исправление инструкции; новое поведение раннера не вводится. Приёмку и статусы после свежего ревью оформляет координатор.

## Три исправленных места

- [README, EN](../../../../README.md): рабочий цикл в записи `estimate_affected_tests` теперь требует явный `noBuild: false` у `run_test_by_filter` для возвращённого фильтра после сохранённых правок.
- [README, RU](../../../../README.md#russian-version): то же требование в русской записи инструмента.
- [AGENTS.md.sample, During edits](../../../../AGENTS.md.sample#build-test-run): то же требование в правилах агента.

Во всех трёх местах сказано, что пропуск `noBuild` отключает сборку и может выполнить старые бинарники, а ручная pre-test build может вызвать то же whole-project расширение T09-C1, что и `run`. Для whole-project оставлен `run_dotnet_test`, чей обычный вызов по умолчанию собирает. Финальный полный suite/CI и принятый механизм T09-C1 не меняются.

## Источник умолчания и уже существующее доказательство

[Tools/TestTools.cs](../../../../Tools/TestTools.cs): `FilterTestOmitSkipsRebuild = true`; `RunTestByFilter` передаёт его общему исполнителю. Его `noBuild` использует `NoBuildOmitTrueDescription` с явным «Omit means true». У `run_dotnet_test`/`run_specific_test` соответствующие omit-константы равны false. Новый контракт умолчания не придумывался.

Существующий [EstimateAffectedTestsRunTests.cs](../../../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsRunTests.cs), `Run_builds_changed_sources_instead_of_passing_on_stale_binaries_and_continues_after_test_failure`, меняет исходник на failing body, вызывает `RunTestByFilter(project, filter)` без `noBuild`, проверяет прежний PASS и отсутствие нового маркера, затем `run: true` исполняет новый код и получает Failed:2. Это ранее выполненная регрессия task-09, уже включённая в [task-11 validation](task-11-final-validation.md); нового запуска или RED в этом исправлении не было.

## Аудит связанных мест

- [ARCHITECTURE](../../../ARCHITECTURE.md#rapid-tia-level-0): описывает estimate-only и внутреннюю последовательность `run` со сборкой; не предлагает вручную исполнить возвращённый фильтр с опущенным `noBuild`. Требуемая правка отсутствует.
- [Hosting/McpToolHelpCatalog.cs](../../../../Hosting/McpToolHelpCatalog.cs): справка `estimate_affected_tests` описывает результат-план и `run=true`, а `run_test_by_filter` перечислен в `RelatedTools`, без инструкции выполнить его с умолчаниями. Собственная справка `run_test_by_filter` уже явно говорит `Default noBuild=true` и описывает сборку при false. Строки не менялись.
- [Tools/TestTools.cs](../../../../Tools/TestTools.cs): description `estimate_affected_tests` говорит о `run=true`, без ручной инструкции для фильтра; параметр `run_test_by_filter.noBuild` уже документирует omit=true. Description и schema не менялись.
- Остальные упоминания `run_test_by_filter` в текущих README/sample — каталог, справка параметров, выбор инструмента для raw VSTest filter и повтор **после сборки**. Они не повторяют неверный рабочий цикл для возвращённого estimate-фильтра; исторические отчёты/RED и replay с намеренным `noBuild: true` не переписывались.

## Проверка исправления

- Сопоставлены EN/RU/sample: каждый из трёх workflow содержит явный `noBuild: false`, предупреждение о старых бинарниках и связь manual build с T09-C1.
- Все локальные file links в изменённых документах разрешаются; `git -c core.whitespace=cr-at-eol diff --check` — PASS.
- Изменения только в Markdown/sample: `README.md`, `AGENTS.md.sample`, этот evidence и добавленный раздел implementation report. C#, project/build settings, тесты, версия, зависимости и tool schema не менялись.
- По поручению раунда 2 и [AGENTS.md](../../../../AGENTS.md) docs-only исправление не требует повторных build/SourceStructure/main/Lifecycle или bump версии. Эти прогоны **не выполнялись**; прежние результаты не выдаются за новый запуск. Новая искусственная RED-фаза не создавалась.
