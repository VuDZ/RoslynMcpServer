# Coverage

## Reviewed target

`docs/rapid-tia/epoch-2-level1-prototype/task-01-forward-reverse-probe-hi.md` целиком, как план спайка E2/task-01 на дату текста (статус planned, 2026-11-08).

Рядом прочитан `docs/rapid-tia/epoch-2-level1-prototype/README.md`: цель эпохи, место task-01 и то, что остальные задачи эпохи отложены до итогов спайка. Расхождение «эпоха обещает стенд и сохранённый граф, task-01 выносит их из scope» само по себе находкой не стало: README эпохи уже говорит, что дальнейшие задачи нарезаются по итогам этого спайка.

## Context inspected

- `docs/rapid-tia/README.md`: R-01…R-20, O-03, O-06, O-07, O-08, уровень 0, состав ответа, уровень 1 (корни, рёбра, RTA, объявленная цель виртуального вызова, R-14, идентичность узла), построение графа R-08/R-09, слепые зоны, стенд полноты, строка эпохи 2 в таблице эпох.
- `docs/rapid-tia/epoch-1-project-level/task-06-adapter-test-names-probe-low.md` и `docs/rapid-tia/epoch-1-project-level/evidence/task-06-adapter-names.md` — что именно измерено про FQN, display name и склейку id. Это отчёт измерения, не ревью.
- `spikes/AdapterTestNames/README.md`, `spikes/AdapterTestNames/Collect-Evidence.ps1` — поля каталога collector.
- Код, на который план опирается:
  - `Services/Testing/TestDiscoveryHelper.cs`
  - `Services/Testing/VstestTestInventory.cs`
  - `Services/Testing/Baseline/BaselineDocumentSnapshotBuilder.cs` (`IsLifecycleMethod`, роль корня эпохи 1)
  - `Services/Navigation/SearchIndexPrewarm.cs`
  - `RoslynMcpServer.Tests/Support/RoslynIndexCache.cs`
  - `RoslynMcpServer.Tests/Workspace/WorkspaceInputMapTests.cs` (`GraphFixture`)
  - `RoslynMcpServer.Tests/Workspace/WorkspaceInputWatcherDeliveryTests.cs` (`DeliveryFixture`)

Проверено и находкой не стало:

- Отдельный процесс обратного поиска, prewarm до первого запроса и холодный/тёплый persistent index согласуются с `SearchIndexPrewarm` и с каталогом индекса Roslyn (`storage.ide` на процесс и путь solution). Отдельного противоречия нет.
- Отсутствие R-RTA. Обратный поиск без прямого графа не восстанавливает неподвижную точку RTA по тестовому классу. План сравнивает R-CHA с F-CHA и сужение RTA показывает только на прямом графе. Это согласовано с вопросом R-04.
- Вынос стенда истории коммитов, VTA, source generators, сохранения графа и R-14 за scope task-01. R-20 описывает именно этот спайк; решение «не строить фон и диск, если обратный поиск дешёвый» можно принять по цене построения и p95 поиска. Поздние замеры эпохи нужны только если прямой граф оставляют.
- Годность пары (таймаут, crash testhost, нулевой прогон, пустое O = N/A, негодные пары не выкидывать) — внутренне согласована и закрывает тихий успех эталона.
- Слепые зоны плана (рефлексия, DI из конфигурации, `dynamic`, атрибуты, генераторы, код вне solution) не занижают порог относительно канона: канон дополнительно называет файлы данных, запуск процессов и тесты, читающие исходники как текст. Не listed-зона попала бы в дефект правил и сделала порог строже, не слабее. Отдельной находкой это не стало.

## Context not inspected

- `docs/rapid-tia/epoch-2-level1-prototype/review/` целиком, включая чужое ревью, ответы автора и перепроверку. В шапке плана и README эпохи видны только счётчики раундов; содержимое карточек не читалось и в выводы не шло.
- Другие каталоги `review-*`, defense и arbitration по rapid TIA.
- Исходники Roslyn и офисный solution. Multi-targeting взят из канона идентичности узла, не из дерева Roslyn на диске.
- Реализация уровня 0 в инструментах эпохи 1 сверх определения в каноне. План ссылается на уровень 0 как на уже принятое правило пути → тестовые проекты.

## Source/code verification

Проверялись контракты, которые план называет готовыми опорами, а не дизайн графа целиком. Графа уровня 1 в production нет; это ожидаемо для спайка.

Сверены: потолок и тип класса в `TestDiscoveryHelper`, раскрытие наследников в `VstestTestInventory`, состав `IsLifecycleMethod`, поля collector, вызовы из `GraphFixture` и `DeliveryFixture`, существование prewarm и дискового индекса.

Тесты не запускались. Это ревью плана, не прогон.

## Coverage limitations

Не проверялось поведение `SymbolFinder.FindReferencesAsync` на `with`, неявных преобразованиях и опущенных необязательных аргументах. План сам ставит сравнение F-CHA ↔ R-CHA, чтобы такие расхождения всплыли и были разобраны. Пока разбор обязателен, отсутствие пооперационного доказательства обратной таблицы находкой не считалось.

Не измерялась устойчивость `TestCase.Id` к вставке `throw`. В F-005 это граница: склейка прогонов должна идти по единице сравнения, а не по сырому id.

Не проверялось, сколько TFM реально включено у текущего `Roslyn.slnx`. Для F-002 достаточно канонического правила ключа и того, что Roslyn назван корпусом именно из-за таких проектов.
