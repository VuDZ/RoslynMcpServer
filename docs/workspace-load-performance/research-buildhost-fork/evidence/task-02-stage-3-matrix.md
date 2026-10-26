# BuildHost/task-02 — этап 3: замер вариантов в одном прогоне

Дата: **2026-10-26**. Статус: **in progress** — стенд готов, Linux-проверка — ниже; pending: прогоны владельца на
Windows. Контракт: [task-02](../task-02-buildhost-optimization-spike-hi.md#этапы); варианты —
[этап 1](task-02-stage-1-references-first.md#итог-этапа-1) и [этап 2](task-02-stage-2-evaluate-ahead.md#итог-этапа-2).

## Стенд

`spikes/BuildHostFork/Invoke-BuildHostForkMatrix.ps1`: тот же стенд, что у baseline (одна сборка спайка, `bin-fork`
с форком в `BuildHost-netcore`, каждая загрузка — новый процесс клиента), но сторон шесть:

| сторона | что включено |
|---|---|
| `original` | BuildHost из пакета 5.9.0 — эталон ответа |
| `fork` | форк без оптимизаций — эталон времени для порогов task-02 |
| `fork-a` | A, `ROSLYN_BUILDHOST_REFERENCES_FIRST=1` |
| `fork-b` | B, `ROSLYN_BUILDHOST_EVALUATE_AHEAD=4`, все записи решения |
| `fork-ab` | A + B |
| `fork-ab-trace` | A + B с трассой task-01; против `fork-ab` — цена трассы (F10 task-01) |

- **Порядок.** Каждый раунд запускает каждую сторону один раз; порядок сдвигается на одну сторону за раунд, так что
  ни одна сторона не идёт всегда первой и не идёт всегда за одной и той же. Раунд 0 — прогрев.
- **Ответ.** Каждый вариант сравнивается с одними и теми же прогонами оригинала по правилам task-00
  (`<вариант>/summary.md`); скрипт падает, если какой-то вариант не equivalent.
- **Сводка.** `matrix.md`: ответ каждого варианта и медианы загрузки, CPU и пика рабочего набора host и клиента —
  отношением к оригиналу и к `fork`. `fork-ab-trace/trace-summary.md` — куда уходит время.
- `-Variants` выбирает часть сторон, `-EvaluateAhead` и `-EvaluateAheadSolution` — настройки B.

Время прогона — шесть загрузок и шесть первых поисков на раунд: Roslyn.slnx на офисном компьютере около часа при
3 раундах, офисное решение около получаса, Roslyn.slnx дома около 25 минут.

## Linux, 2 vCPU, проверка стенда

`RoslynMcpServer.sln`, `-Runs 1 -EvaluateAhead 2` (прогрев и 1 раунд), 2026-10-26: все пять вариантов
**identical**, скрипт закончился с кодом 0. В трассе `fork-ab-trace` A и B включены: решение принято (5 записей),
4 evaluation наперёд, 2 сборки ссылок заранее, clock check — 20 запросов, 0 вне допуска. Порядок раундов: раунд 0 —
`original` первым, раунд 1 — `fork` первым, `original` последним. Времена 5 проектов на 2 vCPU о выигрыше не говорят.

Сравнитель для матрицы берёт прогоны оригинала из общей папки (`ObservationComparer.Run(directory,
originalDirectory)`) и возвращает вердикт ответа; самопроверки сравнителя пройдены. Сборка решения и main-набор CI (Release, Linux-фильтр) — 1 390 passed, 0 failed,
0 skipped; изменения этапа в main-решение не входят.

## Pending: прогоны владельца

После `git pull`, прогрев и 3 раунда:

```powershell
pwsh spikes\BuildHostFork\Invoke-BuildHostForkMatrix.ps1 -Solution <путь>\Roslyn.slnx
```

На офисном компьютере — Roslyn.slnx и офисное решение, дома — Roslyn.slnx. Для анализа достаточно `matrix.md`,
`<вариант>\summary.md` и `fork-ab-trace\trace-summary.md`; JSON и папки трассы содержат пути проектов. Итог
go/no-go по вариантам — по порогам [task-02](../task-02-buildhost-optimization-spike-hi.md#решения-и-пороги)
против `fork`.
