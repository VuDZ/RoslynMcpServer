# Актуальность сборки перед запуском тестов по DLL

Дата: 2026-09-30. Статус: **план, код не начат; детали API и проверки открыты**.

Пожелание 2: тесты запускаются передачей готовой DLL. Проект должен собираться
в контексте Solution. Перед тестами MCP проверяет актуальность результата и,
если необходимо, сам собирает целевой проект через Solution.

## Согласованные решения

- После загрузки workspace существующие сборки считаются грязными/неизвестными,
  даже когда DLL уже лежит на диске. Первый тестовый запуск требует сборки.
- Подтверждение успешной сборки хранится только в текущей сессии. Повторный
  запуск может использовать DLL при подтверждённо неизменных значимых входах.
- Restart, reset и фактическая новая загрузка графа сбрасывают подтверждения.
  Повторный cache-hit `load_workspace` той же сессии сам по себе их не сбрасывает.
- Изменения во время сборки, её ошибка или отмена сохраняют необходимость сборки.
- Долговременное сохранение подтверждений и reuse между сессиями — отдельная
  будущая фича. [Workspace load cache](../workspace-load-cache/README.md)
  кеширует загрузку графа, а не автоматически доказывает актуальность бинарников.

## Текущее основание

Проверено на commit `045b839`, source version `1.5.0`.

[TestTools](../../Tools/TestTools.cs) уже поддерживает `.csproj` в `workspacePath`
и каталог DLL в `binariesPath`. По `AssemblyName` выбирается тестовая сборка.
При `noBuild=false` предварительная сборка выполняется через загруженный
`.sln`/`.slnx` и project target, затем `dotnet test` получает DLL.
При `noBuild=true` предварительный build отсутствует, проверяется наличие DLL.
У `run_test_by_filter` текущий default `noBuild=true`, у двух других test tools
— `false`; общую политику нельзя внедрить только в один адаптер.

Основа команд уже есть в [DotNetTestArguments](../../Services/Build/DotNetTestArguments.cs)
и [SolutionProjectTargetResolver](../../Services/Build/SolutionProjectTargetResolver.cs).
Наличие сборки проверяет [TestAssemblyPathResolver](../../Services/Testing/TestAssemblyPathResolver.cs),
но это не проверка её свежести. Прямой `.dll` в `workspacePath` сейчас не
поддерживается: новый формат входа в этой серии автоматически не обещается.

## Контракт актуальности

Единица подтверждения — результат сборки конкретного проекта/project instance
для конкретного build context. Ключ включает общую session generation,
membership revision, Solution, проект, Configuration, Platform, TFM
и значимые buildArgs/выходной путь.
Нельзя получить `current` для одной конфигурации из успешного build другой.

Различать состояния `unknown`, `dirty`, `building`, `current`. Семантическая
очередь dirty документов и build freshness живут независимо. Known unchanged
текст Roslyn не доказывает, что бинарник был собран из него.

Membership, роли и revisions приходят из одного
[pull-снимка входов](../archive/project-input-watching/input-state-contract.md).
Build-state хранит подтверждения build context и cursor этого снимка;
собственный watcher feed, независимо обновляемый input index и второй session
generation не создаются. Внутреннее размещение общего provider в `SolutionManager`
допустимо: запрещено дублирование источника истины, а не приватное поле само по себе.

Алгоритм для автоматического режима:

1. Однозначно связать тестовую DLL с проектом и Solution build target.
2. Получить общий snapshot и проверить актуальный dependency scope и значимые
   входы, включая shared paths. Pending/unresolved event и coverage unknown
   запрещают skip; отсутствие документа не доказывает нерелевантность пути.
3. При `dirty`/`unknown` собрать целевой проект через Solution. Речь об обычном
   build с проверкой актуальности MSBuild, не о безусловном Clean/Rebuild.
4. Подтвердить только результаты, которые действительно получены/проверены
   этим build и относятся к неизменившимся входам выбранного context.
5. Проверить ожидаемые outputs и стабильность revisions, затем запустить тесты
   по DLL без сборки со стороны тестового раннера.

Ошибка build, timeout и отмена запрещают старт тестов по старой DLL. Изменение
между build и стартом тестового процесса требует повторной проверки/сборки
в пределах общего бюджета; при нестабильных входах — явный отказ, без
бесконечного retry. Атомарный snapshot исходников и изоляция внешнего build
не обещаются: обнаруженная гонка всегда сообщает невозможность подтверждения.

## Область изменений и shared файлы

Input edit загрязняет владельцев и проекты, транзитивно зависящие от них.
Сборка A не подтверждает B только потому, что они используют один физический
файл или общую зависимость. Даже успешный solution target не разрешает
очистить весь Solution.

Dependency closure задаёт верхнюю границу возможного подтверждения, но не
доказывает, что все проекты внутри были проверены сборкой: solution mappings,
`BuildProjectReferences=false`, custom targets и metadata-only references
могут менять фактический build scope. При нехватке evidence сохранить unknown.

Пока полнота наблюдения или проверка входов не доказана, безопасный fallback
— предварительный solution build при каждом запуске. Наличие DLL, отсутствие
FSW-событий и timestamp одной DLL по отдельности не дают `current`.

Согласованный W-01 включает XAML и обнаруженные не-C# inputs в общую карту ролей;
автоматическая синхронизация их текста не требуется для сохранения изменения.
Неизвестные membership rules, новый glob-item/import и metadata-only reference
без достаточного evidence оставляют conservative stale/unknown. Открытый W-02
нельзя закрыть отчётом только по урезанному индексу документов.

Состояние non-C# changes сохраняется в сессии до первого consumer и переживает
semantic flush. Начальный unknown уже заставляет первый запуск строиться;
дальнейшие подтверждения сравниваются с сохранёнными revisions, а не с наличием
подписчика в момент события. Успешный fallback build при неизвестном coverage
не делает его complete и не выдаёт `current` для следующего skip. Он позволяет
обычный немедленный DLL-запуск при проверенных context/outputs и отсутствии
обнаруженной гонки; следующий тест снова требует предварительного build.

## Эпохи

1. [Модель подтверждений и проверка входов](epoch-1-session-state.md).
2. [Автоматическая сборка через Solution перед DLL-тестами](epoch-2-solution-build-before-tests.md).
3. [Гонки, ошибки и интеграционная приёмка](epoch-3-races-and-acceptance.md).

Общая карта входов и её pull provider приходят из
[project-input-watching](../archive/project-input-watching/README.md).
Эпоху 1 можно проектировать параллельно на закреплённом input-state contract;
при отсутствии production provider/evidence действует always-build fallback,
а не временная вторая лента событий. Пропуск сборки требует проверенного
покрытия входов. WPF-исключение с тем же session token —
[wpf-temporary-project-watching](../archive/wpf-temporary-project-watching/README.md).
Код C# в любой эпохе требует полного чтения `docs/code-style.md` перед правками.

Детали параметров, полноты профиля и evidence: [UNRESOLVED.md](UNRESOLVED.md).

## Совместимость и завершение

Сборка через MCP не означает автоматический reload семантического Solution
или перезагрузку analyzer assemblies. Same-identity generator rebuild может
по-прежнему требовать нового процесса. Эта серия подтверждает запуск тестов,
а не снимает ограничения analyzer lifecycle.

[Test runner selection](../test-runner-selection/README.md) остаётся отдельной
осью: DLL-маршрут не расширяет MTP support и не добавляет fallback раннер.
Команды, timeout, прогресс и разбор build failures должны использовать общий
production путь. При выпуске обновить descriptions, help, product README,
ARCHITECTURE и версию; сведения о фактических проверках оформить в отчётах эпох.
