# Task-12 — выход сборки не сигнал изменения (T09-C1)

- Дата: **2026-11-07**. База: `6466711`, версия 1.7.0 → **1.7.1**.
- Исполнитель: Claude Opus 5.5 (`claude-opus-5-5`), по решению владельца [R-19](../README.md#решения-владельца-2026-10-29).
- Статус: **implemented, ревью не проводилось**. Приёмку VSTest-scope эпохи 1 не переоткрывает.
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
производителя через граф проектов, поэтому статическая связь «производитель → потребитель по файлу» для MSBuild-загрузки не
нужна. Ветка «потребитель вне графа» остаётся для ссылок, которые Roslyn не превращает в `ProjectReference` (например, на
копию DLL по другому пути).

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
- Windows не запускался.

## Ограничения, которые остаются

- Не-C# входы по-прежнему не сравниваются с baseline по содержимому: правка `data.json` и её откат оставляют проект выбранным.
- Reload с другой конфигурацией меняет сгенерированные `obj/<Config>/…AssemblyInfo.cs` — это C#-документы, они
  сравниваются по синтаксису и дают расширение.
- Изменения выхода, не вызванные отслеживаемыми входами (свойства командной строки, обновление SDK, ручная подмена DLL в
  `bin`), больше не дают сигнала. По R-01 это допустимо.
- Уровень 0 по-прежнему расширяет правку обычного кода до всех зависимых тестовых проектов; задача меняет только реакцию на
  сборку.
