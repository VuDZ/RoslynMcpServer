# Холостой запуск тестовой сборки, 2026-11-15

Шаг 5б [rapid TIA 2](../README.md). Спайк — `1650beb`. Тесты классов заново не запускались: каталоги
`classes` шага 5 и Debug-вывод те же. Сводки:

- `jellyfin`: [idle-run](il-jellyfin-recall-2026-11-15-idle-run-summary.json);
- `orchard-wide`: [idle-run](il-orchard-wide-recall-2026-11-15-idle-run-summary.json).

`recall-raw.json` остаётся в `LOCAL_ROOT/il-runs` (`jellyfin-recall-idle-2026-11-15`,
`orchard-recall-idle-2026-11-15`). `idle-run.xml` и `idle-run.log` лежат в каталогах `classes` шага 5.

## Команды

`dotnet-coverage` 18.12.0 из `LOCAL_ROOT/tools`. Классы с отчётом пропущены, запустились
только холостые прогоны.

`jellyfin`, `--runner vstest`, 31 с. В `Jellyfin.Server.Implementations.Tests\idle-run.log` ни один тест
не запустился: `No test matches the given testcase filter`.

```text
dotnet TestImpactIlProbe.dll class-coverage --root SOURCE_ROOT/jellyfin --out LOCAL_ROOT/classes --runner vstest
dotnet TestImpactIlProbe.dll recall --root SOURCE_ROOT/jellyfin --classes LOCAL_ROOT/classes --out LOCAL_ROOT/jellyfin-recall-idle-2026-11-15
```

`orchard-wide`, `--runner xunit3`, 5 с. В `OrchardCore.Tests\idle-run.log` строка `Total: 0`. То же у
остальных трёх сборок. Раннер не запустил сборку целиком и не упал до поиска тестов.

```text
dotnet TestImpactIlProbe.dll class-coverage --root LOCAL_ROOT/orchard-wide --bin LOCAL_ROOT/src --bin LOCAL_ROOT/test --out LOCAL_ROOT/classes --runner xunit3
dotnet TestImpactIlProbe.dll recall --root LOCAL_ROOT/orchard-wide --bin LOCAL_ROOT/src --bin LOCAL_ROOT/test --classes LOCAL_ROOT/classes --out LOCAL_ROOT/orchard-recall-idle-2026-11-15
```

Последняя строка обоих прогонов: `test assemblies without an idle run: 0`.

## Классы

| Корпус | Сборок | С холостым запуском | Из них исполняют production-файл |
| --- | --- | --- | --- |
| `jellyfin` | 17 | 17 | 7 |
| `orchard-wide` | 4 | 4 | 0 |

На `orchard-wide` сборщик записал по одному файлу у `OrchardCore.Tests` (`CIFact.cs`) и
`OrchardCore.Tests.Integration` (`AwsFileStoreTests.cs`). Оба файла лежат в тестовом проекте, в граф
production они не входят. У `OrchardCore.Abstractions.Tests` и `OrchardCore.Tests.Functional` файлов 0.

## Recall

«Вне» — пары, чей файл холостой запуск сборки этого класса не исполняет.

| Корпус | Слой | Пары | Держит | recall | filesLosing | Вне: пары | Вне: держит | Вне: recall | Вне: filesLosing |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `jellyfin` | `discovery` | 20 472 | 20 288 | 99,10% | 16 | 19 921 | 19 915 | 99,97% | 6 |
| `jellyfin` | `discoveryWithLoads` | 20 472 | 20 298 | 99,15% | 10 | 19 921 | 19 921 | 100% | 0 |
| `jellyfin` | `discoveryWithNamedLoads` | 20 472 | 20 293 | 99,13% | 11 | 19 921 | 19 920 | 99,99% | 1 |
| `orchard-wide` | `discovery` | 24 734 | 9 449 | 38,20% | 1016 | 24 734 | 9 449 | 38,20% | 1016 |
| `orchard-wide` | `discoveryWithLoads` | 24 734 | 24 733 | 100% | 1 | 24 734 | 24 733 | 100% | 1 |
| `orchard-wide` | `discoveryWithNamedLoads` | 24 734 | 24 733 | 100% | 1 | 24 734 | 24 733 | 100% | 1 |

## Ответы

У `discovery` на `jellyfin` вне холостого запуска остаются 6 потерянных пар, по одной на файл, и все
шесть — класс `BaseItemKindTests`:

- `Emby.Server.Implementations\Playlists\PlaylistsFolder.cs`;
- `MediaBrowser.Controller\Entities\MusicVideo.cs`;
- `MediaBrowser.Controller\Entities\Trailer.cs`;
- `MediaBrowser.Controller\LiveTv\LiveTvChannel.cs`;
- `MediaBrowser.Controller\LiveTv\LiveTvProgram.cs`;
- `MediaBrowser.Controller\Playlists\Playlist.cs`.

Это те же шесть файлов, что разбор шага 5а отнёс к `GetAssemblies` и `GetTypes`. У `BaseItemKindTests`
в полном прогоне 7 потерянных файлов: седьмой, `ItemTypeLookup.cs`, холостой запуск его сборки
исполняет, и эта пара в счёт вне холостого запуска не входит. Из 184 потерянных пар 178 приходятся на
холостой запуск. `discoveryWithLoads` вне холостого запуска не теряет ни одной пары.
`discoveryWithNamedLoads` теряет одну: тот же `PlaylistsFolder.cs` и тот же `BaseItemKindTests`.

На `orchard-wide` холостой запуск раннера `xunit3` production-файлы не исполняет. Доля `discovery`
остаётся 38,20%: вне холостого запуска те же 24 734 пары.

Слой `discoveryWithNamedLoads` оставлен. Холостой запуск вывод шага 5а не меняет: вне него узкий слой
по-прежнему теряет пару, которую широкое держит, и `selectedPairs` меньше на 4 988. Q-01 и шаг 7 этим
шагом не закрыты.

## Замечания исполнителя

Владелец их не подтверждал.

Холостой запуск делит два корпуса. На `jellyfin` 178 из 184 потерянных пар — шум поиска тестов: вне
холостого запуска `discovery` держит 19 915 из 19 921, и шесть оставшихся пар — один класс
`BaseItemKindTests`. Доля 99,10% по всем парам для шага 7 занижена шумом измерения. Считать стоит 99,97%
вне холостого запуска.

На `orchard-wide` тот же приём ничего не снимает. Раннер `xunit3` в холостом запуске production-файлы не
исполняет, и 38,20% с 1016 файлами остаются целиком. Это пропуски графа, а не хоста. Поднимает долю до
100% по-прежнему `discoveryWithLoads`, при precision 7,0% и 354 470 выбранных парах против 39 907 у
`discovery`.

`discoveryWithNamedLoads` на этих числах по-прежнему не замена широкому слою. Вне холостого запуска он
теряет единственную настоящую пару `jellyfin`, которую широкое правило держит, и дешевле всего на 4 988
пар. Слой в спайке оставлен до шага 7: убирать его этим прогоном рано, переключать ответ на него — тоже.
