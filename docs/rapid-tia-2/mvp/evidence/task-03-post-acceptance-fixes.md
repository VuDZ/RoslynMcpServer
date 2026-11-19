# Task-03 — исправления POST-F-01 и POST-F-02 после приёмки

- Дата: **2026-11-18**. Исполнитель: Claude Opus 5.5 (`claude-opus-5-5`), сабагент координатора.
- Основание: [дополнительное ревью](task-03-post-acceptance-review-sdk10-2026-11-18.md) готового `d78bb2d`.
  Ветка `rapid-tia` на `61ba2c2`: в ней уже есть task-04 (`b27d16d`) с внутренним потребителем пары
  `TestClassNarrower.NarrowAsync`.
- Машина: Linux x64, .NET SDK 10.0.114, Release, `TMPDIR=$HOME_DIR/testtmp`. Прогоны последовательные, перед
  сборкой solution — `dotnet build-server shutdown`, сборки с `-nodeReuse:false`. Логи — в игнорируемом
  `TestResults/task03-post/`.
- Публичный инструмент `estimate_affected_tests` не менялся. Его ответ строится без сужения; `TestClassNarrower`
  в продукте пока никто не вызывает.

## Выбранный контракт POST-F-01

**Запрос пары принадлежит baseline, который текущий в момент его начала, и пара целиком сделана из этого baseline
и проектов запроса.** Если во время запроса начинается новый baseline:

- запрос не переключается на новый baseline: прежний цикл повтора с прежним набором проектов убран;
- запрос отвечает для своего baseline: путь, момент публикации, «до правки», текущий снимок и карта — одного
  поколения;
- в сессии такой поздний ответ ничего не оставляет: кэш текущих записей отцепленного baseline не обновляется,
  кэш нового baseline не трогается.

Пара называет свой baseline парой «путь solution, момент публикации» — так же, как `BaselineSessionSnapshot`.
Новый метод `AssemblySnapshotPair.BelongsTo(path, publishedAtUtc)` сверяет её с baseline другого источника. Reload
того же solution сохраняет оба значения (M-10), новый baseline меняет их.

**Потребитель task-04.** `TestImpactEstimate` получил два внутренних свойства: `BaselineWorkspacePath` и
`BaselinePublishedAtUtc`. `TestImpactEstimator.EstimateAsync` заполняет их из того `BaselineSessionSnapshot`,
по которому считал ответ эпохи 1. `TestClassNarrower.NarrowAsync` сверяет пару с ними. Если baseline разные,
каждый выбранный эпохой 1 проект остаётся целиком с новой причиной `TestNarrowingWholeKind.BaselineChanged`:
«Another solution was loaded while the request ran, …; repeat the request.». Повторный запрос считает оба ответа
по одному baseline. Сверка идёт до сужения, поэтому общий путь сборки в двух baseline больше не может дать
«код не изменился».

## Исправление POST-F-02

После каждого успешного запроса кэш текущих записей заменяется целиком (`ReplaceCurrentUnderLock`): в нём
остаются записи этого запроса и пригодные записи «до правки», то есть всё, кроме `Unknown`. Путь, которого нет
ни в «до правки», ни в последнем запросе, освобождается. Путь из «до правки», пропущенный одним запросом,
возвращается к своей записи «до правки», а не перечитывается во вторую копию: неизменившаяся сборка по-прежнему
хранится один раз. Карта «до правки» не трогается.

## Красный прогон

Тесты написаны до исправлений. Чтобы они компилировались, добавлены заготовки контракта без поведения:
свойства `TestImpactEstimate` (не заполнялись), член перечисления `BaselineChanged` и
`AssemblySnapshotPair.BelongsTo`, бросавший `NotImplementedException`.

```bash
TMPDIR=$HOME_DIR/testtmp dotnet test RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~RoslynMcpServer.Tests.Testing.Snapshots|FullyQualifiedName~RoslynMcpServer.Tests.Testing.Narrowing"
```

`Failed: 4, Passed: 78, Skipped: 0, Total: 82` (`red.log`, `red.trx`).

| Тест | Причина падения на прежнем коде |
| --- | --- |
| `AssemblySnapshotSessionTests.Request_paused_while_another_solution_starts_returns_a_pair_of_its_own_baseline` | Пара помечена `Second.slnx`, а её текущий снимок — `Lib` прежнего запроса: смешанная пара из ревью |
| `AssemblySnapshotSessionTests.Current_entry_of_a_path_left_out_of_the_next_request_is_released` | Слабая ссылка на текущую запись `Other` жива после запроса без `Other` |
| `TestNarrowingBaselineTests.Pair_of_another_baseline_keeps_every_selected_project_whole("Other.slnx", 0)` | Ожидался проект целиком, получено `CodeUnchanged`: молчаливая потеря тестов |
| `TestNarrowingBaselineTests.Pair_of_another_baseline_keeps_every_selected_project_whole("Solution.slnx", 5)` | То же; тот же путь, другой момент публикации |

Прошли уже на прежнем коде, это ожидаемо:

- `TestNarrowingBaselineTests.Pair_of_the_estimate_baseline_is_narrowed` — контроль: пара своего baseline даёт
  `CodeUnchanged`;
- `AssemblySnapshotSessionTests.Path_left_out_of_a_request_comes_back_to_its_entry_before_the_edit` — страховка
  исправления POST-F-02: прежний код ничего не удалял. Его red-свидетельство — мутация M4 ниже.

## Зелёный прогон

Тот же фильтр после исправлений: **82/82**, 0 skip (`green.log`, `green.trx`). Из них снимки — 27 (24 прежних +
3 новых), сужение — 55 (52 прежних + 3 новых).

## Мутации, снимающие каждое исправление

Временная правка, сборка, тот же фильтр (`mutation-*.log`). После каждой исходные файлы восстановлены
копированием; SHA-256 `AssemblySnapshotSession.cs` и `TestClassNarrower.cs` после серии совпали с исходными.

| Мутация | Падает | Тесты |
| --- | --- | --- |
| M1. Сверка baseline в `NarrowAsync` отключена | 2 из 82 | `Pair_of_another_baseline_keeps_every_selected_project_whole` (оба случая) |
| M2. Запрос, у которого сменился baseline, повторяется для нового baseline с прежними проектами (поведение `d78bb2d`) | 1 из 82 | `Request_paused_while_another_solution_starts_returns_a_pair_of_its_own_baseline` |
| M3. Кэш текущих записей не чистится, только дополняется | 1 из 82 | `Current_entry_of_a_path_left_out_of_the_next_request_is_released` |
| M4. Кэш чистится без возврата к записям «до правки» | 1 из 82 | `Path_left_out_of_a_request_comes_back_to_its_entry_before_the_edit` |

## Validation

| Проверка | Результат | Лог |
| --- | --- | --- |
| `dotnet build RoslynMcpServer.sln -c Release --no-incremental -nodeReuse:false` | 0 ошибок, 3 прежних предупреждения вне задачи (`HostSession.cs:1179`, `WpfTemporaryProjectCallbackTests.cs:195`, `NuGetFallbackAssemblyResolver.cs:72`) | `release-build.log` |
| Снимки + сужение | **82/82**, 0 skip | `focused-final.log`, `.trx` |
| Снимки отдельно | **27/27** | — |
| SourceStructure | **123/123**, 0 skip; инвентарь ограничений без изменений: новых вызовов делегатов нет | `sourcestructure.log`, `.trx` |
| `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | **13/13**, 0 skip, 2 мин 19 с | `lifecycle.log`, `.trx` |
| Полный main suite | за координатором | — |

## Повторная приёмка координатором, 2026-11-18

Ревью исправлений, раунд 1 — MUST-FIX (PF-01, ниже);
раунд 2 — **ACCEPT**. Общее ревью координатора замечаний не дало.
POST-F-01 и POST-F-02 закрыты. Финальная проверка окончательного кода (baseline `61ba2c2` плюс рабочее
дерево), Linux x64, SDK 10.0.114, Release, `TMPDIR=$HOME_DIR/testtmp`, последовательно, MSBuild без node reuse:

| Проверка | Результат |
| --- | --- |
| `dotnet build RoslynMcpServer.sln -c Release --no-incremental -nodeReuse:false` | 0 ошибок, те же 3 прежних предупреждения |
| SourceStructure + Snapshots + Narrowing | **207/207**, 0 skip |
| `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | **13/13**, 0 skip |
| Main, фильтр Linux shard `Category!=AnalyzerLifecycle&Category!=RequiresWindows&Category!=RequiresPinnedSdk` | **2495/2495**; total = executed = passed, failed/error/timeout/aborted = 0; `21:05:22`–`21:16:55+01:00` |

Артефакты — игнорируемый `TestResults/task03-post-final`. SHA256:

```text
focused.trx        85E2D33D59896F446C7358FC84D1EBA1D9998B21F1A2C23F2F23F43CAB05D09B
lifecycle.trx      AAA3BCEC88A2C9087FD2D420A912F0C205B597FDA99B0138E4B6AEE6837F682F
main.trx           2BFA01BA732CA0420119611E3F7C869782D4BF2A5473C78B23D08E74C95C6F87
release-build.log  75747A3502ECEFFBDFA65315B9E574B0EAE9A371CDC06640BCF9E2AF9BA7CA76
```

Windows после исправлений не запускался.

## Ревью исправлений, раунд 1: PF-01 и PN-01…PN-03

Ревью вернуло MUST-FIX PF-01. Перегрузка `GetPairAsync(Solution)`,
которой пользуется `TestClassNarrower`, привязывалась к baseline только после `FindTestProjectsAsync`. Baseline,
начатый в это окно, получал пару с проектами прежнего solution.

**Исправление.** Обе перегрузки сначала привязывают запрос (`Bind`: снимок и задача его первого чтения под
замком), а уже потом определяют тестовые проекты и строят пару (`BuildPairAsync(PairRequest, …)`). Контракт
выше теперь верен для обеих перегрузок.

**Тесты** — оба в `AssemblySnapshotLifecycleTests`:

- `Solution_request_belongs_to_the_baseline_current_when_it_starts` — регрессия PF-01. В `AdhocWorkspace`
  документ с `TextLoader`, который ждёт сигнала, останавливает `FindTestProjectsAsync`. В это время стартует
  другой solution, затем загрузчик отпускается. Ожидается пара исходного baseline.
- `Pair_after_a_reload_belongs_to_the_baseline_the_session_reports` — PN-03, сквозная сверка меток из настоящих
  источников. Путь solution записан с `..`. Проверяется цепочка: `BaselineSession.Publish` → reload того же
  solution → `GetSnapshot()` → `pair.BelongsTo(snapshot.WorkspacePath, snapshot.PublishedAtUtc)`.

**Красный прогон** (Snapshots | Narrowing, код до исправления PF-01): `Failed: 1, Passed: 83, Total: 84`
(`r1-red.log`, `.trx`). Падает регрессия PF-01: `BelongsTo` исходного baseline ложно, пара помечена новым. Тест
PN-03 прошёл и на прежнем коде — это страховка, а не свидетельство дефекта.

**Зелёный прогон:** 84/84 (`r1-green.log`, `.trx`). Снимков теперь 29, сужения — 55.

**Мутация M5:** привязка запроса перенесена обратно после `FindTestProjectsAsync`. Падает 1 из 84 —
`Solution_request_belongs_to_the_baseline_current_when_it_starts` (`mutation-bind-after-test-projects.log`).
Мутация сделана и снята правкой Edit; SHA-256 файла после неё совпал с исходным.

**PN-02** записан в отчёт: после того как путь выпал из запроса, кэш возвращается к записи «до правки»,
и следующий запрос, снова назвавший путь, может перечитать файл, уже прочитанный раньше. Это лишнее чтение, а
не ошибка. **PN-01** записан ограничением выше.

**Validation раунда 1:**

| Проверка | Результат | Лог |
| --- | --- | --- |
| `dotnet build RoslynMcpServer.sln -c Release --no-incremental -nodeReuse:false` | 0 ошибок, те же 3 прежних предупреждения | `r1-release-build.log` |
| Снимки + сужение | **84/84**, 0 skip | `r1-focused.log`, `.trx` |
| SourceStructure | **123/123**, 0 skip; инвентарь без изменений | `r1-sourcestructure.log`, `.trx` |
| `AnalyzerLifecycle`, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` | **13/13**, 0 skip, 2 мин 19 с | `r1-lifecycle.log`, `.trx` |
| Полный main suite | за координатором | — |

## Границы

- Запрос, начатый до нового baseline, всё ещё отвечает для старого: его пара согласована, но устарела.
  Потребитель отличает её только сверкой baseline. Другого потребителя пары, кроме `TestClassNarrower`, нет.
- Сверка идёт по паре «путь, момент публикации». Два baseline с одним путём и одним моментом публикации
  (`DateTime.UtcNow` двух разных загрузок) не различаются. Новый baseline того же пути в одной сессии бывает
  только после загрузки другого solution между ними (M-10). По ревью исправлений (PN-01) это принято как есть;
  счётчик baseline не вводится.
- ~~`GetPairAsync(Solution)` привязывается к baseline после определения тестовых проектов… Это не меняет
  контракт.~~ Неверно, исправлено в раунде 1 ревью исправлений (PF-01, ниже). Метка и «до правки» тогда были
  нового baseline, а текущий снимок и карта — проектов старого solution.
