# Сверка `recall` на `jellyfin`, 2026-11-15

Шаг 5 [rapid TIA 2](../README.md). Клон `SOURCE_ROOT/jellyfin`,
SHA `2953ed632c9277351483eff04f87a8276390691c` (`master`, `v12.0-rc7-327`).
Спайк — `bdff432`. Сводка без имён: [recall-summary](il-jellyfin-recall-2026-11-15-summary.json).
`recall-raw.json` и каталог `classes` остаются в `LOCAL_ROOT/jellyfin-class-2026-11-15`
и `LOCAL_ROOT/jellyfin-recall-2026-11-15`. Debug-вывод шага 4 не пересобирался.

## Команда

`dotnet-coverage` 18.12.0 взят из `LOCAL_ROOT/tools` (каталог добавлен в `PATH`).
Раннер — `vstest`: `dotnet test` корпуса идёт через VSTest.

Пробный прогон `--limit 3`. В логе `UrlDecodeQueryFeatureTests` строка `Passed: 1, Total: 1`
и «7 files executed». Это один тест класса, не сборка.

Полный прогон, 17 мин 44 с:

```text
dotnet TestImpactIlProbe.dll class-coverage --root SOURCE_ROOT/jellyfin --out LOCAL_ROOT/classes --runner vstest
```

Повтор того же вызова: классов без отчёта 0. Затем:

```text
dotnet TestImpactIlProbe.dll recall --root SOURCE_ROOT/jellyfin --classes LOCAL_ROOT/classes --out LOCAL_ROOT/jellyfin-recall-2026-11-15
```

## Классы

В графе 300 тестовых классов, отчёт есть у 300, ни один не оставил production-файл без исполнения.
Классов без отчёта нет. В именах классов нет `+`.

Тесты те же, что в прогоне покрытия шага 4: 4250 прошли, 0 упали, 21 пропущен.

- `GetSysctlValue_CpuBrand_NotEmpty` — только macOS;
- 17 тестов `ManagedFileSystemTests` — пути Unix, другой файловый том и висячая symlink;
- 3 теста `UserLibraryControllerTests` с `Skip = "Disabled for flaky execution after refactor."`.

## Recall

Исполняемых файлов 1012, пар «файл и исполняющий его класс» 20 472.

| Слой | recall | recallByMethods | filesLosing | filesWithEmptyAnswer | precision |
| --- | --- | --- | --- | --- | --- |
| уровень 0 | 100% | 100% | 0 | 0 | 5,30% |
| `references` | 49,16% | 54,12% | 420 | 335 | 10,38% |
| `discovery` | 99,10% | 99,04% | 16 | 0 | 15,43% |
| `wholeAssembly` | 99,10% | 99,04% | 16 | 0 | 15,30% |
| `referencesWithLoads` | 97,98% | 97,11% | 37 | 0 | 11,17% |
| `discoveryWithLoads` | 99,15% | 99,06% | 10 | 0 | 10,93% |

Пустой ответ у `discovery` по-прежнему не появляется. Слой теряет класс у 16 файлов из 1012:
это не пустой ответ, а неполный. `discoveryWithLoads` оставляет такие файлы у 10.
`referencesWithLoads` поднимает recall с 49,16% до 97,98% и всё ещё теряет класс у 37 файлов.

Чаще всего `discovery` теряет классы сборки `Jellyfin.Server.Implementations.Tests`.
Первые строки `lost.byTestClass`:

- `BaseItemKindTests` — 7 файлов; примеры `Emby.Server.Implementations\Data\ItemTypeLookup.cs`, `Emby.Server.Implementations\Playlists\PlaylistsFolder.cs`;
- `InstallationManagerTests`, `PlayQueueManagerTests`, `AuthenticationManagerTests`, `OptimisticLockBehaviorTests`, `IgnorePatternsTests` — по 4 файла; примеры `ItemTypeLookup.cs` и `MediaBrowser.Controller\Entities\Audio\Audio.cs`.

У `discoveryWithLoads` `BaseItemKindTests` из этого списка уходит. Остаются те же классы по 4 файла,
плюс `MediaStreamSelectorTests`. Сборки файлов — `Emby.Server.Implementations` и `MediaBrowser.Controller`.
