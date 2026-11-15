# Проверки macOS ARM64, 2026-11-15

Статус первого этапа на 2026-11-15: **historical; superseded for local validation**.
Ниже сохранены исходные результаты 1.5.18, включая шесть сбоев. Они закрыты последующим
прогоном: main 1492/1492, SourceStructure 123/123, lifecycle 13/13
([итоговый отчёт](validation-local-tests-2026-11-15.md)). Нативная macOS-приёмка не выполнена;
GitHub CI отложен пользователем.
Объём: [task-01](task-01-macos-arm64-med.md).

## Среда

- Локальный runner: Windows x64, SDK 10.0.402 (временный `global.json`).
- Первая сборка на установленном по умолчанию SDK 11.0.100 остановилась на старых
  игнорируемых исходниках `artifacts/solutiondir-check-20261103-dcc905d7`:
  дубли assembly attributes. На время проверок папка была перенесена вне compile glob.
  Перенос в одноразовый temp sandbox оказался ошибкой: его копия была автоматически удалена.
  Все семь входных файлов восстановлены на исходном месте из журнала их создания 2026-11-03
  (сессия `01a1a324-e400-7f52-af09-7f422bb8dedd`); позднейших обращений к папке до текущей
  задачи в журналах 4–14 ноября не найдено. Содержимое сверено с исходными командами,
  SHA-256 записаны в `artifacts/osx-arm64-validation-20261115/probe-recovery-manifest.json`.
  Генерируемые `obj` обоих вариантов заново созданы MSBuild; это новые build outputs,
  а не побайтовая копия исторических кешей. Временный `global.json` удалён.
  Восстановленная диагностическая папка снова находится в compile glob, как до задачи;
  для повторной сборки сервера без CS0579 её generated-файлы нужно отдельно исключить.
  Исходники задачи не являются причиной этой ошибки.
- macOS runner локально недоступен. WSL: `Wsl/E_ACCESSDENIED`.
- Прямой запуск VSTest из sandbox оборвался до выполнения тестов: testhost не подключился
  за 90 секунд. Lifecycle успешно повторён через Roslyn runner с передачей opt-in через
  `dotnet test --environment`; этот нулевой запуск не засчитывается как проверка.
- Smoke из sandbox выявил запрет named pipes BuildHost. Оба Windows smoke повторены
  вне sandbox, с автоматическим разрешением на запуск, и прошли.

## Выполнено на первом этапе

- Release solution build на SDK 10.0.402: passed; три существующих предупреждения
  (CS8603, CS8601, xUnit1031).
- Парсер argv и очистка BuildHost: 24/24 passed, 0 failed.
  Нативная проверка macOS-reader внутри одного теста на Windows не исполняется
  из-за OS guard; остальные parser-проверки и реальная очистка дочернего BuildHost исполнялись.
- Весь SourceStructure: 123/123 passed, 0 failed; инвентарь структурных ограничений не изменён.
- Финальная сборка решения Release для 1.5.18: passed.
- Основной набор Release, точный фильтр CI `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`:
  **1492 total, 1486 passed, 6 failed, 0 skipped**. Два прогона дали те же шесть сбоев;
  последний выполнен после изменения версии на 1.5.18.
- `AnalyzerLifecycle`, Release, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`:
  **13 total, 13 passed, 0 failed, 0 skipped**; TRX проверен `Assert-NoSkippedTests.ps1`.
- YAML разобран PyYAML; проверены шесть уникальных test jobs, три pack jobs,
  архитектура ARM64, оба macOS-шарда и зависимости, блокирующие неполный релиз.
- PowerShell scripts: синтаксический разбор прошёл; метаданные содержат ровно шесть архивов,
  по одному на каждое сочетание трёх RID и двух режимов поставки.
- `win-x64` self-contained и framework-dependent: publish, упаковка в ZIP, распаковка
  и расширенный smoke прошли. Проверены presence/absence `coreclr.dll` и версия 1.5.18.
- `osx-arm64` self-contained и framework-dependent: кросс-публикации с ReadyToRun прошли.
  У обоих проверены ARM64 Mach-O apphost, `libsqlite3mc.dylib`, BuildHost и версия 1.5.18;
  `libcoreclr.dylib` присутствует только в self-contained. Нативный запуск не выполнялся.
- Diff проверен с `git -c core.whitespace=cr-at-eol diff --check`, с учётом существующего CRLF.

## Шесть сбоев основного набора

Эти файлы не менялись в задаче; первые четыре сбоя принадлежат untracked-файлам,
которые уже находились в рабочем каталоге до начала реализации. Исключения в CI-фильтр
не добавлялись; тесты не исправлялись и не скрывались.

1. `DependencyPartialProofTests.Graph_preserves_inner_frameworks_and_refuses_missing_or_ambiguous_reference`:
   вместо `net10.0`/`net9.0` пустой список. Фикстура закрепляет отсутствующий SDK 10.0.300.
2. `DependencyPartialProofTests.Framework_selection_matches_installed_pack_files_without_loading_analyzers`:
   `The framework selection control requires the installed SDK 10.0.300.`
3. `DependencyPartialProofTests.Graph_discovers_transitive_instances_without_authorizing_admission`:
   0 инстансов вместо 2; та же фикстура с SDK 10.0.300.
4. `IndependentWorkspaceOracleTests.Fresh_ordinary_sessions_have_distinct_workspaces_and_nonzero_controls`:
   exit `-2147450725` вместо 0; oracle требует точного SDK 10.0.300.
5. `RawImportEvidenceTests.Actual_none_channel_is_inspected_before_owned_fixture_cleanup`:
   binlog format 28 при reader до 26; дочерний проект подхватывает SDK 11.
6. `CrossProcessRoundtripTests.Ordinary_unsupported_controls_refuse_without_a_snapshot(multitarget, ambiguous-project-instance)`:
   после 3 минут `IOException` в `RoundtripFixture.Dispose`: временный каталог `App`
   занят другим процессом. Причина зависания и владелец блокировки в этой задаче не установлены.

## Команды и артефакты

Build и основной набор запускались через `run_dotnet_build` / `run_test_by_filter`,
всегда Release. Lifecycle через `execute_dotnet_command`:

```text
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter Category=AnalyzerLifecycle --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1 --logger "trx;LogFileName=lifecycle.trx" --results-directory TestResults/osx-arm64-validation-20261115
```

Публикации — `dotnet publish RoslynMcpServer.csproj -c Release -r <RID> --self-contained <true|false> --no-restore`,
вывод в `artifacts/osx-arm64-validation-20261115/publish/<RID>-<kind>`.
Windows ZIP — `New-PublishZip.ps1`, распаковка — `Expand-Archive`, проверка —
`Invoke-ReleaseSmoke.ps1` против `.github/smoke/SmokeApp/SmokeApp.csproj`.
Локальные результаты сохранены в игнорируемых `artifacts/osx-arm64-validation-20261115/`
и `TestResults/osx-arm64-validation-20261115/`.

## Что требовалось после первого этапа

- Устранить либо отдельно подтвердить baseline шести сбоев полного основного набора —
  выполнено, все шесть реально прошли в [финальном локальном прогоне](validation-local-tests-2026-11-15.md).
- Успешные macOS ARM64 main/lifecycle jobs и smoke обоих нативно распакованных архивов.
- Linux в этой сессии не проверялся. GitHub workflow и публичный релиз не запускались;
  коммит и push не выполнялись.
