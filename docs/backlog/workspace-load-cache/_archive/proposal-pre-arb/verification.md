# Проверка корректности и производительности

Статус: **план проверок**, не отчёт о выполненных тестах.

## Независимый oracle

Для поддерживаемого fixture сравнивать восстановленный workspace с отдельным
свежим `MSBuildWorkspace` на стабильном дереве с одинаковыми properties. Мок,
повторяющий DTO implementation, не доказывает эквивалентность.

Сравнивать нормализованные project instances, document memberships, compiler
options, reference properties, analyzer configs и diagnostics (id, severity,
location, message при стабильности). Выполнять реальные navigation queries.
Для generator fixture проверять emitted sources и диагностику использования
generated-типа; не требовать от существующего name-search поведения, которого
он не поддерживает.

Unsupported допустим только если это заявлено в support profile и действительно
приводит к обычному load. Пропущенный тест нельзя отмечать как passed.

## Матрица

| ID | Сценарий | Ожидаемый результат |
|---|---|---|
| V01 | Два SDK C# проекта с ProjectReference | Равные граф, references, definitions/usages и diagnostics |
| V02 | Nullable, langversion, defines и `#if` | Options и видимые объявления совпадают |
| V03 | Inner TFM / multi-target graph | Корректные instances или явный unsupported |
| V04 | Analyzer, generator, AdditionalFiles, editorconfig | Входы/результаты совпадают либо профиль отвергнут |
| V05 | Required generated compile/config files в obj | Учитываются; отсутствие даёт miss |
| V06 | Linked source в двух проектах, external import | Все членства и входы учтены либо unsupported |
| V07 | Rename, AST edit, code fix после hydration | Диск и следующий semantic call согласованы |
| V08 | Analyzer overlay после edit/disk sync | Нет shadow refs в csproj, overlay сохраняется |
| V09 | Cancellation/failure при смене workspace | Нет частичного графа, leaks и потерянного stale state |
| V10 | Иное содержимое с теми же size/mtime | Strict probe обнаруживает изменение |
| V11 | Добавление/удаление `.cs`, Compile Remove | Epoch 2 делает miss; результат равен свежему DTB |
| V12 | Появился файл для Exists/условного import | Miss/unsupported, не ложный unchanged hit |
| V13 | Изменены ancestor props, NuGet config/assets, SDK | Miss при любом значимом изменении |
| V14 | DLL/analyzer dependency заменена по тому же пути | Miss; не используются старые symbols/generator |
| V15 | Смена properties, TFM, Roslyn/schema | Другой ключ или incompatible miss |
| V16 | Junction cycle/retarget, регистр путей | Нет бесконечного обхода/ошибочного объединения |
| V17 | Два writer и reader, crash при публикации | Только целые поколения либо fallback |
| V18 | Битый/огромный payload, нет места/прав, cleanup | Ограниченный расход ресурсов, обычный load работает |
| V19 | Overflow + новый `.cs`, directory delete/rename | Полная проверка состава, новый корректный граф |
| V20 | Собственная запись MCP при suppressed watcher | Индекс и все memberships обновлены напрямую |
| V21 | Изменение во время hash/DTB/hydrate/flush | Кандидат отвергнут/повторён, события не потеряны |
| V22 | Родительский config, внешний linked input | Изменение обнаружено вне обычного watcher root |
| V23 | Новый процесс, RAM-hit, force, reset | Различимые источники; force обходит оба слоя |
| V24 | Один sln, root A затем B, partial затем full | Нет коллизии keys; hints соответствуют графу |
| V25 | Metadata true/false, DLL есть/нет/битая/stale | Проверенная обработка, честная полнота semantic scope |
| V26 | Symbol index: одинаковый текст, разные defines | Candidates соответствуют project context |

Fixture с произвольным custom target полезен как negative test: распознавание
unsupported должно произойти до выдачи disk-hit. Для каждого нового support
profile нужны положительные тесты, а не только fallback.

## Измерения

На маленьком fixture проверяется корректность, на большом реальном решении —
польза. До измерения записать размер графа, документов, inputs, тип накопителя,
ОС, версии, Configuration/TFM, режимы overlay и metadata.

Сравнивать:

1. Новый MCP-процесс, cache disabled, обычный MSBuild load.
2. Новый процесс, cache absent: стоимость проверки/записи поверх обычного load.
3. Новый процесс, валидный disk-hit.
4. Повторный load в том же процессе: RAM-hit.
5. Новый процесс после изменения input: miss/fallback.
6. Живая сессия: unchanged call, content update, graph update, overflow recovery.

Для каждого сценария не менее 10 повторов, публиковать сырые данные, median и
p95 как ориентировочную оценку на небольшой выборке. Чередовать порядок baseline
и cache runs. Не называть новый процесс «холодным диском»: отдельно указать
состояние OS file cache; не очищать системный cache без отдельной необходимости.

Метрики: load duration, первый полезный semantic response end-to-end, последующий
semantic call, DTB invocation count, probe/hash/hydrate duration, bytes read/hashed,
peak working set, cache bytes и причины misses. Один working-set log в конце
операции не является peak memory. Счётчик OpenSolution недостаточен, если DTB
скрыт за другим API: инструментировать реальный loader path.

## Release gates

- Ноль необъяснённых semantic differences в поддерживаемой матрице.
- Ноль изменений проектных файлов из-за hydration/overlay/disk reconciliation.
- Доказанный cross-process hit без DTB на стабильном поддерживаемом графе.
- Все fallback и сбойные сценарии завершаются без частичной публикации.
- Perf budget зафиксирован до оценки результата; default false сохраняется.
- Обязательные build/test проверки выполняются средствами и по правилам
  репозитория. Недоступность MCP build/test tools явно фиксируется; не утверждать,
  что тесты запускались, если выполнены только статические проверки.
