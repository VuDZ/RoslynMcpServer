# LoadTimeline — временная шкала загрузки (spike)

Контракт: [BuildHost/task-00a](../../docs/workspace-load-performance/research-buildhost-fork/task-00a-load-timeline-before-fork-med.md).

Spike открывает решение тем же `MSBuildWorkspace`, что и сервер (`spikes/ParallelSearch/SolutionLoader`),
с `BinaryLogger` и раскладывает binlog по времени:
- сборки верхнего уровня (запросы BuildHost) и вложенные (задача `MSBuild`);
- evaluation;
- их объединение и промежутки между ними;
- повторные сборки с одинаковыми свойствами и целями.

Команда `evaluate` отдельно оценивает evaluation проектов, которые BuildHost выполняет
без своих логгеров. Поэтому в binlog их нет, и они попадают в промежутки.

```powershell
powershell -ExecutionPolicy Bypass -File .\spikes\LoadTimeline\Invoke-LoadTimeline.ps1 -Solution <путь>\Roslyn.slnx
```

Скрипт делает прогревочную загрузку, затем загрузку с binlog, пробу evaluation и анализ.
Результаты пишутся в `spikes\LoadTimeline\results\timeline-<время>`, эта папка не
попадает в Git. Для разбора достаточно `timeline.md`, `evaluation.md` и `capture.json`,
их пути даны относительно корня Git-репозитория решения. В binlog полные пути и
содержимое проектов, его передают, только если это разрешено.
