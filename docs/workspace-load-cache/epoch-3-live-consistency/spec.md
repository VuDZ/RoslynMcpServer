# Epoch 3 — Live consistency

Статус: **proposed; политика чтения выбрана 2026-10-01 — доверие watcher между
подтверждениями**. Дисковый кэш и остальная приёмка эпохи не выпущены. Запись
на stale/unknown base по-прежнему запрещена A-WRITE.

## Задача

Закрыть O4: после внешнего sync следующий semantic call получает состояние,
разрешённое выбранной freshness policy, либо явный отказ/unknown по её contract.
Write/refactoring на stale/unknown base всегда запрещены A-WRITE независимо от
будущего read policy.

Trace: R-05 — ACCEPT WITH MODIFICATION; E3-02/E3-04 — UNRESOLVED.

## Ортогональные состояния

Не объединять:

1. RAM/index freshness (`trusted`, `content-dirty`, `graph-dirty`, `untrusted`,
   `refresh-failed`);
2. validity старой disk generation;
3. cadence durable capture.

Старая несовпадающая disk generation обязана дать miss. Новая generation не
обязана записываться после каждого edit. Cadence — запись до ответа
`load_workspace` после полной обычной загрузки
([epoch-2](../epoch-2-conservative-disk-cache/spec.md#выбранный-capture-schedule)).

Trace: E3-06 — ACCEPT WITH MODIFICATION.

## Locking и publication

Нормативная схема:

`acquire -> classify -> refresh/load candidate -> validate -> prepare/gate
-> publish -> release`.

Internal under-lock APIs не захватывают semaphore повторно. Semantic accessor
возвращает immutable published snapshot/generation; write preflight повторно
проверяет generation. Dirty events, пришедшие во время flush, не очищаются
старым flush. Cancellation сохраняет pending queue.

Обязательные concurrency cases: simultaneous semantic/edit/reset, graph refresh,
stale candidate, cancellation, event during flush, prepare failure и no recursive
acquire. MVCC/merge не вводятся.

Trace: E3-01 — ACCEPT WITH MODIFICATION.

## Event and input classifier

| Role | Result |
|---|---|
| known explicit dependency | probe; content/graph role по profile |
| potential membership/negative region | validate membership/evidence |
| proven irrelevant | ignore, no DTB |
| relevant unknown/unknown coverage | `untrusted`/`graph-dirty` |

Editor temp и unrelated files не вызывают DTB. Extension blacklist не доказывает
irrelevance: AdditionalFiles/imports могут иметь custom extension.

Content-only допустим только для существующей supported Document category при
доказанной независимости evaluation и DTB targets от bytes. При совмещённых
ролях graph role приоритетна; missing document не считается applied. Все
memberships получают один подтверждённый text.

Trace: E3-03 — ACCEPT; E3-05 — ACCEPT WITH MODIFICATION.

## Watch/probe coverage

Coverage map включает project membership regions, walk-up ancestors, explicit
paths вне roots/в `obj` и locations возможного появления absent inputs.
Общий источник membership/revisions/coverage —
[pull-контракт project-input-watching](../../archive/project-input-watching/input-state-contract.md).
Его session token общий с WPF/watchers и build freshness; самостоятельный
input index или второй session counter эта эпоха не создаёт. Повторный pull
не потребляет revisions других consumer. Это связь моделей, а не закрытие
U-ARB-02 или разрешение public activation cache.
Реализация задаёт grouping, watch limits, startup, overflow/error/cancel и
resource telemetry. Prune recursion не подавляет explicit watch/probe.
Unavailable/lost watcher переводит state в untrusted; дальнейшее read behavior
определяется только после U-ARB-02.

Trace: E3-07 — ACCEPT.

## Own writes

После persistence записываются только реально saved paths, bytes/content hash и
revision. Partial result отмечает только successful paths. Time-window
suppression не отбрасывает более позднее событие по тому же path. Несовпадение,
ABA/нестабильное чтение или event newer than own revision оставляют
pending/untrusted; hash подтверждает состояние bytes, но не автора.

Trace: E3-08 — ACCEPT WITH MODIFICATION.

## Выбранный контракт чтения

Владелец 2026-10-01 выбрал **доверие watcher между подтверждениями**.
Это политика живой сессии, уже реализованная watcher-ом. Она не активирует
дисковый кэш загрузки и не закрывает U-ARB-03 и приёмку V07–V23.

Подтверждение — это `load_workspace` либо flush изменения, которое watcher
действительно доставил.

- Сохранённый `.cs`, который уже есть в графе, читается один раз и применяется
  ко всем документам этого пути.
- Сохранённый additional file или analyzer config, который уже есть в графе и
  не является evaluation input, применяется к опубликованному снимку.
  XAML, resx и Razor по роли учитываются и текст не синхронизируют.
- Новый или удалённый файл документ не добавляет и не удаляет. Граф помечается
  composition-stale, следующий `load_workspace` открывает его заново.
- Файл проекта, solution и `Directory.Build.*` помечают граф stale отдельно.
- Переполнение watcher, ошибка watcher и переименование каталога внутри
  каталога проекта просят следующий flush перечитать известные документы и
  могут пометить граф stale.
- Своя запись подтверждается сохранёнными байтами, включая BOM. Совпадение
  этих байт не добавляет ещё одну ревизию.

Семантический вызов не хеширует дерево и не ставит метку `freshness=unknown`.
Постоянные дыры покрытия (импорты, restore, custom tasks, glob, walk-up, который
не попал в подписку) сами по себе чтение не запрещают и снимок не делают
недоверенным: на живой загрузке полное покрытие не наступает. Изменение, которое
watcher не доставил и об ошибке не сообщил, остаётся невидимым до следующей
загрузки. Отсутствие callback не доказывает, что ничего не изменилось.

Запись и рефакторинг по-прежнему проходят write preflight опубликованной базы.
Стоящий `unknown` покрытия не является запретом правки `.cs`.
`Banned` / `Unavailable` анализаторов эта политика не ослабляет.

### Какие чтения идут при unknown

На живой загрузке покрытие графа всегда `unknown`. Поэтому «отказать всем
чтениям при unknown» останавливает семантические тулы сразу после успешного
`load_workspace`, хотя ни один файл не менялся. «Разрешить все чтения» оставляет
O4 подсказкой: ответ выглядит обычным и тогда, когда watcher событие не доставил.
Выбранная политика — второе, без метки `freshness=unknown`.

Пример, который обязан работать. `load_workspace` открыл солюцию.
`find_symbol_definition` типа из только что загруженного проекта возвращает
объявление. Покрытие при этом уже `unknown` из-за импортов, restore и custom
tasks. Запрет чтения при unknown сделал бы этот вызов ошибкой.

Пример подтверждённого изменения. Агент сохранил `Class1.cs`, watcher доставил
событие, flush применил текст. Следующий `find_usages` видит новый метод. Это
не чтение при unknown: событие было, текст снимка обновлён.

Пример дыры, которую политика принимает. Выше корня наблюдения изменили
`Directory.Build.props`, callback не пришёл и ошибки watcher нет.
`find_symbol_definition` отвечает по старым константам и ссылкам, и ответ ничем
не отличается от свежего. Метка unknown отличила бы этот вызов от первого
примера. Запрет отличить их не может: сервер не знает, стоящий это unknown с
момента загрузки или тихая правка props.

Пример устаревшего состава. Рядом появился новый `Foo.cs`. Граф
composition-stale, документа в снимке нет. `get_diagnostics_for_file` по уже
загруженному файлу считается на старом графе. Запрет этого чтения из-за stale
остановил бы диагностику файлов, которые в графе есть. Новый файл попадает в
снимок только следующим `load_workspace`.

Запись в этот перечень не входит. `apply_patch` и `rename_symbol` по-прежнему
проверяют опубликованную базу. Смена сессии или базы отклоняет правку.
Покрытие `unknown` само по себе правку существующего `.cs` не отклоняет.

### Файл, который сборка держит

Выбранная политика файл на вызове не хеширует, поэтому блокировка DLL сборкой
семантический вызов не роняет. `run_dotnet_build` держит выходную сборку, а
`get_diagnostics_for_file` и `find_usages` читают уже опубликованный снимок в
памяти.

Если бы проверка на каждом вызове читала эту DLL, остались бы три ответа.

Ждать, пока сборка отпустит файл. `get_diagnostics_for_file` висит вместе со
сборкой. Вызов длиннее, клиент может оборвать его по своему таймауту, хотя
снимок в памяти уже пригоден для диагностики.

Повторять чтение несколько раз, потом выбрать один из двух других ответов.
Если агент параллельно сам повторяет тул, повторы складываются. Сборка на это
время DLL всё равно не отпускает.

Сразу считать проверку невыполненной. `find_usages` возвращает ошибку или
unknown, пока `run_dotnet_build` держит DLL анализатора. Агент вызывает
`load_workspace` ещё раз и запускает второй design-time build. Цикл «собрал,
потом посмотрел диагностику» ломается, хотя загруженным документам повторное
чтение этой DLL не нужно.

## Рассмотренные и не выбранные варианты

Сохранены, чтобы выбор не повторять молча. Цифры бюджета по-прежнему нет
(U-ARB-03); ни один отклонённый вариант нельзя включить как тихую оптимизацию.

### Строгая проверка на каждый вызов

Перед каждым семантическим вызовом заново сверять значимые входы, даже если
состояние считается доверенным.

Плюсы. Следующий вызов видит внешнюю правку, которую watcher не доставил:
родительский props вне подписки, переполнение без ошибки, тихий drop inotify.
O4 закрывается подтверждённым состоянием.

Минусы. Горячий путь становится хешированием дерева. Повторный поиск символов
после одной загрузки дорожает до стоимости, которую кэш должен был убрать.
Watcher перестаёт быть ускорителем: проверку всё равно платит каждый вызов.

Риски. На большой солюции продукт медленнее базовой линии. Сборка держит DLL и
assets; проверка в этот момент падает, и если падение запрещает поиск, агент
уходит в повторный `load_workspace` и design-time build. Без утверждённого
бюджета «приемлемо» нечем измерить.

### Периодическое подтверждение

Между проверками доверять watcher, а тихие потери искать по расписанию или раз
в N неизменных вызовов.

Плюсы. Горячий путь обычно дешёвый, а молчаливая потеря всплывает, не дожидаясь
ручной загрузки.

Минусы. В окне между проверками внешний sync невидим. O4 говорит про следующий
semantic call, а не про следующий тик. Период и потолок стоимости надо назвать
заранее; обоих чисел нет.

Риски. Короткий период сходится со строгой проверкой по стоимости. Длинный
оставляет ту же дыру, что доверие watcher, только с отложенным обнаружением.
Проверка, попавшая на файл, который держит сборка, даёт ту же ложную ошибку,
что и строгий вызов.

### Последний снимок и явная метка `freshness=unknown`

Отличать «доказано грязно» от «проверка не выполнена». В первом случае следующий
вызов требует перезагрузки. Во втором отдаёт последний снимок и прямо говорит,
что свежесть не подтверждена.

Плюсы. Поиск жив, пока сборка держит DLL. Агент видит, что снимок не доказан.
O4 закрывается второй половиной формулировки: явная невозможность подтвердить
обновление.

Минусы. Потерянное событие само не находится. Метка легко теряется в длинном
ответе тула. Надо отдельно решить, каким операциям unknown разрешён.

Риски. Unknown, возвращённый как ошибка, зацикливает агента на
`load_workspace`. Смешение «доказано грязно» и «не проверено» в один запрет
чтения останавливает семантические тулы на каждом живом графе, потому что
покрытие и так всегда unknown. Метка, которая звучит как гарантия, противоречит
O8: неизвестность должна отменять hit, а не выглядеть успехом.

## Приёмка после решения gate

- V07–V09, V19–V23 и concurrency matrix.
- External sync, own edit, overflow/new file, directory rename/delete, ancestor
  props, explicit `obj`, linked file, custom AdditionalFile, watcher unavailable.
- Следующий semantic call соответствует выбранному contract; write на
  stale/unknown всегда rejected.
- Измерен unchanged-call overhead по заранее утверждённому budget.
