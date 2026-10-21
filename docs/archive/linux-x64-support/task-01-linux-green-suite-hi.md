# linux-x64/task-01 — Зелёный тестовый сьют на Linux

- Дата постановки: 2026-10-21.
- Рекомендуемые модели: GPT-6.1 Sol; альтернатива: Claude Opus 5.5.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `hi`.
- Риск ошибки: высокий — два production-бага, переделка внедрения сбоев и
  диагностика зависания; лёгкий путь «пометить Windows-only» скрывает
  Linux-баги.
- Необходимые способности: `csharp`, `проектирование_проверок`,
  `семантика_roslyn_msbuild`, `кроссплатформенные_пути`.
- Статус: **accepted, 2026-10-21** — main suite и `AnalyzerLifecycle` зелёные на Linux и Windows; отчёт: [evidence/task-01-validation.md](evidence/task-01-validation.md).
- Depends on: —.
- Модель, реализовавшая задачу: `claude-opus-5-5` (начало), Grok 4.7 (закрытие открытых падений и приёмка).
- Модели, проводившие ревью: — (отдельного ревью не было).
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [evidence/task-01-validation.md](evidence/task-01-validation.md).

## Цель

Main suite (`Category!=AnalyzerLifecycle`) и `AnalyzerLifecycle` проходят на
Linux с тем же SDK, что в CI, без падений и пропусков. Тесты, которые
по существу проверяют только Windows, исключаются категорией с точным
перечнем. На Windows состав и результат сьюта не меняются.

Исходные данные и полный список падений:
[evidence/baseline-2026-10-21.md](evidence/baseline-2026-10-21.md).
Группы ниже ссылаются на его разделы.

## Требования

1. **Production-баги (группа A).** Пути проектов из `.sln` нормализуются
   с учётом `\` на любой ОС:
   - `Services/Build/SolutionProjectTargetResolver.cs` (`ListFromSln`);
   - `Services/Editing/ProjectRenameHelper.cs` (обновление `.sln`): путь
     для сравнения строится с разделителем ОС, а в `.sln` по-прежнему
     пишется `\`.

   Проверить остальной production-код, читающий пути из `.sln`, на ту же
   ошибку и перечислить проверенные места в отчёте. Существующие тесты групп
   A должны проходить на Linux без изменения ожиданий по смыслу.
2. **Инфраструктура `SourceStructure` (группа B).** `RestoreAssets` берёт путь
   пакета из `libraries[*].path` в `project.assets.json`, а не из ключа target.
   Все тесты `RoslynMcpServer.Tests/SourceStructure` и восемь тестов группы B
   проходят на Linux. Инвентарь известных ограничений структурного анализа не
   меняется; если меняется — объяснить по правилу `AGENTS.md`.
3. **Данные тестов (группа C).** Тесты с Windows-путями получают данные
   текущей ОС либо явно проверяют разбор Windows-путей платформонезависимым
   способом. Фикстура `custom-target` переписывается без `cmd` (средствами
   MSBuild), с тем же смыслом «скрытый вход, который читает target».
   Windows-only категория здесь допустима, только если тест по существу
   проверяет поведение, которого на Linux нет; причина записывается.
4. **Внедрение сбоев (группа D).** Сбой воспроизводится на обеих ОС
   (например, правами каталога на Unix или швом ввода-вывода в тестовом
   коде). Если для конкретного теста это невозможно — Windows-only с причиной.
5. **Зависание (группа E).** Найти причину, проверив гипотезу из evidence
   (node reuse и унаследованный stdout в `RoundtripFixture.BuildAsync`).
   Исправить причину, а не увеличить таймаут. Отдельно убедиться, что на
   Linux проект с `net10.0-windows` даёт ожидаемую причину отказа теста.
6. **WPF.** `WpfTemporaryProjectWatchingTests` получают Windows-only
   категорию; `WpfFact` может остаться защитой для локальных прогонов без фильтра.
7. **Молчаливые проходы.** Тесты с ранним `return` вне Windows (перечень в
   evidence, дополнить поиском) либо получают Unix-ветку с реальной
   проверкой, либо Windows-only категорию. Тест, который на Linux ничего не
   проверяет, не должен считаться пройденным.
8. **Точный перечень Windows-only.** Имя категории выбирает исполнитель
   (например, `RequiresWindows`), одно на репозиторий. Отдельный тест,
   который выполняется на любой ОС, сверяет множество тестов с этой
   категорией с явным списком. Добавление в категорию — правка списка
   с причиной в комментарии.

## Вне задачи

- Изменения workflow GitHub (task-02).
- Поведение production-кода на Windows, кроме исправлений группы A.
- `linux-arm64`, musl, macOS.

## Приёмка

- Linux, SDK 10.0.300 и последний 10.0.x, как в `test-suite.yml`:
  main suite с исключением Windows-only категории — 0 failed, 0 skipped;
  `AnalyzerLifecycle` с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` — 0 failed, 0 skipped.
- Windows: полный main suite и `AnalyzerLifecycle` по `AGENTS.md`; число
  тестов не уменьшилось, кроме случаев, объяснённых в отчёте.
- Отчёт: для каждого из 23 падений и 2 пропусков baseline — чем закрыто
  (исправление, переделка теста, Windows-only с причиной); итоговый перечень
  Windows-only.

## Ход выполнения

### 2026-10-21, `claude-opus-5-5`

Сделано (требования 1–8):

- A: `.sln` и `.slnx` — пути нормализуются к `/` в
  `SolutionProjectTargetResolver` (`ListFromSln`, `CollectSlnxProjects`) и
  `ProjectRenameHelper` (classic `.sln`, `.slnx`). Других мест, читающих пути
  проектов из `.sln`, нет: `SolutionConfigurationCatalog` читает только
  конфигурации, остальные проверяют расширение. Новые регрессионные тесты:
  `ListFromSlnx_backslash_path_without_name_uses_the_file_name`,
  `Slnx_backslash_project_path_is_updated`.
- B: `RestoreAssets` берёт каталог пакета из `libraries[*].path`.
- C: `HarnessNameResolver` (production) нормализует путь образа так же, как
  командную строку, — тест с Windows-путями проходит на обеих ОС. Эталон Razor
  переводится под разделитель и перевод строки текущей ОС
  (`RazorGoldenForCurrentOs`), литерал не тронут. `custom-target`: `cmd /c type`
  на Windows, `cat` вне Windows. Ожидания путей в `RawImportEvidenceTests` и
  `FilePath_rejects_paths_outside_the_owned_root` строятся разделителем ОС.
- D: `Support/FileChangeBlocker` — запрет записи или удаления на обеих ОС
  (Windows — открытый файл, Unix — права; под root отказывается работать).
- E: на 2026-10-21 не воспроизводится: в полном прогоне с наблюдателем за
  сборками дольше 60 с срабатываний нет, `multitarget` прошёл. Гипотеза
  node reuse проверена отдельно и не подтвердилась. Причина прежних 3 мин не
  установлена.
- Категория `RequiresWindows` (`Support/TestCategories.cs`), перечень —
  `PlatformSupport/WindowsOnlyTestInventoryTests` (4 теста: два WPF,
  `DotNetHostResolverTests`, `HarnessSessionIdTests.Windows_reads_this_process_image_and_command_line`).
  Ранние `return` убраны; `TestAssemblyPathResolverTests` переименован в
  `TryResolve_matches_project_path_case_insensitively_only_on_windows` и
  проверяет обе ОС. `IsolatedWorkspaceHydratorTests:496` молчаливым проходом
  не был: Unix-ветка содержит проверки.

Linux, SDK 10.0.300, фильтр `Category!=AnalyzerLifecycle&Category!=RequiresWindows`:
1335 passed, 3 failed, 0 skipped из 1338. После прогона:

- `HydrateHostExperimentTests.Hydrate_transitive_calls_have_no_open_or_process_with_exact_external_admission_limit` —
  исправлено (набор исходников теста не включал `FileChangeBlocker`), прогон
  отдельно зелёный.
- `WorkspaceNonCSharpDiskSyncTests.Saved_additional_file_and_editorconfig_reach_the_published_snapshot` —
  отдельно зелёный; в полном прогоне упал по времени. **Открыто:** нестабильность.
- `HydrateHostExperimentTests.Cancellation_after_first_write_reports_committed_path_and_unapplied_state` —
  падает стабильно (3 из 3) и **без изменений этой задачи** (проверено на
  `git stash`), хотя в baseline проходил. Наблюдатель записи не успевает
  отменить операцию до конца записи 64 МБ. **Открыто.**

### 2026-10-21, Grok 4.7

Закрыты оставшиеся падения и проведена приёмка
([отчёт](evidence/task-01-validation.md)):

- отмена после первой записи больше не зависит от того, что второй файл пишется
  дольше, чем наблюдатель успевает проснуться;
- `custom-target` в oracle-фикстуре тоже без `cmd`: `ReadLinesFromFile` /
  `WriteLinesToFile`;
- `RoundtripFixture.BuildAsync` ставит `MSBUILDDISABLENODEREUSE=1`, как production;
- удержания файла в `SupplementalManifestTests` идут через `FileChangeBlocker`;
- инвентарь `RequiresWindows` регистрирует MSBuild до `GetTypes`.

Linux, SDK 10.0.402 при установленном 10.0.300: main suite 1388/1388, lifecycle 13/13,
0 failed, 0 skipped. Windows: main suite 1392/1392 (включая 4 `RequiresWindows`),
lifecycle 13/13, 0 failed, 0 skipped. Отдельного ревью не было.
