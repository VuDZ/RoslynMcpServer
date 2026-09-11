# Сводка оценки review

Статус: **предложения по результатам review; исходная спецификация не изменена**.
Все 42 замечания разобраны индивидуально в [ответах](README.md).
Эта сводка не является утверждённой новой архитектурой или разрешением реализовать
все перечисленные варианты. Приоритет имеют требования U/H1/H2/H3 из README ответа.

## Решения по finding IDs

**ACCEPT — 20:**

- R-03.
- E1-01, E1-02, E1-03, E1-04, E1-05, E1-07, E1-08, E1-09.
- E2-02, E2-03, E2-04, E2-05.
- E3-06.
- E4-04, E4-05, E4-06.
- E5-03.
- E6-01, E6-03.

**PARTIALLY ACCEPT — 19:**

- R-01, R-02.
- E1-06, E1-10.
- E2-01, E2-06.
- E3-01, E3-02, E3-03, E3-04, E3-05.
- E4-01, E4-02, E4-03.
- E5-02, E5-04, E5-05.
- E6-02, E6-04.

**REJECT — 2:**

- R-04: файловая мутация и binding уже разделены в разных эпохах; их перечисление
  в одном списке вопросов не отменяет этого разделения.
- E2-07: предложенная критиком ticks-only реализация нарушает уже явные требования
  content identity и приёмку same-timestamp mutation. Новый критерий не требуется.

**NEEDS CLARIFICATION — 1:**

- E5-01: подтверждающие metadata не установлены. Нельзя выбрать между обязательным
  provenance, сохранением missing-path эвристики и ограничением поддержки без
  evidence и решения о допустимом риске.

Частичное принятие не означает согласия с каждым Suggested change. В частности,
не приняты автоматический pass-through unknown analyzer diffs, удаление живых
shadow generations на clear и признание отсутствующего файла доказательством
его происхождения.

## Наиболее важные выводы

1. Тестовый oracle должен реально читать generated marker внутри production
   lifecycle тестового хоста. Новый MCP инструмент не нужен. Чистая диагностика
   не проверяет исполнение V2.
2. Cache-hit load, reset+load и process restart — разные операции. Flag-off на
   cache hit и loader lifetime после reset нельзя скрывать словом reload.
3. Сохранение overlay после edit и обновление версии генератора — независимые
   задачи: первая требует reuse mapping, вторая — доказанного binding contract.
4. Запись файлов до apply и последующий fallback делают «единую границу записи»
   существенно шире простого strip helper. Новый reject должен быть до записей.
5. ALC, обязательный hot reload, MVCC и shared persistent cache не заданы U как
   самостоятельные цели. Их стоимость нельзя оправдывать только удобством автора
   спецификации. При этом поддержанный режим не должен молча выдавать чужой код.
6. Корректность критики проверялась по коду: например, rename делает flush через
   `FindDocumentAsync`; его потенциальная проблема — повторное получение другого
   snapshot, а не отсутствие этого вызова.

## Полный перечень предлагаемых изменений

Ниже перечислены изменения, необходимые при включении принятых частей findings.
Подробные условия и непринятые части остаются в индивидуальных ответах.

| Изменение | Findings | Затрагиваемые разделы и результат |
| --- | --- | --- |
| C-01. Развести текущее поведение, цель и release gate | R-01, R-02 | README «Цель», «Инварианты»; эпоха 6 «Контракт снимков»: целостность, freshness после flush, область подтверждённой матрицы |
| C-02. Определить операции и флаг | E1-02, E1-04, E2-02, E6-01, E6-03 | Сценарий 1, контракты 2/3/6: cached load, reset+load, restart, stale reopen, true/false/omitted; не вводить автоматически forceReload |
| C-03. Сделать oracle исполняемым | E1-01, E1-05, E1-03 | Эпоха 1 «Сценарий», «Воспроизводимость»: in-process test host с MSBuild bootstrap, один Project snapshot, точная generated constant, Consumer use, negative control, A/B isolation |
| C-04. Полная матрица путей обновления | E1-06, E1-07, E1-08, E1-09, E1-10, E4-06, E5-05 | Эпоха 1: text edit, overlay apply, watcher delivery+flush, several Consumers, missing/existing path, forced rebuild, foreign same-name analyzer; подготовка/загрузка/исполнение проверяются отдельно |
| C-05. Наблюдать потери и lazy failures | E1-09, E3-06, E5-04 | Эпохи 2/3/5: не терять reapply results; prepared/rewrite не выдавать за execution success; reason codes, generation correlation, first-use errors |
| C-06. Зафиксировать mapping и I/O-free reapply | R-03, E2-04, E4-05, E6-04 | Эпоха 2 «Требования» и эпоха 4 dependencies: подготовленное original↔shadow mapping; document edit/watcher не хешируют и не копируют DLL |
| C-07. Уточнить content identity и layout evolution | E2-01, E2-02 | Эпоха 2 «Требования», «Алгоритм»: versioned policy + набор relative paths/hashes; main-only и dependency-set не смешиваются; hash на обещанном refresh |
| C-08. Уточнить протокол публикации | E2-03, E2-05 | Эпоха 2 «Алгоритм», «Приёмка»: manifest записан последним до move, валидация готового каталога, отказ при повреждении, interruption tests; cross-process test при shared root |
| C-09. Ресурсы раньше loader overhaul | E2-06 | Эпоха 2 «Ошибки и границы»: измерения размера/роста, disk-full, ownership, поддержанная очистка после остановки владельцев; root/retention decision остаётся открытым |
| C-10. Feasibility и выбранный loader contract | E3-01, E3-05 | Эпоха 3 «Требуемый контракт», «Решение», «Приёмка»: исследование отдельно от реализации; same-process V2 или обоснованный restart режим; безопасный отказ при unsupported refresh |
| C-11. Разделить discovery и binding | E3-02, E3-03, E3-04 | Эпоха 3 «Подготовка зависимостей»: fixture с явными helpers прежде ALC; sharing host contracts до эксперимента; scope resolution и реальные owner lifetimes |
| C-12. Ограничить сложность snapshot tracking | E4-01 | Эпоха 4 «Требования», non-goals: минимальный operation context/mapping, без общего MVCC/истории snapshots; проверка stale до side effects |
| C-13. Включить preflight и failure workflow | E4-02, E4-04, E4-06 | Эпоха 4 «Обновление overlay», «Приёмка»: preflight до записи; unknown diff policy; success/partial persistence/reconciliation; post-apply overlay, cancellation и I/O failure tests |
| C-14. Отдельно проверить границу загрузки | E1-07, E4-03 | Эпохи 1/4 и будущий архитектурный decision: actual load path и existing-path lock coverage; не сужать anti-lock обещание без evidence |
| C-15. Provenance discovery до matcher rollout | E5-01, E5-02, E5-03, E5-04, E5-05 | Эпоха 5: feasibility gate, decision table существующих/отсутствующих/недоступных путей, loaded inner Project как источник TFM/output, reason codes и foreign-marker regression. Итоговый fallback не выбран |
| C-16. Ранний factual docs fix и итоговый аудит | E6-01, E6-02, E6-03, E6-04 | В будущем: ARCHITECTURE, historical README и C remarks recompute-on-read; release behavior matrix; reuse acceptance в 2/4, этап 6 только сверяет код/выбранные ограничения и deferred |

## Более широкие и вновь выявленные архитектурные риски

Эти пункты не получают ID исходного review и не добавляются молча в scope
реализации. N-01 и N-04 развивают проблемы критика; остальные обнаружены при
сверке связанных сценариев. Ни один runtime race ниже не объявляется воспроизведённым.

### N-01 — Неатомарность document persistence

**Факт по коду:** `ApplySolutionChangesToDiskAsync` пишет `.cs` до semaphore и
до apply, затем при false синхронизирует тексты по одному. Cancellation/I/O
error может прервать метод между файлами. Возврат списка paths не описывает
полноту workspace/project-state применения. Это шире E4-02/E4-04 и не специфично
для analyzer overlay.

**Следствие:** preflight устраняет только отказы, известные до записи. Он не
создаёт транзакцию нескольких файлов и MSBuild project persistence. Нельзя
просто переставить `TryApplyChanges` перед записями и объявить атомарность:
сам `MSBuildWorkspace` может записывать файлы.

**Предлагаемый отдельный scope:** описать допустимую partial-success семантику,
reconciliation и внутренний result type. Решение о rollback/transaction engine
выносится отдельно; эта серия не должна вводить его незаметно.

### N-02 — Смешение snapshot внутри операции

**Факт:** `RenameSymbol` получает `Document` через `FindDocumentAsync`, вычисляет
symbol, затем снова получает `GetCurrentSolution` для baseSolution. Между await
может измениться session/snapshot. Аналогично `GetCurrentSolutionAfterDiskSyncAsync`
сначала освобождает lock после flush, потом читает snapshot.

**Риск:** symbol из одного snapshot используется с другим solution, вплоть до
смены workspace. Это не доказывается отсутствием lock у getter и не решается
общим исключением rename из freshness гарантий.

**Далее:** проверить конкурентный сценарий и минимальный per-operation snapshot
contract; не превращать это автоматически в MVCC.

### N-03 — Load и включение overlay не одна операция

**Факт:** W вызывает `LoadAsync`, затем отдельный
`ShadowCopyInSolutionAnalyzerReferencesAsync` с новым захватом lock; последний
использует текущий `_loadedPath`, а не expected path исходного запроса.

**Риск:** при разрешённых конкурентных tool calls другой load может вклиниться,
и opt-in/summary первого запроса окажется привязан к другой solution. Один
stdio session не доказывает отсутствие конкурентной обработки запросов.

**Далее:** проверить фактическую модель MCP dispatch. Если race достижим,
нужен expected-session check или общий operation scope; не новый public parameter.

### N-04 — Raw workspace и окно до overlay

Existing-path lock может появиться при ранней либо сторонней семантической
операции на raw workspace до подготовки overlay. Write strip не изолирует
загрузку. Review называет `TryApplyChanges` подозреваемым, но конкретный триггер
не установлен. Проверки E1-07 должны локализовать load path прежде выбора
другого loader/backend или сужения исходной задачи H1.

### N-05 — Manifest integrity не равна trust boundary

Новый читаемый с диска manifest вводит relative paths и reuse готовых файлов.
Хеши не удостоверяют происхождение, если данные и manifest можно подменить
вместе. Нужны безопасное разрешение путей внутри generation root, отказ от
абсолютных/выходящих путей и ownership каталога. Проверку существующего manifest
нельзя заменить доверием к полю hash без проверки файлов.

Это не требование sandbox для недоверенных генераторов: U такого режима не
задаёт, а ALC не является security boundary. Не требуется автоматически подпись,
удалённый trust service или отдельный процесс; нужна локальная модель доверия
для cache и защита от ошибочных путей, особенно перед cleanup.

### N-06 — Requested, prepared и active state сейчас смешаны

**Факт:** C включает `_shadowCopyAnalyzersEnabled` только при хотя бы одном
`Applied`. При partial rewrite один bool не сообщает, какие references активны;
при полном fail после предыдущего успеха initial-enable path и reapply path
ведут себя по-разному. Это связано с E1-04/E1-09/E3-06, но шире поля `SkipReason`.

**Далее:** минимально разделить requested mode, mapping активных references и
результат последней попытки refresh. Не проектировать обобщённый state machine
с persistence без необходимости. Сохранение stale допустимо только с наблюдаемым
результатом, а не скрытым `Applied=true`.

### N-07 — Процессная изоляция тестов

T прямо указывает отсутствие MSBuild bootstrap. Генераторы и L живут дольше
workspace: параллельные tests в одном xUnit процессе могут загрязнять друг друга
и выдавать order-dependent результаты. Нужен изолированный test host на сценарий;
внутри lifecycle/reload сценария процесс намеренно один. Cross-process publication
использует два таких host с одним test-owned root. Не смешивать это с публичным
MCP API и не хранить внешний GenRepro как production project.

## Downstream impact

- **API:** новый public MCP инструмент/параметр не требуется для oracle, mapping
  или reset+load. Внутренние prepare/apply results и operation context могут
  измениться; adapters должны корректно отражать partial failure. False/omitted
  semantics требуют compatibility решения, а не скрытой смены default.
- **Модель состояния:** workspace snapshot, prepared mapping, artifact generation
  и loader lifetime должны различаться. Нельзя сериализовать `Solution` ради mapping
  или удерживать неограниченную историю snapshots без обоснования.
- **Persistence:** versioned shadow manifest/layout; старые ticks-каталоги не
  считать готовыми content generations и не мигрировать перезаписью. Удаление
  старого layout не является частью автоматического rollout по умолчанию.
- **Транзакции:** preflight до side effects и явная reconciliation. Атомарность
  assignment `_solution` не равна атомарности `.cs`/`.csproj`/workspace вместе.
- **Security/authorization:** identity выбора analyzer и разрешения зависимостей
  влияет на исполняемый код. Проверки путей cache обязательны для нового layout;
  разрешение arbitrary analyzer edits не выводится из обычного rename.
- **Observability:** различать copy/rewrite/load/execute, stale generation,
  skip reasons, partial writes и disk-full. Логи должны указывать project и
  generation, а summary оставаться кратким и не обещать успешную генерацию.
- **Тестирование:** MSBuild bootstrap, process isolation, точный маркер, негативные
  контроли, все write paths, failure/cancel, existing output и два процесса при
  shared root. В этом проходе эти tests только предложены, не запущены.
- **Миграции и совместимость:** document edit перестаёт подхватывать generator
  output; обновление требует выбранной явной операции. Release notes обязательны.
  Историческая ручная очистка v1.3.4 остаётся отдельной, не расширяется автоматически.
  Номер релиза и SLA reload не выдумываются.

## Неразрешённые вопросы для независимого арбитра

Арбитр в этом проходе не запускался; это повестка следующего решения, а не
запрос на немедленное разрешение пользователем каждого технического шага.

| ID ответа | Связанные findings | Точный вопрос | Необходимое evidence и позиция ответа |
| --- | --- | --- | --- |
| A-01 | E3-01, E3-05 | Может ли restart-required стать окончательным поддержанным режимом вместо обязательного in-process V2? | Матрица трёх операций с exact marker, стоимость/частота restart. U не задаёт hot reload как безусловное требование; исследование может выбрать restart, но не оправдать молчаливую неверную семантику |
| A-02 | E4-01, E4-02, E4-04 | Какие analyzer diffs допустимы в document-oriented CodeAction и как сообщать partial persistence? | Реальные CodeAction примеры и fault injection; позиция — preflight до записей, known overlay strip, unsupported changes не pass-through автоматически; общий rollback вне неявного scope |
| A-03 | E2-05, E2-06 | Shared root или session root, и необходим ли automatic retention в этой серии? | Размер/рост, несколько MCP процессов, lifetime DLL после clear. Позиция — сначала ownership/disk-full/операционная очистка; не обещать delete живого root по PID |
| A-04 | E5-01, E5-02 | Что делать с missing analyzer без доказуемого provenance? | Evaluated metadata на Roslyn 5.9.0, тест missing foreign same-name, исходный GenRepro. Позиция — не вводить skip-all и не называть unique-name fallback доказательством; решение о компромиссе явно |
| A-05 | E1-04, E6-03 | Повторный false/omitted должен выключать overlay или сохранять session opt-in? | Существующие клиентские сценарии и описание параметра; bool не различает omitted/false. До решения документировать sticky текущее поведение и reset-путь |

R-04 и E2-07 не требуют отдельного архитектурного арбитража: основания отказа —
прямые формулировки исходных спецификаций. Если критик не согласен, нужны новые
evidence/контрпримеры, не повторение реализации, уже запрещённой acceptance.

## Проверки этого прохода

Сверка была read-only относительно исходных требований, proposal, review и
production code. Добавлены только восемь файлов `review-response/`.

- 42 исходных ID → 42 ответа: отсутствующих, лишних и дублирующихся ID нет.
- Подсчёт verdict совпал со сводкой: 20 / 19 / 2 / 1.
- Все относительные Markdown links разрешаются в существующие файлы.
- SHA-256 всех 18 исходных файлов двух серий, включая review, совпали до и после
  создания ответов. Tracked-файлы репозитория не изменены.
- Функциональные тесты не запускались; runtime hypotheses не выдаются за
  подтверждённые экспериментом failures.
