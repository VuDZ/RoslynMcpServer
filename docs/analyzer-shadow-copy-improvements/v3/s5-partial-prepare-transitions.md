# S5 — обеспечить безопасную публикацию частичного prepare

Статус: **не выполнено**. Зависимость: [S4](s4-provenance-failure-gate.md).
Результат шага: смешанный результат подготовки имеет проверенный безопасный
путь публикации и честную диагностику.

## Основание

Один `HasAnyApplied` не доказывает, что все подтверждённые in-solution
references безопасны. После исправления отдельных failure cases нужно
проверить несколько analyzers и переходы между active/stale/blocked.
Этот сценарий выявлен статическим анализом как риск; отдельного runtime
воспроизведения частичного prepare в ревью не было.

Targets: `AnalyzerShadowPrepareOutcome`, `AnalyzerShadowMapping`,
`SolutionManager.CompletePrepare`, `EvaluatePreparedMapping`, publication
state и load summary, lifecycle fixtures с несколькими генераторами.

## Работа

Проверять допустимость публикации по всем относящимся к overlay references,
а не только наличию одной успешно подготовленной. Неуспешная confirmed
reference не возвращается на real output вследствие успешной подготовки
другой. Для неё допустим прежний совместимый stale mapping, если execution
gate разрешает его, либо исключение из безопасного snapshot. Если безопасную
публикацию нельзя доказать, semantic snapshot недоступен целиком.

Заявленный результат должен различать prepared, фактически applied,
сохранённый stale и blocked. Частичный prepare не сообщается как полный
refresh или подтверждённое исполнение всех генераторов. Если решение
блокирует всю публикацию, подготовленные файлы сами по себе не означают
`Applied=true` в пользовательской сводке.

Подготовить fixture минимум с двумя генераторами с разными assembly identities,
чтобы тест частичного результата не подменялся ожидаемой CLR collision.
Проверить первую подготовку с одной файловой ошибкой и refresh со старым
mapping. Проверить также переход из active в restart-required и последующие
обычные публикации: stale V1 не должен снова становиться исполняемым.

Exact inverse должен понимать допустимый опубликованный snapshot, включая
намеренно исключённые references, если выбрана такая форма fail-closed.
Эти исключения нельзя сохранять как удаление исходных analyzer items из
`.csproj` или молча принимать как произвольный analyzer diff.

## Приёмка

- При одном successful и одном failed prepare отсутствует confirmed real
  output в semantic snapshot и process assemblies. Неподготовленный marker
  отсутствует; разрешённый marker проверяется точно, если snapshot доступен.
- Edit/flush/reconciliation сохраняют тот же допуск без analyzer I/O;
  `.csproj` byte-identical, повторная сборка не получает shadow includes.
- File-failed refresh явно сохраняет stale только там, где это допустимо;
  restart-required не исполняет ни stale V1, ни неподдержанную V2.
- Forced rebuild существующего real output после разрешённых semantic
  операций успешен и меняет hash; отсутствие marker не заменяет lock check.
- Load summary согласован с фактическим snapshot при частичном результате,
  отсутствии всех applied entries и полном запрете публикации.
- Существующие immutable generation, PDB optional и foreign-reference
  гарантии не ослаблены ради прохождения матрицы.

## Результат

Не выполнено.
