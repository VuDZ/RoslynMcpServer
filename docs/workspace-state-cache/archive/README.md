# Workspace state cache — постепенный MVP

> Архив исходной версии, 2026-10-07. Актуальные требования — [в корне темы](../README.md); [индекс истории](index.md).

Статус: **draft / ready-for-review, 2026-10-07**. Реализация не начата;
runtime, версия и defaults этим пакетом не меняются.

Владелец выбрал постепенное сохранение/восстановление workspace state с индексом
оффлайн-изменений и существующими watcher’ами. Прежняя большая программа
[workspace load cache v2](../../backlog/workspace-load-cache/README.md) отложена;
её experiments/findings используются по применимости, её O1–O8, пять verdicts,
closure gates и обязательные implementation packets не наследуются новым MVP.
Текущие production write/admission/lifecycle инварианты остаются обязательными.

## Цель и граница

Ускорить первый полезный semantic-запрос в новом PID за счёт восстановления
описания уже загруженного решения. Сохранять projects/options/documents/references
и индекс inputs. Исходники читаются с диска; Roslyn Solution/Compilation/SyntaxTree,
source/generated texts, session IDs, shadow paths и operation contexts не сериализуются.
Не использовать git/ast, `.vs` cache и private Roslyn storage для validation.

Поддержка наращивается по работающим контролам. Первый disk MVP допускает
whole-request fallback при изменении входов. Затем добавляются content-only
refresh и отдельно selective project reopen. Положительный результат нужен
на неизменённом реальном проекте/решении, не только на synthetic fixture.
Неподдержанные MSBuild inputs и ambiguous TFM честно идут на ordinary load;
универсальная полнота произвольного build graph не обещается.

## Маршрут

- [E0 — snapshot round-trip](epoch-0-snapshot-roundtrip/README.md): 8 задач;
  ordinary capture, минимальный DTO/codec, isolated hydrate, real query/edit,
  baseline и restore benchmark. Production path ещё не меняется.
- [E1 — disk cache MVP](epoch-1-disk-cache-mvp/README.md): 11 задач;
  input/binary probe, store, host/preparation/load/watch/write integration,
  restart/failure tests и hit/miss benchmark. Это первый полезный production scope.
- [E2 — content refresh](epoch-2-content-refresh/README.md): 7 задач;
  offline dirty owners, source/additional/config/metadata refresh, новый capture
  и edit/build → restart benchmark. Graph/membership change сохраняет fallback.
- [E3 — project refresh](epoch-3-project-refresh/README.md): 7 задач;
  narrow reopen spike, mapping/adapter/membership/publication, tests и benchmark.
  No-go spike оставляет E1/E2 пригодными и downstream selective scope deferred.

Всего **33 небольшие задачи**. Суффиксы `low/med/hi/xhi` обозначают классы
сложности, модели и reasoning указаны в шапках. Несколько рекомендованных моделей
допустимы; их равная пригодность не считается установленной. Новые внешние
альтернативы проверяются на фактическом scope, а история фиксирует точную модель.

## Индекс входов

При ordinary load сохраняются пути, роли и все projects/memberships-потребители:

- C# documents, AdditionalFiles и analyzer-config documents независимо от расширения.
- Загруженные `.sln`/`.slnx`/`.csproj`, реально импортированные `.props`/`.targets`,
  стандартные walk-up candidates `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, применимые NuGet.Config.
- Restore inputs по реальным путям: assets, lock/config files, импортируемые
  `*.nuget.g.props/targets`. Explicit inputs из obj и внешние linked paths
  проверяются точечно, даже если фоновой обход их каталогов pruning исключает.
- Реально используемые metadata/analyzer/generator binaries; signing key/ruleset
  и другие явно заданные compiler inputs, когда они присутствуют в выбранном срезе.
- Каталоги возможного появления source/config inputs; known absence фиксируется,
  чтобы обнаружить новый ancestor config/import. Каталог оценивается по sorted
  включённым именам/видам/child hashes, не по Directory.LastWriteTime.

Обход ограничен каталогами проектов, необходимыми ancestor candidates и explicit
dependencies; node_modules/VCS/build output trees целиком не сканируются.
Файл с двумя ролями получает более строгую evaluation роль. Новый path в region
привязывается к владельцам region и вызывает membership dirty до новой evaluation.
Профиль задаёт, как обнаруживаются новые файлы каждой поддержанной роли;
одни hashes уже известных Documents не заменяют membership scan.

`.config`/`.json` сами по себе не означают compiler input. Runtime-only appsettings,
web/App.config не включаются автоматически; actual build/additional role включает
их в manifest. Razor/XAML/resx входят при явно принятой поддержке соответствующего
build profile; пока такого capture/generation path нет, request получает fallback.
Custom targets с неизвестными дополнительными inputs не считаются поддержанными.

## Проверка бинарей

Proposed fast policy: path + size + LastWriteTimeUtc для всех; full hash до
**16 MiB** включительно, для больших — SHA-256 первых/последних **64 KiB**.
Один physical path читается один раз. Изменившаяся stamp/bytes сразу даёт dirty;
full hash гигабайтного уже изменённого файла ради подтверждения dirty не нужен.
Порог и chunk size могут меняться по отдельному решению после benchmark.

Это эвристика `mvp-fast`: изменение середины большого файла при сохранении размера,
timestamp и краёв может остаться незамеченным. Manifest/response/report сохраняют
название validation policy и её параметры. Atomic/repeated stat не доказывает
невозможность такой подмены. Integrity checksum самого envelope остаётся полным.
Текущие analyzer DLL execution/shadow-copy checks этой эвристикой не заменяются.
Полный binary validation может появиться отдельным режимом позднее; он не prerequisite E1.

## Поведение первого MVP

Proposed `load_workspace`: `useDiskCache=false`; `forceReload=false`.
Force обходит RAM и disk и выполняет обычную загрузку. Disabled path сохраняет
текущий RAM matcher: omitted arguments не очищают existing values. Disk request
key включает normalized workspace path, фактически effective load properties
и semantic modes; absent/requested/effective значения фиксируются отдельно.
Разные графы не объединяются одним hash key. Overlay mode не переносится как
прежняя активная session: новый PID/new key получает новую session и текущий prepare.
BuildArgs остаются suffix будущих CLI builds, отдельно от semantic graph identity.
При `useDiskCache=false` диск не читается и capture/write cache не выполняются.
Force при включённом disk cache обходит чтение старого состояния, но после
успешной ordinary загрузки может записать новое стабильное состояние.

Сначала ordinary load, затем stable base capture до ответа load. На enabled RAM
hit без валидного capture evidence скрытый force capture не обязателен: ответ
возвращает `capture=not-attempted` с причиной. При новом PID read/probe/hydrate/
prepare/publication предшествуют выдаче semantic workspace. Стартовые changes
в E1 дают ordinary fallback; E2/E3 расширяют reuse по собственным specs.
Cache failure не отменяет успешную обычную загрузку. `reset_workspace` очищает
RAM, но не disk. Инвалидация/чистка disk store не требует отдельного GC проекта.

Ответ/лог сообщает минимум: `baseGraphSource=ram|disk|msbuild`, effective cache
policy/validation profile, capture/write outcome, bounded fallback reason и
число dirty проектов. RAM hit не называется disk hit. `corrupt`, `incompatible`,
`unsupported`, `inputs_changed`, `membership_changed`, `hydrate_failed`,
`prepare_failed`, `disabled`, `forced` различаются. Diagnostic/log strings в C# — English.

Hydrated host проходит действующие write preflight/exact inverse и analyzer
admission/publication gates. Fresh provenance не фабрикуется из cache DTO.
Недопустимую execution state нельзя публиковать raw для получения cache hit.
Same-identity analyzer update сохраняет restart-required политику. Unsupported
hydrated write operation отвергается до disk I/O с явным ordinary-load маршрутом.

## Ревью и исполнение

[Порядок исполнения](execution.md), [benchmark protocol](benchmark.md),
[инструкции рецензенту](review/README.md) и [общий стандарт учёта](../../README.md#учёт-задач-ревью-и-статистики).
Spec и задачи каждой эпохи проверяются как один пакет; review task на модель
не заводится. Принятие плана, task acceptance и приёмка реализации различаются.
Public activation отдельной работающей эпохи может рассматриваться после её
validation; будущие эпохи не блокируют уже принятый scope. Default остаётся
выключенным, пока владелец отдельно не выбрал изменение default.

До запуска E0 требуется принять его план с общими документами. Реализация
следующей эпохи начинается после принятого применимого scope предыдущей.
Результат E3 может быть deferred; это не отменяет принятый MVP E1/E2.
Серия не обещает закрыть требования отложенного v2.

## Статистика

- Ревью плана серии: модели/даты/раунды/исправления/отчёты —; не проверено.
- Ревью планов эпох: охват E0–E3; все не проверены; раунды/исправления —.
- Ревью задач: охват всех 33 задач; реализация не начата; раунды/исправления —.
- Приёмка реализации эпох: охват E0–E3; не начата; раунды/исправления —.

Сводка содержит суммы только известных значений по отдельным scope, модели и
ссылки на исходные reports. Дубли findings объединяются со сохранением provenance;
plan/task/epoch acceptance числа не смешиваются. Раунды и fixed finding counts
считаются по общему стандарту. Непроверенные/неизвестные данные не заменяются нулями.

## Подготовка пакета

**2026-10-07: docs prepared / ready-for-review.** Scope: новый MVP-маршрут,
четыре specs/карты эпох, 33 задачи, benchmark и review/accounting инструкции.
[Проверка подготовки и ограничения](preparation-report.md). Это проверка
документации автором, не независимое ревью планов и не implementation acceptance.
