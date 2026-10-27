# D/E0/task-13 — реализация и validation

Дата: **2026-10-23**. Сервер **1.5.13**, ветка `research/workspace-load-performance`.
Реализация — Claude Opus 5.5. Независимого ревью не было. Офисный контроль pending.

## Что сделано

- `Services/Workspace/MissingSourceFileTextLoader.cs`. Пока файла нет, loader бросает
  `InvalidDataException`, и Roslyn сразу возвращает пустой текст с сообщением.
  Когда файл появился, loader читает его через новый `FileTextLoader(path, null)`.
- `Services/Workspace/MissingSourceFiles.cs`:
  - `Find` проверяет существование каждого пути один раз, параллельно. Учитываются
    документы, additional documents и analyzer config documents;
  - `TryInstallLoaders` меняет loader в самом workspace через защищённые
    `Workspace.On*TextLoaderChanged`, вызванные через reflection.
- `SolutionManager.LoadCoreAsync` вызывает это сразу после `OpenAndCaptureAsync`,
  до первой публикации, и пишет строку
  `workspace_missing_source_files {"documents","files","installed","elapsedMs","paths"}`
  (до 10 путей).
- `scripts/Measure-WorkspaceLoad.ps1` выводит эту строку в разделе каждого прогона
  `summary.md`. Для сервера до 1.5.13 он пишет «unavailable».
- Версия 1.5.13, CHANGELOG, абзац в `docs/ARCHITECTURE.md`.

## Почему замена не в опубликованном снимке

Изначальный план предлагал `Solution.WithDocumentTextLoader` в опубликованном снимке.
Декомпиляция Roslyn 5.9.0 показала, что это создало бы на диске пустой отсутствующий
файл при любой записи инструмента:

- кандидат `ApplySolutionChangesToDiskAsync` строится от опубликованного снимка;
- `Workspace.ApplyChangedDocument` сравнивает только ссылку на источник текста
  (`TextDocumentState.HasTextChanged`: «no content check is done»);
- `MSBuildWorkspace.ApplyDocumentTextChanged` сохраняет текст в файл.

Замена в самом workspace даёт одинаковый источник текста в снимке и в кандидате,
поэтому такой записи нет. Это проверяет интеграционный тест ниже.

## Проверки

| проверка | результат |
|---|---|
| `MissingSourceFileTests` (3 теста) | PASS |
| — отсутствующий файл с новым loader | пустой текст, < 500 мс |
| — контрольный документ с `FileTextLoader` и другим отсутствующим путём | ≥ 900 мс, тот же пустой текст |
| — файл создан после установки loader | текст читается с диска |
| — MSBuild-проект с `<Compile Include="Generated\VersionInfo.cs" />` без файла | строка лога `documents: 1, installed: true`; текст пустой за < 500 мс |
| — запись инструмента в другой документ от опубликованного снимка | `WorkspaceApplied`, отсутствующий файл не создан |
| — файл создан, событие Created, синхронизация | текст документа совпал с диском |
| мутация: установка loader отключена | интеграционный тест падает, 1032 мс на чтение |
| `scripts/Test-MeasureWorkspaceLoad.ps1`, `scripts/Test-FirstSearchDiagnostics.ps1` | PASS |
| Release build решения | PASS, только прежние предупреждения CS8603, CS8601, xUnit1031 |
| main CI `Category!=AnalyzerLifecycle&Category!=RequiresWindows`, Release, Linux (SourceStructure входит) | 1386 passed / 0 failed / 0 skipped |
| AnalyzerLifecycle, `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, Release, Linux | 13 passed / 0 failed / 0 skipped |

Наблюдение из теста: Roslyn делит уже загруженный текст между документами с одним
путём. Поэтому контроль использует другой путь. В офисе каждый проект с
`VersionInfo.cs` всё равно платил около 1 с: поиск идёт по проектам последовательно,
и, по-видимому, текст соседнего документа к тому моменту ещё не был загружен или был
вытеснен. Причина не проверялась.

## Не проверено

- Офисный эффект: ожидается, что проекты с `VersionInfo.cs` уложатся в десятки
  миллисекунд вместо ~1 с, а в сводке исключений будет одно `InvalidDataException`
  на чтение вместо 18 `FileNotFoundException`. Это проверит офисный прогон
  `Invoke-FirstSearchDiagnostics.ps1` на 1.5.13 вместе с A/B server GC.
- Стоимость проверки существования файлов на большой загрузке под Windows с
  антивирусом. Время пишется в `elapsedMs` строки лога.
- Полная загрузка `refreshAllDocuments` после переполнения watcher по-прежнему
  помечает граф устаревшим для отсутствующих путей. Это прежнее поведение, оно не менялось.
