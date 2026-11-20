# Дефекты, найденные при подготовке README на Jellyfin

Пакет перенесён в архив 2026-11-21. [Статус проверки на нативном Linux](linux-recheck.md#проверка-на-ad7ebb3).

2026-11-20. **Исправления приняты.** Закрыты D-01…D-06 и D-08, а для D-07 устранено ложное сообщение
о несовпадении SDK. Release-сборки и основные CI-сюиты прошли на Windows и Linux.
[Отчёт реализации и приёмки](implementation.md), [план исправлений](plan.md), [исходная проверка](validation.md).

[Повторная проверка на нативном Linux](linux-recheck.md): 2026-11-21 на `ad7ebb3` основная Release-сюита прошла 2688/2688,
`SourceStructure` 128/128, `AnalyzerLifecycle` 13/13. На Jellyfin подтверждены D-01…D-06, D-08 и исправление N-01,
в том числе D-02 сравнением `sed -i` с обычной записью и прогоном тестов.

2026-11-21. **Исправление N-01 принято.** Полная пересборка Jellyfin без правок оставляет план TIA пустым,
а правка одного метода остаётся видимой. Прошли независимое ревью и Release-сюиты на Windows и Linux.
[Разбор, исправление и итоговая проверка N-01](n01-investigation.md#проверка-исправления).

Ниже сохранены исходные наблюдения на Jellyfin.

## Условия

- Сервер: релиз **v1.6.0**, архив `RoslynMcpServer-1.6.0-linux-x64-self-contained.tar.gz` с GitHub Releases.
  Вызовы шли через простой stdio MCP-клиент (`initialize` → `tools/call`), `ROSLYN_MCP_WORKSPACE` — корень Jellyfin,
  `ROSLYN_MCP_HOME` — отдельный каталог.
- Решение: shallow clone `jellyfin/jellyfin` master `0503e92` (2026-11-20), `Jellyfin.slnx`, 41 проект,
  после `dotnet restore`.
- SDK: системный 10.0.114, затем 10.0.404 из `~/.dotnet-tia` (см. D-07).
- Машина двухъядерная Linux x64. Тайминги здесь не приводятся как характеристика производительности.

Сырые ответы инструментов не сохранены в репозитории; ниже — дословные фрагменты.

## Сводка

| ID | Инструмент | Что не так | Предварительная оценка |
|---|---|---|---|
| D-01 | `estimate_affected_tests` | Правка сразу после загрузки: 661 «изменённый» файл, план на 17 проектов, ответ 457 КБ | Высокая: неверный ответ и переполнение контекста |
| D-02 | `estimate_affected_tests` | Атомарное сохранение через временный файл (`sed -i`) делает граф проектов stale и расширяет план до целых проектов | Средняя: зависит от редактора агента |
| D-03 | `find_implementations` | Обобщённый и необобщённый интерфейс с одним именем выводятся под одинаковыми заголовками, списки дублируются и суммируются | Средняя |
| D-04 | `get_decompiled_class_skeleton` | В сигнатурах видны токены метаданных и маркеры nullability (`02000021 TMDbLib.Rest.RestClient!`) | Средняя: шум в контексте агента |
| D-05 | `run_specific_test` | Список «Matched tests» склеен с перепутанными кавычками | Низкая |
| D-06 | документация `get_diagnostics_for_file` | Справочник обещает id вида `IDE0001`, а инструмент возвращает только диагностику компилятора | Низкая, только документация |
| D-07 | `load_workspace` / сборка | Не дефект сервера: загрузка проходит, а сборка падает на старом SDK; отметить для README и диагностики | Информация |
| D-08 | `run_dotnet_build` | Жёсткий таймаут 180 с на шаг без параметра: полная сборка Jellyfin на медленной машине убита | Средняя: на больших решениях инструмент неприменим, агент уходит в shell |

## D-01. Оценка затронутых тестов сразу после загрузки

Шаги: `load_workspace` на `Jellyfin.slnx` (решение заранее собрано), затем сразу, без паузы, правка одной строки
в `Emby.Naming/TV/SeasonPathParser.cs` через `sed -i` и `estimate_affected_tests` с `run: true`.

Фрагмент ответа:

```text
Changed since load: 5444 members in 661 files. 0 changed types identified in assemblies compared after the builds of this run. Directly selected: 1660 tests. Run plan: 17 projects.
Run: `.../tests/Jellyfin.Api.Tests/Jellyfin.Api.Tests.csproj` whole project.
Reasons: New test or unknown baseline: .../tests/Jellyfin.Api.Tests/Auth/CustomAuthenticationHandlerTests.cs; New test or unknown baseline: ...
```

Размер ответа — 457 067 байт, в основном строки `Reasons:` с перечислением файлов. Тот же сценарий с паузой
90 секунд между загрузкой и правкой дал `Changed since load: 1 members in 2 files` и ответ около 10 КБ.

Вероятная причина: захват baseline идёт в фоне после публикации, и незаконченный хвост становится «unknown»
(см. [архитектуру](../../ARCHITECTURE.md#rapid-tia)). Вопросы для разбора:

- должен ли инструмент дождаться захвата baseline или явно сообщить, что он не закончен, вместо 661 «изменённого» файла;
- нужен ли предел размера ответа и сворачивание однотипных причин (`N files: New test or unknown baseline`).

Отдельно: в этом прогоне правка нарушала StyleCop (`SA1107`, у Jellyfin предупреждения — ошибки), поэтому
сборки упали. Это ошибка теста, а не сервера, но причины в плане от неё не зависят.

## D-02. Временный файл атомарного сохранения расширяет план

Шаги: загрузка, пауза 90 секунд, вставка строки через `sed -i` (создаёт временный файл в том же каталоге и
переименовывает его), `estimate_affected_tests`.

```text
Changed since load: 1 members in 2 files. ... Run plan: 14 projects.
Reasons: The project graph is stale; reload the workspace to narrow by assemblies.; Project expansion without a call graph: .../Emby.Naming/TV/SeasonPathParser.cs; ...
Not covered:
- Path outside the loaded input map: .../Emby.Naming/TV/sedapbB4N
- Project graph stale; reload required to refresh membership and project references.
```

Все 14 тестовых проектов пошли целиком; сужение по сборкам не применялось. `run_specific_test` в той же сессии
добавил заметку «A saved `.cs` file appeared or disappeared outside the loaded workspace snapshot», хотя `.cs`-файлов
не появлялось. Многие редакторы и инструменты сохраняют файлы так же. Вопрос: считать ли кратковременный файл без
расширения `.cs`, исчезнувший к моменту вызова, поводом для stale-графа.

## D-03. `find_implementations`: одинаковые заголовки для обобщённого и необобщённого интерфейса

Запрос: `find_implementations` с `symbolName: "IRemoteMetadataProvider"`. В Jellyfin есть
`IRemoteMetadataProvider` и `IRemoteMetadataProvider<TItemType, in TLookupInfoType>`.

```text
Found **2** matching base type(s), **24** type(s).

### `MediaBrowser.Controller.Providers.IRemoteMetadataProvider` (interface) — implementations (transitive)
1. `AudioDbAlbumProvider` — ...
...
12. `TmdbSeriesProvider` — ...
### `MediaBrowser.Controller.Providers.IRemoteMetadataProvider` (interface) — implementations (transitive)
1. `AudioDbAlbumProvider` — ...
```

Оба заголовка одинаковые: у обобщённого нет арности или параметров типа. Списки совпадают, и итог «24 type(s)»
считает одни и те же 12 классов дважды. Агент не может понять, какой раздел к какому интерфейсу относится.

## D-04. Скелет декомпилированного класса с токенами метаданных

Запрос: `get_decompiled_class_skeleton`, `assemblyName: "TMDbLib"`, `fullTypeName: "TMDbLib.Rest.RestRequest"`.

```text
public RestRequest(02000021 TMDbLib.Rest.RestClient! client, 0200008A System.String! endpoint);
public 0200040F System.Threading.Tasks.Task`1![[02000024 TMDbLib.Rest.RestResponse!]] Get(02000365 System.Threading.CancellationToken cancellationToken);
```

Перед каждым типом стоит токен метаданных, обобщённые типы выводятся в синтаксисе рефлексии (`Task`1![[...]]`),
к ссылочным типам добавлен `!`. Для `TMDbLib.Client.TMDbClient` скелет занял 53 654 символа. `get_decompiled_method_body`
для тех же типов выдаёт обычный C#, проблема только в скелете.

## D-05. `run_specific_test`: склеенный список тестов

Запрос: `run_specific_test`, проект `Jellyfin.Naming.Tests`, `className: "SeasonPathParserTests"`.

```text
Matched tests: `Jellyfin.Naming.Tests.TV.SeasonPathParserTests.GetSeasonNumberFromPathTest', 'Jellyfin.Naming.Tests.TV.SeasonPathParserTests.GetSeasonNumberFromPathTest', 'Jellyfin.Naming.Tests.TV.SeasonPathParserTests.GetSeasonNumberFromPathMixedLibraryTest`
```

Похоже на `string.Join("', '", ...)` внутри одной пары обратных кавычек. Повтор `GetSeasonNumberFromPathTest`
стоит проверить: это строки theory или дубликат.

## D-06. Справочник `get_diagnostics_for_file` обещает анализаторы

Справочник инструментов пишет, что вывод содержит id вида `CS0246`, `IDE0001`: [`docs/tools.md`](../../tools.md), строка 91, и
[`docs/tools.ru.md`](../../tools.ru.md), строка 89, на `main` с коммита `a8759d4`. До переноса справочника тот же текст стоял в
`README.md`, строки 383 и 1336. Инструмент вызывает `semanticModel.GetDiagnostics()` (`Tools/CodeAnalysisTools.cs`),
то есть возвращает только диагностику компилятора; диагностик `IDE*` и других анализаторов в ответе не будет.
Нужно либо поправить справочник, либо решить, должен ли инструмент запускать анализаторы.

## D-07. Загрузка проходит, сборка падает на старом SDK

Не дефект сервера, записано для полноты. С системным SDK 10.0.114 `load_workspace` загрузил все 41 проект,
а `run_dotnet_test` упал на сборке:

```text
error CS9057 CSC — Analyzer assembly '.../Jellyfin.CodeAnalysis.dll' cannot be used because it references version '5.9.0.0' of the compiler, which is newer than the currently running version '5.0.0.0'.
```

Ответ сервера при этом был хорошим: ошибка, контекст SDK, предупреждение о `global.json`. Семантическая загрузка
проходит, потому что сервер использует собственный Roslyn. Вопрос: стоит ли предупреждать уже при загрузке, что
компилятор SDK старше анализаторов решения. С SDK 10.0.404 сборка прошла, кроме двух тестовых проектов, которые
не компилируются в самом Jellyfin master (`CS7036`, `CS1061`).

## D-08. `run_dotnet_build` убивает долгую сборку, таймаут не настраивается

Шаги: после переименования из примера README (50 изменённых файлов) вызвать
`run_dotnet_build` с `workspacePath: "<jellyfin>/Jellyfin.slnx"` и `noIncremental: false`.

```text
## Build timed out

Exit code: `137`.
...
--- dotnet build -v:minimal (TIMED OUT after 180s) (exit 137, stdout 32737 chars, stderr 0 chars) ---
...
**MCP_DOTNET_TIMEOUT:** process exceeded the tool timeout and was killed (`Kill(entireProcessTree)`).
If the next `dotnet` call is slow: check zombie processes (`Get-Process dotnet`), delete locked `obj`/`bin` if needed, ...
```

Та же сборка из shell (`dotnet build Jellyfin.slnx`, инкрементально) завершилась и выдала только две ошибки,
которые уже есть в этом коммите Jellyfin. Машина двухъядерная, поэтому сборка шла дольше трёх минут; на более
быстрой машине тот же предел упрётся в решение побольше.

Причина в коде: `run_dotnet_build` (`Tools/BuildTools.cs`) вызывает `DotNetBuildProbe.RunAsync` без
`stepTimeout` и `overallBudget`, поэтому действуют значения по умолчанию из `Services/Build/DotNetBuildProbe.cs`:
`DefaultStepTimeout` 180 с на шаг и `DefaultOverallBudget` 300 с на весь вызов. Параметра таймаута у инструмента
нет, хотя у `run_dotnet_test`, `run_dotnet_run` и `estimate_affected_tests` он есть. По умолчанию инструмент ещё и
передаёт `--no-incremental`, что делает упор в предел вероятнее.

Последствия: агент не может собрать большое решение через MCP и, вопреки `AGENTS.md.sample`, переходит на shell
`dotnet build`. Дополнительно подсказка в ответе ориентирована на Windows (`Get-Process dotnet`), а запуск шёл
на Linux.

Вопросы для разбора:

- добавить `timeoutSeconds` в `run_dotnet_build`, согласованный с остальными инструментами сборки и тестов;
- пересмотреть значения по умолчанию или связать их с таймаутом MCP-клиента;
- сделать подсказку о зависших процессах зависимой от платформы.
