# Скорость обычной загрузки workspace

Дата: **2026-10-22**. Текущий статус: **исследование продолжается в Git-ветке research/workspace-load-performance; production диагностика 1.5.8 и source-only поиск/исправление replay 1.5.9 реализованы и проверены; офисный baseline получен. Исследование форка BuildHost in progress: task-00 (форк без изменения поведения) accepted 2026-10-24; task-00b (утечка при ранней отмене) in progress, смягчение в сервере 1.5.16, пакет с фиксом ещё не вышел**.

Ниже сохранена история standalone экспериментов 2026-10-10; прежние pending CI и commit-not-created описывают состояние на ту дату.
E0/task-05 и task-05.1: **implemented, 2026-10-10 — оба блокера профиля сняты;
обе направленные калибровки не проходят пороги, диагностические планы не проходят
gate; no-go, шаг 2 не выполнялся. Scope — standalone spike, профили,
калибровка/диагностическая симуляция и evidence, без production; формальная
приёмка code/tests pending единого зелёного main CI: 1362 passed / 1 прежний
SDK-specific failed ([отчёт и validation](epoch-0-stage-measurement/evidence/task-05.1-profile-retry.md))**.
E0/task-07: **implemented, 2026-10-10 — шаг 0 и 40 прогонов завершены, no-go; XML N=4 даёт 1,84×, но меняет OutputFilePath/diagnostics, snapshot допускает повторы ROA=false dependencies. Независимое ревью round 2 ACCEPT; scope — standalone spike/evidence, без production; code/tests acceptance pending main CI: 1362 passed / 1 прежний SDK-specific failed** ([результат](epoch-0-stage-measurement/evidence/task-07-core-first-parallel-spike.md), [review/validation](epoch-0-stage-measurement/evidence/task-07-core-first-parallel-spike/validation.md)).
E0/task-07.1: **implemented, 2026-10-10 — шаг 0 и 52 matrix attempts завершены, no-go; PowerToys equivalent, лучший N=4 depth2 25,228 с против 41,825 с (1,658×), Files cluster N=2/4 невыполнимы при ε=0,15. Независимое ревью round 2 ACCEPT для standalone реализации/evidence; code/tests acceptance pending main CI: 1362 passed / 1 прежний SDK-specific failed / 0 skipped; commit не создан, production и E1 не активированы** ([результат](epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition.md), [review/validation](epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition/validation.md)).
E0/task-07.2: **implemented, 2026-10-10 — 36 matrix runs и 6 дополнительных load processes, no-go; PowerToys cluster N4 23,306 с против 41,536 с (56,11%), pre-load отказы Files/RoslynN8 ≤105%. Roslyn parallel correctness и универсальный детерминизм deadline-limited N8 не достигнуты. Независимое ревью round 2 ACCEPT для standalone реализации/evidence отрицательного эксперимента; code/tests acceptance pending main CI 1362 passed / 1 прежний SDK-specific failed / 0 skipped; commit не создан, production/E1 не активированы** ([результат](epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn.md), [validation](epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn/validation.md)).

Происхождение: решение владельца [H-003](../workspace-state-cache/human-decisions.md#h-003--отсрочка-маршрута-disk-manifest), вариант D.

## Текущее продолжение, 2026-10-22

- [E0/task-00](epoch-0-stage-measurement/task-00-current-version-log-measurement-low.md): офисные измерения получены, результат принят в scope замера по существующим логам.
- [E0/task-08](epoch-0-stage-measurement/task-08-source-search-and-office-results-med.md): source-only поиск принят, MainViewModel 36,203 → 34,175 с (−5,60%), ответы побайтно совпали; оба binlog читаются.
- [E0/task-09](epoch-0-stage-measurement/task-09-first-search-io-and-private-project-profile-hi.md): in progress; офисный прогон 2026-10-23 подтвердил причину — повторы чтения отсутствующего сгенерированного `VersionInfo.cs`, ~24 из 31 с первого поиска ([отчёт](epoch-0-stage-measurement/evidence/office-first-search-2026-10-23.md)). Исправление — implemented (сервер 1.5.13; офис: первый поиск 31,4 → 19,5 с при workstation GC, 16,1 с с server GC) [E0/task-13](epoch-0-stage-measurement/task-13-missing-source-files-fast-fail-med.md).
- 2026-10-23, по решению владельца: [E0/task-10](epoch-0-stage-measurement/task-10-first-search-exception-and-trace-diagnostics-med.md) — сводка исключений поиска в сервере и dotnet-trace для офиса; [E0/task-11](epoch-0-stage-measurement/task-11-parallel-project-search-spike-hi.md) — standalone spike параллельного поиска по проектам. Task-10 implemented (сервер 1.5.11, `scripts/Invoke-FirstSearchDiagnostics.ps1`), Windows-проверки pending до офисного прогона ([отчёт](epoch-0-stage-measurement/evidence/task-10-first-search-diagnostics.md)); task-11 in progress — Windows-матрица выполнена 48/48, численный no-go на стенде с 44 Failure загрузки; code/tests acceptance и независимое ревью pending ([отчёт, 2026-10-23](epoch-0-stage-measurement/evidence/task-11-parallel-project-search.md)). Анализ показал: паузы workstation GC занимают 40–46% первого поиска и не параллелятся. Отсюда [E0/task-12](epoch-0-stage-measurement/task-12-server-gc-first-search-med.md) — матрица workstation/server GC; она in progress: Windows-матрица 48/48, go (server) на измеренном стенде, 44 Failure загрузки; офис на 1.5.13: первый поиск ×0,82, паузы GC 4,9 → 1,1 с; приёмка pending ([отчёт, 2026-10-23](epoch-0-stage-measurement/evidence/task-12-server-gc-first-search.md)). Server GC включён в сервере 1.5.12 в research-ветке для офисного контроля; перенос в main — после офисного прогона.
- [Исследование форка BuildHost](research-buildhost-fork/README.md): task-00 — воспроизводимый fork, accepted 2026-10-24 (identical на Smoke, `RoslynMcpServer.sln`, Roslyn.slnx и офисе; [приёмка](research-buildhost-fork/evidence/task-00-fork-baseline.md#приёмка-2026-10-24)); task-00a — временная шкала загрузки, in progress; task-00b — утечка BuildHost при ранней отмене, in progress (смягчение в сервере 1.5.16, пакет с фиксом ещё не вышел); task-01 — граф/тайминги и task-02 — направленная оптимизация, planned. Здесь же перечислены потенциальные пути ускорения и критерии проверки.
- [Предкоммитная validation](epoch-0-stage-measurement/evidence/commit-2026-10-22.md) фиксирует код, тесты и границы сохранённого checkpoint.

## Зачем

Сохранение серии в Git, **2026-10-21**: standalone код и компактный evidence
подготовлены к коммиту по поручению владельца; Release builds и полный main CI
прошли (**1388/0/0**, SourceStructure 123 passed). Это новый
[checkpoint](epoch-0-stage-measurement/evidence/commit-2026-10-21.md);
pending/commit-not-created в записях 2026-10-10 остаются историей того состояния.

Следующий эксперимент по решению владельца **2026-10-21** —
[E0/task-07.3](epoch-0-stage-measurement/task-07.3-evaluated-load-graph-hi.md),
**planned**: граф зависимостей загрузки после MSBuild evaluation, один Roslyn N4
correctness check и только при его успехе — измерение wall с ценой получения графа.
Отрицательные результаты task-07–task-07.2 сохраняются.

[Ревью направления темы и ограничения BuildHost](direction-review-2026-10-21.md),
**2026-10-21, open**: замечания и неисследованные варианты для решения владельца;
требования и границы задач не меняет. Прочитать до начала task-07.3.

У коллег целевое решение загружается в `load_workspace` несколько минут. Где
уходит это время, неизвестно. Серия workspace-state-cache мерила только
SamplePlugin (два проекта, load около 1.4 с) и BTCPay (load 3.6 с), а
профилирование [E1/task-00.4](../workspace-state-cache/epoch-1-disk-cache-mvp/evidence/task-00.4-ordinary-alternative.md)
трассировало только процесс сервера, без BuildHost. Без разбивки по стадиям
нельзя ни выбрать оптимизацию, ни оценить выгоду вариантов
[C](../workspace-host-process/README.md) и
[B](../workspace-state-cache/epoch-1b-optimistic-restore/README.md).

## Цель

1. Получить разбивку времени обычной загрузки и первого семантического запроса
   по стадиям на реальном большом решении, которого нет в этом репозитории.
2. По этим данным выбрать и реализовать оптимизации, не меняющие результат загрузки.

## Границы

- Результат ordinary load не меняется: graph, options, references, diagnostics,
  analyzer admission, write boundary и watcher policy прежние. Оптимизация,
  меняющая наблюдаемый результат, к этой теме не относится.
- Исторические E0/task-04–task-07.3 сохраняют границы без fork BuildHost;
  task-07.3 остаётся public-API экспериментом. По решению владельца 2026-10-22
  добавлено отдельное [исследование форка](research-buildhost-fork/README.md).
  Это разрешение на изолированное исследование, без disk cache, production
  activation и новых defaults.
- Новые записи лога — English, структурированные, по одной на событие. Без
  содержимого файлов, environment и secrets. Пути проектов уже пишутся в лог;
  протокол замера описывает их обезличивание перед отправкой.
- Измерения различают новый PID, RAM hit и первый семантический запрос.
  Файловый cache ОС не сбрасывается; это фиксируется в отчёте.

## Маршрут

- [E0 — разбивка по стадиям](epoch-0-stage-measurement/README.md): 20 задач (включая task-05.1, bugfix task-06, task-07–task-07.3 и поиск task-08–task-15). 2026-10-23: [E0/task-14](epoch-0-stage-measurement/task-14-roslyn-index-storage-spike-hi.md) — go: persistent storage индексов Roslyn (SQLite) при тёплом кеше сокращает первый поиск на Roslyn.slnx вдвое–вчетверо и пиковую память поиска втрое; [E0/task-15](epoch-0-stage-measurement/task-15-roslyn-index-storage-in-server-med.md) включает его в сервере 1.5.14; офис: первый поиск 16,1 → 13,6 с, за день 31,4 → 13,6 с.
  Замер текущей версии по уже существующим логам, время стадий в production
  логе, сводка design-time build из уже снимаемого binlog, baseline на реальном решении,
  spike параллельной загрузки несколькими `MSBuildProjectLoader`.
- [Исследование форка BuildHost](research-buildhost-fork/README.md): отдельная
  planned ветка из трёх задач, не подменяет приёмку E0 и production E1.
- **E1 — оптимизации**: планируется после baseline E0. Задачи заводятся только
  под подтверждённый hotspot, не больше двух за раз.

Кандидаты E1 — гипотезы, а не план. Каждая выбирается только по evidence E0:

- **Provenance replay и snapshot** заметны в общем времени — один проход replay
  с отбором нужных событий либо публикация снимка для навигации до конца replay
  с запретом analyzer execution до его завершения. Второй путь меняет порядок
  admission и требует отдельного ревью.
- **Input map и старт watchers** заметны на больших деревьях — построение после
  публикации или инкрементально. Watcher обязан стартовать до первой disk flush
  (pitfalls 20 и 29).
- **Первый семантический запрос** (compilation, generators) — фоновый прогрев
  compilation после ответа load. Метрика time-to-first-useful должна учитывать
  паузу агента между вызовами, иначе выгода не видна.
- **Design-time build внутри BuildHost** доминирует (прокси: 94–98 % load, одно
  ядро) — в историческом E0 сам BuildHost не меняется, но публичный `MSBuildProjectLoader` позволяет
  запустить несколько BuildHost параллельно. Выгоду и эквивалентность проверяет
  [E0/task-04](epoch-0-stage-measurement/task-04-parallel-loader-spike-hi.md); при no-go
  это довод в пользу C и B. Task-04 дала no-go (1,51× при N=4, гонка на общих obj
  из-за повторных загрузок); [E0/task-05](epoch-0-stage-measurement/task-05-profile-guided-partition-hi.md)
  проверяет разбивку по профилю стоимости без повторов markup.
  Task-05 остановлена на шаге 1: второй binlog PowerToys v27 не читается reader
  до v26, а Files-профиль не содержит evaluation-интервалов. Калибровка и
  реальные загрузки по плану не начаты; гипотеза speedup не проверена
  ([результат](epoch-0-stage-measurement/evidence/task-05-profile-guided-partition.md)).
  Ревью: v27 читается с `AllowForwardCompatibility` (пустая сессия net472),
  стоимость проекта берётся по разрывам между top-level builds. Тот же reader
  в provenance capture сервера даёт `Incomplete` на PowerToys. Продолжение —
  [E0/task-05.1](epoch-0-stage-measurement/task-05.1-profile-retry-hi.md);
  bugfix сервера — [E0/task-06](epoch-0-stage-measurement/task-06-provenance-forward-compatible-replay-med.md).
  Task-05.1 закрыла ветку выводом, что повтор ядра публичным API не убрать;
  2026-10-10 вывод поправлен: `ProjectMap.Create(Solution)` и проект-агрегатор
  позволяют загрузить ядро один раз, а группы — параллельно поверх него.
  Проверка — [E0/task-07](epoch-0-stage-measurement/task-07-core-first-parallel-spike-hi.md);
  порог go снижен владельцем до 75 % baseline при строгой эквивалентности.
  Task-07: 1,84× при N=4, no-go только по эквивалентности — `SolutionDir` не
  задан загрузчикам групп, native-проекты не запрошены, snapshot-граф не видит
  рёбер `ReferenceOutputAssembly=false`. Продолжение —
  [E0/task-07.1](epoch-0-stage-measurement/task-07.1-core-first-equivalence-and-partition-hi.md):
  исправления входов загрузки, кластерное разбиение корней, изолированный прогон
  групп для причины замедления.
  Task-07.1 завершила standalone эксперимент: эквивалентность восстановлена,
  cluster N=4 уменьшил ядро с 35 до 12 проектов, но planning сделал wall хуже
  greedy. Isolated показывает 14–25% общей цены конкуренции; источник большого
  context residual не установлен. No-go полной матрицы: Files не допускает
  требуемого баланса при ε=0,15; [результат](epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition.md).
  Продолжение — [E0/task-07.2](epoch-0-stage-measurement/task-07.2-refusal-fast-planner-roslyn-hi.md):
  отказ до загрузки по [решению владельца](#решение-владельца-2026-10-10-отказ-параллельной-загрузки),
  планировщик с бюджетом 500 мс, разложение примерно 5 с прогрева BuildHost по
  binlog, полный `Roslyn.slnx` с прогнозом до замера; регрессия — PowerToys
  cluster N=4 и Files (ожидается отказ).

### Решение владельца 2026-10-10: отказ параллельной загрузки

- Отказ от параллельной загрузки **до** запуска BuildHost, когда она бессмысленна
  или вредна (план не балансируется, прогноз без выигрыша, оценка памяти выше
  лимита, превышен бюджет планирования), — допустимый исход. Он выполняет
  обычную загрузку и в пороге считается как обычная загрузка плюс время
  планирования.
- Отказ **после** загрузки (`plan-violation`, ошибка или timeout группы) остаётся
  провалом в пороге spike; в production это дорогой откат.
- Порог task-07 и task-07.1 этим решением не переписывается: их no-go остаётся
  в силе, правило действует с task-07.2.
- OrchardCore как дополнительный корпус отложен; прогон на офисном ПК коллег —
  позже, вне task-07.2.

## Хранение evidence

Решение о сохранении результатов, **2026-10-21**:

- В Git сохраняются task specifications, отчёты, review/validation, компактные
  JSON-сводки, параметры/прогнозы до замеров и SHA-256 inventories. Это основания
  go/no-go и входы продолжений; удаление каталога ломает ссылки и теряет историю
  уже проверенных гипотез.
- Перед добавлением task-07.3 весь каталог evidence занимал **2 244 144 bytes,
  73 файла** (около 2,24 MB). Он сохраняется целиком; отдельное LFS-хранилище или
  очистка этих небольших файлов сейчас не нужны. Существующие небольшие .gz
  witnesses остаются частью исторического evidence.
- Полные snapshots, binlogs, obj inventories, логи и TRX остаются вне Git в
  ignored `artifacts/`. Только task-07.2 занимает там около **3,88 GB**.
  Для активного продолжения их сохраняют локально; перед удалением нужного raw
  evidence его следует архивировать отдельно, записав место хранения и hashes.
- SHA-256 подтверждает содержимое имеющегося файла, но не восстанавливает
  утраченный файл. Компактные сводки позволяют проверить опубликованные выводы;
  полный независимый replay требует исходных артефактов и соответствующего SDK.
- Старые hash manifests фиксируют конкретный checkpoint. Позднейшие изменения
  статусов/индексов оформляются новым checkpoint; исторические manifests и
  результаты замеров не переписываются ради совпадения с текущим README.

## Точка входа

[E0/task-00](epoch-0-stage-measurement/task-00-current-version-log-measurement-low.md).
Кода не требует: коллеги снимают первые цифры на текущей опубликованной версии.
Параллельно можно брать [E0/task-01](epoch-0-stage-measurement/task-01-load-stage-timings-med.md).

## Связь с другими темами

- Предложенный в workspace-state-cache E1/task-00.4 замер replay входит в
  E0/task-01 и E0/task-02 этой темы. Evidence task-00.4 используется как
  контекст и не переписывается.
- Baseline E0/task-03 — вход для решений по C и B.

## Учёт

Ведётся по [общему стандарту](../README.md#учёт-задач-ревью-и-статистики).

- Ревью плана темы: модели —, даты —, раунды —, исправления —; итог: не проверено.
- Ревью E0/task-07.2, 2026-10-10: один исполнитель и два новых независимых рецензента Codex (GPT-6); два раунда, четыре must-fix, один фикс-раунд; финальный ACCEPT standalone реализации/evidence no-go с явными ограничениями корректности/детерминизма и причинного timing. Code/tests acceptance pending main CI; [validation](epoch-0-stage-measurement/evidence/task-07.2-refusal-fast-planner-roslyn/validation.md).
- Ревью задач: task-04 — Codex (GPT-6), один раунд проверки исполнителем, независимого ревью нет; [отчёт](epoch-0-stage-measurement/evidence/task-04-parallel-loader-spike.md).
- Приёмка реализации: E0/task-04, 2026-10-10 — standalone spike и 44 замера реализованы, no-go; code/tests acceptance pending единого зелёного main CI из-за SDK-specific теста существующей рабочей копии. Production-оптимизации и E1 не приняты и не активированы; [validation](epoch-0-stage-measurement/evidence/task-04-parallel-loader-spike.md#validation-и-проверка-исполнителем).
- Ревью и приёмка E0/task-05/task-05.1, 2026-10-10: исходный blocker task-05 снят повтором; профиль и две направленные калибровки завершены с no-go (модель непригодна), диагностические планы не проходят gate, шаг 2 не выполнялся. Task-05 — проверка Codex (GPT-6) и ревью Claude Opus 5.5; task-05.1 — один раунд проверки исполнителем Codex (GPT-6), независимого ревью не было. Scope — standalone spike, профили, калибровка/диагностическая симуляция и evidence; code/tests acceptance pending единого зелёного main CI (1362 passed / 1 прежний SDK-specific failed), production и E1 не активированы; [validation](epoch-0-stage-measurement/evidence/task-05.1-profile-retry.md#эквивалентность-и-validation).
- Ревью E0/task-07.1, 2026-10-10: один исполнитель и два независимых новых рецензента Codex (GPT-6); два раунда, одно must-fix и один фикс-раунд; финальный ACCEPT для standalone реализации/evidence отрицательного эксперимента. Формальная приёмка code/tests pending main CI; [validation](epoch-0-stage-measurement/evidence/task-07.1-core-first-equivalence-and-partition/validation.md).
