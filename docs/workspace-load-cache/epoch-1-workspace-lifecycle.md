# Epoch 1 — Production lifecycle и эквивалентная гидрация

Статус: **proposed**. Требует разрешённую implementation ветку Epoch 0.
Disk lookup/public activation отсутствуют.

## Задача

Встроить выбранный hydrate host в тот же production lifecycle, что ordinary
MSBuild load, без регрессии A-LOAD/A-STICKY/A-WRITE/A-ADMISSION. Test-only
hydrator не подтверждает production.

## Capability и persistence contract

До реализации заполняется следующая матрица:

| Operation | Host capability | Disk writer | Preflight | Result/failure/partial | Reload |
|---|---|---|---|---|---|
| read | TBD by U-ARB-05 | n/a | readiness | explicit | TBD |
| text edit | TBD | TBD | A-WRITE | saved paths | TBD |
| add/remove/rename document | TBD | TBD | A-WRITE + project policy | partial paths/errors | TBD |
| project mutation | TBD | TBD | A-WRITE | partial project/files | TBD |
| disk reconciliation | in-memory reflection only | none | generation/role | applied/untrusted | no project write |

TBD не разрешает production hydrate. Read-only downgrade, custom writer и
reload-on-write нельзя выбрать только ради зелёной приёмки.

Trace: R-01, E0-01, E1-01 — ACCEPT/ACCEPT WITH MODIFICATION; U-ARB-05.

## Lifecycle

Обязательный pipeline:

`discover -> coverage -> load/hydrate candidate -> validate -> prepare/gate
-> atomic publication`.

Все стадии работают в одном A-LOAD acquisition через internal under-lock APIs
без recursive acquire. Кандидат и старая сессия имеют явных owners.

| Событие | Old session | Candidate | Key/path | Dirty/admission | Publication |
|---|---|---|---|---|---|
| same-key success | dispose после publish | promote | unchanged | новое подтверждённое состояние | atomic |
| same-key failure/cancel | retain | dispose | unchanged | сохранить pending/dirty | none |
| different-key success | dispose после publish | promote | new | candidate state | atomic |
| different-key failure/cancel | retain как previous, не как ответ на new key | dispose | previous | сохранить previous metadata | none |
| prepare/gate failure | retain допустимый old | reject/dispose | unchanged/previous | ban/unavailable не обходить | none |

Старый workspace другого key не публикуется автоматически как fallback. Loader
side effects не объявляются rollback при disposal candidate.

Trace: E1-02 — ACCEPT; C-02, E3-01 — ACCEPT WITH MODIFICATION.

## Memberships, context и writes

- Hydration сохраняет `path -> all memberships` для linked/TFM disk sync.
- Query/edit выбирает детерминированный context независимо от порядка projects.
  Public selector или ambiguity error требует отдельного compatibility решения.
- External disk reconciliation не создаёт project mutation и не пишет `.csproj`.
  Намеренный AddDocument/project mutation проходит capability/A-WRITE contract.
- Existing unknown-dirty-source path проверяется byte-level: project files до и
  после sync идентичны. Проверка повторяется для Epoch 3 scanner.
- Own-write notification содержит только фактически сохранённые paths/bytes,
  hash и revision; partial persistence сообщает только successful paths.
- Base solution и overlay разделены. Все readers получают только разрешённый
  published snapshot; on/off/omitted/reset/restart и ban/unavailable проходят
  одну regression matrix.

Trace: E1-03, E1-04, E1-05 — ACCEPT WITH MODIFICATION.

## Data evidence

Handoff содержит две независимые таблицы:

1. semantic fields, необходимые production hydrate;
2. dependency/admission evidence, необходимое future reuse.

Documents-only equality и отсутствие temp paths недостаточны. Import может не
быть Document, но его отсутствие во второй таблице блокирует Epoch 2 profile.
Portable analyzer provenance остаётся U-ARB-04.

Trace: E1-07, R-04, C-05 — ACCEPT WITH MODIFICATION.

## Приёмка

- Ordinary MSBuild load и production hydrate проходят одинаковую V01–V09 matrix.
- Проверены text, document и project mutations, disk bytes, следующий semantic
  call, обычная build и отсутствие shadow refs в project files.
- Проверены U-ARB-04 load boundary, stale-write, admission, cancellation,
  two-candidate lifetime и no recursive lock acquisition.
- Decoding fixtures проверяют characters, policy и round-trip bytes.
- Ни один production path не зависит только от test hydrator.

Trace: E1-06 — ACCEPT; C-09 — ACCEPT WITH MODIFICATION.
