# S6 — согласовать нормативы и статусы документации

Статус: **не выполнено**. Зависимость: [S5](s5-partial-prepare-transitions.md).
Результат шага: V3-R4 устранён; документация описывает один актуальный контракт.

## Основание

В v2 одновременно действуют несовместимые формулировки:
E2-S4 оставляет original после ошибки, U-ARB-04 требует fail-closed;
E4-S1 требует отказ stale candidate до записи, но A4-09 объявлен неблокирующим;
E5-S4 требует inaccessible decision до rollout, хотя rollout уже принят,
а решение отсутствует. Inventory также сохраняет устаревшее описание getter
с fallback на `workspace.CurrentSolution`.

Targets: `docs/ARCHITECTURE.md`, пользовательский README/help, документы v2
`README`, `LIFECYCLE-v2`, `epoch-2-immutable-shadow-copies`,
`epoch-4-workspace-write-boundary`, `epoch-4-acceptance`,
`epoch-5-reference-provenance`, `epoch-1-semantic-entry-points`,
`u-arb-04-atomic-load-prepare`, `UNRESOLVED-v2`, `FOLLOWUPS`, `TRACEABILITY-v2`,
а также результаты и статусы v3.

## Работа

Развести raw workspace для persistence и published semantic snapshot.
Сохранение original references в raw не является разрешением исполнять их
после opt-in failure. Актуальный контракт публикации должен учитывать
устойчивое состояние S3, failed capture S4 и частичный prepare S5.

Документировать простой stale-base rejection S2 как обязательную защиту
данных. Историческую оценку A4-09 сохранить с датированной поправкой:
ревью v3 воспроизвело потерю изменений. Non-goal MVCC не освобождает от
проверки базы до записи. Аналогично отметить фактическое закрытие A4-12
только после проверки unknown context.

Уточнить границы E5 acceptance: принята матрица с доступными/missing paths,
а inaccessible остаётся открытым и не входит в подтверждённую поддержку.
Не сохранять утверждение о выполненном gate, который требовал ещё не
выбранной политики. Не назначать rewrite/skip для inaccessible редакторской
правкой. Принятие исправлений v3 и завершение всей исходной серии — разные
статусы.

Обновить semantic inventory по фактическим callers: getter возвращает только
published `_solution`; semantic readers ждут manager boundary; операция
удерживает одну базу и проверяемый write context. Описать recovery после
failed capture и prepare без обещания, что reset выгружает CLR.

Исторические results/acceptance не переписывать как будто новые проверки
были выполнены раньше. Добавить ссылки на конкретные новые evidence и
переопределённые нормы. Старые 36/36 и 9/9 не выдавать за доказательство
отсутствия R1–R3. Синхронизировать help с реализацией без новых MCP параметров.

## Приёмка

- Ни один актуальный норматив не разрешает confirmed raw publication после
  отказа opt-in или сохранение stale candidate до проверки свежести.
- Отказ capture, частичный prepare, stale mapping, restart-required и
  no-overlay различаются в описании и соответствуют реализации.
- Статус inaccessible одинаков в README, нормативе, unresolved и приёмке;
  отсутствует ложное заявление о завершении всей серии.
- Исторические факты имеют версию/дату; новые исправления ссылаются на свои
  тесты и commit. Актуальный getter/inventory соответствует коду.
- Относительные ссылки разрешаются; каждый файл v3 остаётся одним шагом,
  без вложенных планов реализации и подэтапов.

## Результат

Не выполнено.
