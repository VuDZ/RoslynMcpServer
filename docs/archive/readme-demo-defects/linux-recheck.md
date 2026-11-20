# Повторная проверка исправлений на нативном Linux

**Дата** — 2026-11-20. **Ревизия** — `958c3d0` (`fix: resolve README demo defects for 1.6.1`), сервер 1.6.1.0.
**Статус** — CI-сюиты пройдены, сквозная проверка на Jellyfin выполнена частично, найден дефект N-01,
появившийся до ветки. Оставшиеся шаги описаны в разделе [«Что осталось проверить»](#что-осталось-проверить).

Цель — независимо повторить приёмку из [отчёта реализации](implementation.md) на машине, отличной от Windows и WSL2.

## Условия

- Ubuntu 26.04.1 LTS, ядро 7.0.0-42, x64, 2 ядра, 3,8 ГиБ ОЗУ, без WSL.
- Сервер и тесты: SDK 10.0.114 (`/usr/lib/dotnet`), `TMPDIR=$HOME/tmp/testtmp`.
- Jellyfin `821b849e61f6bd0fa760c76de2dc1f39400abd44`, `Jellyfin.slnx`, 41 проект, SDK 10.0.404
  (`DOTNET_ROOT` и `PATH` указывают на отдельную установку; системный 10.0.114 даёт `CS9057`, см. D-07).
- Вызовы шли через [`mcp_calls.py`](../../readme-examples/mcp_calls.py) с шагами `!shell`, сервер запускался
  из `bin/Release/net10.0` этой ревизии. Сценарии лежат в [`recheck/`](recheck/).

## CI-сюиты, Release

| Проверка | Результат |
|---|---|
| `dotnet build RoslynMcpServer.sln -c Release` | 0 ошибок |
| `SourceStructure` | 128 из 128 |
| Основная сюита, `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows` | 2670 из 2670, 13 мин 57 с, пропусков нет |
| `AnalyzerLifecycle` с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | 13 из 13 |

Числа совпадают с Linux-прогоном в WSL2 из [отчёта реализации](implementation.md#проверка-release).

## Сквозная проверка на Jellyfin

| ID | Сценарий | Результат |
|---|---|---|
| D-01 | [b](recheck/b-d01-pending-baseline.json): правка через `sed -i` сразу после загрузки, `run: true` | **Подтверждено частично.** Ответ за 0,8 с: `Source baseline capture is in progress (2391 pending document(s))`, 294 байта, процессы не запускались. Повтор без `run` вернул тот же статус. Шаги после окончания захвата не выполнены |
| D-02 | [c](recheck/c-d02-atomic-save.json), контроль [d](recheck/d-d02-plain-write-control.json) | **Не выполнено**: прогон остановлен, проверку перенесли на более мощную машину |
| D-03 | [a](recheck/a-build-navigation.json): `find_implementations IRemoteMetadataProvider` | **Подтверждено.** Два заголовка: `…IRemoteMetadataProvider` и `…IRemoteMetadataProvider<TItemType, TLookupInfoType>`, итог `12 unique related type(s)`, 24 строки связей. Запрос `MediaBrowser.Controller.Providers.IRemoteMetadataProvider` вернул только необобщённый тип, 12 типов |
| D-04 | a: скелеты `RestRequest` и `TMDbClient` | **Подтверждено.** `public RestRequest(TMDbLib.Rest.RestClient client, string endpoint);`, `Task<TMDbLib.Rest.RestResponse> Get(...)`. Токенов метаданных и `!` нет, `?` на месте. `TMDbClient` занимает 39 469 байт вместо прежних 53 654 символов |
| D-05 | a: `run_specific_test SeasonPathParserTests` | **Подтверждено.** 79 из 79, превью: два отдельно размеченных имени методов |
| D-06 | Справочники | **Подтверждено.** `IDE0001` в `docs/tools*.md` нет, указан `CS0246` |
| D-07 | — | Покрыто юнит-тестами основной сюиты, отдельно не повторялось |
| D-08 | a: `run_dotnet_build` по `Jellyfin.slnx` | **Подтверждено.** Без параметра — `TIMED OUT after 180s`, exit 137, подсказка `ps -ef`. `timeoutSeconds: -1` отклонён до запуска. `noIncremental: false, timeoutSeconds: 1800` прошло за 183,4 с, дольше прежнего предела |

## N-01. Полная пересборка расширяет TIA до всех тестовых проектов

**Найдено при этой проверке, воспроизводится и на 1.6.0, исправлениями ветки не вносилось.**

Шаги ([e](recheck/e-n01-rebuild-and-reload.json) и пары прогонов на 1.6.0/1.6.1): загрузка, пауза до окончания
захвата baseline, `estimate_affected_tests` — `0 members in 0 files`, `Not covered: none`. Затем `run_dotnet_build`
по решению с настройками по умолчанию (`--no-incremental`) и снова `estimate_affected_tests` без правок исходников:

```text
Changed since load: 0 members in 41 files. ... Run plan: 17 projects.
Reasons: Non-C# input changed: .../Emby.Naming/obj/Debug/net10.0/Emby.Naming.GeneratedMSBuildEditorConfig.editorconfig; ...
Not covered:
- Project graph stale; reload required to refresh membership and project references.
```

`run_specific_test` после этого добавляет ложную заметку «A saved `.cs` file appeared or disappeared outside the
loaded workspace snapshot». Ответ 1.6.1 занял 14 107 байт, 1.6.0 — 46 263 байта; план одинаков. Инкрементальная
сборка (`noIncremental: false`) на обеих версиях даёт `0 members in 0 files`, заметки нет.

Сценарий `e` на 1.6.1 сузил условия и проверил восстановление:

- Хватает пересборки одного проекта из shell: `dotnet build tests/Jellyfin.Naming.Tests/... --no-incremental
  --no-dependencies` (5,6 с). После неё `0 members in 1 files`, `Jellyfin.Naming.Tests` идёт целиком с причиной
  `Non-C# input changed: .../Jellyfin.Naming.Tests.GeneratedMSBuildEditorConfig.editorconfig`, а в `Not covered`
  стоит `Project graph stale`.
- Повторный `load_workspace` того же решения снимает stale-граф, но расширение остаётся: baseline при перезагрузке
  сохраняется, и в `Not covered` добавляется постоянное ограничение `Reload/reopen observation window: non-C#
  changes may have been missed ... persists for this baseline`.
- Последующая инкрементальная сборка ничего не меняет. Расширение держится, пока жив baseline этого решения.
- Попутно: ответ с `includeDetails` занял 32 730 байт, это чуть меньше лимита 32 КиБ из D-01.

### Разбор

**Диск.** `--no-incremental` выполняет `Rebuild`: `Clean` удаляет, а сборка заново создаёт файлы в `obj`.
Пересборка одного `Emby.Naming` переписала пять файлов, в том числе `GeneratedMSBuildEditorConfig.editorconfig`,
и содержимое всех пяти совпало побайтно. После полной сборки решения заново созданы сгенерированные компилируемые
файлы: `*.AssemblyInfo.cs`, а в 17 тестовых проектах xUnit v3 — `XunitAutoGeneratedEntryPoint.cs` и
`SelfRegisteredExtensions.cs`, плюс content-файл NuGet `MediaBrowser.Model/obj/.../MimeTypes.Nullable.cs`.

**Код, расширение плана.** `.editorconfig` из `obj` входит в проект как analyzer config и получает
`InputRole.AnalyzerConfig` (`WorkspaceInputMapBuilder.RoleForDocument`). Для этой роли `NoticesFor` в
`WorkspaceDiskEventClassifier` считает событие ревизией входа, путь попадает в `BaselineSession.TouchedPaths`.
`TestImpactComposer.PathKind` относит любой изменённый не-`UserInput` путь к `NonCSharpInput`, а
`TestNarrowingPreconditions.Selection` оставляет такой проект целым. Содержимое не сравнивается: запись того же
текста равна реальному изменению.

**Код, stale-граф.** `WorkspaceDiskEventClassifier.ClassifyKnown` ставит `compositionStale`, когда известный путь
с ролью `UserInput` отсутствует на диске в момент события. Эвристика `LooksLikeUnconfirmedGeneratedArtifact`
узнаёт только `*.g.cs`, `*.g.i.cs`, `*AssemblyInfo.cs` и `*AssemblyAttributes.cs`. Файлы xUnit v3 и NuGet content
в `obj` она не узнаёт, поэтому, вероятно, остаются `UserInput`. Удаление при `Clean` помечает граф устаревшим, и
повторное появление файла этот признак не снимает. Роль этих файлов на Jellyfin напрямую не проверялась.

**Последствия.** Расширение не снимается перезагрузкой того же решения (см. сценарий `e` выше). Из инструментов сервера с `--no-incremental` по умолчанию работает только `run_dotnet_build`.
Сборки `run_dotnet_test` и `estimate_affected_tests` инкрементальны. При этом
[`AGENTS.md.sample`](../../../AGENTS.md.sample) рекомендует агенту `run_dotnet_build` и прямо называет
`--no-incremental` поведением по умолчанию. Обычный цикл «правка → `run_dotnet_build` → `estimate_affected_tests`»
поэтому теряет сужение по сборкам и выбирает все тестовые проекты.

### Вопросы для решения владельца

- Сравнивать содержимое не-C# входов с baseline (хотя бы analyzer config из `obj`) вместо учёта факта записи.
- Расширить распознавание сгенерированных файлов в `obj` или не считать исчезновение файла под `obj`
  изменением состава, если он вернулся до следующего вызова.
- Пересмотреть `noIncremental=true` по умолчанию у `run_dotnet_build` или описать в `AGENTS.md.sample`, что
  полная пересборка сбивает оценку TIA.

## Что осталось проверить

Сценарии запускаются так (Jellyfin заранее собран, рабочее дерево чистое):

```sh
export DOTNET_ROOT=<SDK 10.0.4xx> PATH=<SDK 10.0.4xx>:$PATH
python3 -I docs/readme-examples/mcp_calls.py <bin/Release/net10.0/RoslynMcpServer> <jellyfin> docs/archive/readme-demo-defects/recheck/<scenario>.json <out>
```

Сценарии `b`, `c` и `d` правят `Emby.Naming/Video/StubResolver.cs` (`IsNullOrEmpty` → `IsNullOrWhiteSpace`) и в
конце выполняют `git checkout .`. Перед каждым сценарием рабочее дерево Jellyfin должно быть чистым.

1. **D-01**, сценарий `b`: после окончания захвата `estimate_affected_tests` показывает один изменённый файл,
   правку до захвата — отдельной строкой `Baseline unknown in N file(s)`, ответ не больше 32 КиБ.
2. **D-02**, сценарии `c` и `d`: после `sed -i` и после обычной записи одинаковы число файлов, план проектов и
   выбор тестов, нет `Path outside the loaded input map` и `Project graph stale`. Прогоны с `run: true` дают
   одинаковые итоги, а `run_specific_test` в сценарии `c` не добавляет заметку о `.cs`.

**N-01, 2026-11-21.** Повторная проверка в WSL2 подтвердила сохранение baseline после
`reset_workspace` → `load_workspace` и роль `UserInput` для обоих файлов xUnit v3 на Jellyfin.
Последующее исправление принято после независимого ревью и сквозной проверки.
[Разбор N-01 и результаты проверки исправления](n01-investigation.md#проверка-исправления).

Перед сценариями `b`–`d` сделать инкрементальную сборку решения, а не `run_dotnet_build` по умолчанию:
иначе N-01 исказит сравнение.
