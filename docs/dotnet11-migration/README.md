# Постепенный переход на Roslyn 5.12, .NET 11 и C# 15

Дата спецификации: **2026-11-18**. Версия: **v2**.
Статус: **нормативный план**. Этапы 0–3 **accepted**. Миграция `1.6.1` принята на проверенном `622b0fb`. По решению владельца проверка выбора между двумя релизными SDK 11 отложена до выхода следующего SDK. 2026-11-24 владелец согласовал перенос проверенного кандидата `1.6.3` в `main` одним squash-коммитом. [Отчёт итоговой приёмки](evidence/task-03-packaging-and-acceptance.md).
2026-11-24 ветка миграции обновлена из `origin/main` (`701512d`). Кандидат `1.6.3` с точным SDK `11.0.100` прошёл все девять нативных Release-заданий, smoke шести архивов и Windows-матрицу MCP с SDK 9/10/11. AR-18 закрыт отдельным изменением в `main`, описания исходной точки ниже относятся к состоянию до него. [Повторная проверка](evidence/task-03-packaging-and-acceptance.md#проверка-после-обновления-из-main-2026-11-24).

Принятый scope этапа 0 — зависимости, SDK-матрица штатного BuildHost,
ранняя отмена, исходные проверки, публичный B0 и rollback. В отчёте также зафиксирован Windows x64
прогон `net9.0` с точным SDK `9.0.314`. [Отчёт](evidence/task-00-baseline-and-dependencies.md).

Владелец согласовал ранний перенос runtime после того, как сервер на .NET 10 воспроизвёл
отказ с SDK `11.0.100` при использовании MSBuild внутри процесса. Release-проверки этапов 1 и 2
прошли на совмещённом кандидате B2, B1 сохраняется отдельным контролем.
[Воспроизведение и validation](evidence/task-01-roslyn-and-buildhost.md).

Этот каталог самостоятелен. Чтобы исполнить план, достаточно файлов в этом каталоге.
Текст v1, ревью, защита и арбитраж лежат в [`_archive/`](_archive/README.md) и для нормативного поведения не требуются.
Подготовка документов v1 завершена 2026-11-18; этот проход её не повторяет и не объявляет
её evidence этапов. Отчёт подготовки не описывает pin и матрицу v2.

Продуктовый baseline: ветка `dotnet11-migration-plan`, от `origin/main` на
`6f84e897e059cd87b9cc8946212154291e0e2ed7` (сервер 1.5.25).
Основная ветка репозитория называется `main`, а не `master`.
Ветка создана в чистом worktree; незакоммиченная работа исходного checkout в неё не перенесена.
Факты корневого pin и состава кандидата 5.12.0 ниже зафиксированы по дереву `109560b`.
Это не новый продуктовый baseline и не пройденный этап 0.

Отдельные requirement ID владельцем не назначались. Нормативная цель — постепенный переход,
записанный в этом файле.

## Состав каталога

- [Этап 0 — версии, зависимости и baseline](task-00-baseline-and-dependencies-med.md)
- [Этап 1 — Roslyn и форк BuildHost](task-01-roslyn-and-buildhost-hi.md)
- [Этап 2 — runtime .NET 11 и C# 15](task-02-runtime-and-language-hi.md)
- [Этап 3 — поставки и итоговая приёмка](task-03-packaging-and-acceptance-hi.md)
- [Журнал отличий от v1](CHANGELOG-v2.md)
- [Трассировка](TRACEABILITY-v2.md)
- [Нерешённый gate](UNRESOLVED-v2.md)
- [Вопросы после интеграции](POST-ARBITRATION-ISSUES.md)

## Цель и не-цели

Сервер должен корректно анализировать проекты .NET 11 / C# 15, сохранить проверенные
сценарии прежних SDK и оптимизации BuildHost, затем перейти собственным runtime на .NET 11.
Поддержка языка анализируемого проекта и язык исходников сервера проверяются отдельно.
Обязательны validation, матрица совместимости, замеры, поставки и откат.

В переход не входят: новый загрузчик, новая система кеша, Native AOT, single-file,
новые RID, массовое переписывание кода на C# 15, повторная настройка worker/GC,
новая production-активация кеша workspace, вторая параллельная поставка Roslyn/host
и новая система согласования SDK. Форматы MCP, конфиг сервера, write boundary,
analyzer admission и политика watcher сохраняются, кроме точечных исправлений
совместимости с собственным regression test. Правила выбора SDK самим сервером
этим переходом не расширяются: чтение `rollForward` не реализуется, AR-18 остаётся открытым.
Меняется pin собственной сборки репозитория, как задано этапом 2.

## Цель и порядок

1. [Этап 0 — версии, зависимости и baseline](task-00-baseline-and-dependencies-med.md):
   подтвердить состав нового пакета, совместимость процессов и воспроизводимый контроль.
   Код продукта и версии пакетов в дереве на этом этапе не меняются.
2. [Этап 1 — Roslyn и форк BuildHost](task-01-roslyn-and-buildhost-hi.md):
   обновить согласованный набор Roslyn, перенести форк, проверить RPC и пользовательские проекты.
   Собственный TFM сервера пока `net10.0`.
3. [Этап 2 — runtime .NET 11 и C# 15](task-02-runtime-and-language-hi.md):
   согласованно обновить сервер, тестовые host и CI, повторить проверки и замеры.
4. [Этап 3 — поставки и итоговая приёмка](task-03-packaging-and-acceptance-hi.md):
   нативно проверить шесть архивов, dotnet tool, требования к SDK/runtime и обратный переход.

Этапы 1–3 не начинаются, пока gate этапа 0 не пройден. Они не получают права сузить
матрицу потребительских SDK. Если ранее положительная строка не проходит, эти этапы
начинаются только после явного сужения, названного владельцем и записанного в отчёте
этапа 0. Отказ владельца — no-go обновления: этапы 1–3 не начинаются.
Исполнитель полосу не выбирает. Альтернативы — в [нерешённом gate](UNRESOLVED-v2.md).

Отчёт подготовки и отчёт этапа 0 не являются evidence прохождения этапов 1–3.
Промежуточный успех не меняет public defaults и не означает приёмку всей миграции.
Слияние, push, tag и публикация релиза не выполняются как часть подготовки или
как замена gate.

## Проверенная отправная точка

По исходникам базового commit:

- Сервер, основной тестовый проект, LifecycleTestHost, DeadlockWitnessHost и sample plugin
  используют `net10.0`. Явного `LangVersion` у сервера нет: его штатный язык — C# 14.
- Roslyn — `5.9.0`; compile-time ссылки Microsoft.Build и Microsoft.NET.StringTools —
  `18.6.3`; Microsoft.Build.Locator сервера — `1.11.2`.
- [Форк](../../forks/roslyn-buildhost/README.md) взят из
  `35d9211b841e7613c1d2f8f5af6d628ace696c4c`.
  Его netcore-host — framework-dependent `net8.0`, `RollForward=Major`;
  `MsbuildReferenceVersion` — `17.11.48`, Locator — `1.10.2`.
  Reference-профиль форка — PackageDownload пакетов Microsoft.Build,
  Microsoft.Build.Framework, Microsoft.Build.Tasks.Core и Microsoft.Build.Utilities.Core
  этой версии и четыре `Reference` на их `ref\net8.0`.
  [Guard](../../forks/roslyn-buildhost/build/BuildHostFork.targets) требует ровно Roslyn `5.9.0`.
- В сервере включены A (references first), B (evaluation ahead, четыре worker) и C
  (общий SDK resolution cache на проект). Повторная настройка worker/GC не входит в переход.
- SQLite3MC.PCLRaw.bundle `2.3.5` выбран под конкретную assembly identity Roslyn.
  Совпадение проверяет `RoslynIndexStorageTests`; NuGet dependency graph сам по себе недостаточен.
- [CI](../../.github/workflows/test-suite.yml) сейчас использует канал SDK `10.0.x`.
  [Release](../../.github/workflows/release.yml) сейчас закрепляет `10.0.402` и
  `rollForward` `disable`, перезаписывая `global.json` checkout релиза.
- Корневой `global.json` есть с commit `59f6936`, предка базы `6f84e897`:
  `version` `10.0.100`, `rollForward` `latestFeature`, `allowPrerelease` false.
  `latestFeature` выбирает наивысшие feature band и patch SDK 10.0 не ниже `10.0.100`
  и на SDK 11 не переходит. Пока такой SDK 10 установлен, этот файл не собирает проект `net11.0`.
- `GlobalJsonSdkReader` не читает `rollForward` и `allowPrerelease`. При существующей папке
  названной версии возвращает её. Префикс отката для `10.0.100` — `10.0.100.`;
  папки `10.0.402` и `11.0.100` под него не подходят. При установленной папке `10.0.100`
  reader держит её, а CLI с `latestFeature` может уйти на более высокий band `10.0`.
  Reader не исполняет `latestFeature`.
- `test-suite.yml` ставит `10.0.x` без `global-json-file`; выбор CLI всё равно делает корневой файл.
- `docs/ARCHITECTURE.md` всё ещё утверждает, что корневого `global.json` нет.
  Это устаревшая фраза. Её исправляет этап 3. AR-18 остаётся открытым:
  сервер не исполняет политику `rollForward` CLI.
- Реальные release RID: `win-x64`, `linux-x64`, `osx-arm64`, каждый в двух вариантах.
  Ещё три RID в csproj не означают проверенной платформенной поддержки.
- Нативный CI исходников 1.5.25 пройден. После фиксации статуса в
  [теме релизов](../github-releases/README.md) выгружен [официальный run 44044228033](https://github.com/VuDZ/RoslynMcpServer/actions/runs/44044228033)
  с успешным нативным smoke обоих архивов macOS ARM64 для исходной поставки `v1.5.25`.

Кандидат обновления — Roslyn `5.12.0`, опубликованный 2026-11-13.
По nuspec `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.12.0:

- библиотека `net10.0` и отдельная группа зависимостей `net472`;
- зависимость `Microsoft.Build.Framework` `18.7.1` — это нижняя граница клиента, не reference-пакет форка;
- source commit `1324e1d34a0490454837a22370e75bbaeecffb87`.

В этом commit проект BuildHost задаёт `_MsbuildVersion` `17.11.48`.
Поставляемый `BuildHost-netcore.runtimeconfig.json`: `tfm` `net8.0`, framework `8.0.0`,
`rollForward` `Major`. У пакетов `Microsoft.Build` 18.7.1 и `Microsoft.Build.Framework` 18.7.1
каталога `ref/net8.0` нет. Перевод PackageDownload форка на 18.7.1 из-за зависимости клиента
запрещён: host `net8.0` такой ссылкой не собирается. Из `runtimeconfig` не выводится,
что дочернему host нужен shared runtime 11. Фактический framework процесса показывает smoke.

Эти отправные данные подтверждены скачанным пакетом. Загрузка, RPC и семантические
операции на точных полосах SDK зафиксированы в [отчёте этапа 0](evidence/task-00-candidate-matrix.md).
Другой выбранный пакет или commit требует заново снять те же факты; расхождение
не даёт права пропустить прогоны. Сведения страницы пакета остаются указателем
на кандидат, а не заменой nuspec.

.NET 11 выпущен 2026-11-10. C# 15 штатно соответствует `net11.0`;
включать его для собственных исходников на `net10.0` не планируется.
.NET 10 LTS и .NET 11 STS поддерживаются до 2028-11-14: переход не увеличивает этот срок.
Поддержка полосы Microsoft не равна обещанию этого сервера.
Источники: [релиз .NET 11](https://devblogs.microsoft.com/dotnet/announcing-dotnet-11/),
[версии C#](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-versioning),
[поддержка .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[таблица SDK/MSBuild](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs).

## Границы изменения

Обновляются только зависимости и код, необходимые для совместимости, связанные тесты,
build/publish/pack, документация и evidence. Используются существующие стенды
`spikes/BuildHostFork`, CI и release smoke.

Runtime сервера, runtime отдельного BuildHost, выбранный MSBuild и SDK проекта —
четыре отдельные величины. Self-contained сервер не поставляет SDK и не делает
дочерний framework-dependent BuildHost self-contained. Их версии и пути фиксируются в отчётах.
BuildHost не переводится на `net11` вместе с сервером. Обязательная смена его TFM
допустима только как отдельно доказанный исход этапа 0, с причиной, сравнением
и повторением RPC/scenario gates.

Кеш workspace не получает разрешения на новую production-активацию.
Изменение публичных требований к runtime/ОС/CPU должно быть явно описано и проверено.

Дополнительно для этой ревизии:

- Корневой `global.json` на этапе 2 обновляется на один точный стабильный SDK 11
  с `rollForward` `disable` и `allowPrerelease` false. Тот же номер получают CI и release.
  Пользовательские `global.json` вне репозитория не переписываются.
- Минимальная полоса потребительского SDK этим планом не назначается.
  `10.0.4xx` не является новым минимумом и не записывается как уже выбранный кандидат минимума.
  `PinnedSdk.Version` `10.0.300` не меняется ради зелёного прогона.
- Зависимость клиента от `Microsoft.Build.Framework` 18.7.1 не объявляется доказанным
  отказом runtime на MSBuild 18.0 или 18.6 и не переносит номер 18.7.1 на reference-пакет форка.
- Окружение дочернего `dotnet` может получить opt-out MSBuild Server по правилу матрицы.
  Окружение машины не меняется. `UnregisteredBuildHostReaper` не расширяется на
  произвольные процессы `dotnet`, MSBuild Server и worker nodes.
  `dotnet build-server shutdown` не входит в поставку и в обычную работу сервера.

## Нерешённый gate

Минимальная поддерживаемая полоса потребительского SDK не выбрана.
Отдельный ID этому решению не назначался. Полный текст, невыбранные альтернативы
и поведение, обязательное при любом будущем выборе, — в [UNRESOLVED-v2.md](UNRESOLVED-v2.md).

Прогон этапа 0 подтвердил точные полосы SDK со штатным BuildHost. При отказе ранее положительной
строки сужение матрицы на этапах 1–3 требует явного решения владельца. Зелёный gate нельзя получить сменой RPC guard,
скрытым требованием runtime, тихим отключением старого сценария, прогоном только
на `10.0.402` или формулировкой «поддерживается SDK 10.0.4xx и новее».

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
  Общий `go`, feature flag, результат одной фикстуры и checkpoint без этого evidence
  gate не закрывают.
- Перед приёмкой каждого code-этапа и финального результата — Release build и весь main suite
  на Windows x64, Linux x64 и macOS ARM64, плюс lifecycle на каждой платформе.
  При недоступной платформе её gate остаётся pending; кросс-публикация не заменяет native run.
- `RequiresWindows` исключается только на Linux/macOS. `RequiresPinnedSdk` остаётся исключённым
  из main; не менять его fixtures и SDK pins ради зелёного прогона. Константа `10.0.300`
  сохраняется. Снятие потребительской поддержки и переписывание исторической фикстуры —
  разные действия. Фикстуры `RequiresPinnedSdk` не становятся обязательным положительным
  public-support gate. Если затронут их общий runner или контракт, отдельно выполнить
  затронутые тесты с требуемым точным SDK (текущий inventory включает `10.0.300`)
  и записать реальный прогон, pending или отказ, без подмены ожиданий.
  Отказ фикстуры остаётся отказом или pending даже если владелец позже сузит публичное обещание.
- После исправления — affected tests для обратной связи, затем полный main suite
  на финальном коде перед commit/приёмкой по [AGENTS.md](../../AGENTS.md).
  Нужные lifecycle/платформенные проверки повторяются после влияющих на них изменений.
- Сужение матрицы, смена фильтра и исключение упавшей ранее положительной строки
  не являются способом пройти эту validation.

## Матрица совместимости

На этапах 1 и 2 проверяются следующие случаи; для каждого указать поддерживаемый
результат или воспроизводимый отказ с причиной. Раньше работавший положительный
сценарий нельзя превратить в отрицательный внутри зелёного gate.
Такое изменение — только решение владельца по [нерешённому gate](UNRESOLVED-v2.md).
До этого решения этапы 1–3 не продолжаются.

### Изоляция pin

Для потребительской строки ближайший `global.json` к фактическому рабочему каталогу CLI
и к каталогу, от которого сервер ищет pin, — файл этого случая.
Корневой pin репозитория такой строкой не является. Команда, запущенная из корня,
не доказывает строку, чей `global.json` лежит ниже корня: muxer CLI начинает поиск
с рабочего каталога и к каталогу проекта не спускается. Сервер задаёт рабочий каталог
дочернего `dotnet` от каталога проекта вверх до ближайшего предка с `global.json`
и читает тот же ближайший файл.

В отчёт строки входят путь `dotnet`, версия SDK и путь MSBuild именно этого запуска.
`dotnet --version` в корне репозитория строку не закрывает.
Для `9.0.314`, `10.0.100`, `10.0.300` и `10.0.402` нужны отдельные прогоны.

`RawImportEvidenceFixture`, копирующая корневой `global.json`, после смены pin остаётся
слепком toolchain репозитория. Её ожидания обновляет этап 2 явно. Потребительские строки
SDK 9 и SDK 10 проверяются собственными фикстурами. Пользовательские файлы вне репозитория
не переписываются. Реализация политик `rollForward` в миграцию не входит.

Проверяются выбор executable, architecture, cwd и отсутствие зависимости результата
от SDK, которым собран сервер. Для уже поддерживаемых вариантов `rollForward`
записать, совпадает ли выбор reader с выбором CLI: это результат случая либо
воспроизводимый отказ. Недостающие политики в этом переходе не реализуются.
Расхождение reader с CLI не поручает миграции закрыть AR-18.

### Случаи SDK

У каждого случая записать номер или feature band, `rollForward`, фактические версию
и путь MSBuild, ближайший `global.json`, рабочий каталог процесса и результат.
Обязательные несводимые случаи:

- Проект `net9.0` с C# 13 по умолчанию и точным SDK `9.0.314`. Собственный
  ближайший `global.json` задаёт `rollForward=disable` и `allowPrerelease=false`.
  CLI и BuildHost должны выбрать `9.0.314` в изолированном корне потребительского SDK.
  Проверить сборку, загрузку, diagnostics, definition, references и rename с
  перекомпиляцией. Runtime сервера и BuildHost записать отдельно. На Windows x64
  в этапе 0 фактически использован MSBuild `17.14.43`. [Результат этапа 0](evidence/task-00-baseline-and-dependencies.md#net90-с-точным-sdk-90314).
- `10.0.1xx`, точный представитель `10.0.100` (MSBuild 18.0 по таблице SDK;
  полоса поддерживается Microsoft до ноября 2028). Строка засчитывается только если
  фактический SDK запуска — этот представитель, а не более высокий band.
- Точный `10.0.300`, `rollForward` `disable` (MSBuild 18.6; фикстура `RequiresPinnedSdk`;
  срок полосы `10.0.3xx` у Microsoft закончился в августе 2026). Это результат
  исторической фикстуры, а не само по себе публичное обещание поддержки.
- `10.0.4xx`, точный представитель `10.0.402` (MSBuild 18.9; поддержка Microsoft
  до ноября 2028). Эта строка не назначает минимум.
- Точный SDK 11, выбранный для репозитория (`11.0.1xx` — MSBuild 18.12).
  Это pin сборки репозитория, не замена потребительских строк.
- SDK 10 и SDK 11 установлены рядом; только SDK 10; только SDK 11; отсутствует именно
  закреплённый SDK; SDK нет вообще. «Только SDK 10» и «обе версии установлены»
  не склеивают три полосы SDK 10: у запуска всё равно записаны band, ближайший файл
  и фактический MSBuild. «Только SDK 10» — среда уже собранного сервера и потребительского
  проекта, а не требование собрать репозиторий с pin SDK 11 без установленного SDK 11.

В каждом случае сохранить прежние проверки: точный pin, установленные рядом версии,
отсутствующий pinned SDK и отсутствующий SDK вообще.

### Поставки runtime и проекты

- Server SC и FDD: SC использует встроенный runtime, BuildHost — установленный.
  Для FDD после этапа 2 обязателен shared runtime 11 сервера; отдельно воспроизводимый
  отказ без него. Старые SDK/workloads не должны требовать установки SDK 11 только
  из-за TFM сервера. Требование shared runtime 11 для дочернего host допустимо только
  как результат наблюдаемого процесса. Пока процесс этого не показал, сценарий SC
  с SDK 10 и без shared runtime 11 нельзя объявить сломанным и нельзя объявить доказанным.
  Для строки `net9.0` финальный smoke включает SC с одним SDK `9.0.314` и его
  shared runtime 9, FDD с тем же SDK и отдельно установленным shared runtime 11,
  а также ожидаемый отказ запуска FDD без shared runtime 11. [Сценарии этапа 3](task-03-packaging-and-acceptance-hi.md#native-smoke).
- `.csproj`, `.sln`, `.slnx`, `.slnf`; multi-TFM, linked/shared sources,
  conditional project references, Directory.Build.props/targets, editorconfig,
  additional files и сгенерированные документы.
- Прежние C# 14/net10 fixtures и репрезентативный net11/C# 15 проект:
  union types, closed hierarchies, collection expression arguments, extension indexers,
  labeled break/continue. Проверять parse, binding, diagnostics, navigation,
  references, rename и применимые code actions с компиляцией результата.
  Memory-safety preview не входит в обещание стабильной поддержки.
- Отдельная библиотека, не запускаемый runtime: TFM `netstandard2.0`; явный
  `LangVersion` `15.0`, без `latest` и `preview`; невиртуальный статический метод
  интерфейса с телом и вызов этого метода на типе интерфейса.
  Ожидание на языке 15: нет `CS8701`, символ привязан к этому члену, definition
  и references доступны, effective language version — `15.0` и не подменён default TFM.
  SDK 11 собирает библиотеку. Сервер анализирует её своим Roslyn с тем же ожиданием.
  Тот же исходник и те же references с языком 14 воспроизводят прежнее ограничение.
  Запуск `net472` на Linux и macOS не требуется. Пять конструкций на `net11` эту
  проверку не заменяют. Случай не обещает instance default implementation,
  `static abstract`, `static virtual` и protected-члены интерфейса.
  Это фикстура пользовательского проекта: она не поднимает `LangVersion` исходников
  сервера раньше этапа 2.
- Analyzer/source generator с обычным поддерживаемым TFM и с зависимостью от нового Roslyn;
  generated symbol доступен сразу после publication, затем после rebuild и reload.
  Сборки analyzer/generator на `net11.0` проверяются отдельно: новый parser на runtime 10
  сам по себе не делает их исполнимыми. Не обещать поддержку вне analyzer admission policy.
- Изменение/удаление исходника, import, editorconfig и generator на диске:
  watcher, повторная публикация и поиск не возвращают старые результаты.
  Проверить cold/warm SQLite index и изолированный snapshot/codec roundtrip,
  идентичность parse options и безопасный отказ при несовместимой версии кеша.

### Процессы

Отмена до старта host, во время initialization/evaluation/build, kill/crash host,
повторный load после отказа, reload/dispose/server exit.
Нет зависания, частичной публикации, потерянных diagnostics и оставшихся собственных BuildHost;
host другой сессии/сервера не завершается.

Отдельно наблюдать MSBuild Server. Это наблюдение не заменяет правила BuildHost
и чужих сессий.

- Workspace BuildHost узнаётся по сборке `Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost`.
  Worker node reuse — `/nodemode:1`. MSBuild Server — процесс `dotnet` с `MSBuild.dll`
  и `/nodemode:8`.
- После build/test, отмены, timeout, неуспешной сборки и shutdown записать, был ли
  сервер создан, переиспользован или не запускался: PID, командная строка, время старта.
  Учитывать окружение, унаследованное дочерним процессом. SDK 11 больше не записывает
  `MSBUILDUSESERVER=0`, когда `DOTNET_CLI_USE_MSBUILD_SERVER` не задан.
- Ноль процессов BuildHost не является PASS этого наблюдения. Непроверенный результат
  не является PASS.
- Сначала выполняется прогон текущего runner без нового opt-out.
  `MSBUILDDISABLENODEREUSE=1` страницы нового SDK и страница MSBuild Server не называют
  opt-out нового сервера. Эквивалентность этой переменной ключу `/nr:false` на SDK 11
  не доказана заранее, и план не утверждает, что текущий runner уже оставляет сервер
  или блокирует следующий publish.
- Пока прогон на SDK 11 не показал, что уже стоящий `MSBUILDDISABLENODEREUSE=1`
  не оставляет постоянный сервер, дочерние команды CLI получают
  `DOTNET_CLI_USE_MSBUILD_SERVER=false` и только его. Если унаследованный
  `MSBUILDUSESERVER` при этой переменной всё ещё поднимает сервер, тому же дочернему
  процессу ставится и `MSBUILDUSESERVER=0`. Окружение машины не изменять.
  Если прогон доказал достаточность текущего флага, второй механизм не добавлять.
- `/nr:false` не добавляется вместе с переменными. Он допустим только если прогон
  показал, что названные переменные сервер не гасят, а этот ключ гасит.
- `dotnet build-server shutdown` не вызывать в поставке и не считать доказательством.
  В изолированной тестовой среде команда допустима только после записи наблюдения
  и не превращает неуспех в PASS. Она не ограничена деревом собственного процесса.
- Источники поведения — разделы «MSBuild server enabled by default» и
  «MSBuild server and OpenTelemetry environment variables» страницы
  [what's new in the SDK](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/sdk)
  и страница [MSBuild Server](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-server).
  Список [breaking changes .NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11)
  этого пункта не содержит; одной ссылки на него недостаточно.
  Раздел про OpenTelemetry читается как источник правила переменных окружения,
  а не как поручение добавить OpenTelemetry.

На Windows — WPF temporary projects и доступный net472 fallback.
Чужие языки, отсутствующие проекты, XML errors и missing SDK сохраняют понятную диагностику.
Строка net472 про WPF и fallback не заменяет библиотеку `netstandard2.0` языковой матрицы.

## Сравнения и производительность

Не смешивать обновление compiler, host и runtime в одном неразделимом замере:

1. B0: сервер net10 + Roslyn 5.9 + старый форк A+B+C (исходный контроль).
2. B1: сервер net10 + новый Roslyn + новый форк A+B+C.
3. B2: сервер net11 + тот же новый Roslyn и форк с теми же A+B+C.
4. Внутри новой версии: stock host нового пакета, новый форк без A/B/C и с A+B+C.
   У этих сторон один и тот же новый MSBuildWorkspace client.
   Если TFM/runtime host пришлось изменить, выделить это отдельной парой и зафиксировать причину.

B0, B1, B2 и серии stock/fork сравниваются в одном записанном режиме MSBuild Server.
Тёплый сервер не относить к эффекту смены runtime или форка. Смена opt-out между сериями
требует повторного снятия сравниваемых серий.

Старый/новый Roslyn сравниваются на прежних проектах; новые языковые fixtures —
между пригодными для них сторонами. Полное RPC-сравнение нового форка со stock host
той же версии обязательно. Для межверсионного сравнения объясняются по одному
изменения диагностик и API; их нельзя скрыть общей нормализацией.
Отсутствие `CS8701` у библиотеки `netstandard2.0` на языке 15 — ожидание этого случая;
прочие расхождения диагностик, если появятся, перечисляются по одному.

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
Неуспех не маскировать изменением GC, workers, отключением analyzers, урезанием результата
или сменой режима MSBuild Server внутри сравниваемой пары.
Совместимость может оправдать переход без ускорения; превышение порога требует
исправления либо отдельного явно записанного решения владельца с измерениями.

## Откат и ведение статуса

Каждый принятый code-этап — отдельный согласованный commit/checkpoint с evidence.
До него сохранить SHA контроля; после него обновить файл этапа, этот README и
[индекс docs](../README.md): дата, статус, принятый scope, validation, ограничения.

Откат Roslyn выполняется вместе с форком, Contracts, version guard и связанными package pins.
Возврат одного host DLL или ослабление guard не допускается.
Откат runtime возвращает вместе TFM зависимых проектов, CI/release SDK, корневой pin
и publish paths. Rollback поддерживается предыдущим проверенным бинарным архивом;
предыдущие assets не перезаписываются.

`ROSLYN_MCP_BUILDHOST_OPTIMIZATIONS=0` — диагностика/временное выключение A/B/C,
а не откат Roslyn, RPC или runtime и не PASS наблюдений MSBuild Server.
Opt-out дочернего CLI — не откат Roslyn и не замена gate.
Пользовательские данные, конфиг, логи и чужие кеши при откате сохраняются;
новый кеш должен иметь изолированный ключ или безопасно отвергаться старой версией.
Проверить реальный запуск предыдущего бинарника после нового.

## Полномочия

- Владелец решает, сузить ли явно названную минимальную полосу потребительского SDK,
  если ранее положительная строка не прошла, либо остановить обновление (no-go).
  Исполнитель полосу не выбирает. До прогона такое решение не принимается.
- Закрытие AR-18 и реализация нестрогих политик `rollForward` в полномочия этого
  перехода не входят.
- Приёмка этапа — только его собственный gate с TRX, командами, средой и, где задано,
  native smoke. Публикация релиза — отдельное действие после готовности этапа 3,
  по существующему порядку релизов и указанию пользователя.

## Ревью спецификации

- Автор исходного плана v1: gpt sol 6.1.
- Независимое ревью: Grok 4.7. Дата: 2026-11-18. Раундов: 1.
- Замечания ревью: 4. Blocker: 2 (R-01, R-02). High: 1 (G-01). Medium: 1 (R-03).
- Защита: 2026-11-18, 1 раунд. Модель в каталоге защиты не названа. ACCEPT: 2 (R-01, R-03). PARTIALLY ACCEPT: 2 (R-02, G-01). REJECT: 0.
- Арбитраж: 2026-11-18, 1 раунд. Модель в каталоге арбитража не названа. ACCEPT: R-01, R-03, NEW-ARB-001. ACCEPT WITH MODIFICATION: R-02, G-01. REJECT: 0. Finding со статусом UNRESOLVED нет.
- Интеграция v2: 2026-11-18. Модель в редакции не названа. Новых вопросов после арбитража нет.
- Исправления текста v1 по карточкам: 0. Нормативный текст — эта редакция.
- Нерешённое продуктовое решение: минимальная полоса потребительского SDK. Исполнитель её не выбирает.
- Отчёты: [ревью](_archive/review/README.md), [защита](_archive/response/README.md), [арбитраж](_archive/arbitration/arbitration-summary.md).

## Приёмка реализации

- Модели, проводившие ревью: сабагент Codex, точный идентификатор не зафиксирован.
- Даты ревью и приёмки этапа 0: 2026-11-18.
- Количество раундов ревью: 1.
- Количество исправлений после ревью: 4 уточнения evidence.
- Итог на дату приёмки этапа 0: этап 0 accepted, этапы 1–3 planned.
- Принятый scope: пакетная сверка Roslyn 5.12.0, точная SDK-матрица штатного BuildHost, ранняя отмена, исходные проверки, публичный B0 и rollback.
- Validation и оставшиеся ограничения: свежие Windows x64 и Linux x64 прогоны, нативное macOS ARM64 evidence точного продуктового дерева из Release CI. Офисный B0 pending; минимум SDK остаётся открытым.
- Отчёт: [этап 0, Windows/Linux/macOS baseline, матрица, B0 и rollback](evidence/task-00-baseline-and-dependencies.md).

- Приёмка этапов 1 и 2 — 2026-11-20. Приняты Roslyn `5.12.0`, форк BuildHost, runtime .NET 11, C# 15 и pin `11.0.100`. Все девять нативных Release-заданий B2 прошли без пропусков. Регрессионная проверка с двумя SDK 11 остаётся `pending` по решению владельца. [Отчёт](evidence/task-01-roslyn-and-buildhost.md).
- Приёмка этапа 3 — 2026-11-20, `622b0fb`. Проверены шесть архивов `1.6.1`, `dotnet tool` на трёх платформах, изолированные SDK, lifecycle, откат и публичные контроли производительности. [Отчёт](evidence/task-03-packaging-and-acceptance.md).
