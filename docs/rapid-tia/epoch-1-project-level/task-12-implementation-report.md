# Task-12 — выход сборки не сигнал изменения (T09-C1)

- Дата: **2026-11-07**. База: `6466711`, версия 1.7.0 → **1.7.1**.
- Исполнитель: Claude Opus 5.5 (`claude-opus-5-5`), по решению владельца [R-19](../README.md#решения-владельца-2026-10-29).
- Статус: **accepted, 2026-11-08**, 1 раунд, 0 исправлений ([приёмка](#приёмка)). Приёмку VSTest-scope эпохи 1 не переоткрывает.
- Задача: [task-12](task-12-build-output-signal-med.md). Evidence: [RED](evidence/task-12-red.md), [GREEN](evidence/task-12-green.md).

## Изменения

- [ProjectImpactMapper](../../../Services/Testing/ProjectImpactMapper.cs): `IsSolutionBuildProduct` и
  `ConsumersOutsideProducerGraph`. Путь, у которого в карте входов только роли `Output`/`MetadataDependency` с
  производителем из загруженного solution, не выбирает производителя и проекты, транзитивно зависящие от него по графу.
  Потребитель по ссылке на файл вне графа выбирается как раньше. Без оставшихся владельцев путь не попадает ни в
  изменённые пути (счётчик «in N files»), ни в «Not covered». Карта входов и watcher не менялись.
- Тесты: три теста в памяти в `ProjectImpactMapperTests` (выход и ref-сборка производителя, потребитель вне графа,
  внешняя DLL как охрана), реальный MSBuild в `ProjectImpactMapperIntegrationTests` (настоящие `bin`/`ref` и
  HintPath-потребитель) и в `EstimateAffectedTestsRunTests` (правка → `run` → повторная оценка → откат). Помощники
  фикстур: `ProjectImpactFixture.SetOutput/AddFileReference/WriteAssembly`, `TestImpactRunFixture.RestoreAsync`.
- Документация: README EN/RU (запись инструмента и история версий), `AGENTS.md.sample` (Builds вместо After a build,
  без связи ручной сборки с T09-C1; требование `noBuild: false` для ручного фильтра сохранено), ARCHITECTURE,
  CHANGELOG 1.7.1 (и пропущенная ранее запись 1.6.0), R-19 в программе.
- Версия: `Version`/`AssemblyVersion`/`FileVersion` **1.7.1 / 1.7.1.0 / 1.7.1.0** — исправление поведения без изменения
  поверхности инструмента.

## Находка пробы

MSBuildWorkspace превращает `<Reference><HintPath>` на выход загруженного проекта в `ProjectReference`
([RED, проба HintPath](evidence/task-12-red.md#проба-hintpath)). Такой потребитель выбирается по правке исходников
производителя через граф проектов, поэтому для точного HintPath на `OutputFilePath`/`OutputRefFilePath` загруженного
проекта статическая связь «производитель → потребитель по файлу» не нужна. Это измерено на одном SDK-проекте; копии DLL по
другому пути, нестандартные targets и multi-target загрузки не проверялись. Ветка «потребитель вне графа» остаётся для
ссылок, которые Roslyn не превращает в `ProjectReference`.

## TDD

RED: 6 cases, 1 passed (охрана внешней DLL) / 5 failed по ожидаемой причине — выход относится к своему проекту, после
`run` план становится whole project и остаётся им после отката. GREEN: 6/6.

## Validation

- Группа TIA + SourceStructure + `DotNetTestExecutorTests`, Release, Linux: **660/660**.
- Release solution build на 1.7.1: **PASS**, 0 ошибок; warnings только прежние CS8601/CS8603/xUnit1031.
- Полный main suite, Release, `--no-build --no-restore`,
  `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk` (Linux, как в workflow):
  **2002/2002 passed**, 0 failed/skipped/timeout по TRX, 10 мин 51 с. Прежний Linux main на 1.7.0 — 1996/1996; разница —
  6 новых cases task-12.
- AnalyzerLifecycle не запускался: изменение не затрагивает analyzer/shadow-copy lifecycle и пути load/prepare/publication.
- Windows автором не запускался. Независимый прогон Codex на `2c9b1e7` (Windows, Release): build PASS, TIA +
  SourceStructure 175/175, полный main suite `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` **2010/2010**
  (на 1.7.0 — 2004/2004; разница — 6 новых cases), [ревью Codex](evidence/task-12-review-round-1.md#проверено).

## Ограничения, которые остаются

- Не-C# входы по-прежнему не сравниваются с baseline по содержимому: правка `data.json` и её откат оставляют проект выбранным.
- Reload с другой конфигурацией меняет сгенерированные `obj/<Config>/…AssemblyInfo.cs` — это C#-документы, они
  сравниваются по синтаксису и дают расширение.
- Изменения выхода, не вызванные отслеживаемыми входами (свойства командной строки, обновление SDK, ручная подмена DLL в
  `bin`), больше не дают сигнала производителю и его зависимым по графу. Это принятое следствие R-19; пустой план после
  таких операций не означает, что тесты не нужны.
- Один выходной путь у нескольких проектов (одинаковые `AssemblyName` и `OutputPath`): карта не знает, кто записал файл
  последним, и подавляет его для графовых потребителей всех производителей. Реальная MSBuild-проба Codex потери теста не
  показала; это ограничение модели ([ревью Codex](evidence/task-12-review-round-1.md#ограничения)).
- Ссылки на анализаторы: карта не индексирует `AnalyzerReferences`, поэтому проект, который подключает выход другого
  проекта как анализатор без ребра в графе, по изменению этой DLL не выбирается. Пробел был и до task-12
  ([ревью Grok](evidence/task-12-review-round-1-grok.md#ограничения)).
- Уровень 0 по-прежнему расширяет правку обычного кода до всех зависимых тестовых проектов; задача меняет только реакцию на
  сборку.

## Приёмка

- Дата: **2026-11-08**. Раунд 1: [Codex — ACCEPT](evidence/task-12-review-round-1.md), [Grok 4.7 — ACCEPT](evidence/task-12-review-round-1-grok.md); MUST-FIX по R-18 нет,
  исправлений после ревью 0.
- Принятый scope: продукт сборки проекта из solution не выбирает производителя и его зависимых по графу; потребитель по
  ссылке на файл сборки вне графа выбирается; внешние DLL и пути с другими ролями — как раньше. Версия 1.7.1.
- Validation кода `2c9b1e7`: Linux автора (main 2002/2002) и Windows Codex (main 2010/2010). После ревью менялась только
  документация (формулировки HintPath и R-19, ограничения, статусы), повторных прогонов нет.
