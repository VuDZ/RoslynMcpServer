# Постепенный переход на Roslyn 5.12, .NET 11 и C# 15

Дата: **2026-11-18**. Статус подготовки документации: **completed** —
[проверка плана](evidence/preparation-2026-11-18.md).
Статус перехода: **planned; реализация и приёмка не начаты**.
Подготовленный scope: четыре последовательных этапа, обязательная validation,
матрица совместимости, замеры, поставки и откат. Независимое ревью плана не выполнено.

Ветка: `dotnet11-migration-plan`, от `origin/main` на
`6f84e897e059cd87b9cc8946212154291e0e2ed7` (сервер 1.5.25).
Основная ветка репозитория называется `main`, а не `master`.
Ветка создана в чистом worktree; незакоммиченная работа исходного checkout в неё не перенесена.

## Цель и порядок

Сервер должен корректно анализировать проекты .NET 11 / C# 15, сохранить проверенные
сценарии прежних SDK и оптимизации BuildHost, затем перейти собственным runtime на .NET 11.
Поддержка языка анализируемого проекта и язык исходников сервера проверяются отдельно.

1. [Этап 0 — версии, зависимости и baseline](task-00-baseline-and-dependencies-med.md):
   подтвердить состав нового пакета, совместимость процессов и воспроизводимый контроль.
2. [Этап 1 — Roslyn и форк BuildHost](task-01-roslyn-and-buildhost-hi.md):
   обновить согласованный набор Roslyn, перенести форк, проверить RPC и пользовательские проекты.
   Собственный TFM сервера пока `net10.0`.
3. [Этап 2 — runtime .NET 11 и C# 15](task-02-runtime-and-language-hi.md):
   согласованно обновить сервер, тестовые host и CI, повторить проверки и замеры.
4. [Этап 3 — поставки и итоговая приёмка](task-03-packaging-and-acceptance-hi.md):
   нативно проверить шесть архивов, dotnet tool, требования к SDK/runtime и обратный переход.

Все четыре этапа имеют статус **planned**, дата статуса 2026-11-18; принятый
implementation scope и отчёты об исполнении пока отсутствуют. Отчёт подготовки
не является evidence прохождения этих этапов. Текущий запрос выполнен только в документации.

## Проверенная отправная точка

По исходникам базового commit:

- Сервер, основной тестовый проект, LifecycleTestHost, DeadlockWitnessHost и sample plugin
  используют `net10.0`. Явного `LangVersion` у сервера нет: его штатный язык — C# 14.
- Roslyn — `5.9.0`; compile-time ссылки Microsoft.Build и Microsoft.NET.StringTools —
  `18.6.3`; Microsoft.Build.Locator сервера — `1.11.2`.
- [Форк](../../../forks/roslyn-buildhost/README.md) взят из
  `35d9211b841e7613c1d2f8f5af6d628ace696c4c`.
  Его netcore-host — framework-dependent `net8.0`, `RollForward=Major`;
  reference assemblies MSBuild — `17.11.48`, Locator — `1.10.2`.
  [Guard](../../../forks/roslyn-buildhost/build/BuildHostFork.targets) требует ровно Roslyn `5.9.0`.
- В сервере включены A (references first), B (evaluation ahead, четыре worker) и C
  (общий SDK resolution cache на проект). Повторная настройка worker/GC не входит в переход.
- SQLite3MC.PCLRaw.bundle `2.3.5` выбран под конкретную assembly identity Roslyn.
  Совпадение проверяет `RoslynIndexStorageTests`; NuGet dependency graph сам по себе недостаточен.
- [CI](../../../.github/workflows/test-suite.yml) использует SDK `10.0.x`;
  release создаёт локальный `global.json` с `10.0.402` и `rollForward=disable`.
  В корне репозитория SDK pin отсутствует.
- Реальные release RID: `win-x64`, `linux-x64`, `osx-arm64`, каждый в двух вариантах.
  Ещё три RID в csproj не означают проверенной платформенной поддержки.
- Нативный CI исходников 1.5.25 пройден; нативный smoke обоих macOS-архивов
  остаётся pending в [теме релизов](../../github-releases/README.md).
  Этот долг нельзя считать проверкой новых архивов.

Кандидат обновления — Roslyn `5.12.0`, опубликованный 2026-11-13. NuGet указывает
TFM `net10.0`, Microsoft.Build.Framework >= `18.7.1` и source commit
`1324e1d34a0490454837a22370e75bbaeecffb87`.
Это сведения [страницы пакета](https://www.nuget.org/packages/Microsoft.CodeAnalysis.Workspaces.MSBuild/5.12.0);
этап 0 обязан подтвердить их скачанным nuspec и файлами пакета.
Runtime и build requirements нового BuildHost ещё не проверены.

.NET 11 выпущен 2026-11-10. C# 15 штатно соответствует `net11.0`;
включать его для собственных исходников на `net10.0` не планируется.
.NET 10 LTS и .NET 11 STS поддерживаются до 2028-11-14: переход не увеличивает этот срок.
Источники: [релиз .NET 11](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/),
[версии C#](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning),
[поддержка .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## Границы изменения

Обновляются только зависимости и код, необходимые для совместимости, связанные тесты,
build/publish/pack, документация и evidence. Используются существующие стенды
`spikes/BuildHostFork`, CI и release smoke; новый загрузчик, новая система кеша,
Native AOT, single-file, новые RID и массовое переписывание кода на C# 15 не нужны.

Runtime сервера, runtime отдельного BuildHost, выбранный MSBuild и SDK проекта —
четыре отдельные величины. Self-contained сервер не поставляет SDK и не делает
дочерний framework-dependent BuildHost self-contained. Их версии и пути фиксируются в отчётах.

Кеш workspace не получает разрешения на новую production-активацию.
Форматы MCP, конфиг, приоритеты SDK, write boundary, analyzer admission и политика watcher
сохраняются, кроме точечных исправлений совместимости с собственным regression test.
Изменение публичных требований к runtime/ОС/CPU должно быть явно описано и проверено.

## Общая обязательная validation

Это минимум каждого этапа с code/test/build-setting изменениями, включая финальный diff.
Имена тестов в этапах не заменяют полный main suite. Предпочтительны
`run_dotnet_build`, `run_specific_test`, `run_dotnet_test` Roslyn MCP;
если runner не умеет нужный environment, используется CLI с теми же настройками.

После production C# изменений запускается весь `RoslynMcpServer.Tests/SourceStructure`.
Для изменения Roslyn, TFM и форка этот набор также обязателен: он сам зависит от Roslyn.
Точные identity и kind известных structural-analysis limits сохраняются;
новый предел требует объяснения и точного ожидаемого inventory, а не ослабления count.

Пример для PowerShell 7 из корня checkout; команды проверяются по exit code,
при первом отказе последовательность останавливается:

```powershell
dotnet restore RoslynMcpServer.sln
dotnet build RoslynMcpServer.sln -c Release --no-restore
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~RoslynMcpServer.Tests.SourceStructure" --logger "trx;LogFileName=source-structure.trx" --results-directory artifacts/dotnet11-validation
$mainSuiteFilter = 'Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk'
if ($IsLinux -or $IsMacOS) { $mainSuiteFilter += '&Category!=RequiresWindows' }
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter $mainSuiteFilter --logger "trx;LogFileName=main.trx" --results-directory artifacts/dotnet11-validation
$env:ROSLYN_MCP_ANALYZER_LIFECYCLE = '1'
dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build --no-restore --filter "Category=AnalyzerLifecycle" --logger "trx;LogFileName=lifecycle.trx" --results-directory artifacts/dotnet11-validation
Remove-Item Env:ROSLYN_MCP_ANALYZER_LIFECYCLE
./.github/scripts/Assert-NoSkippedTests.ps1 -ResultsDirectory artifacts/dotnet11-validation
```

- Lifecycle обязателен на этапах 1–3: меняются Roslyn, загрузка/prepare/publication,
  runtime и/или host configuration. Opt-in включается только для этого набора;
  прежнее значение environment восстанавливается при использовании готового runner.
- Для каждого запуска — отдельный свежий каталог результатов, фактический SDK,
  runtime, OS/architecture, command/filter, exit code и TRX. Считать executed/passed/failed/skipped.
  Ноль тестов, skip, timeout и отсутствие среды не являются PASS.
- Перед приёмкой каждого code-этапа и финального результата — Release build и весь main suite
  на Windows x64, Linux x64 и macOS ARM64, плюс lifecycle на каждой платформе.
  При недоступной платформе её gate остаётся pending; кросс-публикация не заменяет native run.
- `RequiresWindows` исключается только на Linux/macOS. `RequiresPinnedSdk` остаётся исключённым
  из main; не менять его fixtures и SDK pins ради зелёного прогона. Если затронут их общий
  runner или контракт, отдельно выполнить затронутые тесты с требуемым точным SDK
  (текущий inventory включает `10.0.300`) и записать результат.
- После исправления — affected tests для обратной связи, затем полный main suite
  на финальном коде перед commit/приёмкой по [AGENTS.md](../../../AGENTS.md).
  Нужные lifecycle/платформенные проверки повторяются после влияющих на них изменений.

## Матрица совместимости

На этапах 1 и 2 проверяются следующие случаи; для каждого указать поддерживаемый
результат или воспроизводимый отказ с причиной. Раньше работавший положительный
сценарий нельзя превратить в отрицательный без явного изменения контракта.

- SDK 10 по точному `global.json`, SDK 11 по точному pin, оба установлены,
  только SDK 10 и только SDK 11; отдельно отсутствующий pinned SDK и отсутствующий SDK вообще.
  Проверить поддерживаемые reader/selector варианты `rollForward`, выбор executable,
  architecture, cwd и отсутствие зависимости от SDK, которым собран сервер.
- Server SC и FDD, где SC использует встроенный runtime, а BuildHost — установленный.
  Для FDD после этапа 2 обязателен shared runtime 11; отдельно воспроизводимый отказ без него.
  Старые SDK/workloads не должны требовать установки SDK 11 только из-за TFM сервера.
- `.csproj`, `.sln`, `.slnx`, `.slnf`; multi-TFM, linked/shared sources,
  conditional project references, Directory.Build.props/targets, editorconfig,
  additional files и сгенерированные документы.
- Прежние C# 14/net10 fixtures и репрезентативный net11/C# 15 проект:
  union types, closed hierarchies, collection expression arguments, extension indexers,
  labeled break/continue. Проверять parse, binding, diagnostics, navigation,
  references, rename и применимые code actions с компиляцией результата.
  Memory-safety preview не входит в обещание стабильной поддержки.
- Analyzer/source generator с обычным поддерживаемым TFM и с зависимостью от нового Roslyn;
  generated symbol доступен сразу после publication, затем после rebuild и reload.
  Сборки analyzer/generator на `net11.0` проверяются отдельно: новый parser на runtime 10
  сам по себе не делает их исполнимыми. Не обещать поддержку вне analyzer admission policy.
- Изменение/удаление исходника, import, editorconfig и generator на диске:
  watcher, повторная публикация и поиск не возвращают старые результаты.
  Проверить cold/warm SQLite index и изолированный snapshot/codec roundtrip,
  идентичность parse options и безопасный отказ при несовместимой версии кеша.
- Отмена до старта host, во время initialization/evaluation/build, kill/crash host,
  повторный load после отказа, reload/dispose/server exit.
  Нет зависания, частичной публикации, потерянных diagnostics и оставшихся собственных BuildHost;
  host другой сессии/сервера не завершается.
- На Windows — WPF temporary projects и доступный net472 fallback.
  Чужие языки, отсутствующие проекты, XML errors и missing SDK сохраняют понятную диагностику.

## Сравнения и производительность

Не смешивать обновление compiler, host и runtime в одном неразделимом замере:

1. B0: сервер net10 + Roslyn 5.9 + старый форк A+B+C (исходный контроль).
2. B1: сервер net10 + новый Roslyn + новый форк A+B+C.
3. B2: сервер net11 + тот же новый Roslyn и форк с теми же A+B+C.
4. Внутри новой версии: stock host нового пакета, новый форк без A/B/C и с A+B+C.
   У этих сторон один и тот же новый MSBuildWorkspace client.
   Если TFM/runtime host пришлось изменить, выделить это отдельной парой и зафиксировать причину.

Старый/новый Roslyn сравниваются на прежних проектах; новые языковые fixtures —
между пригодными для них сторонами. Полное RPC-сравнение нового форка со stock host
той же версии обязательно. Для межверсионного сравнения объясняются по одному
изменения диагностик и API; их нельзя скрыть общей нормализацией.

Контроли: SmokeApp, решение сервера, большой `Roslyn.slnx` и прежнее офисное решение,
если оно доступно. Недоступный private control остаётся pending с причиной, его результаты
не подменяются прежними замерами. Публичные обязательные контроли должны пройти.

На одной машине, при одинаковых SDK/workloads, settings, файлах и состоянии кеша:
один warmup и не менее трёх измеряемых чередующихся запусков с новыми PID.
Cold и warm cache — отдельные серии. Сохранить исходные значения и медианы:
load, first useful semantic query, их сумма, CPU и peak working set отдельно
сервера/host и суммарно; состав workspace, diagnostics и ответы запросов.
Для cold cache изолировать только каталоги эксперимента, не удалять пользовательские кеши.

Пороги этого плана: регрессия медианы load + first useful query более 5% или peak working set
более 10% на любом обязательном контроле останавливает этап. Отдельно расследовать рост
load/first query, если сумма его скрыла. При шуме повторить не менее пяти измерений.
Неуспех не маскировать изменением GC, workers, отключением analyzers или урезанием результата.
Совместимость может оправдать переход без ускорения; превышение порога требует
исправления либо отдельного явно записанного решения владельца с измерениями.

## Откат и ведение статуса

Каждый принятый code-этап — отдельный согласованный commit/checkpoint с evidence.
До него сохранить SHA контроля; после него обновить файл этапа, этот README и
[индекс docs](../../README.md): дата, статус, принятый scope, validation, ограничения.

Откат Roslyn выполняется вместе с форком, Contracts, version guard и связанными package pins.
Возврат одного host DLL или ослабление guard не допускается.
Откат runtime возвращает вместе TFM зависимых проектов, CI/release SDK и publish paths.
Rollback поддерживается предыдущим проверенным бинарным архивом; предыдущие assets не перезаписываются.

`ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — диагностика/временное выключение A/B/C,
а не откат Roslyn, RPC или runtime. Пользовательские данные, конфиг, логи и чужие кеши
при откате сохраняются; новый кеш должен иметь изолированный ключ или безопасно отвергаться
старой версией. Проверить реальный запуск предыдущего бинарника после нового.

Слияние, push, tag и публикация релиза не выполняются при подготовке плана.
Промежуточный успех не меняет public defaults и не означает приёмку всей миграции.

## Ревью плана

- Модели, проводившие независимое ревью: Grok 4.7.
- Даты ревью: 2026-11-18.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 0. План по карточкам не менялся.
- Итог: записано для защиты. Два Blocker (R-01, R-02), один High (G-01), один Medium (R-03). Приёмка плана не закрыта.
- Отчёты: [review/README.md](review/README.md).

## Приёмка реализации

- Модели, проводившие ревью: —.
- Даты ревью и приёмки: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Итог: не проверено; все четыре этапа planned.
- Принятый implementation scope: —.
- Обязательная validation и оставшиеся ограничения: —.
- Отчёты: —.
