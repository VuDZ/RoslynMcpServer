# C/E0/task-06 — Preflight среды и развёртывания

- Рекомендуемые модели: GPT-6.1 Sol или GPT-6 Astra.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — несопоставимая пара процессов или неполный manifest даст ложную применимость shared-режима.
- Необходимые способности: `проектирование_проверок`, `анализ_требований`, `анализ_производительности`.
- Статус: planned, 2026-10-29; preflight до окончательного выбора модели, выполнение не начато.
- Depends on: [task-04](task-04-proxy-routing-and-recovery-spike-hi.md), [task-05](task-05-applicability-checks-med.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.

## Цель

До окончательного O-01/O-05 проверить, способен ли proposed descriptor схлопнуть
настоящие совместимые процессы и допустимо ли выбранное размещение image.
Вопросы: совпадёт ли canonical key у пары одного checkout; где остаётся отличие от
ordinary environment; на чём лежит storage; сколько стоит первая/cached identity;
можно ли доверенно использовать существующий launcher snapshot без второй копии.

## Входы и границы

[Draft design](../design.md), [ревью раунда 5 и измерения](../review-task-01-round-5-2026-10-29/result.md),
[task-05](evidence/task-05-applicability.md), [task-04](evidence/task-04-routing-and-recovery-spike.md).
Результат потребляет незавершённый [task-01](task-01-architecture-decision-hi.md).
Завершение task-01 не является зависимостью этого preflight: используются его
proposed profiles, без их принятия или включения в production.
Перечитать [AGENTS.md](../../../AGENTS.md); до C# правки — весь
[code style](../../code-style.md). Переиспользовать лёгкий стенд spikes/PipeRelay;
не начинать production host/Roslyn integration и не менять глобальный MCP config.
Внешний Linux launcher не редактировать: [L-01](../README.md#l-01-launcher) имеет отдельного владельца.

## Что сделать

1. **Go/no-go среды.** На Codex/Windows и Claude Code/Linux получить по два настоящих MCP одного checkout с одинаковыми workspace, cwd, image/toolset и эффективными load settings. Пару разных checkout оставить отдельным negative control; не выдавать её за совместимую.
2. В памяти сопоставить raw и canonical-v1 descriptor (§2 design), включая policy id. Сохранить полный состав полей key и names-only причины отличий, версии/условия, без env values или per-secret fingerprints. Отличия session/terminal variables должны исчезнуть из execution environment host, а не только hash. Semantic/credential inputs и различия checkout должны остаться различиями.
3. На изолированных MSBuild fixtures проверить ordinary/canonical parity обычного проекта и намеренное отличие проекта, читающего исключённую переменную. Прямой Environment read custom task/child — отдельный контрпример для полноты logging. Не объявлять MSBuild log универсальным detector таких зависимостей. Canonical-v1 остаётся ценой O-05, а не автоматически безопасной очисткой среды.
4. **Go/no-go storage.** Проверить owner/permissions/symlinks двух roots и actual filesystem. Rendezvous tmpfs/noexec допускает только малые runtime данные; image/logs должны уйти в StorageRoot. Storage tmpfs/ramfs/noexec отклоняется; insufficient space и ENOSPC в copy не запускают partial image. Ограничение socket path 103 UTF-8 байта сохраняется. Проверка свободного места не считается резервированием.
5. **Go/no-go startup identity.** Доказать непрерывную immutable publication/use lease с момента до OS CreateProcess, включая начальную загрузку exe/server assemblies; managed lock после запуска недостаточен. Проверить обновление source между запуском/bootstrap и первым shared обращением: verified deployment запрещает изменение, raw mutable worker без доказательства отвечает deployment-unverified и требует restart через verified image. Новый disk hash не должен описывать старый исполняемый код. Windows controlled-stop publish пока не считается доказанной prelaunch защитой.
6. Для выбранного file set отдельно измерить guarded startup identity hash, copy+hash+verify, cached process identity и audited immutable-manifest reuse. Startup должен уложиться в initialize budget harness; time-to-first-useful отсчитывается от запуска процесса. Записать bytes/files, runtime/GC, источник, cold/warm и cache state; по возможности n≥3 на доступный вариант, иначе явно n и ограничение. Повторный attach не должен скрыто читать весь bundle. Stat stamps могут инвалидировать кеш, но не доказывать bytes identity; проверить изменённые bytes при сохранённых size/mtime.
7. Read-only audit launcher: исходник/manifest/состав snapshot и механизм use lease. Совместное использование допустимо лишь при полном file set, immutable publication, одинаковом lease/cleanup протоколе и сохранённом config origin. Проверить prelaunch lease из пункта 5, жизнь host после ухода launcher и запрет удаления активного image. При недостатке evidence reuse не принимается. Зафиксировать L-01 и owner, не удалять RID/native/BuildHost зависимости по широкой маске.

Отрицательный результат совместимой пары, storage или identity — **no-go до выбора
O-01**, а не повод запускать N host молча. Недоступный обязательный harness означает
unverified; при ограниченном scope решение принимает владелец явно. Нельзя зачесть
synthetic пары, одни CODEX presence probes или чужой несовпадающий checkout.

## Конкретный результат

`evidence/task-06-environment-and-deployment-preflight.md`: «Главное» до 10 пунктов,
go/no-go по каждой паре и storage/reuse; base commit/server и версии harness;
метод/команды/n/cache/GC; raw names-only data и тайминги; цена canonical parity;
found defects с owner/link; неизвестные с получателем task-01/task-02/E1.
Отдельно рекомендация O-05 и условия выбора O-01; выбор владельца за него не писать.

## Проверки

Read-only наблюдения не имеют red/green; нужны воспроизводимые шаги. Если добавляется
поведенческий standalone fixture, сначала red нужного контракта, затем green.
При C#/test/build изменениях — Release solution build и полный main filter
`Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` (Linux также
`&Category!=RequiresWindows`), SourceStructure после production C# и AnalyzerLifecycle
при затронутых lifecycle paths по AGENTS.md. Docs-only — links/anchors и diff checks.
В конце обновить статус задачи, epoch/topic/docs index; незавершённую платформу
не объявлять pass или принятым shared rollout.
