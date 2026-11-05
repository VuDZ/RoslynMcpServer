# task-05 — независимое ревью, раунд 1

- Дата: **2026-11-05** (Europe/Moscow).
- Рецензент: Codex; точный runtime model ID средой не предоставлен, **unknown**.
- База: `9686c4ca2780407108cd70e3fc7199248500fb0d`; проверено незакоммиченное
  финальное состояние task-05 поверх этой базы, включая новые файлы, которые обычный
  `git diff` не показывает.
- Scope: внутренний composer, его модели, изменение mapper и callers, focused tests
  и fixtures, TDD evidence; обязательная передача T04-C1/T04-C2.
- Вердикт: **ACCEPT**. Новых must-fix по R-18 нет; **1 раунд ревью, 0 новых
  исправлений после ревью task-05**. Закрытие переданных C1/C2 учитывается отдельно
  и не является новым review-fix раундом task-05.

## Основание и границы

Прочитаны целиком [task-05](../task-05-response-composition-med.md), включая обязательную
передачу и требования к каталогам/настоящему watcher, [второе ревью task-04](task-04-review-round-1-claude.md),
[README эпохи](../README.md), корневой `AGENTS.md`, `docs/code-style.md`, инструкции
`epoch-subagent-loop` и Ponytail. Проверены разделы программы «Состав ответа»,
«Без графа», уровень 0, R-17/R-18 и каталог проверок; дополнительно прочитан контракт
[task-08](../task-08-estimate-tool-hi.md) на входы baseline и диагностику.

Проверен diff пяти существующих файлов: `ProjectImpactMapper.cs`,
`Models/ProjectImpactResult.cs`, `ProjectImpactFixture.cs`, `ProjectImpactMapperTests.cs`,
`ProjectImpactMapperIntegrationTests.cs`. Напрямую прочитаны новые `TestImpactComposer.cs`,
`AffectedTest.cs`, `DocumentImpact.cs`, `ProjectPathImpact.cs`, `TestImpactResult.cs`,
`TestImpactComposerTests.cs`, `TestImpactWatcherTests.cs`, `TestImpactWatcherFixture.cs`,
[отчёт реализации](../task-05-implementation-report.md), [RED](task-05-red.md) и
[GREEN](task-05-green.md).

Для проверки предпосылок прочитаны существующие detector/snapshot builder и модели
baseline, test discovery/matcher, классификация disk events и manager stale/reload
semantics. Эти компоненты в данном ревью не переписывались. Нет Computer Use/IDE UI,
правок production-кода, тестов, fixtures или требований. Посторонний untracked каталог
`.agents/skills/prepare-github-release/` не затрагивался. Статусы task и README остаются
ответственностью координатора после приёмки.

R-18 применён буквально: основание возврата — молчаливая потеря тестов либо неверный
ответ в воспроизводимом реальном сценарии. Грубость, цена и границы будущей интеграции
ниже не превращены в новый раунд.

## Проверенное поведение

- Часть 1 строится по текущим тестовым методам до объединения с graph input и до
  проектного расширения. Изменённый/новый, renamed и promoted тест сохраняется по
  текущему ключу; удалённый тест не предлагается по старому имени. Корень класса
  выбирает тесты этого класса из полного текущего inventory, включая неизменённую
  partial-часть в другом документе.
- Graph tests принимаются как готовый вход, объединяются с прямыми тестами по
  `(ProjectId, BaselineMemberKey)`; причины объединяются. Построения графа в task-05 нет.
- Проектное расширение использует существующий `ProjectImpactMapper.ExpandProjects`:
  сам тестовый проект и транзитивные зависимые тестовые проекты. Сужение проверяется
  отдельно для каждого пути/владельца. Безопасная правка теста не подавляет расширение
  от изменённой библиотеки и от второго non-C# входа того же проекта.
- Сужение допускают только added/modified test/root facts без `BlocksNarrowing` и без
  простого имени класса во входном множестве базовых имён. Проверки используют факты
  настоящего detector: abstract/nested/static/helper/fixture/promoted/new partial и
  прежде нетестовый класс сохраняют требуемое расширение. Generic и qualified base
  names проходят через существующий snapshot builder без semantic alias resolution.
- Неизвестный baseline и отсутствие текущего документа сохраняют проектное
  расширение. Mapper uncovered paths, stale/reload и причины проектных включений
  сохраняются в результате. Прямые тесты не удаляются при расширении проекта.

## Закрытие обязательной передачи task-04

### T04-C1 — CLOSED

Mapper принимает оба настоящих stale-флага. Только `ProjectGraphStaleFromGraphFile`
включает все загруженные тестовые проекты; aggregate stale управляет reload.
Per-path ownership передаётся composer, а graph-file fallback сохраняется отдельно.

Новые native tests получают флаги из `SolutionManager` после реальных файловых
операций, без ручной установки состояния и без вызова watcher callbacks. Проверены
новый тестовый `.cs` до/после настоящего `LoadAsync` reload, неизвестный non-C# путь,
graph-file fallback и операции каталога `into`, `out`, `restore`. Состояние наблюдения
подтверждается `EventRevision`, а состав проекта меняется настоящими `File`/`Directory`
операциями. Каталог остаётся uncovered либо расширяет свой проект с reload; независимый
`Isolated` не добавляется. Unknown non-C# путь остаётся явно uncovered и не расширяет
тестовые проекты.

Таким образом, снятие прежнего расширения до всех тестов не скрывает перечисленные
сценарии каталогов. Существующий task-04 integration test с вручную доставленным
csproj callback сохранён как прежняя проверка; он не используется вместо новых native
регрессий передачи C1/C2.

### T04-C2 — CLOSED

Неизвестный `.cs` сохраняет ближайшего владельца. Само отсутствие файла в solution и
карте больше не считается доказательством необходимости reload: этот совет берётся
из текущего watcher-state.

Native regression отдельно удаляет исходник и изменяет файл из `Compile Remove`,
ждёт настоящее событие, выполняет настоящий reload и проверяет отсутствие документа
и записи карты. После reload nearest ownership и `Specs` сохраняются, а повторного
`RequiresReload` нет. Это закрывает ложный повторный совет из C2 без изменения
manager/watcher lifecycle.

### T04-C3 — сохранено

Полная пересборка без правок может выбирать все тесты через mapped output assemblies.
По прямому условию task-05 это прежнее ограничение не исправлялось и не является
основанием возврата.

## TDD и validation

RED evidence фиксирует **37/37 ожидаемых падений**: 30 `NotImplementedException` и
7 поведенческих расхождений прежнего mapper при настоящем watcher. Ошибок компиляции,
fixture или ожидания watcher не было. Независимо сверены SHA-256 трёх новых файлов
tests/fixture с манифестом RED: все три значения совпадают с финальными файлами.
Новых тестов после RED нет. Две прежние unit-проверки нового `.cs` сохраняют точные
owners/tests/reload/uncovered expectations, адаптируя вход к реальному composition-stale.

Согласно [финальной validation координатора](task-05-final-validation.md), замороженный
код проверен через Roslyn MCP на Windows, Release: solution build PASS с тремя
прежними предупреждениями, focused **74/74**, SourceStructure **123/123**, main suite
**1874/1874** с фильтром `Category!=AnalyzerLifecycle&Category!=RequiresPinnedSdk`.
Exact inventory структурных ограничений не изменён. Рецензент прочитал этот отчёт,
но не заявляет эти прогоны своими и не запускал конкурентные build/tests.

## Ограничения без возврата

- **T05-R1-L01 — входы будущей интеграции.** Caller должен передавать полный текущий
  inventory тестовых документов и предусмотренное task-08 объединение baseline/current
  base-list names, включая удалённого наследника. Composer не собирает их сам.
- **T05-R1-L02 — диагностика baseline/gap.** `Changes=null` включает прямые текущие тесты
  и сохраняет расширение, а `NotCoveredPaths` передаются из mapper. Различение нового
  файла, неизвестного baseline и observation gap, а также подробный текст «Not covered»
  остаются обязанностью task-08 по его явному контракту. Публичного инструмента в этом
  scope ещё нет; это граница интеграции, а не найденный неверный публичный ответ.
- **T05-R1-L03 — прежняя приблизительность.** Синтаксическое распознавание ролей,
  simple-name inheritance без alias resolution, nearest-directory ownership без
  новой оценки Compile rules, текущая стоимость поиска тестовых проектов, restore
  blind spot и T04-C3 сохранены. Удаление/rename может расширять проект целиком.
- **T05-R1-L04 — предел проверки.** Новые native regressions и финальная validation
  выполнены координатором на Windows и малых fixtures. Linux, performance benchmarks,
  публичный tool/formatter/run plan и проекты владельца этим ревью не проверены.
  AnalyzerLifecycle не запускался: его производственные пути и конфигурация в diff
  не менялись.

Код не исправлялся; дополнительных прогонов не было. Риск будущей интеграции —
неполный inventory или потеря baseline/gap диагностики в task-08; её сквозная проверка
остаётся обязательной в этой последующей задаче.
