# Эпоха 1 — Интеграционная проверка жизненного цикла

Статус: **baseline измерен (красный)**. Результат — полный прогон с failures:
см. [epoch-1-results.md](epoch-1-results.md), приёмка [epoch-1-acceptance.md](epoch-1-acceptance.md).
Инвентаризация читателей: [epoch-1-semantic-entry-points.md](epoch-1-semantic-entry-points.md).

## E1-S1. Тестовый хост и oracle

Создать изолированный тестовый процесс с реальным MSBuild bootstrap и production
жизненным циклом `SolutionManager` и analyzer loader. Тест не заменяет этот путь
самостоятельным GeneratorDriver или одним `AdhocWorkspace`. Новый публичный MCP API
для generated documents не добавляется.

Временное решение содержит Generator и Consumer, перенаправленный `OutputPath` и
`ProjectReference OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
Consumer обязательно обращается к сгенерированному члену. Из одного overlay `Project`
получить compilation через `GetCompilationAsync`, найти известные generated type и
field, прочитать `IFieldSymbol.ConstantValue` и сравнить с точным `V1`/`V2`.
Нет типа, поля или константы — failure. Generated text можно сохранить как evidence;
пустые diagnostics не являются oracle версии. Отрицательный контроль с недоступным
генератором должен провалить oracle. Во время V1→V2 исходный Consumer не меняется.

## E1-S2. Обязательная матрица

| Случай | Шаги и обязательные наблюдения |
| --- | --- |
| Базовая генерация | Build, сохранить `.csproj` bytes, opt-in load, точный V1; повторить семантику без изменений |
| Обновление V1→V2 | При неизменных assembly name/version изменить только генератор, завершить build; отдельно cached load, reset+load в том же процессе, process restart |
| Флаг на том же ключе | true→false, true→omitted, false→true; каждый вариант также после reset. Зафиксировать sticky v1.3.5, последующая смена ожидаемого результата только по U-ARB-05 |
| Смена решения | A с opt-in и маркером A, затем B с той же assembly identity, корректным analyzer path и отключённым overlay: проверять маркер B и реально загруженные identity/path. Для B с broken path и выключенным флагом ожидать отсутствие генерации |
| Нет output | Первое включение до build, затем build и повторная загрузка; различать failed prepare и последующее исполнение |
| Несколько Consumer | Общий генератор, повторные подготовки и семантические обращения всех Consumer; обязательный случай эпох 1/2 |
| Внешний одноимённый analyzer | Внешняя DLL и in-solution кандидат имеют одинаковые filename/assembly name, но разные маркеры. Проверить существующий foreign path и отдельно missing foreign path; результат последнего служит evidence U-ARB-01 |
| Сбой повторного overlay | Loaded shadow→document edit→reapply с внедрённой ошибкой подготовки в baseline; результат не должен теряться, нельзя молча заменить активные ссылки broken original references |

Для каждой из трёх операций обновления записать cache hit/reopen графа, попытку
artifact refresh, выбранное поколение, фактическую assembly identity/path и маркер.
В baseline старый timestamp path не называется content generation v2. Коллизия A/B
не объявляется воспроизведённым дефектом до эксперимента.

## E1-S3. Три пути записи и anti-lock

Независимые обязательные подслучаи:

1. Текстовая правка через `UpdateDocumentInMemoryAsync` со штатной записью файла.
2. Rename либо поддержанное преобразование overlay-derived solution через
   `ApplySolutionChangesToDiskAsync`.
3. Реальная доставка FSW, затем production flush через `FindDocumentAsync` либо
   `GetCurrentSolutionAfterDiskSyncAsync`.

Для каждого проверить точный маркер активного поколения, запрошенный новый текст
и побайтовую неизменность `.csproj`. Добавить fallback/reconciliation эпохи 4.
После сохранения выполнить реальную сборку всего repro и убедиться в отсутствии
временных `<Analyzer Include>`.

Все три пути проверить на двух fixtures: отсутствующий resolved analyzer path и
существующий корректный path. После семантического исполнения и после каждого пути
принудительно пересобрать генератор с записью изменённых DLL bytes. Успешная
инкрементальная сборка без записи не доказывает отсутствие lock. Зафиксировать
реальные пути загрузки; successful build не доказывает конкретный предполагаемый
источник блокировки. Результаты входят в gates эпох 3/4 и evidence U-ARB-04.

Watcher проверяется по фазам: ограниченное ожидание появления dirty event,
production flush, проверка опубликованного текста и маркера. Timeout должен
различать недоставку события и неуспех flush. Внутренний seam допускается для
детерминированного flush, но не заменяет хотя бы один тест реального FSW.

## E1-S4. Согласованность и изоляция

Проинвентаризировать все semantic entry points: откуда получен snapshot, где flush,
сохраняется ли та же база до преобразования/ответа, есть ли raw
`workspace.CurrentSolution` compilation. Включить `RenameSymbol`, двухфазные
read→transform операции, `FindDocumentAsync`,
`GetCurrentSolutionAfterDiskSyncAsync`, getter callers, assembly exploration и
decompilation. Не исключать getter callers из общего контракта без проверки.
Rename с flush всё ещё требует проверки повторного чтения базы после вычисления symbol.

Проверить concurrency MCP dispatch и связь `LoadWorkspace`→enable→summary с одной
load session; при возможной конкуренции применять внутренний scope/expected-session
контракт README. Не вводить историю снимков или публичный session token.

Каждый loader lifecycle scenario запускается в собственном процессе; шаги,
проверяющие same-process reload, остаются вместе в нём. Межпроцессная публикация
эпохи 2 использует два child process с одним test-owned root. Параллельные loader
сценарии не разделяют глобальный resolver/CLR.

Helper-only change, отсутствующая/несовместимая helper и конфликт её версий могут
измеряться после готовности точного oracle. Отдельно записывать preparation,
binding и execution; эти эксперименты не объявляют поддержку до решения эпохи 3.

## E1-S5. Воспроизводимость и завершение

Создавать disposable fixture и shadow root во временном каталоге согласно правилам
репозитория; внешний `C:\Scratch\GenRepro` не требуется и не копируется в репозиторий.
Зафиксировать SDK, Roslyn, ОС, bootstrap, команды запуска и restore. Недоступное
окружение — явный skip с причиной, не passed. Ожидания событий ограничены timeout,
фиксированные задержки не заменяют наблюдение.

Эпоха завершается воспроизводимыми тестами и результатами всех обязательных строк,
включая failures. Каждый failure имеет причину, evidence и последующую эпоху/gate;
assertions не ослабляются ради зелёного baseline. В целевой регрессии эпохи 2
ошибка подготовки на edit больше не вызывается: проверяются ноль вызовов prepare
и сохранение mapping/маркера даже при недоступном исходном output.
