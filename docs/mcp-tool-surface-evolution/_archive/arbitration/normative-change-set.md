# Нормативные изменения для следующей ревизии

Этот список относится к документам серии. Не менять код до отдельного запроса на реализацию соответствующей стадии.

## Stage 0 — E0-01

1. В [`stage-0-hygiene.md`](../proposal-v1/stage-0-hygiene.md), «Работы» п. 3, заменить «SDK из `global.json`» на явный setup .NET SDK `10.0.x` в Windows workflow. Не ссылаться на отсутствующий pin-файл и не добавлять его как обязательную работу Stage 0.
2. В acceptance указать: clean Windows runner, успешный setup SDK, лог `dotnet --info` или `dotnet --version`, запуск обоих test jobs с прежними фильтрами и таймаутом. `10.0.x` означает выбор семейства SDK, не точную patch-версию.
3. Сохранить требование до restore изучить revert `b9f2e02`; результат этой проверки не выводить из одного лишь факта отката.

## Stage 2 — E2-01, E2-03, E2-04

1. В [`stage-2-symbol-identity.md`](../proposal-v1/stage-2-symbol-identity.md) и [`review-mapping.md`](../proposal-v1/review-mapping.md) R-§6 удалить предписание использовать `Microsoft.CodeAnalysis.SymbolKey` и запрет server-side handle. Контракт MCP: `symbolId` — непрозрачная строка, созданная сервером на публичных API Roslyn 5.9.0; формат клиент не разбирает. Прямые вызовы internal Roslyn API не входят в Stage 2.
2. Сервис идентичности хранит load-session generation, `ProjectId`, `DocumentId` и якорь декларации для каждого выданного ID. Для символа с несколькими объявлениями сохраняет все относящиеся к нему документы. Новый процесс, `reset_workspace`, фактическая новая загрузка проекта/solution, смена TFM или исчезновение project/document делают прежний ID невалидным. Повторный cached load той же session не инвалидирует его. Не обещать переносимость ID между процессами/сессиями.
3. При выдаче ID сохранить fingerprint полного текста каждого документа объявления. Перед каждым ID-based semantic read и edit получить текущий published snapshot после disk sync, проверить session/project/document и совпадение этих fingerprints, затем восстановить symbol из сохранённого якоря и проверить kind и project context. Если одна проверка не проходит, вернуть явный `stale-id` без выбора ближайшего, первого или одноимённого символа. Изменение другого документа не является причиной `stale-id`. Для edits проверять identity до построения кандидата на запись и повторно обеспечить согласованность с write base через существующий freshness gate.
4. `get_symbol_info` выдаёт ID с указанным lifetime; `get_symbol_source` поддерживает local functions в неизменённом опубликованном snapshot. Удалить обещание, что ID переживает произвольную правку документа объявления. Не заявлять, что checksum объявления один гарантирует сохранение идентичности.
5. Для location-режимов при нескольких project memberships одного file path возвращать различимые candidates (`project`/`document` и location) и требовать выбора; не вызывать `FindDocumentAsync` с first-hit как resolver точного режима. Для legacy path-only вызовов сохранить прежнюю совместимость, явно ограничив гарантию точности для linked-файла.
6. До записи linked-пути выполнить preflight NEW-ARB-001: все документы с этим нормализованным physical path должны иметь один согласованный итоговый текст. Иначе `shared-path-conflict` до любых файловых изменений. Один физический путь записывать один раз, затем согласовать все его опубликованные memberships. В частности, `scope="project"` для rename общего файла не трактовать как изоляцию физической записи.

## Stage 2 — E2-02: публичные формы вызовов

1. Старые JSON-имена tools и параметров сохранить. Прежние валидные вызовы без ID сохранить с теми же defaults и результатами в пределах существующего legacy-контракта.
2. Для `find_usages`, `find_symbol_references`, `get_method_body`, `get_call_graph`, `rename_symbol` сделать поля прежнего адресования необязательными в MCP JSON Schema, чтобы был допустим ID-only вызов. При отсутствии ID потребовать прежний набор полей, кроме явно нового location-режима `find_usages`.
3. Режимы: `find_usages` — `symbolId`, `filePath+line+column` или старый `symbolName`; `find_symbol_references` — `symbolId` или старые `filePath+symbolName`; `get_method_body` и `get_call_graph` — `symbolId` или старые `filePath+className+methodName`; `rename_symbol` — `symbolId+newName` или старые `filePath+symbolName+newName`. `get_symbol_info` — `symbolId`, полная location или query/name с явной выдачей candidates при неоднозначности. `get_symbol_source` — `symbolId`.
4. Если в одном запросе присутствуют поля более одного режима адресования, вернуть ошибку `conflicting-selectors`; если не заполнен ни один полный режим, вернуть ошибку `missing-selector`. Пустые строки считать отсутствующими. `newName` всегда required в схеме и runtime; `scope` и `previewOnly=true` сохранить. Для C# сигнатуры допустимо переставить `newName` перед optional полями, сохранив публичные MCP JSON-имена.
5. В выводах `find_symbol_definition` и `find_usages` прикреплять ID к конкретному кандидату, а не к неоднозначному набору результатов.

## Проверки при реализации Stage 2

1. Compile-time проверка против публичных API Roslyn 5.9.0; без reflection/internal adapter в производственном коде.
2. MCP schema и вызовы: ID-only для всех пяти старых tools, location-only для `find_usages`/`get_symbol_info`, каждый legacy-вызов, `newName` required, отсутствие/смешение selectors, defaults rename.
3. Два проекта с одинаковым `AssemblyName` и одноимёнными символами: ID выбирает нужное тело и проект rename. Linked source: location возвращает project candidates; edit проходит только при согласованном общем тексте, иначе `shared-path-conflict` без записи.
4. Повторный вызов после sync другого файла сохраняет ID; изменение полного текста файла объявления, включая вставку/удаление/перестановку одноимённых local functions и замену декларации тем же текстом при иных изменениях файла, выдаёт `stale-id`; reset/new load/new process инвалидируют ID. Проверить и read, и preview/apply rename.

## Индекс серии

В [`README.md`](../proposal-v1/README.md) указать, что Stage 2 передаётся в реализацию только после внесения этого контракта в канонические документы. Stage 1 и граница docs-only Stage 3 остаются прежними.
