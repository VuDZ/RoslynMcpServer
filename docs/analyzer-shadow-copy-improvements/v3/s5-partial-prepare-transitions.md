# S5 — обеспечить безопасную публикацию частичного prepare

Статус: **выполнено; независимая приёмка
[принята](s5-acceptance.md)** (v1.3.19).
Зависимость: [S4](s4-provenance-failure-gate.md).
Результат шага: смешанный результат подготовки имеет проверенный безопасный
путь публикации и честную диагностику.

## Основание

Один `HasAnyApplied` не доказывает, что все подтверждённые in-solution
references безопасны. После исправления отдельных failure cases нужно
проверить несколько analyzers и переходы между active/stale/blocked.
Этот сценарий выявлен статическим анализом как риск; отдельного runtime
воспроизведения частичного prepare в ревью не было.

Targets: `AnalyzerShadowPrepareOutcome`, `AnalyzerShadowMapping`,
`SolutionManager.CompletePrepare`, `EvaluatePreparedMapping`, publication
state и load summary, lifecycle fixtures с несколькими генераторами.

## Работа

Проверять допустимость публикации по всем относящимся к overlay references,
а не только наличию одной успешно подготовленной. Неуспешная confirmed
reference не возвращается на real output вследствие успешной подготовки
другой. Для неё допустим прежний совместимый stale mapping, если execution
gate разрешает его, либо исключение из безопасного snapshot. Если безопасную
публикацию нельзя доказать, semantic snapshot недоступен целиком.

Заявленный результат должен различать prepared, фактически applied,
сохранённый stale и blocked. Частичный prepare не сообщается как полный
refresh или подтверждённое исполнение всех генераторов. Если решение
блокирует всю публикацию, подготовленные файлы сами по себе не означают
`Applied=true` в пользовательской сводке.

Подготовить fixture минимум с двумя генераторами с разными assembly identities,
чтобы тест частичного результата не подменялся ожидаемой CLR collision.
Проверить первую подготовку с одной файловой ошибкой и refresh со старым
mapping. Проверить также переход из active в restart-required и последующие
обычные публикации: stale V1 не должен снова становиться исполняемым.

Exact inverse должен понимать допустимый опубликованный snapshot, включая
намеренно исключённые references, если выбрана такая форма fail-closed.
Эти исключения нельзя сохранять как удаление исходных analyzer items из
`.csproj` или молча принимать как произвольный analyzer diff.

## Приёмка

- При одном successful и одном failed prepare отсутствует confirmed real
  output в semantic snapshot и process assemblies. Неподготовленный marker
  отсутствует; разрешённый marker проверяется точно, если snapshot доступен.
- Edit/flush/reconciliation сохраняют тот же допуск без analyzer I/O;
  `.csproj` byte-identical, повторная сборка не получает shadow includes.
- File-failed refresh явно сохраняет stale только там, где это допустимо;
  restart-required не исполняет ни stale V1, ни неподдержанную V2.
- Forced rebuild существующего real output после разрешённых semantic
  операций успешен и меняет hash; отсутствие marker не заменяет lock check.
- Load summary согласован с фактическим snapshot при частичном результате,
  отсутствии всех applied entries и полном запрете публикации.
- Существующие immutable generation, PDB optional и foreign-reference
  гарантии не ослаблены ради прохождения матрицы.

## Результат

Выполнено 2026-09-12. Смешанный prepare публикует только доказанно безопасный
snapshot: успешные references — shadow, неуспешные confirmed — исключение или
разрешённый stale, иначе вся публикация banned. Real output не возвращается
из-за чужого успеха.

### Источники и среда

- **База:** `6fe0665` (`fix: withhold opt-in semantics when provenance capture is unsuitable`, v1.3.18)
- **Commit шага:** рабочее дерево этого шага; `AnalyzerShadowPublicationPlanner`,
  `SemanticPublicationState.Allow` / `RestoreExcludedReferences`,
  `SolutionManager.CompletePrepare`, write-boundary inverse, load summary,
  two-generator fixture, `V3PartialPrepareTransitionTests`, csproj `1.3.19`,
  README, этот файл
- **Версия csproj:** `1.3.19`
- **OS / host:** Windows, x64 process
- **SDK:** `10.0.204` (`run_dotnet_build` / `run_specific_test`)
- **MCP binary:** `RoslynMcpServer` (workspace tools; production publish/reload
  этого шага не делались)

### Реализация

- Публикация решается по всем confirmed overlay references, не по `HasAnyApplied`.
- Успешная подготовка одной reference не оставляет другую confirmed на real path.
- Допустимы: fresh apply, stale если execution gate разрешает тот же shadow,
  иначе исключение из snapshot. Restart-required не исполняет stale V1 и V2.
- Restart-ban не поднимается `CompleteFailedPrepare` / ordinary publication.
- Load summary: `prepared` / `applied` / `stale` / `blocked`. Ban не сообщает
  `Applied=true` из подготовленных файлов. Partial не есть полный refresh.
- Exact inverse восстанавливает намеренные exclusions и по-прежнему отвергает
  unknown analyzer diff; `.csproj` не получает shadow includes.
- Fixture: `Generator` + `GeneratorB` (разные assembly identities).

### Команды

1. `load_workspace` → `RoslynMcpServer.sln`
2. `run_dotnet_build` → `RoslynMcpServer.sln`
3. `run_specific_test` class=`AnalyzerShadowPublicationPlannerTests`
4. `run_specific_test` class=`WorkspaceWriteBoundaryTests`
5. `run_specific_test` class=`SemanticPublicationStateTests`
6. `run_specific_test` class=`V3PartialPrepareTransitionTests` (`timeoutSeconds=600`)
7. `run_specific_test` class=`V3RegressionBaselineTests` / `V3PersistentPublicationStateTests` / `V3ProvenanceFailureGateTests`
8. `run_specific_test` class=`Epoch2ImmutableShadowTests` / `AnalyzerReferenceShadowCopierTests`
9. `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers`

### Фактические результаты

| Проверка | Результат |
| --- | --- |
| Mixed prepare (1 success + 1 file fail) | **passed** — нет real published/process; allowed marker exact; blocked marker отсутствует |
| Edit / flush / reconciliation | **passed** — тот же admission, без analyzer I/O; `.csproj` byte-identical |
| File-failed refresh | **passed** — stale только у failed; другая applied fresh |
| Active → restart-required → edit/cached load | **passed** — нет V1/V2; stale V1 не исполняется |
| Forced rebuild real output | **passed** — hash изменился (lock check, не «нет marker») |
| Summary: none applied / partial / restart | **passed** — согласовано со snapshot |
| Exact inverse + unknown diff | **passed** — exclusions restored; extra deletion rejected |
| R1 / R2 / R3, S3, S4, Epoch2, foreign, catalog | **passed** — 63 / 44,503 без изменения |

### Самопроверка

- Production published accessor (`GetPublishedSolutionAsync` / default oracle) не
  видит confirmed real output при частичном результате.
- Load text отличает prepared / applied / stale / blocked.
- Публичная MCP-схема не менялась; catalog 63 / 44,503.

### Ограничения

- Production/MCP publish+reload не выполнялись; номер выпуска в исходниках `1.3.19`.
- Inaccessible U-ARB-01 по-прежнему вне v3 S5.
- Независимая приёмка: [s5-acceptance.md](s5-acceptance.md).
