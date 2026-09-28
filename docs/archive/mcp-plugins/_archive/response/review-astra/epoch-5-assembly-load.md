# Response: review-astra/epoch-5-assembly-load.md

## review-astra/E5-01

### Reviewer claim

Любое имя вне четырёх семейств хоста идёт только через `AssemblyDependencyResolver`. `null` от resolver — пропуск плагина. `System.Runtime` так не находится, и минимальный плагин не грузится.

### Related findings

Conflicts with чтением «вернуть null на любой miss»: fallback в контекст по умолчанию нужен для framework, но не для частной зависимости. Это уточнение той же карточки, не спор с другим ревьюером.

### Problem assessment

`ACCEPT`

### Problem rationale

`System.Runtime` не входит в список `RoslynMcpServer` / `ModelContextProtocol` / `Microsoft.CodeAnalysis` / `Microsoft.Extensions`. В `deps.json` обычной class library его пути нет. Проба ревьюера на runtime 10.0.12: resolver вернул `null`, ALC с правилом «нет пути — ошибка» получил `FileLoadException` на `GetType`. Документированный загрузчик плагинов .NET при miss resolver возвращает `null`, и дальше работает разрешение по умолчанию. Буква эпохи 5 отвергает исправный `net10.0` плагин.

### Correction assessment

`ACCEPT`

### Correction rationale

Три стадии, не один `null`:

1. Имена контракта хоста — уже загруженная сборка хоста (как в эпохе).
2. `AssemblyDependencyResolver` — частные managed-зависимости из каталога плагина.
3. `null` от resolver для сборки framework / контекста по умолчанию — не пропуск. Пропуск — когда имя не из этих стадий и файла зависимости нет.

Иначе fallback «на всё» молча подхватит чужой Newtonsoft из хоста. Тесты: минимальный шаблон с вызовом BCL; отсутствующая частная DLL — пропуск.

### Evidence

Эпоха 5, абзац «нет пути — пропуск». Проба ревьюера. Руководство .NET по ALC плагинов. Контракт README: загрузчик отдаёт сборки хоста по имени, остальное — из плагина.

### Classification

`ARCHITECTURE DEFECT`

### Severity

Blocker у ревьюера. Совпадает: без этого эпоха 5 не может загрузить собственный шаблон.

### Required outcome

Шаблон без частных зависимостей грузится. Отсутствующая частная managed-сборка по-прежнему пропуск, а не тихий bind из хоста.

### Proposed specification change

Эпоха 5: заменить «нет пути — пропуск» на три стадии выше. Два теста.

### Downstream consequences

Резолвер ALC. Теневая копия по-прежнему не копирует framework-сборки: их и не нужно копировать, если их берёт контекст по умолчанию.

---

## review-astra/E5-02

### Reviewer claim

Исключение из `Register` оставляет уже добавленные singleton и фабрики тулов. Хост либо падает на валидации DI, либо публикует тулы плагина со статусом skipped.

### Related findings

Duplicate of `review-ds/E5-01` по проблеме. Correction ds допускает третье состояние «загружен частично»; эта карточка требует commit/discard. Complements `review-astra/E1-01`.

### Problem assessment

`ACCEPT`

### Problem rationale

Эпоха 5 сама пишет, что откатить добавленное нечем, и одновременно обещает «пропущен». `AddToolsFrom` кладёт фабрики в общий `IServiceCollection`. Проба ревьюера: singleton с отсутствующей зависимостью конструктора, catch, затем `Build` в Development падает `AggregateException`. Даже без валидации оставшиеся дескрипторы тулов попадут в `GetServices<McpServerTool>()`. Это противоречит README: битый плагин не роняет сервер и не должен выглядеть загруженным.

### Correction assessment

`ACCEPT`

### Correction rationale

Регистрация одного `Register` транзакционна относительно контейнера хоста: контекст копит дескрипторы и имена, в общий `IServiceCollection` они попадают только после успешного `Register` и успешного создания SDK-тулов (`review-astra/E1-01`). При исключении буфер выбрасывается, имена освобождаются для следующего плагина. Откат произвольных побочных эффектов пользовательского кода (файлы, статика) не обещается. Третье состояние «частично загружен» не принимается: оно оставляет тулы в `tools/list` при статусе пропуска (`review-ds/E5-01`).

### Evidence

Эпоха 5, абзац «откатывать нечем». Эпоха 1, `AddToolsFrom` пишет в `Services`. README, «один битый плагин». Проба Hosting 10.0.8 у ревьюера.

### Classification

`ARCHITECTURE DEFECT`

### Severity

High у ревьюера. У ds та же дыра оценена Medium. Беру High: исход либо падение процесса, либо тул, которого «нет» в отчёте.

### Required outcome

После исключения `Register` в контейнере хоста нет сервисов и тулов этого плагина. Следующий плагин может занять то же имя тула. Процесс стартует.

### Proposed specification change

Эпоха 5: убрать «откатывать нечем». Описать буфер и commit. Тест: throw после `AddSingleton` и `AddToolsFrom`, затем успешный второй плагин с тем же именем тула.

### Downstream consequences

Форма `RoslynMcpPluginContext`: `Services` внутри `Register` не является общим списком до commit. Это видимое изменение контракта автора плагина только в момент исключения; при успехе набор тот же.

---

## review-astra/E5-03

### Reviewer claim

Копирование `runtimes/` не задаёт поиск native DLL. P/Invoke не идёт через `AssemblyLoadContext.Load(AssemblyName)`, без `LoadUnmanagedDll` файл в копии не находится.

### Related findings

Complements `review-astra/C-01`: managed-пакет и native-пакет — разные дыры доставки.

### Problem assessment

`PARTIALLY ACCEPT`

### Problem rationale

Принимается: эпоха 5 описывает только managed resolve, эпоха 3 копирует дерево, включая `runtimes/<rid>/native`, а тест кладёт `native.bin` и никуда его не вызывает. Authoring говорит, что свои пакеты попадают в выход и годятся загрузчику. Для пакета с native это ложное обещание: `DllNotFoundException` на первом P/Invoke.

Не принимается как обязательная реализация `LoadUnmanagedDll` в этой серии. Исходное требование — test-impact на общем `Solution` и Roslyn, без native-пакетов. Копирование дерева нужно, чтобы не выкинуть managed-зависимости и content, а не как контракт RID-поиска.

### Correction assessment

`MODIFY`

### Correction rationale

Явное ограничение v1 вместо загрузчика native. Authoring и эпоха 5 говорят: native-ассеты могут лежать в копии, P/Invoke по ним не резолвится, такой пакет не поддерживается. Тест с настоящим native-вызовом в серию не входит. Если позже понадобится native, это отдельная эпоха с `LoadUnmanagedDll` и RID, а не молчаливое следствие `File.Copy`.

### Evidence

Эпоха 3, рекурсивное копирование и тест `native.bin`. Эпоха 5, только `AssemblyName`. Обсуждение серии: шов для чужого test-impact, выгрузка ALC и песочница вне концепции; native-резолвер не требовался. Документация .NET, на которую ссылается ревью, подтверждает отдельный unmanaged hook.

### Classification

`DOCUMENTATION DEFECT`

### Severity

High у ревьюера. Здесь Medium: ломается класс пакетов, которого серия не обещала, но текст сейчас выглядит так, будто копирование достаточно. High была бы уместна, если оставить текст и не написать отказ.

### Required outcome

Читатель не считает наличие файла в `runtimes/` гарантией P/Invoke. v1 не добавляет unmanaged resolver.

### Proposed specification change

`authoring.md` и эпоха 5: одно предложение ограничения. Эпоха 3: комментарий у теста `native.bin`, что это проверка копирования файла, не загрузки.

### Downstream consequences

Нет нового API. Пакет с native остаётся за границей серии.
