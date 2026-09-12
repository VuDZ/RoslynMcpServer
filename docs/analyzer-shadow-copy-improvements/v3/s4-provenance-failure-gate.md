# S4 — блокировать opt-in semantics при непригодном capture

Статус: **выполнено; независимая приёмка
[принята](s4-acceptance.md)** (v1.3.18, V3-R3 закрыт).
Зависимость: [S3](s3-persistent-publication-state.md).
Результат шага: V3-R3 устранён; failed/incomplete capture не даёт raw semantic snapshot.

## Основание

`CreateFailClosedSnapshot` вызывает `StripInSolutionAnalyzerReferences`,
который перечисляет только provenance-confirmed references. При corrupt
capture подтверждений нет; «удалить подтверждённые» оставляет исходный
snapshot без изменений и возвращает real output semantic readers.

Targets: `AnalyzerProvenanceCaptureService` snapshot status,
`SolutionManager.LoadAndPrepareAsync`, recovery/publication/accessors,
`WorkspaceTools.LoadWorkspace`, `AnalyzerExecutionGate`.

## Работа

Для opt-in сессии при отсутствующем, Failed, Incomplete или чужом по session
capture не публиковать semantic snapshot целиком. Это консервативный отказ
на границе операции, а не попытка угадать происхождение отдельных references.
Он не требует filename fallback или удаления потенциально внешних analyzers.

Сообщение load должно явно отличать открытый MSBuild graph от неготового
semantic workspace: статус capture, причина отказа и отсутствие разрешённого
overlay. Не ограничиваться «0 rewritten» или безоговорочным «success».
Новых MCP параметров и структурированной схемы ответа не требуется.
Недоступные semantic operations возвращают понятную причину через существующий
формат ошибок; не получают null-dereference или raw fallback.

Отказ сохраняется при edit/flush, cached false/omitted и отмене. Cache hit
не делает failed snapshot Complete. Восстановление требует пригодного capture
на разрешённой границе обновления графа; если текущий API переиспользует capture,
сообщение должно указывать reset/reopen, а не обещать исправление cached true.
Reset/reopen по-прежнему не отменяет restart-required CLR identity policy.

Complete capture с отдельной unconfirmed/foreign reference не приравнивать
к failed capture: существующий confirmed-only matcher и диагностика
сохраняются. Политика inaccessible original здесь не выбирается.

## Приёмка

- R3 из S1 зелёный: corrupt capture + opt-in не даёт semantic snapshot,
  exact marker отсутствует, real DLL отсутствует в process assemblies.
- Проверены missing, corrupt и mixed valid/corrupt binlogs, Incomplete/null
  snapshot и несовпадение session; ни один не даёт raw fallback.
- После отказа edit/flush и cached false/omitted не открывают semantics.
- После reset/reopen с успешным capture fresh host исполняет exact V1 из
  shadow path; graph/session действительно новые, а не ошибочно reused.
- Complete capture сохраняет работу missing-path repro и foreign fixtures;
  no-overlay load не получает неоговорённого нового требования к capture.
- В ошибочном opt-in load ответ явно сообщает semantic unavailability;
  временные binlogs очищаются по существующему контракту capture.

## Результат

Выполнено 2026-09-12. V3-R3 закрыт: opt-in без пригодного provenance не публикует
semantic snapshot. Отказ переживает edit / flush / cached false/omitted / cached true;
восстановление только через reset/reopen с новым capture.

### Источники и среда

- **База:** `d45ff5b` (`fix: keep fail-closed publication after failed opt-in`, v1.3.17)
- **Commit шага:** рабочее дерево этого шага; `AnalyzerProvenanceCaptureGate`,
  `SemanticPublicationAdmission.Unavailable`, `SolutionManager` load/prepare,
  `WorkspaceLoadGuidance` / `WorkspaceTools` load text, host inspect,
  `V3ProvenanceFailureGateTests`, правки E5/F09 Failed-capture, csproj `1.3.18`,
  README, `AGENTS.md.sample`, этот файл
- **Версия csproj:** `1.3.18`
- **OS / host:** Windows, x64 process
- **SDK:** `10.0.204` (`run_dotnet_build` / `run_specific_test`)
- **MCP binary:** `RoslynMcpServer` (workspace tools; production publish/reload
  этого шага не делались)

### Реализация

- Пригодный capture для opt-in: `Complete` и `LoadSessionId` текущей сессии.
  Missing / Failed / Incomplete / чужой session → `Unavailable`, `_solution = null`.
- Не вызывается strip-confirmed fail-closed: пустой confirmed-набор больше не
  оставляет raw snapshot.
- Cache hit не делает Failed→Complete и не восстанавливает overlay даже при
  cached true; сообщение указывает `reset_workspace` + повторный load.
- Complete capture с unconfirmed/foreign остаётся confirmed-only matcher.
- No-overlay load по-прежнему публикует raw без нового требования к capture.
- Временные binlogs удаляются по существующему контракту capture.

### Команды

1. `load_workspace` → `RoslynMcpServer.sln`
2. `run_dotnet_build` → `RoslynMcpServer.sln`
3. `run_specific_test` class=`V3RegressionBaselineTests`, `noBuild=true`
4. `run_specific_test` class=`V3ProvenanceFailureGateTests` (по методам)
5. `run_specific_test` Epoch5 Failed-capture / missing-path, F09 replay-failures
6. `WorkspaceWriteBoundaryTests`, `McpToolCatalogTests.Surface_sizes_match_recorded_release_numbers`

### Фактические результаты

| Проверка | Результат |
| --- | --- |
| R3 `V3_R3_corrupt_capture_does_not_publish_or_execute_real_output` | **passed** — `no-solution`, нет real published/loaded/process, marker пуст |
| R1 / R2 | **passed** — S2/S3 не регрессировали |
| Missing / mixed Incomplete / null / session mismatch | **passed** — `Unavailable`, нет raw fallback |
| Edit / flush / cached false/omitted / cached true | **passed** — семантика закрыта; session reused; Failed остаётся Failed |
| Reset/reopen + fresh host exact V1 | **passed** — новый session/graph, shadow path, не real output |
| Complete foreign + missing-path | **passed** — не приравнены к failed capture |
| No-overlay + Failed capture | **passed** — raw refs как прежде |
| F09 temp binlogs | **passed** — `ProvenanceTempDirectoryCount=0` |
| Catalog | **63 / 44,503** без изменения |

### Самопроверка

- Production published accessor (`GetPublishedSolutionAsync` / `oracle` без
  `oracleSource=workspace`) при непригодном capture возвращает отсутствие.
- Load text отличает открытый MSBuild graph от unavailable semantic workspace.
- Публичная MCP-схема не менялась; catalog 63 / 44,503.

### Ограничения

- Production/MCP publish+reload не выполнялись; номер выпуска в исходниках `1.3.18`.
- Inaccessible U-ARB-01 по-прежнему вне v3 S4.
- Независимая приёмка: [s4-acceptance.md](s4-acceptance.md).
