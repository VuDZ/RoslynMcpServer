ID: E6-01
Severity: High
Category: Contract

Target:
epoch-6-contract-and-documentation.md / line 22-24

Claim:
Подхватывание нового build output имеет явную границу — успешная сборка и полный reload, если эпохой 3 не подтверждён более широкий контракт.

Evidence:
«Полный reload» не определён (E1-02). Текущий код уже может подхватить новый timestamp-каталог на *document edit* через `ApplyShadowCopyOverlayIfEnabled`, без load_workspace. Эпоха 2+6 хотят обратное: edit не публикует поколение. Итог эпохи 6 зависит от того, сузят ли поведение относительно shipped. Это смена контракта, не «документация факта». Строка 26-27 говорит: иной контракт требует обоснования до complete. Shipped контракт (случайный recopy на edit) не описан и будет стёрт.

Failure scenario:
1. Пишут docs: «после смены генератора — build, затем reload».
2. Пользователь только правит Consumer — в 1.3.5 иногда получает V2 (новый ticks), иногда V1 (lock overwrite).
3. После эпох 2/4 edit стабильно V1 до reload. Docs эпохи 6 описывают новое поведение как «всегда так было» либо наоборот обещают pickup на edit.

Suggested change:
Таблица: действие × shipped 1.3.5 × цель серии. Явно пометить сужение (edit больше не recopy) как breaking-for-workaround, не как уточнение формулировок. Определить reload = `reset_workspace` + `load_workspace` с флагом, и отдельно cache-hit.

Confidence:
High

---

ID: E6-02
Severity: High
Category: DocsProcess

Target:
epoch-6-contract-and-documentation.md / line 33-36; README.md / line 64-65

Claim:
Устранить в `docs/analyzer-shadow-copy/README.md` утверждение о пересчёте при каждом чтении. Обнаруженные ограничения исправляются в актуальной документации сразу; эпоха 6 — итоговая сверка, не откладывание.

Evidence:
Ложь «GetCurrentSolution пересчитывает overlay каждый раз» уже зафиксирована в README серии (lines 40-42) и в коде (`return _solution ?? ...`). `ARCHITECTURE.md`, `docs/analyzer-shadow-copy/README.md` line 27, remarks `ShadowCopyInSolutionAnalyzerReferencesAsync` всё ещё описывают recompute-on-read. Эпоха 6 зависит от итогов 1–5, значит правка логов откладывается на конец серии — прямое нарушение правила «сразу» из того же набора документов.

Failure scenario:
1. Агент читает ARCHITECTURE / historical README, строит recompute-on-read.
2. Добавляет вызов copier в `GetCurrentSolution` «как в docs».
3. Каждый semantic request делает File.Copy.

Suggested change:
Эпоха 6 не владеет этим diff. Править ARCHITECTURE + historical README + remarks сейчас, статусом «код v1.3.5», без ожидания эпох 1–5. Эпоха 6 сверяет уже поправленное.

Confidence:
High

---

ID: E6-03
Severity: Medium
Category: Lifecycle

Target:
epoch-6-contract-and-documentation.md / line 18-19

Claim:
Описаны все события обновления: load, явный reload, document edit, disk-watcher sync, применение solution changes, clear/dispose.

Evidence:
Список не содержит cache-hit `load_workspace` (самый частый повторный вызов), смену флага shadow copy на уже загруженном workspace, `_projectGraphStale` reopen, `ApplyShadowCopyOverlayIfEnabled` как скрытый recopy. «Явный reload» дублирует load без определения. `GetCurrentSolution()` без lock не событие обновления, но читатель считает его источником истины.

Failure scenario:
1. Матрица эпохи 6 строится по этому списку.
2. Баг same-path flag-off (E1-04) не попадает в контракт.
3. Пользовательский порядок «выключи флаг, load снова» не документирован.

Suggested change:
События брать из реальных входных точек: `LoadCoreAsync` cache hit / miss, `ShadowCopyInSolutionAnalyzerReferencesAsync`, четыре `TryApplyChanges` site, `ClearWorkspaceAsync`. Не из желаемого глоссария.

Confidence:
High

---

ID: E6-04
Severity: Medium
Category: Scope

Target:
epoch-6-contract-and-documentation.md / line 7-9, 20-21, 61-62

Claim:
Эпоха не требует новой функции продукта. Обычное редактирование документа переиспользует подготовленные analyzer references и не инициирует повторную публикацию того же поколения. Результат — точный контракт.

Evidence:
Reuse mapping без recopy — поведение, которого нет в 1.3.5 и которое должны поставить эпохи 2 и 4. Если 2/4 не сделаны, эпоха 6 либо документирует recopy-on-edit (текущее), либо предписывает reuse (ложь). «Нет новой функции» ложно, если reuse ещё не в коде. Зависимость «итоги 1–5» превращает docs-epoch в gate на несделанную работу, при этом complete критерий серии — документация.

Failure scenario:
1. 2/4 частично сделаны, 3 deferred.
2. Эпоха 6 описывает reuse-on-edit как факт.
3. В коде edit всё ещё вызывает copier — регресс docs относительно кода, ради которого эпоха 6 затевалась.

Suggested change:
Эпоха 6 фиксирует только существующий код + явные deferred. Reuse-on-edit — acceptance эпох 2/4, не docs-only утверждение. Не ставить эпоху 6 в зависимость от 3.

Confidence:
High
