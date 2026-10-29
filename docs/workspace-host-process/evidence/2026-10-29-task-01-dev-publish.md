# Task-01: выбор владельца O-03 — текущая схема dev publish

## Главное

- **O-03 выбран владельцем 2026-10-29:** публикация с остановкой MCP используется в разработке. Сохраняется существующий порядок остановки процессов и попытки обновления, без обеспечения непрерывной работы сессий.
- Уточнение «все копии» относится к MCP, запущенным из **текущего каталога разработки**, а не ко всем установкам компьютера. Это совпадает с RepoRoot/PublishDir фильтром текущего скрипта.
- По наблюдению владельца dsh перезапускает MCP; другие harness тоже могут удерживать процессы. При таком конфликте допустим отказ publish; владелец вручную остановит harness/MCP. Новый kill loop или launcher ради этого не требуются.
- Это docs-only фиксация решения на базе `4cdc1b7`, production 1.5.18. `publish-and-verify.ps1` не меняется, процессы не завершаются и publish не запускается этой задачей.
- O-03 и O-05 выбраны; O-01/O-02/O-04 остаются открытыми. Task-01 in progress, task-02/task-06 planned; код, epoch acceptance и production activation не объявлены принятыми.

Исполнитель — GPT Sol-6.1 (`gpt-6.1-sol`), Codex. Владелец явно исключил требование
сохранения живых сессий при dev publish и затем уточнил границу каталога разработки.

## Сверка с текущим скриптом

[Get-RoslynMcpLockHolders](../../../publish-and-verify.ps1#L48) получает процессы
RoslynMcpServer.exe и проверяет executable path внутри RepoRoot/PublishDir;
неизвестный path считается потенциальным holder. Это не отбор по process cwd.
[Stop-RoslynMcpLockHolders](../../../publish-and-verify.ps1#L82) завершает найденные
процессы, затем до 8 секунд проверяет выход и отказывает при оставшихся процессах.
Повторный probe не превращается в бесконечную борьбу с перезапускающим harness.

Выбор отражён в [design §9](../design.md#9-shadow-image-публикация-и-cleanup) и
[README](../README.md#o-03-dev-publish). Проверка immutable startup/image identity
task-06 остаётся отдельным техническим gate; текущее kill-before-publish не объявлено
доказанной prelaunch lease. Непроверенные startup гарантии не служат разрешением
автоматически добавлять hot-update deployment вопреки выбранному O-03.

Применён research-task-authoring: решение/наблюдение владельца отделены от source
facts и экспериментальных результатов. Это последующая запись после раунда 9,
нового независимого ревью нет; counters task-01 9/14 и задач E0 16/39 сохраняются.
Validation: 19 Markdown, 513 локальных links/anchors без ошибок; 54/54 уникальных
tools и 42/42 state IDs; dependency headers/index семи задач совпадают, DAG без циклов;
git diff --check — PASS. Исходные review catalogs и зависимости не изменены.
Build/tests и runtime не запускаются: C#/tests/build settings не меняются.
