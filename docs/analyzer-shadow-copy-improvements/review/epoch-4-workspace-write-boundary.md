ID: E4-01
Severity: High
Category: Concurrency

Target:
epoch-4-workspace-write-boundary.md / line 18-20, 52-53

Claim:
Сохраняется учёт происхождения замен и связь с исходным снимком. Учитывается жизненный цикл снимков, ещё используемых выполняющимися операциями. Переработка всей модели конкурентного редактирования — вне этой эпохи.

Evidence:
`Solution` иммутабелен; in-flight tool держит свой объект. Публикация overlay — одно поле `_solution`. «Связь с исходным снимком» + «снимки in-flight операций» = lineage/MVCC. Это и есть переработка конкурентной модели, объявленная non-goal. Существующий контракт: `TryApplyChanges` false при version conflict. Отдельного учёта snapshot id нет.

Failure scenario:
1. Реализуют provenance table keyed by Solution instance/version.
2. Либо не реализуют и рисуют галочку «учёт снимков» через текущий revert.
3. Оба варианта расходятся со спекой: первое нарушает non-goal, второе — requirement.

Suggested change:
Убрать lifecycle in-flight snapshots из эпохи 4. Оставить: mapping (projectId, original refs, shadow refs) на текущую сессию load. Stale candidate → существующий false от `TryApplyChanges`, без новой политики.

Confidence:
High

---

ID: E4-02
Severity: High
Category: ApplyReject

Target:
epoch-4-workspace-write-boundary.md / line 21-25

Claim:
Снимаются именно overlay-изменения. Нельзя затирать любые отличия analyzer references. Пока намеренное редактирование analyzer references не поддерживается, непонятный diff отклоняется с объяснением, не молча.

Evidence:
`ApplySolutionChangesToDiskAsync` применяет один `TryApplyChanges` ко всему candidate (документы + project state). Отклонение analyzer diff отклоняет rename/code-fix в том же solution. Roslyn `CodeAction` может тронуть analyzer list. Текущий revert молча делает `WithProjectAnalyzerReferences(original)` (`SolutionManager.cs:365-368`) и даёт документам пройти. Новая политика строже и ломает shipped save path.

Failure scenario:
1. Overlay on, code fix добавляет analyzer ref или меняет порядок.
2. Граница: unknown diff → отказ.
3. Исправление/rename не записаны; `.cs` на диске из первой половины метода уже могут быть записаны (цикл `File.WriteAllTextAsync` идёт *до* `TryApplyChanges`, `SolutionManager.cs:473-510`). Объяснение «analyzer diff», диск уже изменён, workspace нет.

Suggested change:
Strip known overlay mapping, остальные analyzer diffs оставить workspace. Не reject-all. Если reject всё же нужен — сначала не писать файлы до успешного apply (сейчас порядок обратный; это отдельный дефект границы, спека его не видит).

Confidence:
High

---

ID: E4-03
Severity: High
Category: Lock

Target:
epoch-4-workspace-write-boundary.md / line 14-17, 35-39

Claim:
Все production `MSBuildWorkspace.TryApplyChanges` идут через границу: снять overlay, затем запись. После успешной записи — новый overlay-снимок. Читатель видит целиком старый или новый снимок.

Evidence:
Граница защищает `.csproj` от shadow `<Analyzer Include>`. Она не защищает от загрузки analyzer с `workspace.CurrentSolution` при document `TryApplyChanges` (E1-07). `UpdateDocumentInMemoryAsync` специально строит candidate *без* overlay. Пропуск через границу «strip overlay» — no-op. Компиляция workspace solution с original path всё ещё возможна. Atomic snapshot `_solution` уже обеспечивается одним присваиванием ссылки; требование «без частично заменённых ссылок» не про `TryApplyChanges`, а про copier loop, который сейчас не публикует промежуточный `_solution`.

Failure scenario:
1. Эпоха 4 «закрыта»: один helper вокруг четырёх call sites.
2. Existing-path fixture после edit лочит real DLL (E1-07).
3. Считают lock проблемой эпохи 3/загрузчика.

Suggested change:
Граница записи ≠ граница загрузки analyzer. Либо запретить compilation/load analyzer на `CurrentSolution` при включённом флаге, либо признать anti-lock только для missing original path. Не прятать это под TryApplyChanges funnel.

Confidence:
High

---

ID: E4-04
Severity: Medium
Category: FailurePath

Target:
epoch-4-workspace-write-boundary.md / line 36-37

Claim:
При неуспешном `TryApplyChanges` candidate не публикуется как текущее состояние.

Evidence:
`ApplySolutionChangesToDiskAsync` при false: foreach changed path `UpdateDocumentInMemoryUnderLockAsync` — публикация текстов в workspace и `_solution = ApplyShadowCopyOverlayIfEnabled(...)` (`SolutionManager.cs:523-535`, `568-570`). Файлы уже на диске. Спека запрещает публикацию candidate; код публикует subset через другой вход. Единая граница, если тупо обернёт только успешный путь, сохранит этот split-brain.

Failure scenario:
1. Overlay-rename, `TryApplyChanges` false (version).
2. Fallback применяет тексты, overlay пересобирается.
3. Спека эпохи 4: «не публиковать» — регресс рабочего fallback, либо граница не покрывает fallback и «все production вызовы через одну границу» ложно.

Suggested change:
Описать fallback как часть границы или удалить его явно. «Не публиковать candidate» ≠ «не применять уже записанные файлы в память». Сейчас формулировка смешивает.

Confidence:
High

---

ID: E4-05
Severity: High
Category: Mapping

Target:
epoch-4-workspace-write-boundary.md / line 18-23, 43-45

Claim:
Снимаются именно изменения overlay. Unit-тесты: снятие известного overlay, отсутствие overlay, неизвестный analyzer diff, устаревший снимок, состав проектов. Может выполняться независимо от эпох 2–3 (README).

Evidence:
«Известный overlay» требует стабильного mapping original↔shadow. Сейчас overlay — повторный copier, shadow path от ticks/copy. Без эпохи 2 mapping в памяти revert умеет только «весь список как в workspace» (уже покрыто `SolutionManagerAnalyzerOverlayTests`). Тест «неизвестный diff» при таком revert не отличим: любой diff списка затирается. Независимость от эпохи 2 ложна (R-03).

Failure scenario:
1. Пишут unit-тесты на текущем SequenceEqual wipe, называют это «снятие известного overlay».
2. Unknown diff тоже wipe — тест unknown либо красный, либо подгоняют ожидание wipe.
3. После эпохи 2 поведение меняется, тесты эпохи 4 врут.

Suggested change:
Эпоха 4 после появления mapping. До него requirement «только overlay diffs» не реализуем без угадывания по path prefix temp.

Confidence:
High

---

ID: E4-06
Severity: Medium
Category: CallSites

Target:
epoch-4-workspace-write-boundary.md / line 7-10, 48-49

Claim:
Исторический дефект может вернуться через любой новый `TryApplyChanges`, если передать solution с overlay. Disk-watcher sync проходит через ту же границу без потери overlay.

Evidence:
Три из четырёх production sites сознательно *не* берут overlay (`UpdateDocumentInMemory*`, `FlushDirtyDocumentsUnderLockAsync`). Дефект v1.3.4 — apply overlay solution. Риск — новый site от `GetCurrentSolution()`, как `ApplySolutionChangesToDiskAsync`. Funnel всех sites через strip полезен как дисциплина, но watcher «без потери overlay» зависит от post-apply `ApplyShadowCopyOverlayIfEnabled` (сейчас с File.Copy, E1-09), не от strip. Граница записи не сохраняет overlay; его восстанавливает post-step, который эпоха 2 хочет лишить I/O.

Failure scenario:
1. Watcher прогоняют через strip (no-op) + забывают post-apply mapping.
2. `_solution = workspace.CurrentSolution` без overlay — генерация пропала, `.csproj` цел.
3. Acceptance «нет temp Analyzer» зелёный, генерация нет.

Suggested change:
Post-apply publish overlay — обязательная часть границы, не отдельный абзац. Проверка watcher: SG marker + byte-identical csproj, не только «прошёл через helper».

Confidence:
High
