# Epoch 2 — Conservative disk cache: unchanged-restart checkpoint

Статус: **proposed checkpoint**. Требует production lifecycle Epoch 1,
доказанные U-ARB-04/U-ARB-05/U-ARB-06 для активируемого profile.

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
8. Capture schedule не выбирается здесь: foreground/background, response timing,
   retry и shutdown регулирует U-ARB-01; safety predicate одинаков.

«Сомнение → miss» не заменяет доказанный источник неизвестных dependencies.

Trace: E2-01 — ACCEPT WITH MODIFICATION; E2-03 — ACCEPT;
E2-05 — UNRESOLVED.

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

- V01–V18/V23 с расширениями из [verification](verification.md).
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
