# E0/task-01 — независимое ревью выполнения, round 1

Дата: 2026-10-08 (Europe/Moscow). Итог: **must-fix**.
Task-01 остаётся **planned**. Этот файл статус задачи не меняет.
Рецензент: Grok 4.7. Сессия рецензента раннер и evidence не писала.
Исполнитель: Grok 4.7 (agent `02b91e41-fb75-4b87-9e94-cd59ff85ac41`).
Раунд: 1. Открыто findings: 1.

## Границы

Проверены task-01, spec E0, benchmark, execution, принятый контроль task-00
(раздел «Выбранный контроль»), раннер, README раннера, отчёт прогона и
`evidence/task-01/`. `docs/backlog/workspace-load-cache/baseline_bench.py`
в git diff пустой; HEAD `442ae060b85be0f4ab641274fe5c8c94d13642d7` совпадает
с commit в compare key. Production C# эта задача не меняла.

## T01-R1-001 — P2: compare key не различает три уровня load и excluded emit/signing

Место: `scripts/workspace-state-cache/ordinary_baseline.py`, `compare_material`,
строки 933–935 и 948; сохранённый ключ
[compare-key.json](task-01/compare-key.json), строка 47; таблица ключа в
[task-01-baseline.md](task-01-baseline.md), строка 78.

**Требование.** [benchmark.md](../../benchmark.md), revision compare context:
compare key различает raw requested/absence, independently merged pre-open
loader globals и evaluated instance values. Тот же ключ включает finite
supported profile и excluded emit/signing.

**Evidence.** В хеш ключа попадает одна тройка
`effectiveConfiguration` / `effectivePlatform` / `effectiveTargetFramework`
из строки health `MSBuild …: (SDK/workspace default)`.
`loadArgumentsOmitted` (configuration, platform, targetFramework, buildArgs,
shadowCopy) лежит на попытке и в `compare_material` не входит, поэтому на
`compareKeyId` не влияет. Evaluated instance values загруженного
`RoslynMcpPlugin` в ключе нет. Отчёт сам пишет, что объявленный TFM `net10.0`
этим ответом не печатается. `overlayMode` и `shadowCopyInSolutionAnalyzers`
зашиты константой, а не сняты как отдельный уровень.
`h002: strong-name-variant-B` и `strongNameEquivalence: false` не содержат
отдельного excluded emit/signing.

**Сценарий ошибки.** Следующее сравнение по этому ключу склеивает явный
Configuration и то же значение из FileSettings, если health-строка совпала,
и не отличает непереданный MSBuild TargetFramework от evaluated TFM инстанса.
Прогон с emit/signing внутри профиля может получить тот же ключ.

**Ограниченная поправка.** В объекте compare key три отдельных уровня:
raw requested с явным absence, loader globals до MSBuild open, evaluated
Configuration/Platform/TFM инстанса контроля. Health-строка сервера может
остаться наблюдением сервера. Excluded emit/signing — отдельное поле ключа.
Переписать `compare-key.json` и таблицу отчёта. Пять таймингов заново не
снимать, если добавляемые факты относятся к этому же запросу: raw absence
уже записан на попытках; evaluated values в тексте load нет, их нужно снять
отдельным наблюдением того же контроля и приложить.

## Не finding

Пять measured success после одного warmup, successes 5 / errors 0 / timeouts 0,
шесть разных PID, warmup вне медианы, median/min/max сходятся с raw.
Publish закончился до первой строки `run.log` (10:18:34 UTC); restore не
запускался; в load/semantic/useful эти длительности не входят.
Ожидаемое объявление во всех шести semantic-телах: `RoslynMcpPlugin.SamplePlugin`,
`SamplePlugin.cs`, строка 5, колонка 21. Заметка composition-stale есть во всех
шести телах и в отчёте; объявление на месте, промахом это не посчитано.
Пины архивного baseline не менялись. p95 не обещан.
Источник identity — 205 csproj/source файлов, без `obj`; это предел подписи
scope, не дыра в счётчиках этого прогона.
Source-comment parity у этого прогона отдельным assertion не покрыта: второго
снимка нет, отчёт не заявляет потерю комментариев.
