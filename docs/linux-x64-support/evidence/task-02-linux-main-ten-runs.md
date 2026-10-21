# task-02 — десять прогонов main suite на Linux

Дата: 2026-10-21. Это локальная серия, не прогоны GitHub Actions.

## Что считается

Чистый clone коммита `89c275894a884ac2179b9b168a4464c9a5124803` в `$HOME/roslyn-ci-suite` (ext4). SDK `10.0.402`, рядом установлен `10.0.300`, `DOTNET_MULTILEVEL_LOOKUP=0`. Команда каждого прогона:

```text
dotnet test RoslynMcpServer.sln -c Release --no-restore --verbosity minimal --filter "Category!=AnalyzerLifecycle&Category!=RequiresWindows"
```

Лимит одного прогона — 20 минут. Все десять завершились раньше и подряд, без падения между ними.

| Прогон | Результат | Длительность тестов |
|---|---|---|
| 1 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 57 s |
| 2 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 58 s |
| 3 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 1 m |
| 4 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 57 s |
| 5 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 59 s |
| 6 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 59 s |
| 7 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 1 m 49 s |
| 8 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 1 m 57 s |
| 9 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 1 m 59 s |
| 10 | Passed 1338, Failed 0, Skipped 0, Total 1338 | 1 m 56 s |

1338 — это закоммиченный набор без четырёх тестов `RequiresWindows`. Инвентарь этой категории входит в 1338 и в каждом прогоне прошёл, так что исключённый набор — ровно этот перечень. Число 1388 из отчёта task-01 больше, потому что тогда в рабочем дереве были ещё не закоммиченные тесты.

Журналы прогонов: `/home/runner/linux-main-10-clean/run-1` … `run-10`. В git они не входят.

## Что в серию не вошло

Прогон того же фильтра по грязному дереву `/mnt/e/Devel/RoslynMcpServer` упал на первом же заходе: `GitChangedFilesHelperTests.RunGitAsync_status_on_this_repo_returns`, `TimeoutException`, helper не вернулся за 30 с. Рядом лежат чужие незакоммиченные файлы, а каталог на 9p. Этот заход не входит в десять. На чистом clone тот же тест уложился во все десять прогонов.

После этой серии `continue-on-error` у Linux-job снят. Ссылки на зелёный прогон четырёх job в GitHub Actions ещё нет: workflow не запушен.
