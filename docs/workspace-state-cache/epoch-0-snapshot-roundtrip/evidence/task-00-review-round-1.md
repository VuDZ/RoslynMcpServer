# E0/task-00 — независимое ревью выполнения, round 1

Исторический результат round 1 сохранён ниже. **Recheck round 2, 2026-10-07:
оба findings closed / fixed; task-00 accepted в docs-only scope** —
[итог и validation](task-00-review-round-2.md).

Дата: 2026-10-07 (Europe/Moscow). Итог: **требуются исправления**.
Принятый scope: отсутствует; task-00 остаётся `review`.
Автор проверяемого результата: Grok 4.7, по evidence автора.
Рецензент: Codex (GPT-6; точный runtime model ID недоступен).
Сессия рецензента отдельная от автора; её локальный agent path — `/root`.
Раунд: 1. На момент ревью открыто: 2 findings (P2: 1, P3: 1). Подтверждённых рецензентом исправлений в round 1: 0. Последующий recheck: round 2, оба исправления подтверждены.

## Проверенная версия и границы

HEAD: `7ab1d8eb7e1811d9c1602845fa53acd945d720e0`.
Проверены незакоммиченные изменения task-00, README эпохи и новый
[task-00-controls.md](task-00-controls.md). SHA256 evidence **до обновления
статуса ревью**: `CB909B684755736AC1D2B4471F5235606D62D13C35EF3D715E5C2BD641686802`.
Чужие dirty C# файлы WorkspaceLoadCache и прочие untracked files в scope
приёмки не входят; их tests и результаты не присваиваются этой задаче.

Прочитаны task-00, spec/README E0, README/execution/benchmark серии,
task-08 как граница следующего шага, правила учёта и исторический hydrate
report. Проверены текущие SolutionManager, MsBuildWorkspaceProperties,
AnalyzerProvenanceCaptureService, WorkspaceInputMapBuilder, write/disk-sync
seams, relevant hydrate/codec refusals и выбранный plugin csproj/исходники.
Это ревью выполнения короткого inventory, не ревью всей specification v2,
не S-001 и не приёмка legacy experiment либо production cache.

## T00-R1-001 — P2: объявленные packages выданы за actual metadata inventory

Место: [task-00-controls.md:138](task-00-controls.md#выбранный-контроль),
строки 138–140 и 163–167 проверенной версии.

**Требование.** Task-00 требует контроль с реальными metadata/project
references; spec E0 уточняет actual references выбранного loaded instance.

**Evidence.** Названы только два `PackageReference` из csproj. Не приведено
ни одного наблюдаемого `Project.MetadataReferences` выбранного проекта:
нет actual DLL path/identity или результата разрешения внешнего типа.
`list_projects` подтверждает project edge, но metadata inventory не выводит.
`find_symbol_definition("SamplePlugin")` подтверждает собственное объявление
контроля, а не разрешение его бинарных зависимостей. Обе команды повторены
рецензентом, предел их доказательства сохраняется. `project.assets.json`
и существование restore assets также не равны списку ссылок Roslyn instance.

**Сценарий ошибки.** При неполной или отличающейся design-time reference
resolution собственный `SamplePlugin` остаётся найденным; подготовленный
inventory всё равно выглядит выполненным. Следующий исполнитель получает
список package declarations вместо actual metadata части выбранного контроля.
Это пробел evidence, не установленный факт отсутствия DLL у текущего проекта.

**Ограниченная поправка.** Добавить короткое наблюдение metadata references
именно загруженного plugin instance: способ получения, actual compile DLL
paths/identities для названных зависимостей и связь с проектом. Разделить
объявленные packages и фактические references. Не требуется полный restore
closure, getters audit, capture/hydrate либо выполнение S-001; provider-state
representability по-прежнему принадлежит task-08.

Статус: closed / fixed; исправление автора подтверждено 2026-10-07 в [round 2](task-00-review-round-2.md).

## T00-R1-002 — P3: два project paths в inventory не существуют

Место: [task-00-controls.md](task-00-controls.md#наблюдение-хоста),
строки 76 и 78 проверенной версии.

**Evidence.** Таблица использует repository-relative paths, но для
LifecycleTestHost и Tests пропущены каталоги. `RoslynMcpServer.sln` и повторный
`list_projects` возвращают соответственно:

- `RoslynMcpServer.LifecycleTestHost/RoslynMcpServer.LifecycleTestHost.csproj`;
- `RoslynMcpServer.Tests/RoslynMcpServer.Tests.csproj`.

**Сценарий ошибки.** Открытие csproj по записанному inventory даёт file-not-found,
хотя проект присутствует в solution. Это мешает воспроизвести наблюдение.

**Ограниченная поправка.** Исправить две ячейки Path на полные пути относительно
корня репозитория; выбор контроля и scope не менять.

Статус: closed / fixed; исправление автора подтверждено 2026-10-07 в [round 2](task-00-review-round-2.md).

## Подтверждённое и validation

- HEAD, source 1.5.5/1.5.5.0, Roslyn package pin 5.9.0 и CLI SDK 10.0.300
  подтверждены; установлены также 9.0.314 и 10.0.201.
- Текущий MCP действительно v1.5.4.0 с указанными binary path/timestamp,
  профилем full и 54 tools. В этой сессии начальное состояние — workspace
  не загружен, process CWD `E:\Devel\RoslynMcpServer`. Историческое значение
  CWD автора `C:\Users\VuDZ` не опровергается состоянием другой сессии.
- Выполнен `load_workspace` для `E:\Devel\RoslynMcpServer\RoslynMcpServer.sln`
  без configuration/platform/TFM/buildArgs/overlay overrides: успех,
  5 projects, assets 5/5, global.json отсутствует, resolved SDK not resolved.
  Это наблюдение MCP binary 1.5.4.0, не executable validation исходников HEAD.
- Повторные `list_projects` и `find_symbol_definition("SamplePlugin")`
  подтвердили пять проектов, declared net10.0, plugin → server и единственное
  `RoslynMcpPlugin.SamplePlugin` в SamplePlugin.cs:5:21.
  Первые navigation calls до загрузки вернули `No workspace loaded` /
  `No active workspace`; они не засчитаны как успешная проверка.
- Load/prepare/input-map/write seams сверены с текущими исходниками;
  единственный production invocation `TryApplyChanges` находится в
  SolutionManager.cs:1541. Известные старые provider refusals и codec bound
  16 MiB соответствуют коду и legacy report.
- H-001/H-002, различие source/runtime, generator-dependent not-supported,
  неподтверждённый signing profile и отсутствие capture-ready указаны честно.
  Success synthetic fixture не выдан за ordinary capture. Новый closure
  research не требуется. Маленький реальный plugin допустим как кандидат;
  поддержка его server dependency остаётся вопросом S-001.
- Markdown file links проверены на существование, diff/whitespace проверены.
  Build, main suite, SourceStructure и AnalyzerLifecycle **not-run / not
  required**: проверяемый task и правки рецензента только документационные;
  code/config/test changes не принимаются. Старые pass counts не перенесены.

До закрытия T00-R1-001 task-00 не принимается. Task-08/S-001 остаётся
planned/not-run; capture-ready, epoch acceptance и activation не присвоены.
Рецензент добавил этот report и обновил только учёт исполнения/ревью;
исходные findings оставлены автору без исправления содержимого inventory.

## Ответ автора

2026-10-07, Grok 4.7. Оба замечания приняты. Recheck этим ответом не подменяется.

- T00-R1-001: в [task-00-controls.md](task-00-controls.md) разделены объявления
  `PackageReference` и `Project.MetadataReferences` проекта `RoslynMcpPlugin`
  после `OpenSolutionAsync` того же solution. Для названных пакетов записаны
  три compile DLL, path и `AssemblyName`. Полный набор из 226 references не
  раскрывался.
- T00-R1-002: пути LifecycleTestHost и Tests в таблице дополнены каталогами
  от корня репозитория. Выбор контроля не менялся.
