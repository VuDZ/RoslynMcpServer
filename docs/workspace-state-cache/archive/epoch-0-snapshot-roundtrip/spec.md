# E0 — Snapshot round-trip

Статус: **draft / ready-for-review, 2026-10-07**. Реализация не начата.

Общие границы — [README серии](../README.md), [порядок исполнения](../execution.md),
[бенчмарки](../benchmark.md). Эти три документа обязательны для ревью эпохи.

## Результат

Получить один сквозной сценарий обычной MSBuild-загрузки, сохранения переносимого
описания и восстановления в новом процессе через публичные Roslyn API.
Первый semantic-запрос должен вернуть ожидаемый результат. Production load path
в этой эпохе не меняется; результат пригоден для последующей интеграции.

## Поддерживаемый срез

В task-00 выбираются маленький контроль и реальный неизменённый проект/решение
с однозначными загруженными project instances. Это не доказательство поддержки
всех больших решений. Сначала проверяются фактические parse/compilation options,
documents/memberships, directed project references и metadata references.
Непредставимые options или неоднозначный TFM дают явный отказ capture.
Исходники читаются с диска; source/generated text в DTO не сохраняется.

Analyzer identities сохраняются, а generator output не принимается за переносимый
Document. Положительный base round-trip не разрешает выполнить DLL из DTO.
Генераторы и overlay должны пройти отдельную production preparation в E1;
зависимый от генерации semantic-сценарий пока является отдельным not-supported
контролем. Нельзя удалить references ради положительного результата.

## Использование прежних результатов

За отправную точку взять [hydrate experiment и его ограничения](../../../backlog/workspace-load-cache/epoch-0-feasibility/evidence/task-01-hydrate-host.md)
и существующие WorkspaceLoadCache tests. Не повторять dependency closure research.
Публичные getters StrongNameProvider/DocumentationProvider и binding inner-TFM
проверяются только для выбранного среза: точное представление либо отказ,
без private reflection и silent defaults.

## Приёмка

Нужны реальный ordinary capture, round-trip в новом PID, независимый expected
semantic result, bounded codec failures и освобождение candidate ресурсов.
Synthetic DTO-only успех не закрывает эпоху. Изолированная правка существующего
`.cs` проверяет сохранение encoding и linked memberships; production write
boundary этим экспериментом не заменяется.

Бенчмарк: ordinary baseline и isolated restore, load/restore/query/capture timings
по [единому протоколу](../benchmark.md). Если выбранный большой корпус ещё
не поддержан, report сохраняет отказ; поддержанный реальный контроль всё равно нужен.
