# Epoch 3 — Live consistency

Статус: **proposed; policy partially unresolved**. Требует Epoch 2 и решения
U-ARB-02 до выбора strict-every-call, watcher-trust или stale-read behavior.

## Задача

Закрыть O4: после внешнего sync следующий semantic call получает состояние,
разрешённое выбранной freshness policy, либо явный отказ/unknown по её contract.
Write/refactoring на stale/unknown base всегда запрещены A-WRITE независимо от
будущего read policy.

Trace: R-05 — ACCEPT WITH MODIFICATION; E3-02/E3-04 — UNRESOLVED.

## Ортогональные состояния

Не объединять:

1. RAM/index freshness (`trusted`, `content-dirty`, `graph-dirty`, `untrusted`,
   `refresh-failed`);
2. validity старой disk generation;
3. cadence durable capture.

Старая несовпадающая disk generation обязана дать miss. Новая generation не
обязана записываться после каждого edit. Cadence остаётся U-ARB-01.

Trace: E3-06 — ACCEPT WITH MODIFICATION.

## Locking и publication

Нормативная схема:

`acquire -> classify -> refresh/load candidate -> validate -> prepare/gate
-> publish -> release`.

Internal under-lock APIs не захватывают semaphore повторно. Semantic accessor
возвращает immutable published snapshot/generation; write preflight повторно
проверяет generation. Dirty events, пришедшие во время flush, не очищаются
старым flush. Cancellation сохраняет pending queue.

Обязательные concurrency cases: simultaneous semantic/edit/reset, graph refresh,
stale candidate, cancellation, event during flush, prepare failure и no recursive
acquire. MVCC/merge не вводятся.

Trace: E3-01 — ACCEPT WITH MODIFICATION.

## Event and input classifier

| Role | Result |
|---|---|
| known explicit dependency | probe; content/graph role по profile |
| potential membership/negative region | validate membership/evidence |
| proven irrelevant | ignore, no DTB |
| relevant unknown/unknown coverage | `untrusted`/`graph-dirty` |

Editor temp и unrelated files не вызывают DTB. Extension blacklist не доказывает
irrelevance: AdditionalFiles/imports могут иметь custom extension.

Content-only допустим только для существующей supported Document category при
доказанной независимости evaluation и DTB targets от bytes. При совмещённых
ролях graph role приоритетна; missing document не считается applied. Все
memberships получают один подтверждённый text.

Trace: E3-03 — ACCEPT; E3-05 — ACCEPT WITH MODIFICATION.

## Watch/probe coverage

Coverage map включает project membership regions, walk-up ancestors, explicit
paths вне roots/в `obj` и locations возможного появления absent inputs.
Реализация задаёт grouping, watch limits, startup, overflow/error/cancel и
resource telemetry. Prune recursion не подавляет explicit watch/probe.
Unavailable/lost watcher переводит state в untrusted; дальнейшее read behavior
определяется только после U-ARB-02.

Trace: E3-07 — ACCEPT.

## Own writes

После persistence записываются только реально saved paths, bytes/content hash и
revision. Partial result отмечает только successful paths. Time-window
suppression не отбрасывает более позднее событие по тому же path. Несовпадение,
ABA/нестабильное чтение или event newer than own revision оставляют
pending/untrusted; hash подтверждает состояние bytes, но не автора.

Trace: E3-08 — ACCEPT WITH MODIFICATION.

## Unresolved freshness contract

До U-ARB-02 эта спецификация **не выбирает**:

- hash/scan на каждый semantic call;
- доверие watcher между подтверждениями;
- periodic cadence silent-event detection;
- выдачу last-known snapshot с `freshness=unknown`;
- read operations, которым разрешён unknown;
- timeout/retry для locked DLL/config;
- взаимодействие unknown с analyzer `Banned`/`Unavailable`.

Ни один из этих вариантов не может быть внедрён как подразумеваемый default.

## Приёмка после решения gate

- V07–V09, V19–V23 и concurrency matrix.
- External sync, own edit, overflow/new file, directory rename/delete, ancestor
  props, explicit `obj`, linked file, custom AdditionalFile, watcher unavailable.
- Следующий semantic call соответствует выбранному contract; write на
  stale/unknown всегда rejected.
- Измерен unchanged-call overhead по заранее утверждённому budget.
