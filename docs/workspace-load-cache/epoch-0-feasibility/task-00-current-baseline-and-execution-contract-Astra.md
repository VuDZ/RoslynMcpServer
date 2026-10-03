# E0/task-00 — current-baseline-and-execution-contract

- Рекомендуемая модель: **Astra**.
- Рекомендуемый reasoning: **high**.
- Примерная сложность: **4/5**.
- Риск ошибки: **высокий** — Ошибочная baseline delta направит последующие lifecycle задачи на устаревшую модель.
- Необходимые способности: `архитектурное_мышление`, `анализ_зависимостей`, `согласованность_компонентов`, `семантика_roslyn_msbuild`.
- Статус и маршрут: [карта эпохи](README.md).
- Depends on: нет.

## Цель

Сначала сверить текущий production baseline с историческим основанием spec; затем определить оставшуюся delta и план доказательств.

## Обязательные входы и границы правок

[Спецификация эпохи](spec.md) и [общие правила исполнения](../task-execution.md)
обязательны. Общие правила включают AGENTS/code style, владение общими файлами,
финальную validation и ограничения activation.

Обязательный отдельный вход — [current-baseline-audit.md](current-baseline-audit.md): начальный code inventory 1.5.4 с конкретной delta. Историческое основание spec — commit 9867318ddb5294ce144bf024b9a61a1a2e3814c3, source 1.3.21; production задачи проектируются по зафиксированному текущему HEAD/dirty tree, не по исторической структуре. Проверить RoslynMcpServer.csproj, SolutionManager, shared input models, analyzer publication, watcher/reconciliation и соответствующие tests. Running MCP версию установить отдельно. sdk-project-v1 spike в репозитории не найден: пересказ в spec не считать доступным harness/raw evidence.

## Конкретный результат

Первый артефакт — актуализированный current-baseline-audit.md: для A-LOAD/A-WRITE/A-STICKY/A-ADMISSION, watcher/shared input, additional/config published text, WPF wpftmp и BOM own-write отдельно already implemented / partial / missing, code/test evidence и remaining delta. Code presence и test pass не смешивать; сохранить исторический pin и отдельно текущий commit/source/dirty paths/running MCP. Указать, какие existing paths переиспользуются, какие меняются и почему.

Затем design.md с двумя перечнями semantic DTO и dependency evidence, capability matrix, independent ordinary/hydrate comparator, DTB counter и воспроизводимым планом sdk-project-v1 spike. Для E0/task-01/03/04/05 выпустить отдельные implementation packets по общим правилам; author task-02 обязан дополнить packet fixtures evidence findings до запуска task-03.

## Проверки и evidence

Проследить текущие load/prepare/flush/write call chains, preflight/inverse и publication accessor; сверить существующие graph/shared-source/non-CSharp/WPF/own-write tests. Подтвердить delta, включая dispose old до нового ordinary open и MSBuildWorkspace-specific поле host. V01–V06/V23 и новые regression assertions привязать к конкретным current paths. Если raw SDK spike недоступен, отметить not-run и поручить воспроизведение task-02.

IDs выше — минимум. Перед acceptance code/test изменений действует полная
coordinator validation из общих правил. К результату приложить diff, commands/
runner outcomes, evidence и limits; failed/not-run не засчитывать как positive.

## Недопустимые упрощения

Не превращать выбранный AdhocWorkspace в read-only host, не выбирать новые feature gates и не считать U-ARB-03 блокером технического spike.

Не менять нормативный contract через task-файл. Незаданные решения или конфликт
со spec вернуть координатору с конкретным evidence.

## Пакеты потребителей

До accepted заполнить exact types/files/signatures по [общим правилам](../task-execution.md).
Producer выдаёт ограниченный пакет каждому потребителю; незаполненный packet
не переносится как design-работа на Sol/Luna исполнителя.

- E0/task-01: `epoch-0-feasibility/design/implementation-packet-task-01.md`.
- E0/task-03: `epoch-0-feasibility/design/implementation-packet-task-03.md`.
- E0/task-04: `epoch-0-feasibility/design/implementation-packet-task-04.md`.
- E0/task-05: `epoch-0-feasibility/design/implementation-packet-task-05.md`.
