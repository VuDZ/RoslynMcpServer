# P2-52. Учитывать сводку VSTest без строк имён тестов

- Рекомендуемые модели: GPT-6.2 Sol.
- Рекомендуемый reasoning: medium.
- Примерная сложность: 2/5.
- Риск ошибки: средний. Агент получает ошибочный сигнал об отсутствии совпадений после успешного выполнения выбранного теста.
- Необходимые способности: семантика фильтра VSTest, сводки консольного логгера, статусы отчёта MCP.
- Статус: accepted, 2026-11-25. Отчёт сохраняет итоги VSTest без имён и признаёт отсутствие совпадений только по подтверждённому свидетельству. [Приёмка](evidence/tasks-49-54-validation.md#p2-52).
- Depends on: —.
- Отчёт о воспроизведении: [повторное ревью](evidence/review-f82d4ff.md#p2-52).

## Воспроизведение

1. Создать xUnit-проект с одним TFM `net11.0` и тестами `Demo.Fixture.Match` и `Demo.Fixture.Other`. В корневом элементе проекта задать `TreatAsLocalProperty="VSTestLogger"`, а в свойствах — `<VSTestLogger>console%3Bverbosity=minimal</VSTestLogger>`.
2. Собрать проект и вызвать `run_specific_test` с `className=Demo.Fixture`, `methodName=Match`, `noBuild=true` и `noRestore=true`.
3. CLI завершает тот же фильтр `FullyQualifiedName~Demo.Fixture.Match` с кодом 0 и выводит полную сводку `Passed: 1, Total: 1` без имён отдельных тестов. MCP показывает `no matching tests` и `zero tests matched the filter`. Сводка с одним исполнением видна в сыром журнале того же ответа.
4. `run_test_by_filter` с тем же фильтром показывает один прошедший тест. `run_specific_test` с логгером `normal` также показывает один прошедший тест.

## Причина и требуемое поведение

`ShouldReportFilteredNoMatch` при положительном общем итоге и отсутствии строки no-match приравнивает отсутствие подходящей строки с именем к нулевому числу совпадений. Учитывать итог выполнений. Если журнал не позволяет проверить имя, сообщать подтверждённый счётчик и неопределённость проверки имени.

## Критерии приёмки

- [x] В указанном прогоне отчёт сохраняет `1 passed`. Статус `no matching tests` появляется при подтверждённом отсутствии совпадений.
- [x] Реальный прогон без совпадений по-прежнему отображается как no-match. Проверка ошибки или падения и обычный логгер сохраняют текущий результат.

## Где смотреть

- [VstestOutputParser.cs](../../Diagnostics/VstestOutputParser.cs).
- [VstestOutputParserTests.cs](../../RoslynMcpServer.Tests/TestRunner/VstestOutputParserTests.cs).
- Связанные карточки — [P2-25](task-25-p2-filtered-test-failure-status-med.md), [P2-48](task-48-p2-vstest-no-match-source-path-med.md).

[Реестр задач](README.md)
