# D/E0/task-11 — Windows-прогон параллельного поиска

Дата: **2026-10-23**. Scope этого отчёта — запуск существующего spike и проверка
его результатов. Код spike и production не менялись. Матрица выполнена;
формальная приёмка реализации и независимое ревью остаются pending.

## Стенд и запуск

- Spike: `bd20ef9f400d`, ветка `research/workspace-load-performance`, перед запуском
  проверена актуальность относительно origin.
- Roslyn: чистый существующий клон `clones/roslyn-deep`, commit
  `013d3a758df6c137497ff37a93f0d4bed103853a`, полный `Roslyn.slnx`, Debug.
- Windows 11 build 26200, Ryzen 7 9800X3D, 8 ядер / 16 потоков, 93,7 GiB RAM.
- Использован уже установленный локальный SDK **10.0.301**, runtime **10.0.9**;
  `DOTNET_ROOT` и начало `PATH` указывали на него. Установка SDK не потребовалась.
- По указанию владельца клон и сырые результаты находятся в
  `E:\Devel\roslyn-mcp-bench`; run ID — `20261023-113651`, результаты —
  `measure/parallel-search/20261023-113651/`. Для передачи на машину анализа
  99 файлов матрицы скопированы побайтно в
  [spikes/ParallelSearch/results/20261023-113651/](../../../../spikes/ParallelSearch/results/20261023-113651/).
  Transcript с локальными путями и исходный diagnostic JSON оставлены на стенде;
  диагностика с относительными путями сохранена ниже. Локальный `.gitattributes`
  отключает преобразование переводов строк, чтобы сохранить hashes файлов.
- Запущен `spikes/ParallelSearch/Invoke-ParallelSearchMatrix.ps1` с `-Solution`
  этого клона и `-OutDirectory` указанного run; остальные параметры по умолчанию,
  включая restore, 2 запроса, DOP 1/2/4/8, прогрев и 5 измерительных раундов.
  PowerShell 7.6.5; запуск 11:36:51–12:24:20 МСК (47 мин 29 с).

## Результат и проверка полноты

- Release build spike: PASS, 0 warnings / 0 errors.
- Self-checks: PASS — порядок, cancellation, исключение, пустое решение,
  проект без документов. Restore Roslyn: exit 0.
- Матрица: **48/48 процессов завершились**, exit скрипта 0; по каждому запросу
  и DOP есть прогрев и ровно 5 измерений. Неуспешных процессов нет.
- Во всех результатах: SDK 10.0.301, 434 проекта, 50 201 документ;
  первый и второй поиск содержат по 434 успешных проектных outcome.
- Хеши первых и повторных ответов совпали во всех DOP и раундах каждого запроса.
  `CSharpCompilationOptions`: 4 объявления; отсутствующее имя: 0.
- Вердикт скрипта: **no-go**. DOP 4 даёт медианы 16,785 с вместо 20,726 с
  для совпадения (ratio 0,810) и 15,088 с вместо 17,752 с для отсутствующего
  имени (ratio 0,850). Порог wall ≤ 0,70 не выполнен; память укладывается в 1,25.
  DOP 2 также не проходит wall gate на обоих запросах. Production DOP не выбран.

Полные медианы CPU/RAM/GC и второго поиска — в неизменённой
[сводке скрипта](task-11-parallel-project-search/summary.md), машинная версия —
[summary.json](task-11-parallel-project-search/summary.json),
[окружение](task-11-parallel-project-search/environment.json),
[SHA-256 всех 101 сырого файла](task-11-parallel-project-search/raw-sha256.txt).
Хеш `workspace-diagnostics.json` в этом inventory относится к сырому оригиналу;
копия в evidence отличается удалением абсолютного корня клона из сообщений.

## Ограничения загрузки и приёмки

**Загрузка не была безошибочной:** каждый процесс записал `workspaceFailures=44`.
После матрицы отдельная временная утилита вне репозитория вызвала тот же
`SolutionLoader.LoadAsync` из собранного spike и прочитала `MSBuildWorkspace.Diagnostics`.
Она воспроизвела те же 434 проекта, 50 201 документ и 44 Failure:

- 41 — `.shproj` не связан с отдельным языком и не открыт как самостоятельный проект;
- 3 — XAML не находит `AbstractOptionPageControl`: два сообщения для
  `Microsoft.VisualStudio.LanguageServices.CSharp.csproj`, одно для
  `Microsoft.VisualStudio.LanguageServices.VisualBasic.vbproj`;
- дополнительно 43 Warning: 34 о дублирующихся source files и 9 о project
  references без matching metadata reference (`csc`, `vbc`, `VBCSCompiler`,
  по три сообщения).

[Полный диагностический inventory](task-11-parallel-project-search/workspace-diagnostics.json)
сохранён с относительными путями. Детали получены отдельной загрузкой после
замеров; в 48 исходных процессах сохранены только счётчики. Стабильные размеры
workspace и хеши ответов не доказывают полноту загрузки. Численный no-go относится
к этому стенду; результат не объявляется успешной загрузкой полного Roslyn или
готовностью production. Исправление XAML-окружения в этот запуск не входило.

Release build серверного solution, main CI, SourceStructure и малый контроль
`RoslynMcpServer.sln` в этом запуске не выполнялись; AnalyzerLifecycle не запускался.
Это запуск существующего benchmark, а не приёмка изменений C#/tests из другого
агента. Task-11 и эпоха не помечены accepted. Коммит фиксирует сырые измерения,
отчёт, сводки, диагностику и статусы этого прогона; публикация в remote не выполнялась.
