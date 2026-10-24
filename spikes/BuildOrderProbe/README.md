# BuildOrderProbe — порядок сборок и evaluation наперёд (spike)

Контракт: [BuildHost/task-02](../../docs/workspace-load-performance/research-buildhost-fork/task-02-buildhost-optimization-spike-hi.md),
шаг 0.

Проба собирает проекты решения так, как это делает BuildHost 5.9.0: те же глобальные свойства, одна
`ProjectCollection` без логгеров, один `BuildManager`, design-time build целей `Compile`, `CoreCompile` и
`DesignTimeMarkupCompilation`, для проекта с несколькими TFM — переключение `TargetFramework` у общего `Project`
и возврат в конце. Порядки:

- `client` — как клиент 5.9.0: проект собирается раньше, чем загружены проекты, на которые он ссылается;
  design-time build, дошедшая до ещё не собранной ссылки, заставляет MSBuild оценить её заново;
- `references-first` — ссылки собираются раньше проекта (часть A task-02);
- `evaluate-ahead` — тот же порядок, и как только проект оценён, корневые evaluation его ссылок идут в
  рабочих потоках, пока сборки выполняются по одной (часть B).

Каждый прогон — отдельный процесс; прогрев и `-Rounds` раундов, порядок трёх вариантов меняется от раунда к
раунду. `compare` проверяет, что все прогоны собрали одно и то же: для каждого проекта и TFM — хеш результата,
целевого пути и извлекаемых workspace items (аргументы компилятора, Compile, ReferencePath, ProjectReference,
анализаторы, AdditionalFiles, editorconfig) в их порядке.

```powershell
pwsh spikes\BuildOrderProbe\Invoke-BuildOrderProbe.ps1 -Solution <путь>\Roslyn.slnx
```

Параметры: `-Rounds` (по умолчанию 3); `-Workers` — список числа потоков для `evaluate-ahead`, по умолчанию 2, 4
и число логических процессоров минус один, каждое — отдельный вариант; `-ServerGc` — ещё `references-first` и
`evaluate-ahead` с наибольшим числом потоков под server GC; `-SkipRestore` для уже восстановленного решения.

Кроме времени, `summary.md` показывает, как evaluation легли на часы прогона: какая доля их времени
перекрывалась со сборками, сколько их шло одновременно в среднем и во сколько раз evaluation проекта дольше той
же evaluation в `references-first` — отдельно для проектов с одним и с несколькими TFM. Высокая одновременность
при большом замедлении и без выигрыша значит, что evaluation ждут друг друга, а не идут рядом со сборками. Решение с `global.json` требует закреплённого SDK, скрипт это
проверяет. Результаты — в `spikes\BuildOrderProbe\results\probe-<время>`, эта папка не попадает в Git. Для
разбора достаточно `summary.md` и `environment.json`: в них нет путей. JSON прогонов и
`mismatches-private.json` содержат полные пути проектов.

Пределы:
- порядок обхода — записи решения и ProjectReference корневой evaluation в глубину; клиент берёт ссылки из
  результата сборки по каждому TFM, поэтому обход — приближение клиентского порядка, одинаковое во всех
  прогонах;
- ссылка, которая появляется только в сборке или требует других глобальных свойств, в обходе не видна, и её
  вложенная evaluation остаётся и в `references-first`;
- это не форк и не клиент: RPC, извлечение `ProjectFileInfo` и работа клиента сюда не входят.

Проверено на `RoslynMcpServer.sln`, Linux, 2 vCPU, 2026-10-25: 9 прогонов, результаты identical; вложенных
evaluation 3 в `client` и 1 в двух других порядках. Время на такой машине шумит и о выигрыше не говорит.
