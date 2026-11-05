# E1/task-06 — независимое ревью, раунд 1

- Дата: **2026-11-05**.
- База: `422e90b`; проверено рабочее дерево перед приёмкой task-06.
- Рецензент: Codex, отдельная независимая сессия; точный runtime model ID средой не предоставлен (`unknown`).
- Вердикт: **ACCEPT**.
- MUST-FIX: **нет**. Применён R-18: возврат только при молчаливой потере тестов или неверном результате в реальном сценарии.

## Scope

Прочитаны целиком [task-06](../task-06-adapter-test-names-probe-low.md),
[README эпохи](../README.md), `AGENTS.md`, `docs/code-style.md`, инструкции
`epoch-subagent-loop` и Ponytail. Проверены R-16/R-18 и текущий раздел
[«Ответ инструмента» программы](../../README.md#ответ-инструмента), включая его
единственную tracked-правку относительно базы: правила adapter/runtime-имён и
уточнение размерного бюджета.

Новые файлы прочитаны непосредственно, а не только через `git diff`:

- Все 17 C# исходников, четыре `.csproj`, README и `Collect-Evidence.ps1` в
  [spikes/AdapterTestNames](../../../../spikes/AdapterTestNames/README.md).
- [Отчёт измерений](task-06-adapter-names.md), все 17 data-файлов в
  [каталоге evidence](task-06-adapter-names/catalog.json),
  [отчёт реализации](../task-06-implementation-report.md) и
  [validation координатора](task-06-final-validation.md).
- Существующие исключения `spikes` в `RoslynMcpServer.csproj` и `SourceSetScope`,
  отсутствие fixture-проектов в server solution/main test project, CI-фильтр.

Чужой untracked `.agents/skills/prepare-github-release/` исключён из scope.
Статусы задачи и индекса ожидают решения координатора и не являются находкой.
Рецензент изменил только этот отчёт; production-код, данные, требования и статусы
не редактировал, commit не делал.

## Проверки и результат

**Фикстуры.** Каждый адаптер имеет ровно пять cases: nested, поддерживаемая
generic/закрытая форма, inherited и две параметризованные строки. Унаследованный
метод действительно объявлен в базе, а assertion `GetType()` проверяет конкретный
`InheritedTests`. Открытые абстрактные generic-базы xUnit/MSTest не объявлены
самостоятельно исполняемыми тестами. NUnit измеряет `[TestFixture(typeof(int))]`.
Public nested fixtures нужны непосредственно для измерения. Server code, version,
dependencies и build settings не изменены; существующие исключения `spikes`
сохраняют изоляцию от main suite.

**Настоящий FQN.** Отдельная офлайн-проверка прочитала сохранённые
`*.raw-discovery.txt`, `catalog.json`, `*.list-tests.txt`, `*.filters.txt` и
`*.trx-excerpts.txt`, без `artifacts`, `bin/obj` и живого `dotnet`.
Все четыре `TestDiscovery.Completed` имеют `IsAborted=false`, `TotalTests=5`,
пять уникальных Id и пустые списки неполностью обнаруженных/пропущенных sources.
Все 20 записей каталога точно совпадают с исходными `Id`, `FullyQualifiedName`,
`DisplayName`, `ExecutorUri`. List-tests отдельно совпадает с DisplayName.
Значения FQN не выводятся из display names или TRX `className/name`.

**Выбор фильтром.** Для каждого из 28 фильтров независимо сопоставлены сохранённые
TRX counters/results и каталог. Множество execution `testId` точно равно множеству
discovery Id, чьи actual FQN содержат значение `FullyQualifiedName~` без учёта
регистра. Counters `total/executed/passed` совпадают с размером этого множества;
failed/error/timeout/aborted/notExecuted и остальные проверенные отрицательные
счётчики равны нулю. Результат: **16 ненулевых выборок, 20 passed cases, 12 ожидаемых
zero-match**. Нулевые выборки не засчитаны как passes. Для parameters каждый
адаптер сохраняет и исполняет обе разные row identities.

**VSTest и происхождение.** У xUnit v3 сохранены executor
`executor://xunit/VsTestRunner3/netcore/`, discovered extension
`Xunit.Runner.VisualStudio.VsTestRunner` версии `3.1.5.0` и stdout VSTest adapter.
Закреплённые `xunit.v3.mtp-off`/`xunit.v3.core.mtp-off=3.2.2` присутствуют в
resolved inventory, `Microsoft.Testing.Platform*` отсутствует. Package references
каталога совпадают с четырьмя `.csproj`.

Дополнительно проверены локальные оригиналы: SHA256 всех четырёх diagnostics и
28 TRX совпадают с сохранёнными; startup headers, полные serialized discovery
messages и отобранные XML-строки сохранены без изменений. Все четыре resolved
package inventories совпали с `project.assets.json`. Первый вспомогательный
inventory-check остановился на разном порядке сортировки NUnit в Python и
PowerShell; повторное сравнение множеств подтвердило полное совпадение состава.
Эта дополнительная проверка оригиналов отделена от успешного офлайн-replay.
Пакет replay содержит ровно **17 файлов, 143284 bytes**.

**Норма для task-07.** Наблюдения прямо поддерживают `Outer+Inner`, конкретный
`InheritedTests`, `ClosedGenericTests.Generic` у xUnit/MSTest,
`GenericTests<Int32>.Generic` у измеренного NUnit и method prefix без аргументов
для параметризованных строк. Source-формы с точкой вместо `+`, открытым `<T>` или
declaring base дали ноль у всех четырёх адаптеров. Поэтому точечная правка
программы необходима и обоснована. Fallback на проект целиком с явной причиной,
когда конкретная generic/inherited identity неизвестна, предотвращает выбор по
предположенному имени. Универсальный formatter и task-07 production-код здесь
не реализуются.

**Финальная validation.** Прочитан отчёт координатора: после freeze C#/csproj
четыре независимых fixture-прогона дали по **5/5**, Release solution build прошёл
с тремя указанными прежними warnings, Windows main CI suite
`Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk` — **1874/1874**.
Это результаты координатора, не новый запуск рецензента. Оснований запрашивать
повторный build/test не выявлено. R-16 явно исключает эту эмпирическую пробу из RED.

## Находки и ограничения

Новых MUST-FIX нет; открытых finding IDs нет.

- **T06-L01 — границы измерения.** Выводы относятся к закреплённым версиям четырёх
  адаптеров, Windows и пяти случаям каждого проекта. Иные версии, Linux, MTP,
  nested+generic, generic-методы, custom names и несколько derived-классов не
  измерялись. Эти ограничения уже указаны в implementation/evidence; непроверенное
  runtime-имя не следует считать известным при task-07.
- **T06-L02 — повторный сбор.** Collector зависит от фиксированного snapshot и
  локальных diagnostics/TRX/assets; stdout сохраняется отдельно. Для нового
  измерения нужны эти входы и обновлённая metadata. Это не зависимость replay:
  actual FQN, identities, фильтры и результаты доступны в сохраняемых 17 файлах.
- **T06-L03 — оригиналы после checkout.** Полные diagnostics/TRX игнорируются;
  их SHA256 и ограниченные оригинальные выдержки остаются в evidence.
  Проверка полных файлов требует их наличия, но unit-тестам task-07 они не нужны.

Build/test повторно не запускались; AnalyzerLifecycle и публичная активация вне
scope. Приёмка этого probe не означает завершение task-07 или всей эпохи.
