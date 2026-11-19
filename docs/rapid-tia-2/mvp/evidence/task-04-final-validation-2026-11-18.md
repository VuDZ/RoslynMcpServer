# Task-04 — финальная validation и приёмка

Дата: **2026-11-18**, Linux x64. Статус: **accepted** координатором после
ACCEPT ревью раунда 2. Scope — внутренняя модель сужения: для тестового
проекта, который эпоха 1 выбрала целиком из-за правки C#-кода, тестовые классы по ступеням 0–3 либо
названная причина, по которой проект остаётся целиком. Причина выбора эпохи 1 стала значением в
`TestImpactResult`. Решение владельца [M-11](../README.md#решения-владельца-по-плану): «фильтр не строится»
относится к ступени. Текст ответа и параметры `estimate_affected_tests` не меняются, сужение в продукте пока
не вызывается (task-05). Версия и CHANGELOG не меняются.

## Ход ревью

- Раунд 1 — MUST-FIX. F-01 и F-02 — пути молчаливой потери тестов: класс,
  найденный только по графу «до правки», без класса инвентаря; проверки снимков только на пути правки, хотя
  поиск проходит и через сборки вне его. F-03 — поиск по текущему графу не был закреплён тестами. Всё
  исправлено тем же исполнителем, N-01…N-04 закрыты, см. раздел «Ревью раунда 1» в
  [отчёте](../task-04-implementation-report.md).
- Раунд 2 — ACCEPT.
- Общее ревью координатора: замечаний нет. Необязательные замечания раунда 2 принимаются как ограничения:
  - N-01: проверка снимков по карте соседства идёт по имени файла. Если одна целевая платформа
    multi-target зависимости не собрана или «неизвестна», проект всегда остаётся целиком. Теряется сужение,
    а не тесты; смотреть в замере task-06.
  - N-02: при ошибке перечисления выходного каталога карта соседства task-01 пуста. Передаётся в task-05/06.
  - N-03: `.cs`-документ, удалённый из workspace синхронизацией с диском, получает вид `NonCSharpInput`
    вместо `EditedDocumentMissing`. Проект остаётся целиком, текст тот же; task-05 учитывает это, если
    показывает вид.
  - Мутацию «только транзитивные зависимости» тесты не ловят: в фикстурах карта соседства уже содержит все
    зависимости, как после настоящей сборки. Рецензент признал это допустимым.

## Окружение и команды

Baseline `d78bb2d`, рабочее дерево с окончательным кодом. SDK **10.0.114**, Release, `TMPDIR=$HOME_DIR/testtmp`,
прогоны последовательные, MSBuild без node reuse. Фильтр main — Linux shard `.github/workflows/test-suite.yml`.

```bash
dotnet build RoslynMcpServer.sln -c Release --no-incremental -nodeReuse:false
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter 'FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure|FullyQualifiedName~RoslynMcpServer.Tests.Testing.Narrowing|FullyQualifiedName~ProjectExpansionReasonTests|FullyQualifiedName~VstestClassFilterTests'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter 'Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk'
```

## Результаты

- **Release build solution:** 0 ошибок, 3 прежних предупреждения вне задачи: CS8603
  `NuGetFallbackAssemblyResolver.cs:72`, CS8601 `HostSession.cs:1179`, xUnit1031
  `WpfTemporaryProjectCallbackTests.cs:195`.
- **Focused:** **188/188**, 0 skips (SourceStructure, Narrowing, `ProjectExpansionReasonTests`,
  `VstestClassFilterTests`). Инвентарь ограничений структурного анализа не изменился.
- **Main (Linux):** **2487/2487**; total = executed = passed = 2487, failed/error/timeout/aborted = 0.
  Начало `2026-11-18T20:05:50+01:00`, конец `20:17:54+01:00`.
- **AnalyzerLifecycle** не запускался: задача не меняет пути загрузки, подготовки и публикации workspace,
  lifecycle analyzer/shadow-copy и конфигурацию тестов или сборки.

Сырые артефакты лежат в игнорируемом `TestResults/task04-final`. SHA256:

```text
focused.trx        CE722936DCF0DE6B6AFA7DD96D44232BE98F6D26EFB240B7A31FDC7599DD0274
main.trx           07ADBEF71AD3E7E8CDAE9A3078033DEFD21647F4227FCE00538FC9A47E6F1184
release-build.log  3857D01628F68251D0D9B53512B8999B2867701B552B391DDE64F66168B7D62C
```

## Границы

Windows и RequiresPinnedSdk не запускались. Сужение пока не вызывается инструментом; ответ агенту, флаг `run`
и сквозные проверки — task-05 и task-06.
