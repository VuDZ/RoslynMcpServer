# task-03 — независимое ревью, раунд 15

- Дата: 2026-11-04.
- Рецензент: Codex, новая независимая сессия; runtime model ID недоступен.
- База1b617c4, первоначальный R14-001 freeze: SolutionManager, WorkspaceDiskWatcherStarter, BaselineDirectoryDisappearanceTests.
- Вердикт: **MUST-FIX**. Task-03 не принята; R14-001 остаётся открытым до завершения исправления и обязательной validation.

## Обязательная доработка

WorkspaceDiskWatcherStarter.SubscriptionsToStart строит root lifetime guards для всех descriptor roots, включая чистые metadata/output SDK-каталоги. Каждый непокрытый ancestor добавляет отдельный FileSystemWatcher, увеличивая потребление ресурсов сверх C# baseline-сценариев. Parent Linux main дал1736/1738: inotify user limit128 при создании plugin host и неожиданно затронутый timestamp guard. Требуется ограничить новые lifetime seeds, сохранив обычные content watchers и защиту linked roots, затем повторить validation при штатных лимитах.

Согласован минимальный scope: recursive membership seeds сохраняются; nonrecursive seeds исключаются только при непустом Roles, состоящем целиком из MetadataDependency/Output. Unknown/empty/mixed и остальные роли сохраняются. Обычные metadata/output content watchers остаются и могут быть parent boundary. Полная lifetime coverage чистых metadata/output roots не заявляется; эти пути сами по себе не являются C# baseline document keys.

## Подтверждённая часть и границы

Остальная реализация R14-001 и RED-хронология приняты. Descendant expansion выполняется под прежним session gate и exact-map short circuit; guards узкие и nonrecursive, закрываются прежним disposal. При обычном recursive deletion gap не вводится.

После остановки suite диагностическое чтение /proc показало max_user_instances128, max_user_watches524288, fdlimit10240, uid1000 и0 видимых inotify instances. Посторонние процессы не завершались, лимиты и параллелизм не менялись. Точный пиковый вклад новых guards и причинность второго failure ещё не измерены: это гипотеза, а не доказанная причина обоих failures.

[Исходные результаты validation](task-03-validation-before-round-16.md). Рецензент не запускал runtime и не менял файлы. Новая доработка относится к R14-001, отдельный unique finding ID не добавляется.
