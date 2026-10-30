# Coverage

## Reviewed target

Повторное ревью программы Rapid TIA после `977b546` (`docs: address rapid TIA plan review round 1`). Полностью прочитан обновлённый `docs/rapid-tia/README.md`. С ним сверены `docs/rapid-tia/review-round-1-responses.md` и `docs/rapid-tia/validation-2026-10-30.md`: это ответы и проверка автора, которые в коммит `c6f7f85` не входили.

Каталог этого раунда: `docs/rapid-tia/review-grok-round-2/`. Рецензент тот же, Grok 4.7. Раунд 1 в `review-grok/` не переписывался.

## Context inspected

- Раунд 1 `docs/rapid-tia/review-grok/` — только как список собственных прошлых утверждений, которые надо подтвердить или оставить открытыми. Новых выводов из него не извлекалось.
- `docs/test-runner-selection/epoch-1-explicit-runner.md` — команда `mtp` и допустимые строки `run_test_by_filter`.
- `Services/Workspace/StateCache/Models/SnapshotProjectKey.cs` — путь, конфигурация, платформа, target framework. Формулировка ключа проекта в разделе «Хранение» с типом совпадает.
- `Services/Build/DotNetCliRunner.cs` — процесс с `UseShellExecute = false`.
- Ветка `origin/usage-statistics`, `Config/ServerUserDirectories.cs` и тесты к нему: `StateDirectory` на Linux — `$XDG_STATE_HOME` / `~/.local/state/roslyn-mcp`, на Windows — `%LOCALAPPDATA%\RoslynMcpServer`, при `ROSLYN_MCP_HOME` — заданный каталог. На этой ветке типа нет; сверка через `git grep` по `origin/usage-statistics`.

## Context not inspected

- `docs/rapid-tia/review-codex/` по-прежнему не читался. Сводки находок Codex в `review-round-1-responses.md` использовались только как указатель, какой абзац README автор считает закрытием, и проверялись по тексту README.
- Защита и арбитраж. Отдельного каталога арбитража нет.
- Полный обход раннера MTP в коде: программа test-runner-selection ещё без реализации, норма взята из её эпохи 1.
- Поведение `FileTextLoader` отдельным экспериментом не проверялось. Раздел «Фиксация baseline текстов» оценен как контракт, не как воспроизведённый прогон.

## Source/code verification

Подтверждены ключ проекта, способ запуска процесса и каталог состояния на `usage-statistics`. Тесты Rapid TIA не запускались: реализации по-прежнему нет.

## Coverage limitations

Не запускался xUnit MTP и не снимался фактический фильтр вложенного класса. Для F-002 достаточно уже записанного контракта `run_test_by_filter`: строка с `|` и строка с точкой на конце класса в перечень разрешённых видов не входят.

Оракул отдельной выборки стенда для правок связывания не восстановлен: в программе сказано, что `throw` её не измеряет, и не сказано, что измеряет. Это отмечено в F-001, отдельным finding не стало.
