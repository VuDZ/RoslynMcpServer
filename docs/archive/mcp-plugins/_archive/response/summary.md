# Summary

Спека и оба `review-*` не менялись. Числа ниже — сводка разбора, не оценка ревьюеров.

## Accepted

Проблема принята целиком:

- `review-astra/R-01` — сырой `GetPublishedSolutionAsync` не тот снимок, что у semantic-тулов.
- `review-astra/C-01` — build библиотеки не копирует частные NuGet-зависимости.
- `review-astra/C-02` — ссылка на apphost-exe не даёт метаданных.
- `review-astra/E1-01` — материализация SDK-тула позже границы `Register`.
- `review-astra/E1-02` — дедупликация имён не должна быть процесс-глобальной.
- `review-astra/E2-01` — неверный тип `plugins` роняет процесс.
- `review-astra/E3-01` — тест копирования байтов не доказывает разблокированный `bin` при живом ALC.
- `review-astra/E4-01` — шаблон попадёт в компиляцию хоста.
- `review-astra/E5-01` — `null` resolver на `System.Runtime` отвергает минимальный плагин.
- `review-astra/E5-02` — исключение `Register` оставляет DI и тулы.
- `review-astra/E6-01` — приёмка не видит `Program` и первый `tools/list`.
- `review-ds/E1-01` — коллекция наполняется при резолве options; счётчик падает в каталог.
- `review-ds/E2-01` — то же, что astra E2-01.
- `review-ds/E4-01` — то же, что astra E4-01, плюс generated `.cs`.
- `review-ds/E5-01` — то же, что astra E5-02.
- `review-ds/E6-01` — лог хоста недоступен до `Build`.
- `review-ds/E6-02` — help плагина не из чего собрать, не записав имя в каталог.

## Partially accepted

| Finding | Принято | Не принято |
| --- | --- | --- |
| `review-astra/E5-03` | Копирование `runtimes/` не является native-резолвером; authoring этого не говорит. | Обязательный `LoadUnmanagedDll` и тест P/Invoke в этой серии. v1 явно отказывает native. |
| `review-ds/R-01` | `Version` продукта не гарантирует версии MCP и Roslyn, которые загрузчик подменяет. Сценарий MCP 1.3 против 1.4 реален. | Снимать продуктовый `minHostVersion` до «подсказки». Exact-равенство всех сборок, включая каждый патч Roslyn, из требований не следует. |

## Rejected

Нет. Ни одна проблема не отклонена целиком.

Отклонённые исправления при принятой проблеме:

- третье состояние «загружен частично» в `review-ds/E5-01`;
- замена продуктовой версии одной осью пакетов в `review-ds/R-01`;
- обязательный native loader в `review-astra/E5-03`;
- второе наполнение коллекции из отчёта в `review-ds/E1-01`, если старт просто резолвит options до счётчика.

## Needs clarification

Отдельного finding со статусом `NEEDS CLARIFICATION` нет.

Открытый хвост правки `review-ds/R-01` — в Unresolved (`U-001`): зерно сравнения `Microsoft.CodeAnalysis`.

## Новые архитектурные риски

Отдельных `D-*` / `NEW-D-*` нет. Дыры, которые выглядели новыми при чтении (fallback `IsToolActive`, generated `.cs` в глобе, падение валидации DI), уже сидят в `review-ds/E1-01`, `review-ds/E4-01` и `review-astra/E5-02`.

## Изменения в спеке, если принять вердикты

См. Proposed change set. Кратко по файлам предложения, без кода:

- `docs/mcp-plugins/README.md` — пример анализа на `GetSanitizedPublishedSolutionAsync`; совместимость из двух осей; таблица эпох разрешает csproj-`Remove` в эпохе 4.
- `docs/mcp-plugins/authoring.md` — тот же вход анализа; доставка частных зависимостей; ссылка на DLL, не на exe; native не поддерживается; `minHostVersion` не единственный гейт.
- Эпохи 1, 2, 4, 5, 6 — правки `P-005`…`P-015`.
- Эпоха 3 — только пояснение, что `native.bin` не про P/Invoke.
- Эпоха 7 — не закрывает серию сменой статуса, пока authoring не совпадает с этими правками.

## На арбитраж

Развилка, где требования допускают два продукта:

- `U-001` — насколько жёстко сравнивать Roslyn (мажор или ещё и minor). Оба варианта совместимы с «не грузить заведомо чужой контракт»; точный порог требованием не задан.
- Native в v1 защитой закрыт как вне серии. Если заказчик считает пакет с native частью первого плагина коллег, это отменяет `P-012` и возвращает рецепт `review-astra/E5-03`. Сейчас такого требования нет.

Откат регистраций против «частично загружен» на арбитраж не выносится: «битый плагин не публикует тулы» уже выбирает откат.

# Cross-review synthesis

## Finding clusters

### C-001

- Source: `review-astra/R-01`
- Проблема: рекомендованный снимок решения не сбрасывает очередь диска и не совпадает со встроенными semantic-тулами.
- Corroboration: нет. Доказательство — методы `SolutionManager` и вызовы `NavigationTools`.
- Disagreement: нет.
- Позиция: принять. Звать `GetSanitizedPublishedSolutionAsync`, не заводить новый метод.

### C-002

- Source: `review-ds/R-01`
- Проблема: гейт совместимости смотрит не на те сборки, которые подменяются.
- Corroboration: нет.
- Disagreement: нет (astra этот абзац не разбирала).
- Позиция: частично. Продуктовый минимум остаётся. Добавить сверку MCP `Major.Minor` и мажора Roslyn. Minor Roslyn — `U-001`.

### C-003

- Source: `review-astra/C-01`
- Проблема: частные managed-зависимости не попадают в выход обычного build.
- Corroboration: нет. Проба SDK ревьюера согласуется с дефолтом `CopyLocalLockFileAssemblies`.
- Позиция: принять в эпохе 4.

### C-004

- Source: `review-astra/C-02`
- Проблема: authoring разрешает `Reference` на native apphost.
- Corroboration: нет.
- Позиция: принять, ссылаться на `RoslynMcpServer.dll`.

### C-005

- Source: `review-astra/E1-01`, `review-ds/E1-01`
- Проблема: момент появления тула в коллекции SDK не совпадает с текстом эпох и не изолирован по плагину.
- Corroboration: стороны разные (исключение против пустой коллекции и счётчика), корень один — отложенные фабрики `McpServerTool`.
- Disagreement: нет.
- Позиция: материализовать тул внутри границы плагина; тесты и счётчик резолвят options; в каталог не писать.

### C-006

- Source: `review-astra/E1-02`
- Проблема: «в этом процессе» читается как static набор имён.
- Corroboration: нет.
- Позиция: уникальность на один контейнер.

### C-007

- Source: `review-astra/E2-01`, `review-ds/E2-01`
- Проблема: неверный тип JSON роняет хост до плагинов.
- Corroboration: да, независимо, один и тот же `TryReadFile`.
- Disagreement: только цифра severity (High и Medium). Беру High.
- Позиция: `JsonException` → `ParseFailures`, хост жив.

### C-008

- Source: `review-astra/E3-01`
- Проблема: гарантия разблокированного `bin` не доказана тестом с живым ALC.
- Corroboration: нет.
- Позиция: тест эпохи 3 оставить; доказательство перенести в эпоху 5. Без второго OS-процесса и без native.

### C-009

- Source: `review-astra/E4-01`, `review-ds/E4-01`
- Проблема: глоб SDK компилирует `samples` в хост и ломает каталог тулов.
- Corroboration: да. ds добавил generated-файлы из `obj`.
- Позиция: `Compile`/`None` `Remove` в эпохе 4. В каталог не добавлять.

### C-010

- Source: `review-astra/E5-01`
- Проблема: отказ на resolver miss отвергает framework-сборки.
- Corroboration: нет. Проба `System.Runtime` достаточна.
- Позиция: три стадии. Fallback в default context не распространяется на отсутствующую частную DLL.

### C-011

- Source: `review-astra/E5-02`, `review-ds/E5-01`
- Проблема: `Register` уже мутирует общий DI к моменту исключения.
- Corroboration: да, проблема одна.
- Disagreement: исправление. Astra — commit/discard. Ds — ещё и статус «частично».
- Позиция: только commit/discard. Частичный статус не вводить.

### C-012

- Source: `review-astra/E5-03`
- Проблема: текст выглядит так, будто скопированный `runtimes/` загружает native.
- Corroboration: нет.
- Позиция: явно отказать native в v1, не строить `LoadUnmanagedDll`.

### C-013

- Source: `review-astra/E6-01`
- Проблема: цель «первый `tools/list` процесса» не покрыта тестом entry point.
- Corroboration: нет.
- Позиция: один subprocess smoke, in-process тесты не выкидывать.

### C-014

- Source: `review-ds/E6-01`
- Проблема: «stderr + лог» невыполним до `Build` теми аргументами, которые эпоха перечисляет.
- Corroboration: нет.
- Позиция: stderr сразу, та же запись в лог после `Build`.

### C-015

- Source: `review-ds/E6-02`
- Проблема: help плагина не имеет носителя метаданных вне каталога.
- Corroboration: нет.
- Позиция: отчёт старта хранит описание и параметры; Kind/Group для плагина — `plugin`, не группа каталога.

## Reviewer disagreements

### Откат против частичной загрузки

- `review-astra/E5-02` требует выбросить регистрации, если `Register` бросил.
- `review-ds/E5-01` допускает то же **или** третье состояние, при котором тулы остаются в списке.
- Evidence: README, «один битый плагин не роняет сервер» и не должен выглядеть загруженным; эпоха 6 показывает статус в info.
- Позиция: откат. Частичная загрузка оставляет вызываемый тул при статусе skipped.
- Спорного не остаётся, если принять эту норму. Остаётся только предел: побочные эффекты вне DI не откатываются. Оба ревьюера его признают.

### Severity `E2-01`

- Astra: High. Ds: Medium. Проблема одна.
- Позиция: High, потому что не стартует процесс. На содержание правки цифра не влияет.

Других несовместимых выводов нет. Совпадающие локальные ID (`R-01`, `E1-01`, `E5-01`, `E6-01`) — разные claims, не спор об одном утверждении.

## Cross-cutting issues

1. Граница «один плагин не роняет хост» дырявая в четырёх местах сразу: тип JSON до старта (`C-007`), исключение внутри `Register` (`C-011`), исключение SDK при материализации (`C-005`), глоб шаблона ломает каталог ещё до плагинов (`C-009`). Закрытие только catch вокруг `Register` недостаточно.

2. Приёмка систематически уже обещания: байты вместо живого ALC (`C-008`), in-process старт вместо процесса (`C-013`), stderr вместо лога (`C-014`), шаблон без частной зависимости (`C-003`). Это не повод ослабить обещания authoring.

3. Эпоха 7 не имеет права ограничиться сменой статуса. C-01, C-02, снимок решения, native-отказ и формулировка совместимости живут в authoring и в README темы.

# Proposed change set

## P-001

- Source: `review-astra/R-01`
- Sections: README, пример и «кухня»; `authoring.md`, «Чем пользоваться»; эпоха 5, тест.
- Outcome: анализ, совпадающий со встроенными тулами, вызывает `GetSanitizedPublishedSolutionAsync`. Два других метода объяснены. Тест без предварительного сброса очереди чужим тулом.
- Rationale: общий менеджер не равен снимку после disk-sync.
- Dependencies: нет.

## P-002

- Source: `review-ds/R-01`
- Sections: README, «Совместимость»; эпоха 2; `authoring.md`, таблица `minHostVersion`.
- Outcome: продуктовый минимум остаётся. До `Load` entry сверяется ссылка плагина на MCP (`Major.Minor` равен загруженному) и мажор `Microsoft.CodeAnalysis`. Источник ссылки — `deps.json`, запас — явные поля, если runtime-ассеты исключены.
- Rationale: загрузчик подставляет сборки хоста, а не `Version` продукта.
- Dependencies: `U-001` не блокирует MCP-половину. Minor Roslyn в эту правку не включать, пока нет решения.

## P-003

- Source: `review-astra/C-01`
- Sections: `authoring.md`; эпоха 4.
- Outcome: build копирует частные managed-зависимости и не копирует сборки контракта хоста. Тест вызывает код пакета, которого нет у хоста.
- Rationale: дефолт SDK class library.
- Dependencies: нет. Усиливает смысл копии в эпохе 3, но не меняет её правила исключений.

## P-004

- Source: `review-astra/C-02`
- Sections: `authoring.md`.
- Outcome: ссылка без исходников — на `RoslynMcpServer.dll` из publish, не на exe.
- Rationale: exe — apphost без метаданных.
- Dependencies: нет.

## P-005

- Source: `review-astra/E1-01`, `review-astra/E5-02`, `review-ds/E5-01`
- Sections: эпохи 1 и 5.
- Outcome: `Register` копит дескрипторы. В контейнер хоста они попадают после нормального возврата и после успешного `McpServerTool.Create` для каждого принятого метода. Исключение выбрасывает буфер и освобождает имена. Статуса «частично загружен» нет.
- Rationale: и DI-валидация, и отложенный SDK падают уже после обещанного пропуска.
- Dependencies: нет.

## P-006

- Source: `review-ds/E1-01`
- Sections: эпохи 1, 5, 6.
- Outcome: тесты и старт читают коллекцию и `CurrentToolCount` только после `IOptions<McpServerOptions>.Value`. Плагины в `McpToolCatalog` не добавляются. Fallback «пустая коллекция = число каталога» не используется, когда отчёт старта содержит плагинные тулы.
- Rationale: наполнение коллекции делает SDK options setup, не `Build`.
- Dependencies: `P-005` определяет, какие фабрики к этому моменту вообще лежат в DI.

## P-007

- Source: `review-astra/E1-02`
- Sections: эпоха 1.
- Outcome: уникальность имени — один `IServiceCollection`. Тест двух хостов в одном процессе.
- Rationale: коллекция тулов и так создаётся на хост.
- Dependencies: `P-005` (освобождение имени после отката — тот же словарь контейнера).

## P-008

- Source: `review-astra/E2-01`, `review-ds/E2-01`
- Sections: эпоха 2, `RoslynMcpFileSettings`.
- Outcome: ошибка типа известного ключа, включая `plugins`, становится `ParseFailures`. Хост стартует. Успешно прочитанный второй файл настроек сливается как сейчас.
- Rationale: нынешний `try` покрывает только чтение текста.
- Dependencies: нет.

## P-009

- Source: `review-astra/E3-01`
- Sections: эпоха 3 (пояснение), эпоха 5 (тест).
- Outcome: при удержанном ALC, загруженном из копии, исходный entry перезаписывается. Второй запуск загрузчика видит новые байты.
- Rationale: блокировка Windows сидит на пути `LoadFromAssemblyPath`, не на факте `File.Copy`.
- Dependencies: `P-010` и `P-011`, иначе шаблон не из чего грузить.

## P-010

- Source: `review-astra/E4-01`, `review-ds/E4-01`
- Sections: эпоха 4, README серии (общий файл), эпоха 7.
- Outcome: хостовый csproj исключает `samples\**` из `Compile` и `None`. Приёмка: типа шаблона нет в сборке хоста после чистой и повторной сборки. Каталог тулов не пополняется именем шаблона.
- Rationale: SDK глобит `**/*.cs` под корнем проекта.
- Dependencies: нет. Блокирует осмысленный прогон эпох 5–7.

## P-011

- Source: `review-astra/E5-01`
- Sections: эпоха 5.
- Outcome: host-контракт → resolver плагина → framework/default. Miss частной зависимости — пропуск, не bind из хоста. Два теста.
- Rationale: `System.Runtime` не лежит в deps.json плагина и не входит в четыре семейства.
- Dependencies: нет.

## P-012

- Source: `review-astra/E5-03`
- Sections: `authoring.md`, эпохи 3 и 5.
- Outcome: v1 не резолвит native. Наличие файла в `runtimes/` этого не обещает.
- Rationale: серия про managed Roslyn; unmanaged hook не был требованием.
- Dependencies: нет. Не отменяет `P-003`.

## P-013

- Source: `review-astra/E6-01`
- Sections: эпоха 6, тесты.
- Outcome: subprocess smoke entry point с `ROSLYN_MCP_PLUGINS`, первый `tools/list` и `tools/call`. In-process тесты остаются.
- Rationale: цель эпохи — процесс, приёмка — прямой вызов метода.
- Dependencies: `P-010`, `P-011`.

## P-014

- Source: `review-ds/E6-01`
- Sections: эпоха 6, `Program`.
- Outcome: stderr в момент пропуска; те же строки в лог хоста сразу после `Build`, без повторного обхода плагинов. Приёмка читает файл лога.
- Rationale: `ILogger` появляется с контейнером.
- Dependencies: нет.

## P-015

- Source: `review-ds/E6-02`
- Sections: эпоха 6, форматтер help.
- Outcome: отчёт старта хранит описание и параметры принятых тулов. Help читает их. Kind и Group для плагина — `plugin`. Близкие имена включают плагинные тулы. В `McpToolCatalog` и `McpToolHelpCatalog` записей нет.
- Rationale: форматтер сегодня умеет только каталог.
- Dependencies: `P-005`, `P-006`.

# Unresolved

## U-001

- Finding: `review-ds/R-01`
- Неизвестно: отвергать ли плагин при том же мажоре Roslyn и другом minor (5.0 против 5.9).
- Почему важно: патч продукта `1.0.35` уже перевёл Roslyn на 5.9.0. Слишком жёсткий порог заставит пересобирать плагин на каждом таком патче; слишком мягкий пропустит несовместимый API.
- Нужно: решение человека. До него в спеку входит только разный мажор Roslyn и `Major.Minor` MCP (`P-002`).

# Ledger

`Source finding → Problem → Correction → Cluster → Change`

- `review-astra/R-01` → ACCEPT → MODIFY → C-001 → P-001
- `review-astra/C-01` → ACCEPT → ACCEPT → C-003 → P-003
- `review-astra/C-02` → ACCEPT → ACCEPT → C-004 → P-004
- `review-astra/E1-01` → ACCEPT → MODIFY → C-005 → P-005
- `review-astra/E1-02` → ACCEPT → ACCEPT → C-006 → P-007
- `review-astra/E2-01` → ACCEPT → ACCEPT → C-007 → P-008
- `review-astra/E3-01` → ACCEPT → MODIFY → C-008 → P-009
- `review-astra/E4-01` → ACCEPT → ACCEPT → C-009 → P-010
- `review-astra/E5-01` → ACCEPT → ACCEPT → C-010 → P-011
- `review-astra/E5-02` → ACCEPT → ACCEPT → C-011 → P-005
- `review-astra/E5-03` → PARTIALLY ACCEPT → MODIFY → C-012 → P-012
- `review-astra/E6-01` → ACCEPT → ACCEPT → C-013 → P-013
- `review-ds/R-01` → PARTIALLY ACCEPT → MODIFY → C-002 → P-002 / U-001
- `review-ds/E1-01` → ACCEPT → MODIFY → C-005 → P-006
- `review-ds/E2-01` → ACCEPT → ACCEPT → C-007 → P-008
- `review-ds/E4-01` → ACCEPT → ACCEPT → C-009 → P-010
- `review-ds/E5-01` → ACCEPT → MODIFY → C-011 → P-005
- `review-ds/E6-01` → ACCEPT → MODIFY → C-014 → P-014
- `review-ds/E6-02` → ACCEPT → ACCEPT → C-015 → P-015

Source findings: 19.
Problem ACCEPT: 17.
PARTIALLY ACCEPT: 2.
REJECT: 0.
NEEDS CLARIFICATION: 0.
Clusters: 15.
Clusters с независимым совпадением двух ревьюеров: 3 (`C-007`, `C-009`, `C-011`). `C-005` — смежные, не одинаковые claims.
Разногласие ревьюеров по исправлению: 1 (откат против частичной загрузки, `C-011`). Разногласие severity без разногласия сути: `C-007`.
Новых `D-*`: 0.
Proposed changes: 15 (`P-001`…`P-015`).
Unresolved: 1 (`U-001`).
