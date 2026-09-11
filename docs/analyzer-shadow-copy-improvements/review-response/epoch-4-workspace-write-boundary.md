# Ответ: эпоха 4

Источники: [review](../review/epoch-4-workspace-write-boundary.md),
[спецификация](../epoch-4-workspace-write-boundary.md).
Обозначения источников — в [README ответа](README.md).

## E4-01 — PARTIALLY ACCEPT

**Основание.** Учёт произвольной истории снимков был бы лишним scope, которого U
не задаёт. Но знать mapping, применённый к конкретному candidate, не равнозначно
MVCC. Только current-session mapping тоже недостаточно: между чтением документа
и сохранением может произойти reload и замена ссылок. C вдобавок пишет файлы
до `TryApplyChanges`, поэтому его false не защищает диск от stale candidate.

**Изменение.** «Требования»: исключить registry всех in-flight snapshots и
самостоятельный merge engine. Определить минимальный operation context:
исходный immutable snapshot, mapping/generation сессии; несовместимость проверять
до первого side effect. Точная форма token — будущий design decision, не
обязательство внедрить версионную БД. Изменить связь с non-goal concurrency.

**Последствия.** Внутренний apply contract и stale/reload tests; нет persistence
Roslyn snapshots. Более широкая неатомарность записи — N-01, а не скрытая часть
«учёта mapping».

## E4-02 — PARTIALLY ACCEPT

**Основание.** Ключевая критика подтверждена: C пишет `.cs` в цикле до lock,
revert и apply. Добавить reject там же — значит отказать после side effects.
Однако передать любой unknown analyzer diff в workspace также опасно: H2
показывает, что этот путь редактирует `.csproj`, а не просто валидирует.
Намерение произвольного CodeAction не следует автоматически из rename.

**Изменение.** «Требования», «Обновление overlay»: сначала классифицировать
candidate и снять known overlay, до любых записей проверить unsupported diff.
Поддерживаемые намеренные analyzer edits требуют отдельного контракта;
неизвестные изменения не молча стирать и не автоматически сохранять. Порядок
операций и отсутствие записей при preflight rejection сделать acceptance.

**Последствия.** Строже поведение code fixes, возможные совместимые исключения
нужно установить по реальным CodeAction. Меняется внутренняя граница записи,
error reporting и failure tests. Это не даёт транзакции файловой системы:
I/O failure после preflight всё ещё может оставить частичные изменения (N-01).
Политику допустимых analyzer edits вынести на арбитраж A-02.

## E4-03 — PARTIALLY ACCEPT

**Основание.** Strip защищает project persistence, не все способы загрузки
analyzer. Но review пока не показывает, что сами четыре `TryApplyChanges`
вызывают compilation/load реального output; это гипотеза E1-07. Сужать исходную
anti-lock задачу H1 до missing-path без измерений было бы необоснованно.

**Изменение.** «Проблема», «Проверки и приёмка»: разделить write boundary и
semantic/load boundary. Привязать existing-path lock test к каждой операции;
проверить явные semantic reads raw `workspace.CurrentSolution` и раннюю загрузку
до включения overlay. Если обнаружена утечка, оформить отдельный design decision
по границе загрузки, а не считать funnel её исправлением.

**Последствия.** Тесты actual load paths и более точная область гарантии.
Новый backend workspace или запрет внутренних Roslyn compilation не выбирается
этим ответом. Atomic assignment ссылки тоже не решает freshness всех readers.

## E4-04 — ACCEPT

**Основание.** В C после false есть text-only fallback; он синхронизирует часть
уже записанных файлов и может публиковать последовательные snapshots. Запрет
публикации всего rejected candidate не равен запрету такой reconciliation.
Исходная спецификация не определяет эту важную ветку.

**Изменение.** «Обновление overlay»: отдельно описать preflight rejection без
side effects, successful apply, failed apply после записи и reconciliation
фактически сохранённых текстов. Неприменённые project changes не публиковать.
Fallback включить в общий workflow и тестировать; не удалять его молча.

**Последствия.** Результат операции должен различать полный успех, частичную
запись и reconciliation failure. Текущий `IReadOnlyList<string>` может оказаться
недостаточным внутренним return type; adapters потребуют согласования ошибок.
Полная rollback-транзакция не обещается. Необходимо тестировать cancellation
и ошибку отдельного файла, а не только `TryApplyChanges == false`.

## E4-05 — ACCEPT

**Основание.** Тесты T проверяют wipe списка, а не inverse конкретного overlay.
H2 требует не сохранять shadow, U предлагает снимать именно внесённое изменение.
Без явного mapping такие tests не могут подтвердить новый контракт.

**Изменение.** «Требования», «Проверки и приёмка» и README dependencies: этап 4
после контракта mapping этапа 2. Тест известного mapping должен сохранять
несвязанные ссылки/их порядок; отдельные tests неизвестного diff и stale session
не должны подгоняться под нынешний wipe. Нельзя узнавать происхождение только
по строковому префиксу temp path.

**Последствия.** Внутренний DTO и переработка guard tests; сохраняется исходный
запрет temp references в `.csproj`. Разработка mapping возможна раньше полного
файлового слоя, но его контракт — реальная зависимость.

## E4-06 — ACCEPT

**Основание.** В C watcher строит raw solution, а возвращает семантическую
работоспособность именно post-apply overlay. H3 требует обеих половин;
проверка одного отсутствия temp Analyzer пропустит потерю генерации.

**Изменение.** «Обновление overlay», «Приёмка»: единый workflow включает
preflight/strip, apply или явную reconciliation, затем публикацию overlay по
готовому mapping без файлового prepare. Все три входа E1-06 и fallback E4-04
проверяются на маркер, новый текст и `.csproj` bytes.

**Последствия.** Централизация может состоять из общего orchestration и узкого
apply helper; нельзя держать semaphore повторно из under-lock callers.
Дополнительных file I/O на semantic getter или watcher publish не появляется.
Общая архитектура транзакций остаётся отдельным вопросом N-01.
