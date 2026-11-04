# task-03 — независимое ревью, раунд 18

Дата2026-11-04. Codex, новая независимая сессия; runtime model ID недоступен. База1b617c4, eight-file freeze565.

Вердикт **MUST-FIX** — refinement R14-001, не новый unique finding ID.

## Exact entry не доказывает, что исчезнувший путь был файлом

QueueDiskPath пропускает descendant enumeration при любом map.Find(fullPath)!=null. Builder индексирует optional walk-up candidates через File.Exists(candidate). Допустимый каталог App/Directory.Build.props/Sub/A.cs может содержать Known C# документ, а сам Directory.Build.props иметь EvaluationInput entry с Present=false.

При Deleted такого каталога shortcut пропускает A.cs; журналируется лишь .props path, graph-stale не поднимает baseline gap. Требуется short-circuit только при подтверждённом exact file entry (Present=true) и regression для optional candidate directory. Обычные Known file deletions сохраняют быстрый путь. Strict descendants должны исключать exact self entry, не создавая recursion loop; outer enumeration уже охватывает все mapped descendants.

Остальные C# scope, transitive closure, missing-root Error routing и TDD приняты. Рецензент не запускал runtime и не редактировал файлы.

[Все main этого кандидата прошли](task-03-validation-before-round-19.md), но приёмка не объявляется из-за известного пропуска C# входа. После минимальной правки нужны свежие проверки и независимое ревью.
