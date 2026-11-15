# Проверка коммита osx-arm64 после интеграции, 2026-11-15

Статус: **local-validation-complete; native-validation-pending**.
Принятый объём: поддержка macOS ARM64 в CI и релизах, два режима поставки,
расширенный smoke, безопасная очистка BuildHost на macOS и относящиеся к задаче исправления тестов.
Версия подготовленного коммита — **1.5.25**.

## База и отделение посторонних изменений

Исходный рабочий каталог находился на `4e5e9f9` и отставал от `origin/main` на 24 коммита.
Интеграция выполнена в отдельном чистом worktree поверх
`59f693658671d46caa5c76bcbe63bef37bea7d8d`, без переноса посторонних изменений рабочего каталога.

- Взяты только файлы задачи; из `docs/README.md` — только строка GitHub binary releases.
- Запись о coverage, backlog, экспериментальные untracked C#-файлы, диагностические каталоги,
  скрипты статистики и настройки агентов в коммит не входят.
- Существующая версия 1.5.24 повышена до 1.5.25; откат к подготовленной ранее 1.5.18 не выполнялся.
- Исправления `RoundtripFixture`, исключение `artifacts` и соответствующий SourceStructure scope
  уже находились в актуальной main. Они сохранены; повторных изменений этих мест в коммите нет.
- Проверены неизменность исходного backlog, инвентаря untracked-файлов и настоящего индекса
  исходного checkout. Подготовка патча использовала отдельный временный индекс.

## Финальный код

На Windows x64 с SDK 10.0.402 и штатным `global.json` новой main:

- Release solution build — **passed**.
- Полный основной набор, фильтр `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` —
  **1523 total, 1523 passed, 0 failed, 0 skipped**.
- Весь SourceStructure — **123/123 passed**; инвентарь структурных ограничений не ослаблялся.
- `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` — **13/13 passed**, 0 skipped.
- PowerShell syntax и метаданные шести архивов версии 1.5.25 — passed.
- Проверка staged diff и отсутствия посторонних файлов — passed.

Число тестов отличается от прежних 1492: проверена актуальная main и только выбранный состав
коммита, без экспериментальных исходников из первоначального рабочего каталога.
Остались существующие compiler/analyzer warnings вне изменённых строк.

Команды: `run_dotnet_build` решения, Release; `run_test_by_filter` tests project для
SourceStructure и основного CI-фильтра, Release, после сборки с `noBuild=true`.
Lifecycle: `dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter Category=AnalyzerLifecycle --environment ROSLYN_MCP_ANALYZER_LIFECYCLE=1`.

Первоначальные проверки поставки — [первый отчёт](validation-macos-arm64-2026-11-15.md).
Разбор и устранение шести локальных сбоев — [локальный отчёт](validation-local-tests-2026-11-15.md).
Нативный запуск на macOS не выполнялся. GitHub workflows вручную не запускались;
обычный push в main запускает CI автоматически, его результат в этот локальный отчёт не входит.
