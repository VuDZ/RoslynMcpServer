# Coverage

## Reviewed target

Повторное ревью программы Rapid TIA по текущему `docs/rapid-tia/README.md` на `83635e1` (`docs: review rapid TIA plan round 3`). Нормативный текст — состояние после ответов на раунд 2. С ним сверены `docs/rapid-tia/review-round-2-responses.md` и разделы, на которые эти ответы ссылаются.

Каталог этого раунда: `docs/rapid-tia/review-grok-round-3/`. Рецензент тот же, Grok 4.7. Каталоги раундов 1 и 2 не переписывались.

## Context inspected

- `docs/rapid-tia/README.md` целиком.
- `docs/rapid-tia/review-round-2-responses.md` — что автор считает закрытием находок раунда 2.
- `docs/rapid-tia/review-grok-round-2/` — только как список собственных утверждений прошлого раунда, которые надо подтвердить или оставить открытыми. Новых выводов из формулировок того каталога не извлекалось.
- `docs/test-runner-selection/epoch-1-explicit-runner.md` — команда `mtp`, предварительная сборка, `noBuild`, прогон проекта без фильтра, четыре допустимые строки `run_test_by_filter`.
- `Tools/TestTools.cs` — `FilterTestOmitSkipsRebuild`, `DotNetTestOmitSkipsRebuild`, отказ `RunTestByFilter` при пустом фильтре.
- `Services/Build/TestBuildRoute.cs` — опущенный `noBuild` становится флагом вызывающего инструмента.

## Context not inspected

- `docs/rapid-tia/review-codex/` не читался. Учёт ревью в README называет номера находок Codex; их тексты evidence не были.
- Защита и арбитраж. Отдельного каталога арбитража нет.
- `docs/rapid-tia/validation-2026-10-30.md` в этом раунде заново не читался: ответы раунда 2 на V-01…V-10 не опираются, а проверка автора независимым ревью не является.
- Живой `dotnet test` на xUnit v3 MTP не запускался. Оценка `S` в 1–3 с взята как утверждение программы, не как измерение.

## Source/code verification

Подтверждены два разных значения опущенного `noBuild` у `run_test_by_filter` и `run_dotnet_test` и то, что пустой фильтр до процесса не доходит. Реализации Rapid TIA по-прежнему нет, её тесты не запускались.

## Coverage limitations

Транзитивность в F-001 — развилка формулировки. Код `ProjectDependencyGraph` в репозитории не вызывается; имя метода в находке — парный к тому, который README уже называет для обратного направления. Поведение «прямые ссылки против замыкания» из каталога программы не различается, отдельный прогон это не проверял.

Последовательность сборки в F-002 сверена с текущими инструментами и с эпохой раннера. Как именно будущий `estimate_affected_tests` вызовет сервис, в коде смотреть не на что.
