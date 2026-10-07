# Ревью планов workspace state cache

Статус: **ожидает независимого ревью, 2026-10-07**. Отчётов пока нет.

## Вход и scope

Рецензент читает [README серии](../README.md), [execution](../execution.md),
[benchmark](../benchmark.md), spec/README/tasks проверяемой эпохи и применимые
existing production seams. Legacy v2 — источник findings, не обязательный новый
scope. Не требовать замкнутости всего SDK/restore как prerequisite первого MVP.
Актуальные write/analyzer/publication инварианты обходить нельзя.

Можно проверить E0–E3 одной сессией или назначить отдельную сессию на эпоху.
В scope отчёта явно перечисляются фактически проверенные документы. Первоначально
одна или две модели; отдельные task-файлы для их review не создаются.
При xhi task execution review рекомендуется несколько независимых рецензентов.

## Что проверить

- Есть ли полезный результат каждой эпохи; нет ли зависимости E1 readiness
  от optional E3; real positive контроль вместо all-unsupported success.
- Capture options/references и host ownership работают на actual ordinary load;
  известные representability findings не спрятаны за synthetic DTO.
- Offline scan находит новые/удалённые входы и связывает shared/global input со
  всеми owners; explicit ignored/external inputs не потеряны.
- `mvp-fast` binary heuristic названа честно; sample hash не обещает полную
  неизменность и не заменяет analyzer execution checks.
- New PID, preparation/provenance, startup watcher race, cancellation и write
  boundary отражены в задачах; нет второго live index или новых blanket read bans.
- Tasks небольшие, outputs конкретны, dependencies ацикличны; runtime choices
  не требуют огромных предварительных decision/implementation packets.
- Benchmark учитывает весь useful путь, capture/miss costs и failures; expected
  query/compare key зафиксированы заранее; independent review не выдаётся за tests.

## Отчёт

Имя: `epoch-N-round-R-model.md` либо `series-round-R-model.md`.
Actual model пишется точным доступным именем; неизвестное не угадывать.
Шапка содержит date, scope, author/reviewer session identities (если доступны),
проверяемую version/commit/diff, номер раунда, итог и количество подтверждённых
исправленных findings. Initial report сохраняет ещё не исправленные findings.

Каждый finding: stable ID, priority, claim/evidence, конкретный failure scenario
и требуемая ограниченная поправка. Ответ автора и результат recheck сохраняются
рядом или в том же report. Duplicate findings разных моделей объединяются с
сохранением всех sources. Авторская проверка подготовки не является независимым ревью.

Раунды и fixed findings ведутся по [общему стандарту](../../README.md#учёт-задач-ревью-и-статистики).
Все модели одного checked version — один round. Счётчики README эпохи/серии
обновляет координатор после recheck. Plan accepted и implementation accepted
не смешиваются; untouched/not-reviewed данные остаются `—`.

После принятия плана эпохи report здесь сохраняется. При archival всей серии
review history/статистика/ссылки переносятся вместе с ней.
