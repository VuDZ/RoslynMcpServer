ID: E2-01
Severity: Blocker
Category: Data consistency

Target:
epoch-2-conservative-disk-cache.md / «Алгоритм load» / шаги 3–5

Claim:
При RAM miss и `useDiskCache` проверить совместимость, поддержку графа, membership manifests, explicit dependencies и strict hashes. На полном совпадении гидратировать без full DTB. Любое сомнение — обычный MSBuild. Не склеивать hydrated и stale проекты.

Evidence:
«Поддержка графа» и membership без DTB опираются на snapshot предыдущего DTB (C-01). Алгоритм не имеет канала узнать о новом условном входе, которого не было в manifests. Шаг 5 «любое сомнение» не срабатывает: hashes совпали, сомнения нет. Смешивание hydrated/stale запрещено — и не нужно: весь граф берётся из одного устаревшего snapshot.

Failure scenario:
1. Появился файл, меняющий evaluation (`Exists`, новый import, `Compile Remove` вне сохранённого glob-корня).
2. Проверки шага 3 зелёные.
3. Шаг 4 публикует hit с нулём DTB. Семантика ≠ свежий MSBuild. V12 не может быть реализована этим алгоритмом.

Suggested change:
Либо на Epoch 2 всегда прогонять дешёвую evaluation-only проверку (это интерпретатор / MSBuild, запрещённый README), либо объявить V12 вне эпохи и снять «полный miss на новое членство» как гарантию. Текущий алгоритм утверждает гарантию, которой у него нет входов.

Confidence:
High

---

ID: E2-02
Severity: High
Category: Measurement

Target:
epoch-2-conservative-disk-cache.md / приёмка «0 DTB операций»; README критерий first-semantic

Claim:
Отдельный процесс подтверждает hit с нулём DTB. Выполнен performance-gate Epoch 0. Иначе opt-in остаётся экспериментальным.

Evidence:
0 вызовов DTB — свойство host (Adhoc никогда не зовёт DTB), не доказательство эквивалентности (E0-01, V-01). First-semantic после hit включает compilation, SG, overlay prepare (R-04). Приёмка эпохи подменяет критерий README. `forceReload` после успеха пишет кэш — измерение «hit» на только что записанном поколении в том же дереве не показывает агентский цикл.

Failure scenario:
1. Тест: load+capture, новый процесс, load, счётчик OpenSolution = 0 → эпоха закрыта.
2. Первый `find_usages` занимает столько же, сколько baseline, плюс hash-probes.
3. Perf-gate README красный, приёмка эпохи зелёная.

Suggested change:
Обязательный oracle: DTB count **и** first-semantic e2e **и** сравнение diagnostics/navigation со свежим MSBuild. 0 DTB без e2e не является приёмкой.

Confidence:
High

---

ID: E2-03
Severity: High
Category: Cache identity

Target:
epoch-2-conservative-disk-cache.md / алгоритм шаг 2; cache-contract §9

Claim:
RAM path сохраняется; disk capture из непроверенного RAM-hit не делается. `useDiskCache` обновляется без reopen. После `reset_workspace` следующий load может получить disk-hit.

Evidence:
Включение `useDiskCache=true` на уже загруженном workspace не создаёт запись (намеренно). `reset_workspace` чистит RAM, не диск — если записи не было, следующего hit нет. Агент читает Description «opt-in кэш» после повторного load в том же PID и считает, что межпроцессный слой активен.

Failure scenario:
1. `load_workspace` без флага (обычный агентский путь).
2. Повторный load с `useDiskCache=true` — RAM-hit, диск пуст.
3. Reload MCP — полный DTB. Фича «включена» в аргументах и отсутствует на диске.

Suggested change:
RAM-hit + переход `useDiskCache` false→true либо force capture после probes, либо явный `cache=not_written_unverified_ram` в ответе. Молчаливый no-op запрещён.

Confidence:
High

---

ID: E2-04
Severity: High
Category: Scope

Target:
epoch-2-conservative-disk-cache.md / «Изменившийся только `.cs` тоже даёт miss»

Claim:
SHA-256 всех значимых входов. Изменение только `.cs` — miss. Это простой безопасный baseline для следующих оптимизаций.

Evidence:
Цель README — ускорить reopen после рестарта MCP. Агентский workload почти всегда меняет `.cs`. 4C, который мог бы оставить hit при неизменном графе, optional и сам противоречит запрету интерпретатора (E4-01). Вместе с C-06 (build трогает `obj`) hit-rate на заявленной нагрузке стремится к нулю даже при идеальном профиле.

Failure scenario:
1. Epoch 2 shipped, dogfood на этом репо или BrqMover: после любого edit/reload — miss.
2. Handoff пишет «алгоритм корректен, hit 0%».
3. Основной маршрут объявляют успешным по V10–V18 (miss сработал), не по цели ускорения.

Suggested change:
Либо Epoch 2 не закрывает цель README и не должна быть на основном маршруте как «ускорение», либо `.cs` content-hash вынести из inter-process key уже в Epoch 2 (оставив membership+graph+config). Текущий «безопасный baseline» опровергает собственную миссию.

Confidence:
High

---

ID: E2-05
Severity: High
Category: Concurrency

Target:
epoch-2-conservative-disk-cache.md / «После успешного стабильного load сохранить новое поколение»; cache-contract §7 «не более одной повторной попытки»

Claim:
Capture после успешного стабильного load поддерживаемого графа. Ошибка записи кэша не делает load ошибкой. Изменение во время hash — отвергнуть, одна попытка, затем load без записи.

Evidence:
Сразу после `load_workspace` агент начинает `apply_patch` / build. Watcher и own-write идут параллельно с capture. Одно отклонение + один retry на «стабильном» дереве в интерактивной сессии часто даёт *ноль* записей. Стабильность не определена (idle N секунд? нет dirty set?).

Failure scenario:
1. Load успешен, capture стартовал, агент пишет `.cs`.
2. Два reject, кэш не записан. Load всё равно Success / `msbuild`.
3. Рестарт — снова DTB. Статистика «процент hit» в handoff считается по тем сессиям, где агент ждал, то есть не по реальным.

Suggested change:
Capture отложить до idle (нет dirty, нет in-flight write) или до конца первой semantic-паузы. Иначе «межпроцессный кэш» существует только в тестах без агента.

Confidence:
High

---

ID: E2-06
Severity: High
Category: Lifecycle

Target:
epoch-2-conservative-disk-cache.md / реализация «атомарное хранение, writers/readers, cleanup и corruption входят в эпоху»

Claim:
Store, parallel writers/readers, cleanup, corruption fallback входят в эпоху, не откладываются после выпуска.

Evidence:
Это отдельный продукт (leases, PID reuse, atomic pointer, checksum, 2 GiB, schema). Он строится *поверх* нерешённых C-01/E2-01. Эпоха одновременно вводит публичные параметры `useDiskCache`/`forceReload` и metadata источника. Объём работ провоцирует закрыть store на fixture и отложить support-profile дыры как «сомнение → miss».

Failure scenario:
1. Store и параметры shipped.
2. Support/V12 «пока miss на всё сомнительное» — на реальном sln всегда miss.
3. Агенты начинают передавать `useDiskCache=true`; наблюдаемый эффект ноль; откатывать публичные параметры дороже, чем не вводить их.

Suggested change:
Разделить: 2a store+DTO без production hit (как Epoch 1), 2b hit только после закрытия C-01/V12. Не выпускать параметры, пока hit-rate на одном реальном большом решении не измерен.

Confidence:
High
