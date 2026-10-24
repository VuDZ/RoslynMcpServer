# BuildHost/task-00a — исправление SolutionDir в пробе evaluation

Дата: **2026-10-24**. Статус: **исправление проверено**; task-00a остаётся
**in progress**, офисная временная шкала pending.

## Причина и изменение

`EvaluationProbe.Run` открывал каждый проект напрямую через пустой
`ProjectCollection`. В этом режиме MSBuild не получает контекст решения:
`$(SolutionDir)common.cs.netcore.props` превращается в `common.cs.netcore.props`,
и импорт ищется рядом с проектом вместо каталога `.sln`.

Проба теперь нормализует путь решения и задаёт `SolutionDir` в global properties
коллекции как абсолютный каталог решения с завершающим разделителем. Это свойство
наследуется и при загрузке каждого TFM с дополнительным `TargetFramework`.
Изменение ограничено `spikes/LoadTimeline/EvaluationProbe.cs`; production-загрузчик,
capture и форк не менялись. Остальные свойства и режимы evaluation не изменены.

## Проверка

- `spikes/LoadTimeline/Test-EvaluationProbe.ps1`: до исправления воспроизведён
  `InvalidProjectFileException` на импорте `$(SolutionDir)common.cs.netcore.props`.
  После исправления `.sln` и `.slnx` проходят: проект находится вне каталога решения,
  путь решения относительный и содержит пробел, исходная evaluation и обе
  TFM-evaluation используют импорты рядом с решением. Для каждого формата CSV
  содержит один проект и три evaluation.
- Полный `Invoke-LoadTimeline.ps1` на синтетическом SDK-проекте `net10.0`
  с таким же импортом: restore, warmup, capture, evaluate, analyze прошли.
  Загружен один проект, три документа (исходник и два сгенерированных SDK),
  workspace failures — 0; один binlog, одна сборка верхнего уровня,
  ошибок чтения binlog — 0. В sandbox named pipe BuildHost недоступен
  (`UnauthorizedAccessException`); полный цикл выполнен вне sandbox.
- Spike собран в Release: 0 ошибок, 0 предупреждений.
- `RoslynMcpServer.sln`, Release: сборка успешна; три предупреждения в файлах
  вне правки (`CS8603`, `CS8601`, `xUnit1031`).
- Основной CI suite в `RoslynMcpServer.Tests`, Release,
  `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`: **1432 passed,
  0 failed, 0 skipped**. Включает SourceStructure.
- `AnalyzerLifecycle` отдельно не запускался: lifecycle и production-пути
  загрузки не менялись.

Синтетические входы и результаты находятся в ignored-каталоге
`spikes/LoadTimeline/results/`: регрессия —
`evaluation-check-cca5ba6640a44d0cabe21c6bf33e2a08`, полный цикл —
`solutiondir-smoke-4311a1736e5e4c9b92d9aa99a241da5c/output`.
Офисное решение здесь не запускалось; его повторный прогон нужен для завершения
task-00a. Исторические результаты Roslyn.slnx не пересчитывались.
