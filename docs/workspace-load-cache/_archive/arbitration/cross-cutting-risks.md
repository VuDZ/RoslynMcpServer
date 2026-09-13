# Сквозные риски и ограничения согласованности

## 1. Correctness profile и product usefulness — разные gates

Взаимодействия: R-05, R-06, E0-02, E0-04, E2-04, E2-06, V-04, H-01.

Узкий profile может быть корректным и бесполезным для целевой монорепы. Negative
admission tests подтверждают безопасный отказ, но не semantic equivalence или
hit-rate. Следующая редакция не должна превращать `default=false`, fixture-only
или zero-DTB в разрешение public activation. Epoch 2 остаётся промежуточным
unchanged checkpoint, пока O4/O5 не закрыты.

## 2. Base graph, overlay и semantic readiness — разные состояния

Взаимодействия: R-04, C-05, E1-04, E2-03; Astra N2/N4.

Источник `disk` доказывает только происхождение base DTO. Он не доказывает
portable analyzer provenance, успешный prepare/gate, наличие disk write или
исполнение generators. Один `cacheHit`/`success` bool создаст ложную гарантию.
Session-sticky opt-in не переносится между PID автоматически.

## 3. Три вида атомарности нельзя смешивать

Взаимодействия: C-02, E1-02, E2-06, E3-01, E3-08; Astra N6.

Atomic store pointer защищает целостность payload. Atomic `_solution`
publication защищает manager state. Ни одно не даёт filesystem snapshot,
multi-file rollback или rollback process-lifetime CLR loader. Сохранение старого
workspace после failed candidate не восстанавливает уже загруженные assemblies.

## 4. Admission completeness и hydrate completeness различны

Взаимодействия: R-02, C-01, C-04, E1-07, E2-01.

Полный Roslyn DTO не доказывает полноту negative dependencies. Полный список
imports не доказывает target inputs. Analyzer capture `Complete` не доказывает
полноту всего project graph. Ordinary load success не разрешает cache capture.
Две evidence tables и раздельные reason codes обязательны.

## 5. Strict hashing не исправляет неизвестную зависимость

Взаимодействия: C-01, C-06, E2-01, E4-02.

SHA-256 доказывает bytes только известного input. Он не обнаруживает неизвестный
absent path, новый wildcard region или изменение current toolset selection.
Ослабление до stat/watcher увеличивает риск, но усиление hash policy не заменяет
dependency closure и current-resolution probe.

## 6. Current toolset нельзя сравнивать только с сохранённым собой

Взаимодействия: R-02, C-01, C-03, E2-01; Astra N7.

Compatibility probe должен получить текущее effective SDK/MSBuild/environment
resolution независимо от cached fingerprint. Проверка лишь сохранённых путей и
версий может пропустить иной выбор SDK при roll-forward. Значимые environment
properties должны быть allowlisted, а не логироваться/сохраняться целиком.

## 7. Reader lifetime может переживать метод hydrate

Взаимодействия: C-02, C-08, E2-06.

TextLoader, metadata/analyzer reference или иной lazy consumer может читать
payload после publication. Lease, освобождённый при возврате hydrate, создаст
cleanup race. Если все inputs материализуются заранее, это должно быть доказано;
иначе ownership живёт до disposal соответствующей session/generation.

## 8. Membership sync и query context нельзя объединять

Взаимодействия: C-09, E1-03, E1-05, V-02; Astra N5.

External text change должен попасть во все linked/TFM memberships, но diagnostics
или code fix могут требовать один конкретный context. «Первый найденный» после
hydrate недетерминирован, а применение operation ко всем contexts может быть
семантически неверным. Отдельная membership map не разрешает автоматически
публичную неоднозначность.

## 9. Disk reconciliation и пользовательская project mutation различны

Взаимодействия: E1-01, E1-05, E3-08.

Scanner отражает уже случившееся изменение и не получает права менять `.csproj`.
Пользовательский AddDocument может требовать project-file persistence, но только
через preflight/write contract. Общий `TryApplyChanges` без origin/capability
классификации может либо потерять мутацию, либо внести нежелательный project diff.

## 10. Live freshness, cache validity и durable capture имеют разный cadence

Взаимодействия: E2-05, E3-02, E3-04, E3-06, E3-07.

RAM обязан соответствовать выбранной live policy; старая disk generation может
просто стать invalid и давать miss; новая generation не обязана публиковаться
после каждого edit. Попытка одним state `trusted` описать все три приводит либо
к hash-scan на каждом query, либо к скрытому watcher trust, либо к write storm.

## 11. Own-write attribution не является filesystem transaction

Взаимодействия: E1-05, E3-08.

Hash/revision после собственной записи подтверждает только наблюдаемые bytes.
Он не доказывает автора события и не исключает ABA. Time-window suppression
может потерять внешний overwrite; path-only dedup может очистить более новую
revision. Pending state и повторная validation обязательны при несовпадении.

## 12. Partial semantic graph и CLI graph — разные поверхности

Взаимодействия: E4-03, E4-04.

Partial mode не должен сужать явно заданный CLI target, но syntax discovery и
semantic queries видят только loaded projects. Один общий «partial hint» не
защищает refactoring/test workflows. Coverage является частью результата каждой
затронутой операции, а не только ответа `load_workspace`.

## 13. Cache trust и analyzer execution — security boundary

Взаимодействия: C-05, C-08, E2-01; Astra N3.

Checksum manifest защищает от случайной порчи, но не подтверждает происхождение,
если manifest и DLL изменяемы одной стороной. До выбранной threat model cache DTO
не получает права направить loader на произвольный путь. Persisted provenance не
должен превращать временный binlog с потенциальными секретами в долговременный
нефильтрованный payload.
