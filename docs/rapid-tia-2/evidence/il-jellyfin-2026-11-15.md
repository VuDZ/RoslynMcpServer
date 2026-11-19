# Спайк на `jellyfin`, 2026-11-15

Шаг 4 [rapid TIA 2](../README.md). Клон `SOURCE_ROOT/jellyfin`,
SHA `2953ed632c9277351483eff04f87a8276390691c` (`master`, `v12.0-rc7-327`).
Спайк — сборка `5255518`. Сводки без имён:
[measure](il-jellyfin-2026-11-15-summary.json),
[coverage](il-jellyfin-2026-11-15-coverage-summary.json).
`raw.json` и `coverage-raw.json` остаются в `LOCAL_ROOT/jellyfin-2026-11-15`.

## Сборка и тесты

`dotnet build Jellyfin.slnx -c Debug`: 0 ошибок, 211 предупреждений, 1 мин 45 с.
Покрытие — `dotnet-coverage` 18.12.0, один файл Cobertura. Прогон 58 с, код выхода 0.

4250 тестов прошли, 0 упали, 21 пропущен:

- 1 тест `GetSysctlValue_CpuBrand_NotEmpty` — только macOS;
- 17 тестов `ManagedFileSystemTests` — пути Unix, `/dev/shm` и висячая symlink;
- 3 теста `UserLibraryControllerTests`, один из них theory с пометкой «Disabled for flaky execution after refactor».

## Граф и покрытие

`measure`: сумма этапов 0,7 с, пик 102 МБ. 300 тестовых классов, 1550 тестовых методов,
1915 файлов с уровнем 0. До загрузки по имени доходят 71 тестовый класс в слое `references`
и 73 в `discovery`.

| Слой | Медиана доли уровня 0 | Файлов без ответа | Пропуск среди 1012 исполняемых |
| --- | --- | --- | --- |
| `references` | 9,8% | 569 | 33,1% (335 файлов) |
| `discovery` | 24,7% | 17 | 0% |
| `wholeAssembly` | 24,7% | 3 | 0% |
| `referencesWithLoads` | 39,9% | 0 | 0% |
| `discoveryWithLoads` | 47,7% | 0 | 0% |

Слой `discovery` уже не пропускает ни одного исполняемого файла: 17 пустых ответов не исполняются
или отсутствуют в отчёте. Правило загрузки поднимает медиану с 24,7% до 47,7% и убирает оставшиеся
пустые ответы. Один файл лежит в сборке без тестовой ссылки и тестами не исполняется.
