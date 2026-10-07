# Бенчмарки workspace state cache

Статус: **draft / ready-for-review, 2026-10-07**. Числа ускорения ещё не получены.

## Контролы и сравнимость

E0/task-00 выбирает небольшой контроль и неизменённый реальный проект/решение.
Сначала можно использовать RoslynMcpServer, если chosen profile допускает его;
для large case использовать фактически доступный target. [BTCPay candidate](../backlog/workspace-load-cache/btcpay-candidate.md)
и [прежний baseline](../backlog/workspace-load-cache/baseline-benchmark.md)
дают готовые пути/символы и опыт, но их v2 admission gates и выборка не наследуются.
Нельзя менять корпус, убирать analyzers или переписывать csproj ради hit.
Not-supported large target не скрывается и не заменяет положительный real control.

Compare key включает server build/commit, source version, OS/CPU/RAM, workspace
revision/content identity, SDK/MSBuild/Roslyn, effective configuration/platform/TFM,
semantic/overlay mode, binary policy threshold/chunks и выбранный query.
Git допустим для закрепления benchmark корпуса, но не как cache validation input.
Изменённые файлы/bytes mutations перечисляются независимо от VCS.

## Прогон

- Release server publish выполняется до замеров; restore корпуса тоже вне load
  тайминга, кроме отдельно названного restore-сценария. Versions/pins фиксируются.
- Каждый измеряемый restart — новый PID. RAM-hit отдельный контроль и не disk hit.
- Один warmup не входит в median, затем минимум пять успешных сопоставимых попыток
  каждого основного сценария. Все raw попытки, включая failures/timeouts/retries,
  сохраняются. Медиана только successful attempts с одинаковым compare key;
  при failures результат помечается partial, проблема не исчезает из отчёта.
- OS file cache не сбрасывается: это new-process/warm-files benchmark. Cold-I/O
  сценарий можно добавить отдельно; нельзя смешать его с этой медианой.
- Полезный query и ожидаемый результат выбираются до прогона. Таймер включает
  first-use compilation/generator work. Query miss не считается полезным ответом.
- Mutations выполняются на owned копии/fixture; пользовательское решение не меняется.
  Same scenario повторяется с одинаковым содержимым/условиями и восстановлением setup.
- Каждый attempt пишет PID, outcome, raw durations и input differences в machine-
  readable data. Report ссылается на сохранённые raw files; утилита сама вычисляет
  median/min/max. p95 требует минимум 20 successful attempts и не обещается при n=5.

## Метрики

End-to-end: load, первый полезный semantic call и их сумма `useful`.
Stages: input enumeration/stat/text hashing/binary sample/read/decode/hydrate/
prepare/publication/capture/write — только фактически имеющиеся stages.
Также visited entries, bytes hashed/sample-read, уникальные binaries, число
dirty/reloaded проектов, actual Open/DTB observations, disk envelope size и
peak working set. Отсутствующую stage instrumentation отмечать not-measured.
Capture до load response входит в load/useful, его нельзя вынести из hit/miss
сравнения ради красивого результата. RAM warm cache не смешивается с disk restore.

## Сценарии по эпохам

- E0: forced ordinary baseline; isolated cross-process restore; capture overhead.
- E1: forced ordinary, empty-cache load, unchanged disk hit, changed-source miss,
  corrupt-cache fallback. Отдельно miss overhead и startup validation cost.
- E2: single/batch existing source edit, linked edit, AdditionalFile/config change,
  changed metadata binary; graph/membership miss; no-op build и changed build.
- E3: dirty single project csproj, shared props, source membership add/delete/rename,
  selective merge и whole-request fallback. Actual opened scopes учитывают
  transitive работу MSBuild loader, не только requested dirty count.

## Решение по результатам

До первых замеров E1 зафиксировать в control report baseline setup и правило
сравнения: минимум пять successful attempts, одинаковый compare key и expected
query; useful median hit меньше forced ordinary median. Miss overhead и scatter
показываются числами и принимаются отдельно; целевой процент ускорения пока не задан.
Недостаточный/нестабильный выигрыш записывается как performance not-accepted,
с raw evidence; не менять scenario или исключать failures задним числом.
Новая policy/threshold или control требует нового compare key и отдельного прогона.

Эпоха показывает собственный результат, не ожидает выполнения поздних optimizations.
Нет заранее обязательной многонедельной workload/hit-rate программы. Unsupported
profiles, эвристический binary blind spot, skips и not-run cases перечисляются
как пределы измерения. Functional correctness подтверждается tests/fresh ordinary
comparison отдельно от скорости.
