# Windows review 1.5.23 и исправление 1.5.24

Дата: **2026-10-30**, Europe/Moscow. Ветка: `usage-statistics`, commit `056d46a`.
Scope: реализация O-01…O-05 из [disk-watcher-inotify](README.md), diff от `7e5e71c`.
Статус: **F-WIN-001 fixed в 1.5.24, подтверждён реальной junction-регрессией и Windows
build/main/lifecycle 2026-10-30**. Исходное наблюдение относится к 1.5.23;
[исправление и проверки](#исправление-1524) приведены ниже. Приёмка всего пакета не заявляется.
[Машиночитаемое подтверждение](windows-validation.json).

Оптимизация исключает point watcher под точными корнями NuGet/dotnet и удаляет подписки,
которые покрыты реально запущенным рекурсивным watcher. В обычном дереве каталогов
проверены сохранение доставки из `obj/`, fallback при отказе родителя, диагностика
и совместимость новых полей статистики со старыми JSONL. Масштаб: несколько MCP
на пользователя; Linux-проверка двух копий сохранена в отчёте реализации, здесь — Windows.

## F-WIN-001 — junction ошибочно считается покрытым родительским watcher

Severity: **P2 / must fix**. Код: [WorkspaceDiskWatcherStarter.cs:51](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs#L51),
[IsCovered:73](../../Services/Workspace/WorkspaceDiskWatcherStarter.cs#L73).

**Сценарий.** Есть проект `parent/Root.csproj` и вложенный
`parent/linked-project/Child.csproj`, где `linked-project` — Windows junction на каталог
за пределами `parent`. Карта создаёт рекурсивные дескрипторы обоих проектов;
`InputPathCanon` сохраняет путь через junction, а не раскрывает алиас.

**Ошибка.** `IsCovered` проверяет только лексическую вложенность пути. После запуска
watcher на `parent` подписка вложенного проекта пропускается. Но Windows
`FileSystemWatcher` на родителя не следит за содержимым junction: изменения исходников
в дочернем проекте не доставляются. Отказов и error callback нет; диагностика
показывает штатный watcher, а поиск может оставаться на старом снимке до reset.

**Подтверждение выполнением.** Контроль: два настоящих `FileSystemWatcher`, на родителя
и на сам junction; создание файла в целевом каталоге даёт **0** событий родителю и **1**
дочернему watcher. Затем вызван настоящий `WorkspaceDiskWatcherStarter.Start` из
собранной **1.5.23.0** с двумя рекурсивными дескрипторами: **started=1, failures=0,
callbacks_for_linked_project=0, errors=0**, включая повтор с ожиданием 8 с, как в штатных
тестах доставки. Это регрессия объединения подписок: раньше дескрипторы разных каталогов
запускались отдельно.

**Минимальное исправление.** Не считать путь покрытым, если участок между родительским
watcher и каталогом дескриптора проходит через junction/reparse point. При невозможности
доказать покрытие сохранять отдельный watcher. Не менять глобальную каноникализацию
алиасов; добавить реальную регрессию доставки для этого случая. Нужна проверка и
промежуточного junction, а не только атрибутов конечного каталога.

**Если оставить.** Экономия watcher достигается за счёт молчаливой потери изменений
в поддерживаемом дереве проектов. Существующие тесты обычной вложенности это не ловят.

## Автоматизированные проверки

Windows, SDK **10.0.402**, PowerShell **7.6.5**, финальный код 1.5.23:

- Release solution build — **PASS**, остаются предупреждения nullable и xUnit.
- Main `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` — **1506/1506 PASS**,
  без skips; весь SourceStructure включён, known limits не менялись.
- `ROSLYN_MCP_ANALYZER_LIFECYCLE=1`, `Category=AnalyzerLifecycle` — **13/13 PASS**,
  без skips, счётчики TRX проверены.
- `Test-McpUsage.ps1` — **50 сценариев / 167 assertions PASS**.
- Изолированный self-contained win-x64 publish — **PASS**, FileVersion **1.5.23.0**.
- Published EXE + stdio + analyzer — **823 checks PASS**, 41 событие, 2 процесса,
  5 попыток load, 11 завершённых вызовов.

Первый main дал **1505/1506**: в
`UsageWorkspaceInstrumentationTests.Loaded_snapshot_access_and_explicit_load_keep_the_catalog_bit`
записался 1 ожидаемый tool event из 6. Класс отдельно — **5/5**, затем полный main —
**1506/1506**. Причина начального сбоя не установлена; результаты сохранены отдельно.
Это не подтверждение гарантированной потери событий в обычном использовании.

## Evidence и границы

Локальные ignored artifacts, без Markdown-ссылок на отсутствующие в GitHub файлы:

- `artifacts/watcher-review-1.5.23/`: ответы build/main/recheck/lifecycle/publish/PowerShell;
  `lifecycle/windows-1523-lifecycle.trx`.
- Junction witness: `artifacts/watcher-review-1.5.23/junction-witness.ps1`;
  результаты — `junction-b7241deddfe8480e9ce7f6c73068dac2/result.json`
  и `actual-starter-result.json` под тем же evidence root.
- Published EXE: `artifacts/usage-dogfood/server-1.5.23-20261030-windows-review/`.
- Protocol probe: `artifacts/usage-integration/7dfcf09dd0fe4612ae77e4e6fbcd9de4/`.

На этапе исходного review 1.5.23 production C# и тесты не менялись. Работающий MCP Codex по `get_mcp_server_info` остаётся
**1.5.21.0**; его конфиг и процесс не переключались. Linux suites и реальный Claude Code
1.5.23 выполнены другим агентом и сохранены в [реализации](README.md#реализация-1523);
Windows-junction witness их не заменяет. Linux symlink-вариант и macOS этим review не
проверялись. Cursor/dsh/OpenCode и длительный сбор остаются отдельными этапами.

**Исходный verdict 1.5.23:** исправить F-WIN-001 до приёмки; finding затем закрыт ниже.

## Исправление 1.5.24

Дата: **2026-10-30**. Статус: **fixed — F-WIN-001**.

Проверка покрытия проходит от каталога дескриптора к реально запущенному
рекурсивному watcher. Любой промежуточный `ReparsePoint` сохраняет отдельную подписку;
ошибка чтения метаданных также не считается доказанным покрытием. Сам корень
запущенного watcher не проверяется как промежуточная ссылка: он наблюдает собственное
поддерево, поэтому обычное объединение подписок внутри него сохраняется.
Каноникализация путей и классификация событий не менялись.

Регрессия `Descriptor_across_a_junction_keeps_its_watcher_and_delivers_changes`
создаёт настоящий NTFS junction командой `mklink /J`, проверяет два запущенных watcher
и доставку события из физического каталога за ссылкой. Три варианта: рекурсивный
watcher на junction, рекурсивный watcher ниже junction и точечный watcher ниже junction.
До исправления все **3/3 падали** (started=1 вместо 2), после — **3/3 PASS**.
Метод помечен `RequiresWindows` и внесён с причиной в точный Windows-only inventory;
это ограничение платформы самой fixture, а не скрытие Linux-падения.

Финальные проверки **1.5.24**, Windows SDK 10.0.402, Release:

- Solution build — **PASS**, новых предупреждений нет.
- Полный класс watcher — **8/8 PASS**; watcher + Windows-only inventory — **10/10 PASS**.
- Main `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` — **1509/1509 PASS**,
  без skips, включая весь SourceStructure; known limits не ослаблены.
- Opt-in `Category=AnalyzerLifecycle` — **13/13 PASS**, без skips по TRX.
- Self-contained win-x64 publish **1.5.24.0** и настоящий EXE + stdio + analyzer —
  **822 checks PASS**, 41 событие, 2 процесса, 5 попыток load, 11 завершённых вызовов.
  Сборка: `artifacts/usage-dogfood/server-1.5.24-20261030-junction-fix/`; protocol evidence:
  `artifacts/usage-integration/cc79370745ff44e8aa1cc04fa0c1f181/`.

Первый main после добавления регрессии дал **1508/1509**: точный Windows-only inventory
ожидал прежний список методов. Добавлена только новая junction-регрессия с причиной;
после этого целевые тесты и полный main пройдены. Неуспешный прогон сохранён.

Evidence исправления: `artifacts/junction-fix-1.5.24/` — red/green, build, целевые тесты,
первый и финальный main, source SHA256 и `lifecycle/junction-1524-lifecycle.trx`.
Linux 1.5.24, Linux symlink-вариант и macOS в этом исправлении не прогонялись;
ранее приведённые результаты Linux относятся к 1.5.23. Подключённый Codex MCP не переключался.

**Verdict по F-WIN-001: fixed, подтверждено регрессией и Windows suites.**
