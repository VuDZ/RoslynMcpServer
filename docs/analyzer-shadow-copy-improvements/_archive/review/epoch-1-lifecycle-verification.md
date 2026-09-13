ID: E1-01
Severity: Blocker
Category: Observability

Target:
epoch-1-lifecycle-verification.md / line 20-22

Claim:
Семантическая проверка читает значение символа либо generated document; отсутствия ошибок компиляции недостаточно. Маркер версии — константа `V1`/`V2`.

Evidence:
В репозитории нет вызовов `GetSourceGeneratedDocumentsAsync`. `find_symbol_definition` по generated имени — известный out-of-scope (`SymbolFinder`, roslyn#63375). `get_diagnostics_for_file` смотрит `semanticModel.GetDiagnostics()` (`CodeAnalysisTools.cs`, diagnostics-only). `get_file_content` читает диск, не in-memory SG. MCP-инструмента, возвращающего константу или текст generated document, нет. Инвариант 6 серии запрещает новые MCP-параметры без отдельного обоснования; эпоха 1 не вводит test-only API.

Failure scenario:
1. V1 и V2 эмитят один тип/имя, меняется только константа.
2. Oracle — `get_diagnostics_for_file`: оба раза «No compiler errors».
3. Baseline записывает «reload исполняет новую версию», хотя версия не наблюдалась.

Suggested change:
Зафиксировать oracle, который существует: in-process `project.GetSourceGeneratedDocumentsAsync()` в тестовом хосте `SolutionManager`, либо менять наблюдаемое имя символа в Consumer (тогда шаг 7 обязан менять Consumer и это уже не «только генератор»). Запретить MCP diagnostics как oracle версии.

Confidence:
High

---

ID: E1-02
Severity: Blocker
Category: Lifecycle

Target:
epoch-1-lifecycle-verification.md / line 36-37

Claim:
Изменить генератор на `V2`, пересобрать и выполнить полный reload workspace без перезапуска процесса. Проверить, какая версия реально исполняется.

Evidence:
`LoadCoreAsync` при том же path+Configuration+Platform+TFM и `!_projectGraphStale` не открывает solution заново, возвращает `_solution ?? _workspace.CurrentSolution` (`SolutionManager.cs:653-670`). Флаг `shadowCopyInSolutionAnalyzers` в ключ кеша не входит. `reset_workspace` (`ClearWorkspaceAsync`) обнуляет флаги overlay, но не `_analyzerAssemblyLoader`. «Полный reload» не определён: cache hit + повторный `ShadowCopyInSolutionAnalyzerReferencesAsync`, либо dispose workspace + open, либо ничего из этого.

Failure scenario:
1. Тест вызывает `load_workspace` на тот же `.sln` после сборки V2.
2. Попадает в RAM-кеш; MSBuild заново output не резолвит; `CompilationOutputInfo` старый.
3. Либо наоборот: кеш hit, shadow copy всё же гоняется с диска (WorkspaceTools после LoadAsync), timestamp сменился, появляется новый path — это не «reload workspace», а побочный recopy. Результаты шага 7 нельзя сопоставить эпохе 3.

Suggested change:
Разобрать три операции с именами: `cache-hit load_workspace`, `reset_workspace+load_workspace`, `process restart`. Шаг 7 обязан указать одну. Для эпохи 3 отдельно прогнать все три.

Confidence:
High

---

ID: E1-03
Severity: High
Category: Isolation

Target:
epoch-1-lifecycle-verification.md / line 38-39

Claim:
Загрузить другое решение и затем решение с выключенным флагом; проверить, что состояние overlay не переносится между загрузками.

Evidence:
`LoadCoreAsync` на другой path сбрасывает `_shadowCopyAnalyzersEnabled` / `_shadowCopyRootDirectory` (`SolutionManager.cs:679-680`). `_analyzerAssemblyLoader` — `readonly` поле, живёт с `SolutionManager` (`SolutionManager.cs:55`). `ClearWorkspaceAsync` его не заменяет. `InProcessAnalyzerAssemblyLoader` кеширует `Assembly` по пути и вешает `AppDomain.CurrentDomain.AssemblyResolve` один раз навсегда (`InProcessAnalyzerAssemblyLoader.cs:31, 60-76`).

Failure scenario:
1. Solution A, генератор identity `Generator, Version=0.0.0.0`. Overlay включён, сборка в loader.
2. `load_workspace` Solution B с одноимённым генератором, флаг выключен. Флаги overlay чистые.
3. Если B всё же грузит тот же simple name через свой analyzer path, `LoadFrom`/resolve может вернуть сборку A. Шаг 8 зелёный по `.csproj`/флагам и слепой к исполнению.

Suggested change:
Шаг 8 проверять не флаги, а исполняемый маркер + что loader не перенёс identity. Иначе это не isolation test, а проверка reset двух bool.

Confidence:
High

---

ID: E1-04
Severity: High
Category: Lifecycle

Target:
epoch-1-lifecycle-verification.md / line 38-39

Claim:
Состояние overlay не переносится между загрузками (в том числе «затем решение с выключенным флагом»).

Evidence:
Повторный `load_workspace` того же path — cache hit, флаги overlay не сбрасываются (`IsSameLoadCache` ветка не трогает `_shadowCopyAnalyzersEnabled`). `ShadowCopyInSolutionAnalyzerReferencesAsync` вызывается только если аргумент `true` (`WorkspaceTools.cs`, блок `if (shadowCopyInSolutionAnalyzers)`). Вызов с `false` не снимает overlay: `_solution` остаётся переписанным.

Failure scenario:
1. `load_workspace(..., shadowCopyInSolutionAnalyzers=true)`.
2. Повторно тот же path с `false` (пользователь выключил workaround).
3. `GetCurrentSolution()` всё ещё shadow refs. Тест шага 8 с *другим* path этого не ловит.

Suggested change:
Отдельный случай: same-path, flag off, cache hit. Зафиксировать фактическое поведение (overlay липнет) как дефект или как контракт. Сейчас спецификация подразумевает обратное.

Confidence:
High

---

ID: E1-05
Severity: High
Category: Observability

Target:
epoch-1-lifecycle-verification.md / line 27-28

Claim:
Загрузить workspace с включённым shadow copy, запросить семантическую модель Consumer, подтвердить маркер `V1`.

Evidence:
Единственный shipped путь к semantic model Consumer-файла — `FindDocumentAsync` → `document.GetSemanticModelAsync()` (`get_diagnostics_for_file`). Маркер `V1` в semantic model сам не появляется, пока его не прочитать из generated tree или `GetConstantValue`. Историческая проверка v1.3.5 была «нет CS0103» (`docs/analyzer-shadow-copy/epoch-3-inmemory-overlay-fix.md`, verification). Это ровно тот oracle, который эпоха 1 запрещает как достаточный.

Failure scenario:
1. Overlay сломан, генератор не бежит, в Consumer нет обращения к generated типу — diagnostics чистые.
2. Шаг 2 «подтвердили V1».
3. Дальнейшие шаги измеряют стабильность отсутствия ошибок, не генерации.

Suggested change:
Consumer обязан содержать использование generated члена, зависящее от версии; либо in-process чтение SG text. Не опираться на пустой diagnostic list.

Confidence:
High

---

ID: E1-06
Severity: High
Category: WritePath

Target:
epoch-1-lifecycle-verification.md / line 29-33

Claim:
Изменить обычный документ через штатный путь редактирования и отдельно через disk-watcher sync. После каждого изменения повторить семантическую проверку `V1`.

Evidence:
Штатных write path два, и они не изоморфны. `apply_patch` / `update_file_content`: диск + `UpdateDocumentInMemoryAsync` = `workspace.CurrentSolution.WithDocumentText` + `TryApplyChanges` без revert + `ApplyShadowCopyOverlayIfEnabled` (полный recopy). `rename_symbol` / AST / code fix: overlay-solution → `ApplySolutionChangesToDiskAsync` → `RevertAnalyzerReferenceOverlayForApply` → `TryApplyChanges` → снова `ApplyShadowCopyOverlayIfEnabled`. Disk-watcher: `WorkspaceDocumentDiskSync.ApplyAsync(workspace.CurrentSolution)` + `TryApplyChanges` без revert (`SolutionManager.cs:781-803`). Спека говорит «штатный путь» в единственном числе.

Failure scenario:
1. Тест использует только `apply_patch` — зелёный.
2. Агент делает `rename_symbol` — другой diff доходит до `TryApplyChanges`.
3. Либо наоборот: watcher-тест вызывает инструмент без flush (E1-08) и пишет ложный fail.

Suggested change:
Три именованных шага: text-edit (`UpdateDocumentInMemoryAsync`), overlay-apply (`ApplySolutionChangesToDiskAsync`), watcher+flush. Все три обязательны. Нельзя закрыть эпоху одним из них.

Confidence:
High

---

ID: E1-07
Severity: High
Category: Lock

Target:
epoch-1-lifecycle-verification.md / line 34-35

Claim:
Пока сервер работает, принудительно пересобрать Generator. Не считать успешную инкрементальную сборку без записи DLL доказательством отсутствия lock.

Evidence:
Исторический GenRepro: `AnalyzerReference.FullPath` отсутствует на диске, поэтому workspace solution не открывает real output. Вторая заявленная цель флага — не лочить существующий правильный output (`ARCHITECTURE.md`, `AnalyzerReferenceShadowCopier` summary). Документные `TryApplyChanges` идут по `workspace.CurrentSolution` с исходными AnalyzerReferences (`UpdateDocumentInMemoryAsync`, disk-sync). Если исходный path существует, компиляция workspace solution может сделать `LoadFrom` real DLL до/параллельно overlay. Шаги 3–5 уже произошли до шага 6. Эпоха 1 описывает один repro с перенаправленным `OutputPath` (missing path) и этим проверяет lock.

Failure scenario:
1. Fixture только missing-path — шаг 6 зелёный.
2. Тот же флаг на solution, где analyzer path существует (только anti-lock).
3. После `apply_patch` `dotnet build` Generator → MSB3027. Матрица врёт, что lock закрыт.

Suggested change:
Два fixture: missing resolved path; existing resolved path. Шаг 6 на обоих, и обязательно после text-edit, не на idle workspace после load.

Confidence:
High

---

ID: E1-08
Severity: Medium
Category: Watcher

Target:
epoch-1-lifecycle-verification.md / line 29-30, 62-63

Claim:
Изменить документ через disk-watcher sync. Тайминги watcher проверять ожиданием наблюдаемого состояния с timeout, не фиксированными задержками.

Evidence:
Watcher только кладёт путь в `_dirtySourcePaths`. Применение — `FlushDirtyDocumentsUnderLockAsync`, вызываемый из `EnsureDiskChangesAppliedAsync` / `FindDocumentAsync` / `GetCurrentSolutionAfterDiskSyncAsync`. Сам факт события FSW не меняет `_solution`. «Наблюдаемое состояние» без flush-вызова никогда не наступит. Timeout будет ждать вечно, если oracle — `GetCurrentSolution()` как в `RenameSymbol`.

Failure scenario:
1. Тест пишет `.cs` на диск, ждёт, пока `GetCurrentSolution().GetDocument().GetText()` изменится.
2. Timeout, запись «watcher broken».
3. Либо вызывает `get_diagnostics_for_file` (flush есть) и не тестирует watcher отдельно от flush-on-read.

Suggested change:
Oracle watcher = dirty set попал в flush, затем overlay snapshot. Инструмент без `EnsureDiskChangesAppliedAsync` не является oracle. Не путать FSW delivery с публикацией `_solution`.

Confidence:
High

---

ID: E1-09
Severity: High
Category: OverlayLoss

Target:
epoch-1-lifecycle-verification.md / line 29-33, 67-68

Claim:
После каждого изменения документа генерация `V1` сохраняется. Есть тесты повторного анализа, редактирования, disk sync.

Evidence:
После каждого успешного `TryApplyChanges` вызывается `ApplyShadowCopyOverlayIfEnabled` → `ShadowCopyInSolutionAnalyzerReferences` → `File.Copy(source, shadow, overwrite: true)` (`AnalyzerReferenceShadowCopier.cs:176-183`). При `IOException` ссылка не переписывается, остаётся оригинал (`catch` → `Applied: false`). Caller выбрасывает `results` (`ApplyShadowCopyOverlayIfEnabled`: `var (rewritten, _)`). `_shadowCopyAnalyzersEnabled` остаётся true. `InProcessAnalyzerAssemblyLoader.LoadFromPath` уже мог открыть предыдущий shadow path.

Failure scenario:
1. Load+overlay, semantic model загрузил shadow DLL.
2. `apply_patch` → recopy overwrite на locked path → skip.
3. `_solution` без shadow refs, генерация пропадает, флаг всё ещё on, revert на следующем rename всё ещё активен. Критерий «сохраняется V1» либо красный (правильно) либо зелёный, если oracle — diagnostics без использования generated типа (E1-05).

Suggested change:
Шаги 3–4 считать тестом потери overlay, не «happy path lifecycle». Логировать RewriteResult на reoverlay; сейчас они отбрасываются. Не закрывать эпоху 1, пока этот путь не измерен отдельно от шага 2.

Confidence:
High

---

ID: E1-10
Severity: Medium
Category: Scope

Target:
epoch-1-lifecycle-verification.md / line 42-50

Claim:
Дополнительные случаи (нет output; несколько Consumer; helper DLL; два генератора с разными версиями helper) формируют baseline. Последние два — baseline эпохи 3, не обязательство поддержать.

Evidence:
Без oracle исполнения (E1-01) и без определения reload (E1-02) «baseline эпохи 3» из эпохи 1 будет набором skip/fail без различения file-prep vs load. Несколько Consumer на первом load копируют в один и тот же `{ticks}` path дважды (`CopyToShadowDirectory` детерминирован именем проекта генератора + ticks). Это уже lock/overwrite на шаге 2, не в эпохе 3.

Failure scenario:
1. Доп. случай «два Consumer» падает на первом `File.Copy` overwrite ещё до helper/ALC.
2. Fail вешают на эпоху 3.
3. Эпоха 3 начинает ALC против файловой гонки.

Suggested change:
«Несколько Consumer» — обязательный случай эпохи 1/2, не эпохи 3. Helper/version conflict не запускать, пока нет executable oracle и стабильных поколений.

Confidence:
High
