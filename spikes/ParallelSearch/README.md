# ParallelSearch — изолированный spike параллельного поиска объявлений

Контракт: [task-11](../../docs/workspace-load-performance/epoch-0-stage-measurement/task-11-parallel-project-search-spike-hi.md).
Проект не входит в solution/CI сервера и исключён из его Compile/None items.

Spike повторяет поиск сервера (`FindSourceDeclarationsAsync(project, name, ignoreCase: true,
Type | Member)` по каждому проекту) и загрузку (MSBuildWorkspace с теми же MEF-сборками,
только C#). DOP 1 — последовательный цикл сервера; DOP 2/4/8 держат столько проектов
одновременно и склеивают результат по индексу проекта, поэтому ответ не зависит от DOP.

## Запуск матрицы на Windows

Нужна машина с запасом памяти: Roslyn.slnx не загружается на 3,75 ГБ (OOM, 2026-10-23).

1. Клон Roslyn на commit протокола:

   ```powershell
   git clone https://github.com/dotnet/roslyn.git D:\src\roslyn
   git -C D:\src\roslyn checkout 013d3a758df6c137497ff37a93f0d4bed103853a
   ```

2. .NET SDK **10.0.301** (его закрепляет `global.json` Roslyn, `rollForward: patch`):
   `winget install Microsoft.DotNet.SDK.10 --version 10.0.301` или установщик с dot.net.
   Скрипт проверит наличие и остановится с подсказкой.

3. Из дерева ветки `research/workspace-load-performance` этого репозитория:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\spikes\ParallelSearch\Invoke-ParallelSearchMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
   ```

   Скрипт собирает spike, выполняет self-checks, `dotnet restore` решения и затем
   2 запроса × 4 DOP × (1 прогрев + 5 замеров) = 48 новых процессов. Каждый процесс
   заново загружает Roslyn, поэтому прогон занимает порядка часа и больше.
   Во время замера не собирать и не открывать другие решения.

4. Результаты — в `spikes\ParallelSearch\results\<время>`: JSON каждого процесса,
   его вывод (`*.out.txt`), `environment.json`, `summary.md` и `summary.json` с вердиктом.
   Пути проектов в них относительные. Скрипт в конце печатает `git add/commit/push`
   для передачи папки на машину анализа; после разбора папка удаляется из репозитория.

`-Runs`, `-Degrees '1,2,4'`, `-MatchName`, `-MissingName`, `-SkipRestore` меняют параметры;
`-SummarizeOnly <папка>` пересчитывает сводку без новых замеров. Пороги вердикта
зафиксированы в task-11 до измерений и в скрипте не настраиваются.

## Прочие команды

```text
dotnet bin/Release/net10.0/ParallelSearch.dll check
dotnet bin/Release/net10.0/ParallelSearch.dll measure --solution PATH --name NAME --dop N --output JSON
```

`check` — порядок ответа при DOP 1/2/4/8, внешняя отмена, проброс первой ошибки без
обёртки и отмена остальных проектов, пустое решение, проект без документов.
