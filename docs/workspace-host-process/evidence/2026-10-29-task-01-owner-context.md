# Task-01: выбор владельца O-05 — общий контекст и private fresh

## Главное

- **Решение владельца, 2026-10-29:** WorkspaceKey — абсолютный path и весь набор Configuration/Platform/TFM. Полный inherited environment не участвует в выборе workspace; host сохраняет контекст своего создателя.
- **Выбран вариант 1:** --force_fresh создаёт новый private host только для инициирующего proxy. Он не становится общим кандидатом и не заменяет host других клиентов. Это не автономный workspace внутри proxy.
- В load_workspace добавляется планируемый forceFresh для выполнимой агентом инструкции без изменения harness config. Новый private создаётся по явному intent; обычный повторный load сохраняет текущий private.
- **Последнее уточнение владельца:** дополнительный блок фактических параметров и инструкция нужны только при reused attachment. Создатель нового shared/private host не получает этот блок. Cache hit и авторство создания — разные признаки.
- Показанный actual context не доказывает равенство всех environment inputs. Это принятая цена общего контекста; CLI result и host build proof по-прежнему различаются. Namespace/image/plugin/protocol/analyzer/write admission не отменён.
- [Design §2](../design.md#2-совместимость-и-стартовый-контекст--u-c-01), task-01/task-02/task-06 и indices приводятся к этому решению. O-01…O-04 отложены, task-01 in progress, task-06/task-02 planned; production activation не разрешена.
- Раунды 8–9 перепроверили эту редакцию: два major R8-F01/R8-F02 исправлены и подтверждены; новых must-fix в проверенном slice нет. Итого task-01 **9 rounds/14 fixes**. Runtime preflight не запускался.

## Основание и изменение контракта

База — `9f1fe65`, production 1.5.18. Исполнитель GPT Sol-6.1 (`gpt-6.1-sol`), Codex.
Владелец предложил простой ключ и force_fresh, уточнил, что под TFM подразумевал
весь набор TFM/Platform/Configuration, а флаг должен создавать новый host. На выбор
между private и новым общим поколением ответил «пункт 1». Затем отдельно ограничил
вывод параметров/инструкции случаем подключения MCP к уже существующему host.

Это заменяет прежние рекомендации canonical-v1/strict, а не объявляет их
выполненными. Оригиналы reviews/response раундов 5–7 остаются историей. Ревью
раунда 7 не охватывало это новое решение и не является его технической приёмкой.

## Что получает агент

Для reused attachment load показывает фактические path/Configuration/Platform/TFM,
toolset/runtime, cwd/config origin, analyzer policy/host load settings, generation/
epoch и происхождение среды host. Omitted global property показывается как
«не передано MSBuild; project defaults», а не предполагаемый Debug или единый TFM.
Session-only buildArgs отделены от контекста host. Env values/credentials не выводятся.

Инструкция предлагает сверить контекст с ожидаемым и вызвать load_workspace с
нужными параметрами и forceFresh=true. Она не предлагает reset соседнего host.
Значения и признак created/reused относятся к подтверждённой текущей привязке,
а не вычисляются по cached flag. Cached/brief output не скрывают reuse block;
новый host собственного создания не добавляет этот блок.

## Границы и проверки

Текущий [load_workspace](../../../Tools/WorkspaceTools.cs#L35) ещё не принимает
forceFresh; production-код не меняется. [Global properties](../../../Services/Workspace/MsBuildWorkspaceProperties.cs#L6)
сейчас включают Configuration/Platform/TFM, defaults разрешаются в
[SolutionManager](../../../Services/Workspace/SolutionManager.cs#L893).
Startup читает config/cwd и plugins до первого tools call
([Program](../../../Program.cs#L56)); private host не отменяет image proof R6-N01.

Private endpoint не discoverable обычным proxy; owner-bound scope/capability
проверяется при attach. Его crash recovery не уходит в shared. Повторный explicit
fresh — новый instance; retries одного intent используют один instance id.
Failure/cancel до switch commit сохраняет прежнюю живую привязку. Общие outputs
остаются общими на диске и требуют прежних gates. Проверки включены в task-06
на лёгком стенде и в task-02 с настоящим Roslyn, включая оба направления output
created/reused, launch race, cached/brief, cleanup и отсутствие proxy workspace.

Применён research-task-authoring: решение отделено от результата измерения,
нулевой вариант сохранён, неизвестная выполнимость направлена task-06/task-02.
Полное ревью плана E0 не повторяется. C#/tests/build settings не меняются;
Build/main/SourceStructure/AnalyzerLifecycle не запускаются для этого docs-only diff.

## Замечания раунда 8

R8-F01 **ACCEPT, major**: private выбор мог теряться вместе с reset/unbind defaults.
В §2 добавлен отдельный owner-bound intent по WorkspaceKey до конца proxy session.
Reset/retire/cleanup host не удаляют его; explicit/config lazy load того же resolved
key использует private, не подменяя очищенные defaults. Failed fresh не фиксирует
новый intent. Сценарий 24 добавлен task-06/task-02.

R8-F02 **ACCEPT, major**: shared recovery не задавал контекст нового создателя.
Теперь победитель нового startup attempt определяет environment/cwd/settings нового
generation; это не восстановление старой среды ушедшего creator. Остальные proxy
обновляют receipt и проверяют explicit/sticky policy до исполнения. Created/reused
определяется заново; reuse block остаётся в explicit load, не в semantic response.
Сценарий 25 добавлен task-06/task-02. Раунд 9 подтвердил оба исправления; дополнительный
reuse block не появился в semantic response или у создателя нового host.

## Финальная validation и учёт

[Research-task-review Codex, раунды 8–9](../review-task-01-2026-10-29/result.md),
[coverage](../review-task-01-2026-10-29/coverage.md): R8-F01/R8-F02 закрыты, +2 fixes.
История прежних раундов сохранена; итог task-01 — 9 rounds/14 fixes, сумма задач E0 —
16 rounds/39 fixes. Это приёмка затронутого документационного контракта, не задачи,
эпохи или production activation. O-05 выбран владельцем; O-01…O-04 отложены.

После исправлений применён research-task-authoring. Links/anchors, уникальное
покрытие 54 tools/42 state IDs, согласованность семи task headers/index dependencies,
acyclic DAG и git diff --check — PASS. Исходные reviews раунда 5 не изменялись.
[Машинная сводка](../epoch-0-design-and-spike/evidence/task-01-data/validation-owner-context.json)
фиксирует базу, counts, статус и hashes. Task-01 in progress, task-06/task-02 planned.
Build/tests и runtime preflight не запускались: изменены только документы.
