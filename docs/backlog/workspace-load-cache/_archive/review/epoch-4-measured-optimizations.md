ID: E4-01
Severity: High
Category: Support profile

Target:
epoch-4-measured-optimizations.md / 4C «Членство без полного DTB»

Claim:
Изменение source set без DTB разрешать только узкому профилю с доказанной моделью Include/Exclude/Remove/conditions; unknown → full load. Делать только после доказательства более простого режима.

Evidence:
Модель Include/Exclude/conditions *и есть* интерпретатор MSBuild, запрещённый README. 4C — единственный пункт, который мог бы спасти hit-rate Epoch 2 (E2-04). Он optional и одновременно нелегален по ограничениям серии. «Доказанная модель» для SDK default globs всё равно ломается на `Directory.Build.props` с кастомным Compile.

Failure scenario:
1. Epoch 2 закрыта с miss-on-`.cs`.
2. Hit-rate нулевой; 4C начинают как «измеренная оптимизация».
3. Либо пишут частичный glob-evaluator (нарушение README), либо unknown всегда full load (4C не существует).

Suggested change:
Либо снять запрет интерпретатора и сделать 4C отдельным продуктом, либо признать, что консервативный v2 не имеет пути к полезному hit-rate. Не держать 4C как спасательный клапан основного маршрута.

Confidence:
High

---

ID: E4-02
Severity: High
Category: Consistency

Target:
epoch-4-measured-optimizations.md / 4B «Size+mtime shortcut»; «для live session доверие watcher»

Claim:
Size+mtime допустим лишь как отдельная non-strict политика. Strict остаётся доступен. Доверие watcher требует coverage и обработки lost events.

Evidence:
Epoch 3 требует strict probes на каждый semantic call (E3-02). 4B — единственный способ сделать Epoch 3 терпимым. «Отдельная политика» без поля в ответе load/semantic и без ключа кэша означает, что агент не отличит strict hit от stat hit. Потерянные FSW события без ошибки — ровно pitfall 20. Non-strict + watcher trust = отрицание контракта §6/§7 под именем оптимизации.

Failure scenario:
1. Profiler показывает, что probes доминируют (неизбежно после Epoch 3).
2. Включают size+mtime и watcher-trust, default на live path.
3. Подмена содержимого при тех же size/mtime (V10) проходит; эпоха 2 ещё обещает, что V10 ловится.

Suggested change:
Non-strict не может быть тихой заменой Epoch 3. Отдельный `freshness=strict|stat|watcher` в каждом semantic ответе и отдельный disk schema. Иначе 4B фальсифицирует контракт, не оптимизирует его.

Confidence:
High

---

ID: E4-03
Severity: High
Category: Cache identity

Target:
epoch-4-measured-optimizations.md / 4A metadata references и partial load

Claim:
В обоих cache keys учитывать scope и canonical roots. Metadata — отдельный opt-in. Смена режима — reopen. Замену source graph на DLL не считать эквивалентностью full mode.

Evidence:
Disk-hit Epoch 2 обещан для полного source graph. Смена metadata/partial — reopen = DTB, то есть уничтожение выигрыша, ради которого строили кэш. Два режима в ключе = два поколения; агент, который переключает scope (сначала project A, потом find в B), платит DTB дважды и получает неполный search. «Общий partial hint» исторически игнорируется агентами.

Failure scenario:
1. `graphScope=project` на большом sln — быстрый hydrate части.
2. `find_usages` не видит B; агент не поднимает scope, чинит не тот граф.
3. Expand = full load, cache miss относительно partial generation. 4A не ускоряет заявленный e2e, меняет семантику поиска.

Suggested change:
Не вводить partial/metadata, пока full-graph disk-hit не доказан на реальном sln. Если вводить — semantic tools, требующие полноты, должны отказывать, не «hint + частичный список». Иначе 4A создаёт новый класс ложных навигационных ответов, более опасный, чем медленный DTB.

Confidence:
High

---

ID: E4-04
Severity: Medium
Category: Lifecycle

Target:
epoch-4-measured-optimizations.md / 4A «Проверить отдельно CLI build/test»; «не менять явную цель команды из-за scope»

Claim:
CLI build/test не меняют цель из-за graph scope. Учитывать test discovery / binary resolution, которые используют текущую solution как подсказку.

Evidence:
`run_dotnet_test` / `get_test_list` / `binariesPath` завязаны на загруженный sln и список проектов (`ARCHITECTURE.md`). Partial solution с metadata-only dependency ломает discovery по синтаксису (проекта нет в workspace) при том, что CLI мог бы собрать весь sln. Эпоха требует «проверить», но не фиксирует, какой граф видит `get_test_list` при `graphScope=project`.

Failure scenario:
1. Scope = один test-проект.
2. `get_test_list` без `projectName` возвращает только его тесты; агент считает, что в sln их больше нет.
3. `run_dotnet_test` по загруженному sln гоняет остальные. Два инструмента, два мира.

Suggested change:
Зафиксировать: CLI всегда на файле workspace на диске, semantic graph может быть уже; `get_test_list` обязан писать `incomplete=true` и requested roots. Без этого 4A ломает уже закрытые контракты test tools.

Confidence:
High
