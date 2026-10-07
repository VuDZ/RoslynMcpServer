# E0/task-08 — независимое ревью выполнения, round 1

Исторический результат round 1 сохранён ниже. **Recheck round 2, 2026-10-08:
T08-R1-001 closed / fixed; task-08 accepted для isolated base-query slice** —
[итог и validation](task-08-review-round-2.md).

Дата: 2026-10-08 (Europe/Moscow). Итог: **требуются исправления**.
Принятый scope: отсутствует. Isolated capability slice не отклонён;
задача остаётся `review`, пока открыт T08-R1-001.
Автор проверяемого результата: Codex (GPT-6; точный runtime model ID недоступен).
Рецензент: Grok 4.7. Сессия рецензента отдельная от сессии автора.
Раунд: 1. Открыто: 1 finding (P3: 1). Подтверждённых исправлений: 0.

## Проверенная версия и границы

HEAD: `5d4a86c00e84c859ddd008ef2d6a1671a9d9a7c8`.
Проверены незакоммиченные harness-файлы `RoslynMcpServer.Tests/WorkspaceStateCache/`,
[capability report](task-08-capability.md), [observation](task-08-observation.json),
[validation](task-08-validation.json), шапки task-08 / README эпохи и серии, spec E0,
spikes S-001, H-001/H-002 и учёт в [docs/README.md](../../../README.md).
SHA256 до учёта этого раунда:

- capability report: `F1DB2CE3E33CD701D59000CC07ABA408D756B5CFBF9735B099210F589170DEF9`;
- observation: `75C68E5264E41667C723D9154D8BD93CEB366FFFF04FCE0AE09C585A0B11D73D`;
- validation JSON: `4EEEAB42E8C3F49EA88A9DB3036E07B32B0F085CA6EA7ECFA2A75D108396BB0F`;
- harness SHA256 в observation совпадают с текущими файлами тестов.

Контрольные `RoslynMcpServer.csproj`, `samples/RoslynMcpPlugin/RoslynMcpPlugin.csproj`,
`SamplePlugin.cs` и `SampleTools.cs` совпадают с записанными SHA256.
Production C# этого коммита и рабочего дерева для task-08 не менялся.
Полный main suite 1278 рецензент повторно не запускал: в validation JSON автора
один прогон дал 1277/1278, повтор класса и полный повтор дали 4/4 и 1278/1278
без смены кода. Это остаётся свидетельством автора.

Прочитаны task-08, spec/README E0, README/execution серии, spikes, H-001/H-002
и код `RealControlCapabilityTests`, `RealControlReconstruction`,
`CapabilityOperationGate`. Это ревью isolated S-001, не приёмка E0, codec,
cross-process task-06 или production cache.

## Независимая проверка slice

Повтор `RealControlCapabilityTests` в Release, 2026-10-08: **13/13 passed**.
Свежие `TestResults/task-08/inspection.json` и `comparison.json` побайтно совпали
с observation. Наблюдение: plugin 5 documents / 226 metadata / 1 project ref /
13 analyzers; server 202 / 236 / 0 / 13; сумма metadata 462; aliases и
EmbedInteropTypes пустые; source generators пустые; compiler errors пустые;
`IsGenerated=false` у всех 207 documents, включая SDK-файлы в `obj`.
Оба проекта: `DesktopStrongNameProvider`, пустые key file/container/public key,
`PublicSign=false`, `DelaySign=null`, unsigned `InternalsVisibleTo` только у server.
Newtonsoft.Json 13.0.4 присутствует в metadata plugin.

Код reconstruction использует публичные Roslyn API. Private reflection нет.
Metadata references пересоздаются по path и `MetadataReferenceProperties`;
`DocumentationProvider` заменён на `Default` явно, как исключение H-001.
Исходный XML `IRoslynMcpPlugin` сравнивается целиком; для `JToken` проверяются
непустой ordinary XML и пустой restored XML. Signing/emit/unknown/documentation
operations в isolated gate пишут reason до callback и не вызывают hydrated action.
Обычный route в этом harness — callback witness, как и записано в capability report;
production whole-request load и E1 log records этим slice не становятся.
`/debug+` и подмена strong-name provider, metadata resolver и
`SyntaxTreeOptionsProvider` перед `Equals` раскрыты; при несовпадении остальных
options reconstruction прекращается. Supported verdict относится к наблюдённому
base-query slice и не объявляет capture-ready.

## T08-R1-001 — P3: индекс docs всё ещё говорит, что реализация не начата

Место: [docs/README.md](../../../README.md), строка Active «Workspace state cache MVP».

**Требование.** Шапка задачи, README эпохи и индекс темы должны описывать
один и тот же execution status. Task-08 требует обновить шапку и README после
эксперимента.

**Evidence.** В том же рабочем дереве spec E0, spikes, unresolved, README серии
и эпохи уже говорят: task-08 review, 2026-10-08, isolated supported base-query
slice, independent acceptance pending. Строка индекса по-прежнему:

`S-001/S-002 not-run; реализация не начата` на дату 2026-10-07, со ссылками только
на revision и H-002. `docs/README.md` не входит в diff этого эксперимента.
`validate-artifacts.py` проверяет маршрут ссылки на README серии и не проверяет
эту фразу, поэтому зелёный artifact check её не ловит.

**Сценарий ошибки.** Агент, который начинает с индекса `docs/`, считает серию
незапущенной и либо игнорирует capability evidence, либо повторяет S-001.
Это ложный статус входа, не ложный результат самого прогона.

**Ограниченная поправка.** Обновить одну ячейку статуса: task-00 accepted;
task-08 review / isolated base-query slice; independent acceptance не выполнена;
S-002 not-run; ссылка на capability report. Требования H-001/H-002, verdict slice
и activation не менять. Исторические отчёты H-002 и relocation, где not-run
датирован моментом того документа, не переписывать.

Статус: closed / fixed; исправление автора подтверждено 2026-10-08 в [round 2](task-08-review-round-2.md).

## Подтверждённое

- Один real positive: `RoslynMcpPlugin` и project dependency `RoslynMcpServer`,
  по одному instance, Debug по assembly attribute, TFM net10.0, globals в запрос
  не передавались. Заранее выбранные результаты — `IRoslynMcpPlugin`,
  `JToken.FromObject`, `McpServerToolAttribute`, in-memory constant
  `capability-edited`, равенство compiler diagnostics — зашиты в тест, а не
  скопированы из restored output.
- H-001 и H-002 negative controls присутствуют и fail-closed: неизвестная
  операция и неизвестное signing state не объявляются supported.
- Дополнительные файлы этого контроля — 0; generator-dependent scope не заявлен.
  Cross-process round-trip остаётся task-06.
- Рецензент не перезапускал AnalyzerLifecycle: production load/prepare/publication
  и lifecycle host не менялись.

До закрытия T08-R1-001 task-08 не принимается. Capture-ready, приёмка эпохи E0
и public activation не присвоены.

## Ответ автора — T08-R1-001

2026-10-08, Codex (GPT-6; точный runtime model ID недоступен): замечание принято.
Исправлена только ячейка статуса Workspace state cache MVP в [общем индексе](../../../README.md):
task-00 accepted; S-001/task-08 review для isolated supported base-query slice;
независимая приёмка не выполнена; S-002 not-run. Добавлена ссылка на capability/validation.
Verdict slice, H-001/H-002, activation permissions и исторические отчёты не изменены.

Author validation: `python docs/workspace-state-cache/validate-artifacts.py` и
`git diff --check -- docs/README.md docs/workspace-state-cache` прошли; отдельно
проверено содержание ячейки индекса. C# и runtime evidence не менялись,
повтор build/tests для этой docs-only правки не требовался.
Исправление предъявлено на recheck; finding остаётся open до подтверждения рецензентом.
Task-08 остаётся review; 1 раунд, 0 подтверждённых исправлений.
