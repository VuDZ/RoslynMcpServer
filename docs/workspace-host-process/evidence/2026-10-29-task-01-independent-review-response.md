# Ответ на независимое ревью task-01, 2026-10-29

## Главное

- **ACCEPT по всем трём замечаниям Grok 4.7**: independent/F-01 blocker, independent/F-02 major, independent/F-03 minor. Возражений нет: каждый пробел подтверждён текущим кодом.
- Исправлен [design](../design.md): скрытые load в list/graph идут через proxy switch, project/package clear меняет общую epoch и запрещает прежние proofs, Git heuristic считается по исходному snapshot.
- Добавлены проверяемые последовательности 17–19 для task-02. Это исправление проекта контракта; production-код не менялся и runtime pass не заявлен.
- Повторный research-task-review подтвердил все три исправления; новых must-fix по этому diff нет. O-01…O-04 остаются отложенными владельцем, task-01 незавершена, task-02 не начинается.

## Основание и границы

Исходное [ревью](../review-task-01-independent-2026-10-29/result.md) и
[coverage](../review-task-01-independent-2026-10-29/coverage.md) прочитаны полностью.
База исправлений — `d54a93a`, production **1.5.18**. Автор ответа — Codex,
точный model ID недоступен. Это ответ и правка design по поручению владельца,
а не разрешение на реализацию или изменение уже принятого default-режима.

Оригинал review не редактируется. SHA-256 до исправлений и при финальной проверке
должны совпасть:

- `result.md`: `0BC8659DA6597551F2B6D49EAEF72EB43D5046190DD5A3887548806482757654`.
- `coverage.md`: `322EF135CF913DDF795A0DA947D6E406832CD05E0197FE5EDB71B6518EBABACB`.

ID исходного каталога сохранены с префиксом **independent/**, чтобы не смешать их
с F-01…F-03 предыдущего [ревью Codex](../review-task-01-2026-10-29/result.md).

## independent/F-01 — ACCEPT

Факт подтверждён: [ListProjects](../../../Tools/UtilityTools.cs#L1412) и
[GetProjectGraph](../../../Tools/UtilityTools.cs#L1477) при непустом workspacePath
вызывают manager.LoadAsync. [LoadAsync](../../../Services/Workspace/SolutionManager.cs#L242)
передаёт управление LoadAndPrepareAsync, а fresh open освобождает прежний graph.
Это нарушило бы закреплённый key host и изоляцию остальных клиентов; read-only
classification инструмента не означает отсутствия этих эффектов.

В design §4/§5 разделены path/defaults/attach и чистая read-часть. Чужой key
проходит тот же switch с загрузкой до commit и rollback привязки при отказе.
Для своего key остаётся coordinated cache hit/graph-stale refresh, с прежними
sticky правилами; host adapter больше не может скрыто вызвать LoadAsync чужого пути.
Omitted path у unbound list/graph не вводит новую lazy загрузку. Публичные имена,
параметры и формат list/graph не меняются. Сценарий 17 проверяет оба инструмента,
успех/отказ/cancel, одного оставшегося клиента A и same-key/omitted ветки.

Следствие для task-02: разрез проходит внутри двух UtilityTools методов,
а не только между названиями tools. Новая публичная API не требуется.

## independent/F-02 — ACCEPT

[ProjectTools](../../../Tools/ProjectTools.cs#L58) после package edits и применённого
rename вызывает ClearWorkspaceAsync; dry-run rename возвращается раньше.
[ClearWorkspaceAsync](../../../Services/Workspace/SolutionManager.cs#L768) обнуляет
manager state, но [SessionBuildState](../../../Services/Workspace/SolutionManager.cs#L66)
остаётся прежним объектом. Его [Observe](../../../Services/Build/SessionBuildState.cs#L145)
не является проверкой внешнего session/generation/epoch ticket.

В §7 добавлен общий mutation/clear протокол: admission/drain, новая WorkspaceEpoch
до возможной записи, invalidation всех session tickets/write contexts/proofs,
пустые per-session build states и notification всем proxy. Проверка epoch на каждом
RPC обязательна независимо от доставки notification. Descriptor и proxy defaults
сохраняются отдельно от очищенного manager; следующий load идёт single-flight.
CLR history при clear остаётся, HostGeneration тот же; общий reset/retire по O-02
остаётся отдельным действием. Успешный ответ сохраняет совет вызвать load_workspace.

Сценарий 18 проверяет двух клиентов, прежний ticket/proof state, завершившийся
proxy CLI, потерянное notification, один следующий load, dry-run и partial-write/
clear failure. Существующий запрет content proof без input hashes не ослабляется:
fixture не получает current proof только из CLI exit=0.
Если rename меняет loaded csproj path, новый путь требует другого key и switch;
reload старого пути не переадресует всех клиентов молча.

Следствия: внутренние epoch notifications и admission должны охватывать эти три
tools; их нельзя считать частным reset только вызывающей сессии. Старый CLI не
обрывается от clear, но не подтверждает новый graph. Abandoned-writer records и
OS output gates переживают invalidation по прежнему контракту.

## independent/F-03 — ACCEPT

[GetChangedFiles](../../../Tools/UtilityTools.cs#L161) вызывает
[SuggestTestProjects](../../../Services/GitChangedFilesHelper.cs#L93), который
обходит Solution.Projects/Documents. Передачи только git anchor недостаточно для
сохранения существующего успешного результата.

В §5 host закрепляет исходный snapshot/call reference, proxy исполняет git,
а host вычисляет heuristic по полученным путям и закреплённому graph. Proxy
форматирует список. Без graph остаётся пустой список без создания host; switch
во время git не подменяет A на B. Сценарий 19 сопоставляет ordinary/shared heuristic
и проверяет switch и потерю host. При потере исходного graph нельзя выдать
неизвестные suggestions за пустой список: возвращается явная ошибка полноты с
сохранённым Git result, без повторного запуска git.

Следствие: добавляется внутренний heuristic этап; публичная schema не меняется,
новый lazy load либо switch по явному git path не вводится.

## Validation и учёт

Правки оформлены по research-task-authoring. Сохранены нулевой контроль, отложенные
решения владельца, известные primitives и получатели будущих runtime проверок.
Новый самостоятельный production defect этим чтением не объявлен; исправляются
три архитектурных пробела, найденных перед интеграцией.

Перепроверку выполнил Codex `/root/task01_research_review`, точный model ID недоступен:
[раунд 4 и покрытие](../review-task-01-2026-10-29/result.md).
Подтверждены **3** исправления independent/F-01…F-03; новых must-fix нет.
Grok 4.7 повторно эту версию не проверял; его исходный вердикт не переписан.
Нумерация общих раундов task-01: 1–2 — прежний Codex; 3 — отдельный Grok по
опубликованному `d54a93a`; 4 — Codex по новому техническому diff исправлений.
Итого task-01: **4 раунда / 6 подтверждённых исправлений**, два независимых набора ID.
Три последних fixes подтверждены в документации, runtime регрессии остаются task-02.

До финального bookkeeping проверены **298** локальных ссылок/anchors в 12 Markdown,
broken=0, routing table **54/54**, S01–S42 **42/42**, `git diff --check` — PASS.
Окончательная проверка пакета после синхронизации индексов фиксируется ниже.
Build/main suite/SourceStructure/AnalyzerLifecycle не требуются:
изменяются только документы, C#/tests/build settings не меняются.
Исходные review result/coverage сохранены с прежними SHA-256; счётчик увеличен
только после подтверждения рецензентом.

Финальная validation после bookkeeping: **335** links/anchors в **12** Markdown,
broken=0; каталог **54/54** без пропусков/дубликатов, состояния **42/42**.
`git diff --check` — PASS; original review hashes совпали. Все изменения находятся
в `docs/`; [машинная сводка](../epoch-0-design-and-spike/evidence/task-01-data/validation-independent-review.json).
Исторические validation snapshots и исходный review не переписывались.
