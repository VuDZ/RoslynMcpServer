# Исправления по ревью task-00, 2026-10-28

- Статус: **выполнено, 2026-10-28**.
- Основание: [ревью результатов task-00](../review-task-00-2026-10-28/result.md),
  F-01–F-05; запрос владельца поправить найденные замечания.
- База: `27ede33`, сервер 1.5.17 → **1.5.18**.
- Исполнитель и рецензент: `claude-opus-5-5` (Claude Code), одна сессия.

## Изменения

- **F-02, код.** `Services/Workspace/SolutionManager.cs`:
  `ForgetSanitizedPublishedSolution` сбрасывает кеш sanitized snapshot в
  `ClearWorkspaceAsync` и в свежем пути `LoadCoreAsync`; internal
  `SanitizedSnapshotSource` для теста. Тесты —
  `RoslynMcpServer.Tests/Workspace/SolutionManagerSanitizedSnapshotTests.cs`.
  Patch bump 1.5.18 и строка `CHANGELOG.md`.
- **F-01, F-04, F-05.** Раздел «Главное» в
  [инвентаризации](../epoch-0-design-and-spike/evidence/task-00-state-inventory.md),
  отметка об исправлении утечки в разделе о чистом proxy.
- **F-03.** [README темы](../README.md): наблюдение ~18 МБ помечено как возможно
  сделанное до Server GC; [task-05](../epoch-0-design-and-spike/task-05-applicability-checks-med.md):
  п. 4 «Режим GC», версия сервера в разборе ~20 MCP, таблица памяти по режимам GC.
- Учёт: шапка task-00, статистика ревью задач в карте E0, «Учёт» в README темы,
  строка `docs/README.md`.

Требования задач, зависимости и разрешения на реализацию не менялись.

## Validation

- Red: с убранными вызовами сброса `run_specific_test`
  `SolutionManagerSanitizedSnapshotTests`, Release — **2 failed**
  (`Assert.Null` после clear, `Assert.NotSame` после свежего load другого проекта).
- Green: с фиксом тот же прогон — **2 passed**.
- MCP `run_dotnet_build` `RoslynMcpServer.sln`, Release — **PASS**.
- MCP `run_test_by_filter` `RoslynMcpServer.Tests.csproj`, Release, `noBuild`,
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk&Category!=RequiresWindows`
  (Linux, как в workflow; включает SourceStructure) — **1428 passed, 0 failed**.
- `ROSLYN_MCP_ANALYZER_LIFECYCLE=1 dotnet test … -c Release --no-build
  --filter Category=AnalyzerLifecycle` — **13 passed, 0 failed, 0 skipped**, 2 m 20 s.
  Запускался, поскольку фикс затрагивает reset и путь свежего load.
- Markdown: 410 локальных ссылок и anchors во всех документах темы,
  `docs/README.md` и двух новых навыках — broken 0.
- `git diff --check` с `cr-at-eol` (CRLF в `RoslynMcpServer.csproj` сохранён) — PASS.
- Не запускались: `RequiresWindows`, `RequiresPinnedSdk`, Windows и macOS; замер
  освобождаемой памяти после фикса не проводился.
