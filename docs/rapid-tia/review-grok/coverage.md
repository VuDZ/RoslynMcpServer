# Coverage

## Reviewed target

Полностью прочитан `docs/rapid-tia/README.md` (программа Rapid TIA, 2026-10-29, код не начат): решения R-01–R-12, открытые O-03, O-05, O-06, дизайн baseline и уровней 0–1, сохранение графа, поверхность `estimate_affected_tests`, каталог случаев, эпохи 1–4.

Результаты записаны в `docs/rapid-tia/review-grok/`. Это ревью Grok 4.7, один каталог, без правок спецификации.

## Context inspected

- `docs/user-data-directory/README.md` целиком — путь `tia/` и зависимость эпохи 4.
- `docs/README.md` — индекс и статус темы. Строка индекса, которая пересказывает другой каталог ревью, не использовалась как evidence.
- `docs/mcp-tool-surface-evolution/README.md` — новый инструмент как minor, исторический размер `lite` на v1.3.24. Сверка с `Hosting/McpToolCatalog.cs`: группа `core` совпадает с признаком lite-core.
- `docs/workspace-state-cache/README.md` — поведение MVP: production path ещё не меняется, `reset_workspace` чистит RAM, не disk, hydrate описан как часть загрузки. Отдельного finding по этому стыку нет: R-03 и запрет сбрасывать baseline при повторной загрузке того же solution читаются совместно (нулевая точка — загрузка или восстановление, которыми solution появился; последующий reload ту же точку не двигает).
- `CHANGELOG.md` — v1.5.18 (освобождение предыдущего solution), v1.5.17 и v1.5.15 (ожидание отмены прогрева поиска).
- `Services/Workspace/SolutionManager.cs` — `ClearWorkspaceAsync`, ранний выход `LoadAsync` при том же solution и при `_projectGraphStale`, `StartSearchPrewarm`, `StopSearchPrewarmAsync`, `ForgetSanitizedPublishedSolution`, обработка событий диска и `MarkProjectGraphStaleFromGraphFile`. Файл целиком не читался.
- `Services/Navigation/SearchIndexPrewarm.cs` целиком.
- `Services/Testing/TestAttributeMatcher.cs` целиком — корни xUnit, NUnit и MSTest уже есть; setup-атрибуты программа не возлагает на этот тип.
- `Services/Testing/TestFilterHelper.cs` целиком и `RoslynMcpServer.Tests/TestRunner/TestFilterHelperTests.cs` — оператор `~`, экранирование, отсутствие случая вложенного и generic-класса.
- `Services/Workspace/WorkspaceDiskPathFilter.cs` целиком.
- `Services/Workspace/WorkspaceDiskEventClassifier.cs` — классификация graph file, `SyncsNonCSharpDocumentText` (resx, xaml, razor не синхронизируются).
- `Services/Workspace/WorkspaceInputMapBuilder.cs` — категории walk-up props, resources, restore inputs, project references.
- `RoslynMcpServer.Tests/SourceStructure/ReachabilityAnalysis.cs` — комментарий о границах: виртуальный и интерфейсный вызов идёт к статически связанному члену и не раскрывается. Утверждение программы об этом коде подтверждается. Тело обхода делегатов не читалось.

## Context not inspected

- `docs/rapid-tia/review-codex/` как каталог ревью. Поиск по дереву и список файлов темы показали имена файлов этого каталога и несколько строк совпадений. Эти строки не читались дальше и не использовались как evidence, в том числе чтобы не подгонять состав findings.
- Защита, арбитраж и decision ledger этой темы. Отдельных каталогов рядом со спецификацией, кроме `review-codex` и этого каталога, нет.
- `docs/ARCHITECTURE.md`, `PRIVACY.md` и ветка `usage-statistics` целиком. В программе они названы местами будущей правки, не действующим контрактом TIA.
- Полный `SolutionManager`, полный обход `ReachabilityAnalysis`, реализация `run_test_by_filter` и адаптеров xUnit, NUnit, MSTest.
- Замеры `docs/workspace-load-performance/`: по поиску они не понадобились для сверки инварианта 1.5.18, он взят из CHANGELOG и `SolutionManager`.

## Source/code verification

Сверены существующие швы, на которые программа опирается: освобождение solution при reset и новой загрузке, ожидание остановки прогрева, устаревание проектного графа без немедленного пересчёта `ProjectDependencyGraph`, классификация csproj и props, отказ синхронизировать resx как текст, форма фильтра VSTest, распознавание тестовых атрибутов трёх фреймворков, нераскрытие виртуальных вызовов в SourceStructure.

Тесты не запускались. Это ревью спецификации, реализации Rapid TIA в дереве нет.

## Coverage limitations

Не проверялось фактическое FullyQualifiedName вложенного класса и Theory у трёх адаптеров VSTest — для F-006 это отмечено как нужная сверка, не как уже наблюдённый формат `+` / `` `N ``.

Не проверялось, как скоро `GetCompilationAsync` замечает отмену. F-007 опирается на то, что работа — компиляция проекта, и на уже записанное ограничение ожидания прогрева, а не на измеренный срок отмены компиляции.

Корпус тестов этого репозитория не инвентаризировался поштучно. Утверждение F-008 о малом числе DI- и интерфейсных сценариев — качественная оценка по назначению репозитория (MCP-сервер и тесты на `AdhocWorkspace`), не по подсчёту виртуальных вызовов.

Поведение workspace state cache в production не сверке с кодом: спецификация cache сама пишет, что production path ещё не меняется.
