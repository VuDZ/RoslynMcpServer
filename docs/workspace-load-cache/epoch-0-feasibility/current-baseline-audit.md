# Current baseline audit — вход E0/task-00

Статус: **начальный code inventory, не приёмка эпохи**. Прочитан текущий код;
build/tests в рамках этого docs-only изменения не запускались. E0/task-00 должен
актуализировать audit на своём HEAD и дополнить проверенными test outcomes.

## Два основания

- Историческое base truth спецификации: commit
  `9867318ddb5294ce144bf024b9a61a1a2e3814c3`, source 1.3.21.
- Текущий HEAD при подготовке inventory:
  `cf3360c6ee1a4a19519a4b9daed9953c6f423882`.
  [RoslynMcpServer.csproj](../../../RoslynMcpServer.csproj) содержит Version 1.5.4.
  Рабочее дерево содержит незакоммиченные docs/scripts; текущий C# код для этого
  inventory не менялся. При исполнении task-00 заново фиксируются HEAD/dirty paths.
- Running MCP — отдельная величина, в этом docs-only аудите не запрашивалась.
  Версия csproj не доказывает версию запущенного процесса.

## Реализовано в текущих paths и оставшаяся delta

- **A-LOAD — частично.** [LoadAndPrepareAsync](../../../Services/Workspace/SolutionManager.cs#L807)
  удерживает acquisition для load/prepare; уже есть under-lock методы и
  [published accessor](../../../Services/Workspace/SolutionManager.cs#L369).
  **Delta:** [LoadCoreAsync](../../../Services/Workspace/SolutionManager.cs#L1949)
  на ordinary reopen останавливает watcher, dispose old и очищает state до нового
  open. Retain old при candidate failure/cancel, двухвладельческая lifetime модель
  и atomic candidate promotion из spec ещё должны быть интегрированы.
- **Hydrate host — отсутствует в этом production пути.**
  Поле `_workspace` — MSBuildWorkspace, ordinary load создаёт этот host и использует
  provenance capture. **Delta:** Adhoc candidate через общий lifecycle и capabilities;
  не переносить только type поля без аудита всех callers/write paths.
- **A-WRITE — существующее ядро.**
  [WorkspaceWriteBoundary.Preflight](../../../Services/Workspace/WorkspaceWriteBoundary.cs#L17)
  проверяет session/base/publication и выполняет exact inverse.
  [ApplySolutionChangesToDiskAsync](../../../Services/Workspace/SolutionManager.cs#L689)
  ведёт запись через manager boundary. **Delta:** применить те же проверки и partial
  results к Adhoc/.cs adapters, включая all memberships; current code не является
  доказательством hydrate write equivalence.
- **A-STICKY/A-ADMISSION — существующие prepare/publication paths.**
  [LoadAndPrepareAsync](../../../Services/Workspace/SolutionManager.cs#L807) вызывает
  provenance gate/prepare и fail-closed publication; analyzer loader живёт у manager.
  **Delta:** portable disk evidence, повторная DLL проверка и новый session-bound
  snapshot перед тем же gate; не обходить existing banned/unavailable behavior.
- **Watcher/shared input — существующая модель.**
  [PullInputSnapshot](../../../Services/Workspace/SolutionManager.cs#L535),
  [WorkspaceInputSession](../../../Services/Models/WorkspaceInputSession.cs) и
  [role classifier](../../../Services/Workspace/WorkspaceDiskEventClassifier.cs#L14)
  уже дают session/revisions/coverage и distinguish known/potential/unknown paths.
  **Delta:** связать их с cache candidates, profile evidence, retention и гонками;
  не заводить второй index/session counter и не переписывать selected read policy.
- **Additional/config text — уже отдельный published-snapshot путь.**
  [WorkspaceNonCSharpDiskSync](../../../Services/Workspace/WorkspaceNonCSharpDiskSync.cs)
  читает updates и применяет их без Workspace.TryApplyChanges;
  [flush](../../../Services/Workspace/SolutionManager.cs#L2128) хранит этот слой
  отдельно. **Delta:** сохранить слой при hydrate/publication/refresh и all-membership
  mapping; нельзя заменить это записью additional files или analyzer config на диск.
- **WPF wpftmp — уже учитывается.**
  [QueueDiskPath](../../../Services/Workspace/SolutionManager.cs#L2646) исключает
  temporary project только если его нет в LoadedProjectPaths.
  **Delta:** сохранить поведение при новой session/input-map модели, проверить
  [WPF callback regressions](../../../RoslynMcpServer.Tests/Workspace/WpfTemporaryProjectCallbackTests.cs).
- **Own-write/BOM — существующий протокол.**
  [WorkspaceFilePersistence.WriteTextAsync](../../../Services/Workspace/WorkspaceFilePersistence.cs#L12)
  регистрирует begin, пишет `PersistedPayload.FromText`, подтверждает actual bytes
  через CommitOwnWrite, при ошибке abandon.
  [WorkspaceInputReconciler](../../../Services/Workspace/WorkspaceInputReconciler.cs)
  сравнивает наблюдение с committed bytes/revision.
  **Delta:** retain pending при cancel/reset/flush races и hydrate adapters,
  подтверждённые checks на external overwrite/ABA/partial result.

## Evidence, которое ещё требуется

- Existing regression suites для входа: WorkspaceWriteBoundaryTests,
  WorkspaceWriteEncodingTests, WorkspaceSharedSourceSyncTests,
  WorkspaceNonCSharpDiskSyncTests, WorkspaceInputFreshnessTests,
  WorkspaceInputWatcherDeliveryTests, WpfTemporaryProjectWatchingTests и
  AnalyzerLifecycle publication tests. Наличие этих файлов не равно прохождению
  V01–V23 на future hydrate; результаты task-00/следующих задач записываются отдельно.
- В repo найден NonCSharpDocumentTextSyncSpikeTests, но воспроизводимый harness/raw
  evidence именно `sdk-project-v1` из spec не найден. Это **evidence gap**:
  E0/task-02 должен восстановить проверяемый experiment с SDK/Roslyn versions,
  fixtures, commands и supported/unknown outcomes.
- Этот inventory — обязательный named input для E1/task-00 и E3/task-00.
  Их design явно отмечает reused/current paths и remaining delta; старый baseline
  1.3.21 не принимается за описание текущего SolutionManager.
