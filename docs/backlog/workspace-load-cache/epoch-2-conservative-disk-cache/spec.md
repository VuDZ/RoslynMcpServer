# Epoch 2 — Conservative disk cache: unchanged-restart checkpoint

Статус: **proposed checkpoint**. Требует production lifecycle Epoch 1.
Hydrate host — `AdhocWorkspace`, writer только для `.cs` (U-ARB-05). Admission
profile — `sdk-project-v1` (U-ARB-06). Overlay после disk hydrate — повторная
проверка DLL и новый `LoadSessionId` (U-ARB-04). Решения 2026-10-01.
Capture — запись поколения до ответа `load_workspace` (U-ARB-01, 2026-10-02).

## Задача

Пропустить полный DTB в новом PID только для полностью неизменного,
поддержанного и доказанно полного request. Изменение source/membership здесь
даёт miss. Epoch не закрывает O4/O5 и не означает `series complete`.

Trace: R-05, E2-04 — ACCEPT WITH MODIFICATION.

## Load protocol

1. Нормализовать request identity и requested/effective session policy.
2. `forceReload=true` обходит RAM и disk.
3. Same-key RAM path сохраняет disk sync/stale guard. RAM hit не подтверждает
   disk generation; false→true может вернуть
   `write=not-attempted: unverified-ram`.
4. При RAM miss выполнить support detector и весь dependency protocol:
   compatibility, positive/absent/region/target evidence, completeness,
   bounded membership scan, explicit probes и strict hashes.
5. `unknown`, budget exhaustion, unreadable input или неполное evidence дают
   ordinary load до hydrate и до загрузки DLL из DTO.
6. На полном admission hydrate отдельного candidate, validate consumed bytes,
   acquire reader ownership для lazy reads, prepare/gate и atomically publish.
7. Любой failure даёт ordinary load без partial hydrated graph. После успешного
   стабильного ordinary load reusable capture разрешён только при completeness.
8. Capture выполняется по выбранному расписанию ниже. Safety predicate тот же:
   полнота, `unknown` запрещает запись, указатель только после повторной
   сверки хешей.

«Сомнение → miss» не заменяет доказанный источник неизвестных dependencies.

Trace: E2-01 — ACCEPT WITH MODIFICATION; E2-03 — ACCEPT;
E2-05 — решение владельца 2026-10-02.

## Выбранный capture schedule

Владелец 2026-10-02 выбрал **запись поколения до ответа `load_workspace`**.
Это ответ U-ARB-01. Disk-hit не включён. Численный budget public activation
остаётся U-ARB-03.

Замер той ночи на `RoslynMcpServer.sln`: пять проектов, `obj` уже был,
`OpenSolutionAsync` без binlog, который продукт пишет в том же вызове.
Два прогона: 1335 мс и 1248 мс, 372 документа, диагностик нет. Первый хеш
356 файлов `**/*.cs` (3.2 МБ) после open — 220 мс. Повтор набора из 397 путей
(15 МБ, вместе с assets, analyzer DLL и файлами workspace под `obj`) —
35–42 мс. 253 metadata DLL (44 МБ) — 51 мс. Manifest на 397 строк — 2 мс
и 31 КБ. Исходный текст в payload не пишется. За оба окна снимок исходников
не изменился. Watcher увидел 12–13 событий в `obj`/`bin`: это сама загрузка.

Контракт:

1. После успешной полной обычной загрузки тот же вызов считает хеши входов
   и публикует атомарный указатель до возврата. Ответ несёт `written` или
   `failed`.
2. Успех загрузки от успеха записи не зависит. Отказ preflight байты не
   меняет. Отмена вызова, reset и shutdown не публикуют недописанный
   кандидат. Повторной записи после ответа нет: внутри вызова одна попытка.
3. Новое поколение после каждого edit не пишется. Несовпавшие байты дают
   miss старого поколения. Следующий capture делает процесс, который снова
   прошёл полную обычную загрузку.
4. Сдвиг хешей за время записи выбрасывает кандидата. Частота правок агента
   внутри этого окна не измерялась. На этом дереве окно — около 0,25 с, и
   для выбора расписания она не нужна.

Замер — образец решения такого размера. Target workload и пороги median/p95,
hit-rate и miss-overhead он не утверждает.

## Store-correctness checkpoint

До любого public activation обязательны immutable complete generations, schema
и size limits, checksum, atomic pointer, competing readers/writers, reader
ownership до last lazy read, crash/PID reuse/pause, corruption/no-space/
permissions fallback и safe cleanup. Cleanup evaluation cache не касается
analyzer shadow store.

Зелёный store разрешает только store checkpoint. Он не разрешает public flags
без dependency admission, semantic equivalence и performance gate.

Trace: C-08, E2-06 — ACCEPT WITH MODIFICATION.

## Public activation checkpoint

Public activation требует одновременно:

- zero full-DTB на positive unchanged cross-process hit;
- fresh-MSBuild equivalence matrix;
- successful prepare/admission и semantic readiness;
- заранее согласованный e2e budget и hit-rate на target workload;
- bounded miss/capture overhead;
- все store/fallback guarantees.

Zero-DTB не заменяет остальные условия. Fixture-only разрешает experiment.

Trace: E2-02 — REJECT (исходная конъюнкция сохранена);
E0-04, V-04 — ACCEPT WITH MODIFICATION.

## Observability

Каждый attempt сообщает effective policy, base source, validation mode,
capture status, write status/reason, overlay requested/effective, readiness,
coverage и fallback reason. Логируются counts/bytes и probe, load, hydrate,
prepare, publication, first-semantic durations без secret values.

## Приёмка

- V01–V18/V23 с расширениями из [verification](../verification.md).
- Positive hit в отдельном PID, strict size/mtime collision test.
- Known absent becoming present, unknown condition/custom target и membership
  change дают правильный miss reason и ordinary fallback.
- Source edit, no-op build, build и edit+build+restart измерены отдельно.
- Store-correctness и activation verdict записаны раздельно.

## Handoff

Указать пять decision verdicts. `next epoch allowed` не означает
`public activation allowed`; `series complete` для Epoch 2 всегда false, пока
O4/O5 не закрыты.

Trace: H-01 — ACCEPT WITH MODIFICATION.
