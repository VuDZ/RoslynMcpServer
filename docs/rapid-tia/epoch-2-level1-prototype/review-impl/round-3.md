# Ревью реализации прототипа task-01, раунд 3

- Дата: **2026-11-10**.
- Рецензент: `claude-opus-5-5`, сессия с владельцем.
- Проверенная версия: **`cc44982`** — исправление Grok 4.7 по I-08 из [раунда 2](round-2.md).
- Scope: закрытие I-04 и I-08, повтор сквозных прогонов `oracle` раунда 2. Это последний раунд по
  [R-18](../../README.md#решения-владельца-2026-10-29).
- Итог: **ACCEPT** в scope I-01…I-08. Прототип годен для замеров task-01. Новое замечание I-09 (Low) касается только
  окружения сквозного теста. По R-18 оно не повод для нового раунда: прототип не теряет тесты молча и не даёт неверного
  ответа.

## Что проверено

- Правка: общий помощник `Model/ProjectNameSuffix.cs` разбирает `Name(tfm)` и `Name (tfm)`. Его используют
  `ProjectKey.TargetFrameworkOf` и `RootIndexBuilder.NameMatches`. Других разборов суффикса в коде прототипа нет.
  `MultiTargetPredictionTests` перешёл на настоящий формат `Lib(net10.0)`. Добавлен `ProjectNameSuffixTests`, а
  `OracleEndToEndTests` теперь собирает `Prod` с `netstandard2.0;net10.0` и `Tests` с `net10.0;net9.0`.
- `dotnet test spikes/AffectedTestsProbe/AffectedTestsProbe.Tests/AffectedTestsProbe.Tests.csproj -c Release`
  (Linux, SDK 10.0.114, установлен только runtime 10.0): **23/24**, падает `OracleEndToEndTests` — см. I-09. С локально
  добавленным в этот тест `<RollForward>Major</RollForward>` — **24/24 PASS**. Правка в репозиторий не вносилась.
- Тот же набор на `cc44982` без правок, с .NET 9: через `dotnet-install.sh` в каталог пользователя поставлены SDK
  10.0.114, runtime 10.0.14 и 9.0.22, запуск с `DOTNET_ROOT` и `PATH` на этот каталог. Результат — **24/24 PASS**.
  Сквозной тест требует `controlExecuted = 8` и оба TFM в O, значит testhost `net9.0` поднялся на runtime 9.0.
- Сквозные прогоны `oracle` на solution раунда 2 вне репозитория, в git-клоне. После каждого прогона `git status` не
  показывал изменённых файлов:
  - `Prod`: `netstandard2.0;net10.0`, `Calc.Add` и поле `public int Seed = 1 + 1;`;
  - `Tests` (xUnit v2): `Adds` и `Rows` (две `InlineData`) вызывают `Add`, `Seeded` читает `Seed`, `Unrelated` не
    вызывает ничего.

| Прогон | `Tests` | F-CHA = F-RTA = R-CHA | O | Выполнено / неотображённые | Пара |
|---|---|---|---|---|---|
| `M:Prod.Calc.Add(...)` | `net10.0;net9.0`, `RollForward=Major` | `Adds`, `Rows` × net10.0, net9.0 | то же | 10 / 0 | Valid |
| `F:Prod.Calc.Seed` | то же | `Adds`, `Rows`, `Seeded` × net10.0, net9.0 | то же | 10 / 0 | Valid |
| `M:Prod.Calc.Add(...)` | `net10.0;net9.0` без `RollForward` | `Adds`, `Rows` × net10.0, net9.0 | — | 5 / 0 | `zero executed`, `judgment` не печатается |
| `M:Prod.Calc.Add(...)` | `net10.0` | `Adds`, `Rows` | `Adds`, `Rows` | 5 / 0 | Valid |

`targets` для multi-TFM — `net10.0` и `net9.0`. Ключи F-\*, R-CHA и O содержат настоящие TFM, `unspecified` в них
нет, `outsideScope` пуст. У single-TFM тестового проекта ключ — `unspecified`, как задумано, и `-f` не передаётся.

## Закрытые замечания

- **I-08 — закрыто.** Multi-TFM проекты MSBuildWorkspace попадают в scope по `--test-project Tests`. TFM в ключах и
  `targets` настоящие.
- **I-04 — закрыто.** Каждый TFM гоняется отдельным `dotnet test -f` со своими `diag`/`trx`, cases отображаются на
  единицы своего TFM. Неотображённых 0 из 10.
- I-01…I-03, I-05…I-07 закрыты в [раунде 2](round-2.md). Повторные прогоны их не опровергли.

## I-09, Low — сквозной тест требует runtime net9.0

`AffectedTestsProbe.Tests/OracleEndToEndTests.cs`: тестовый проект `Tests` нацелен на `net10.0;net9.0` без
`RollForward`. Если runtime 9.0 не установлен, testhost `net9.0` не стартует. Прототип при этом отвечает верно: пара
помечается `zero executed`, `judgment` не печатается. Но тест падает на `invalid`, поэтому набор прототипа красный на
машине только с .NET 10. Это как раз то условие, которое в раунде 2 было названо предпосылкой второго TFM тестов.

С установленным runtime 9.0 тест проходит без правок (см. «Что проверено»), так что дефекта в коде теста нет — есть
неявная зависимость от окружения.

Нужно одно из двух: добавить `<RollForward>Major</RollForward>` в `Tests.csproj` этого теста (правка в одну строку,
проверена локально, 24/24) или явно записать runtime 9.0 в требования к запуску прототипа. Новый раунд ревью не
нужен.

## Ограничения, не названные замечаниями

- Имя single-TFM проекта, которое само кончается скобками (`Foo(Legacy)`), будет разобрано как TFM `Legacy`. Тогда
  `dotnet test -f Legacy` не соберётся, и пара станет негодной по сборке. Это видимый отказ, а не тихая потеря.
- Прогоны на Roslyn, NUnit/MSTest и Windows не делались — это работа самих замеров task-01.
