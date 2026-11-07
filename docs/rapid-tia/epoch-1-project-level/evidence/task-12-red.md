# Task-12 — RED

- Дата: **2026-11-07**. База: `6466711`, версия 1.7.0. Linux, .NET SDK 10.0.114, Release.
- Исполнитель: Claude Opus 5.5 (`claude-opus-5-5`).
- Production-код не менялся; добавлены только тесты и помощники фикстур.

## Результат

`dotnet test -c Release` с фильтром по пяти новым тестам: **6 cases, 1 passed / 5 failed**.

| Тест | Итог | Причина |
|---|---|---|
| `ProjectImpactMapperTests.Solution_project_output_selects_neither_its_producer_nor_dependent_tests(referenceAssembly: False)` | Failed | `ProjectIds` не пуст: `bin/…/Library.dll` относится к Library |
| `…(referenceAssembly: True)` | Failed | то же для `obj/…/ref/Library.dll` |
| `ProjectImpactMapperTests.File_reference_consumer_outside_the_project_graph_is_selected_without_the_producers_tests` | Failed | выбраны App, Isolated и Specs вместо одного Isolated |
| `ProjectImpactMapperTests.Assembly_without_a_solution_producer_still_selects_its_consumer` | Passed | охрана от регрессии: внешняя DLL уже выбирает потребителя |
| `ProjectImpactMapperIntegrationTests.Real_project_outputs_select_nothing_and_a_HintPath_consumer_follows_the_project_graph` | Failed | настоящие `App.dll` и `ref/App.dll` выбирают App |
| `EstimateAffectedTestsRunTests.Estimate_after_run_keeps_the_filter_and_a_restored_edit_leaves_an_empty_plan` | Failed | после `run` план стал whole project |

Ответ estimate после `run` в последнем тесте (пути сокращены):

```text
Changed since load: 1 members in 2 files. Directly selected: 1 tests. Run plan: 1 projects.
Run: `…/First/First.csproj` whole project (project-level).
Reasons: Changed test: …/First/Cases.cs; Project expansion without a call graph: …/First/bin/Release/net10.0/First.dll; Project-level expansion selects the whole project.
```

После отката правки:

```text
Changed since load: 0 members in 1 files. Directly selected: 0 tests. Run plan: 1 projects.
Run: `…/First/First.csproj` whole project (project-level).
Reasons: Project expansion without a call graph: …/First/bin/Release/net10.0/First.dll; Project-level expansion selects the whole project.
```

Это и есть T09-C1 в том виде, в каком его воспроизвёл [второй рецензент task-09](task-09-review-round-2-claude.md#t09-c1--после-собственного-run-проект-до-конца-baseline-идёт-целиком).

## Проба HintPath

Первая версия интеграционного теста ожидала, что потребитель с `<Reference><HintPath>…/App.dll</HintPath></Reference>`
не связан с App в графе проектов. Она упала на проверке `consumer.ProjectReferences`: MSBuildWorkspace сам превратил
ссылку на выход загруженного проекта в `ProjectReference` на `App.csproj`. Значит, такой потребитель выбирается по
правке исходников App через граф проектов, и статическая связь «производитель → потребитель по файлу», упомянутая в
[задаче](../task-12-build-output-signal-med.md#вне-scope), для MSBuild-загрузки не нужна. Тест переписан под этот факт и
снова упал по ожидаемой причине (таблица выше). Тест в памяти с потребителем вне графа остаётся охраной для ссылок,
которые Roslyn не превращает в `ProjectReference`.
