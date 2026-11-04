# task-03 — независимое ревью, раунд 19

Дата2026-11-04. Codex, новая независимая сессия; runtime model ID недоступен. База1b617c4, eight-file freeze570.

Вердикт **MUST-FIX** — следующий prefilter bypass внутри R14-001, не новый unique finding ID.

QueueDiskPath подавляет IsWpfTemporaryProject(fullPath) перед обработкой Deleted descendants. Допустимый каталог App/Scratch_wpftmp.csproj/Sub/A.cs может содержать Known Compile input. При выносе всего Scratch_wpftmp.csproj за recursive App tree приходит Deleted каталога; ранний WPF return теряет A.cs без journal/gap. App root существует, поэтому missing-root Error routing не помогает.

Требуются regression и обработка mapped descendants до подавления самого temporary-project root, с сохранением graph/classification suppression настоящих временных файлов и mapless callbacks. Этот file-name фильтр не является доказательством, что исчезнувший путь был файлом.

Present=true-only shortcut, strict self exclusion и однократный nested обход приняты. Остальные C# lifetime scope и missing-root Error guards приняты. Рецензент runtime не запускал и файлы не менял.

[Main suites кандидата прошли](task-03-validation-before-round-20.md), но задача не принимается при известном C# loss. После исправления требуется fresh review и финальная validation.
