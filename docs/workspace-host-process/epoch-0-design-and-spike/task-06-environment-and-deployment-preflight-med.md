# C/E0/task-06 — Preflight среды и развёртывания

- Рекомендуемые модели: GPT-6.1 Sol или GPT-6 Astra.
- Рекомендуемый reasoning: GPT: high.
- Примерная сложность: 3/5; класс `med`.
- Риск ошибки: средний — несопоставимая пара процессов или неполный manifest даст ложную применимость shared-режима.
- Необходимые способности: `проектирование_проверок`, `анализ_требований`, `анализ_производительности`.
- Статус: in progress, 2026-10-29; Linux/Claude Code выполнен, пара Codex/Windows ждёт данных владельца; объём сокращён владельцем — пункты 2, 3 (кроме MSBuild-контрпримера), 5 и сценарии 24–25 перенесены в task-02.
- Depends on: [task-04](task-04-proxy-routing-and-recovery-spike-hi.md), [task-05](task-05-applicability-checks-med.md).
- Модель, реализовавшая задачу: `claude-opus-5-5` (Claude Code).
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [отчёт](evidence/task-06-environment-and-deployment-preflight.md).

## Цель

До окончательного O-01 проверить выбранный владельцем O-05: небольшой WorkspaceKey,
общий контекст создателя host, отдельный private fresh и reuse-only output load.
Вопросы: схлопываются ли реальные пары при разных session environments; получает ли
агент фактические параметры только при reuse и может ли создать отдельный host;
допустимы ли storage/startup identity; сколько стоит hash и можно ли reuse launcher.

## Входы и границы

[Draft design](../design.md), [ревью раунда 5 и измерения](../review-task-01-round-5-2026-10-29/result.md),
[task-05](evidence/task-05-applicability.md), [task-04](evidence/task-04-routing-and-recovery-spike.md).
Результат потребляет незавершённый [task-01](task-01-architecture-decision-hi.md).
Завершение task-01 не является зависимостью этого preflight: используются его
выбранный O-05 и остальные proposed contracts, без включения в production.
Перечитать [AGENTS.md](../../../AGENTS.md); до C# правки — весь
[code style](../../code-style.md). Переиспользовать лёгкий стенд spikes/PipeRelay;
не начинать production host/Roslyn integration и не менять глобальный MCP config.
Внешний Linux launcher не редактировать: [L-01](../README.md#l-01-launcher) имеет отдельного владельца.

## Что сделать

**Сокращение объёма, решение владельца 2026-10-29:** пункты 2, 3 (кроме
MSBuild-контрпримера), 5 и дополнительные сценарии 24–25 — прототипы механизмов;
они перенесены в [task-02](task-02-bridge-spike-hi.md) и выполняются там один раз
на настоящем Roslyn. Здесь остаются пункты 1, 4 (факты среды), 6, 7 и контрпример.

1. **Go/no-go reuse.** На Codex/Windows и Claude Code/Linux получить по два настоящих MCP одного checkout с одинаковыми path/Configuration/Platform/TFM и namespace/image admission. Разные session environments не должны дробить WorkspaceKey. Проверить negative controls разных checkout и каждого load property; не выдавать их за совместимую пару.
2. На лёгком стенде воспроизвести created/reused receipt, cache hit и launch race. Создатель нового shared/private host не получает дополнительный reuse block. Присоединившийся proxy получает actual path/global properties/toolset/cwd/config origin/host policy и инструкцию forceFresh также при cached/brief load. Не выдумывать значения отсутствующих global properties и не сохранять env values/credentials; environment parity помечена unverified.
3. **Go/no-go private fresh.** Проверить startup --force_fresh и доступный агенту load_workspace(forceFresh=true): новый host с тем же WorkspaceKey, приватный scope только для инициатора, прежний shared и его клиенты сохраняются. Repeat load не создаёт новый host; новый explicit fresh создаёт. Проверить failure/cancel до switch commit, private owner admission, recovery без shared fallback и EOF cleanup. На MSBuild fixture показать контрпример environment-зависимого graph и отличие inherited shared context от fresh; не объявлять logging универсальным detector. Это изолированный стенд без production Roslyn integration; task-02 повторяет на настоящем workspace.
4. **Go/no-go storage.** Проверить owner/permissions/symlinks двух roots и actual filesystem. Rendezvous tmpfs/noexec допускает только малые runtime данные; image/logs должны уйти в StorageRoot. Storage tmpfs/ramfs/noexec отклоняется; insufficient space и ENOSPC в copy не запускают partial image. Ограничение socket path 103 UTF-8 байта сохраняется. Проверка свободного места не считается резервированием.
5. **Go/no-go startup identity.** Доказать непрерывную immutable publication/use lease с момента до OS CreateProcess, включая начальную загрузку exe/server assemblies; managed lock после запуска недостаточен. Проверить обновление source между запуском/bootstrap и первым shared обращением: verified deployment запрещает изменение, raw mutable worker без доказательства отвечает deployment-unverified и требует restart через verified image. Новый disk hash не должен описывать старый исполняемый код. Windows controlled-stop publish пока не считается доказанной prelaunch защитой.
6. Для выбранного file set отдельно измерить guarded startup identity hash, copy+hash+verify, cached process identity и audited immutable-manifest reuse. Startup должен уложиться в initialize budget harness; time-to-first-useful отсчитывается от запуска процесса. Записать bytes/files, runtime/GC, источник, cold/warm и cache state; по возможности n≥3 на доступный вариант, иначе явно n и ограничение. Повторный attach не должен скрыто читать весь bundle. Stat stamps могут инвалидировать кеш, но не доказывать bytes identity; проверить изменённые bytes при сохранённых size/mtime.
7. Read-only audit launcher: исходник/manifest/состав snapshot и механизм use lease. Совместное использование допустимо лишь при полном file set, immutable publication, одинаковом lease/cleanup протоколе и сохранённом config origin. Проверить prelaunch lease из пункта 5, жизнь host после ухода launcher и запрет удаления активного image. При недостатке evidence reuse не принимается. Зафиксировать L-01 и owner, не удалять RID/native/BuildHost зависимости по широкой маске.

Отрицательный результат совместимой пары, storage или identity — **no-go до выбора
O-01**, а не повод запускать N host молча. Недоступный обязательный harness означает
unverified; при ограниченном scope решение принимает владелец явно. Нельзя зачесть
synthetic пары, одни CODEX presence probes или чужой несовпадающий checkout.

Дополнительно на том же лёгком стенде проверить сценарии 24–25 design: tool-only
private intent переживает reset/unbind/retire/cleanup отдельно от defaults и binding;
explicit/config lazy load того же resolved key не уходит в shared. При уходе старого
shared creator и последующем crash новый startup winner задаёт контекст своего
generation; до исполнения валидируются policy требования и created/reused receipt.
Reuse block остаётся только в explicit load reused клиента, не в semantic response.

## Конкретный результат

`evidence/task-06-environment-and-deployment-preflight.md`: «Главное» до 10 пунктов,
go/no-go по каждой паре и storage/reuse; base commit/server и версии harness;
метод/команды/n/cache/GC; names-only data и тайминги; цена принятия общего контекста;
found defects с owner/link; неизвестные с получателем task-01/task-02/E1.
Отдельно выполнимость выбранного O-05 и условия выбора O-01; выбранное решение
не объявлять технически реализованным лишь по наличию текста.

## Проверки

Read-only наблюдения не имеют red/green; нужны воспроизводимые шаги. Если добавляется
поведенческий standalone fixture, сначала red нужного контракта, затем green.
При C#/test/build изменениях — Release solution build и полный main filter
`Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` (Linux также
`&Category!=RequiresWindows`), SourceStructure после production C# и AnalyzerLifecycle
при затронутых lifecycle paths по AGENTS.md. Docs-only — links/anchors и diff checks.
В конце обновить статус задачи, epoch/topic/docs index; незавершённую платформу
не объявлять pass или принятым shared rollout.
