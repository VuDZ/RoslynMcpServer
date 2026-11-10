# Ревью реализации прототипа task-01, раунд 2

- Дата: **2026-11-10**.
- Рецензент: `claude-opus-5-5`, сессия с владельцем.
- Проверенная версия: **`7cb6291`** — исправления Grok 4.7 по [раунду 1](round-1.md).
- Scope: закрытие I-01…I-07 и сквозные прогоны `oracle`. Новое замечание — только I-08, найденное этими прогонами.
- Итог: **changes required**. I-01, I-02, I-03, I-05, I-06, I-07 закрыты. I-04 исправлен в коде, но на настоящем
  multi-TFM тестовом проекте не срабатывает из-за **I-08, Blocker**. Следующий раунд — третий, последний по
  [R-18](../../README.md#решения-владельца-2026-10-29).

## Что проверено

- `dotnet test spikes/AffectedTestsProbe/AffectedTestsProbe.Tests/AffectedTestsProbe.Tests.csproj -c Release` —
  **19/19 PASS** (Linux, SDK 10.0.114), включая новый сквозной `OracleEndToEndTests`.
- Сквозные прогоны `oracle` на маленьких solution вне репозитория, в git-клоне, с проверкой отката (`git status`
  чистый после каждого прогона):
  - `Prod`: `netstandard2.0;net10.0`, `Calc.Add` и поле `public int Seed = 1 + 1;`;
  - `Tests` (xUnit v2): `Adds` и `Rows` (две `InlineData`) вызывают `Add`, `Seeded` читает `Seed`, `Unrelated` —
    ничего; вариант А — `net10.0`, вариант Б — `net10.0;net9.0` (`RollForward=Major`).

| Прогон | F-CHA = F-RTA = R-CHA | O | Неотображённые | Пара |
|---|---|---|---|---|
| А, `M:Prod.Calc.Add(...)` | `Adds`, `Rows` | `Adds`, `Rows` | 0 из 5 | Valid |
| А, `F:Prod.Calc.Seed` | `Adds`, `Rows`, `Seeded` | `Adds`, `Rows`, `Seeded` | 0 из 5 | Valid |
| Б, `M:Prod.Calc.Add(...)` | — | — | — | `No in-scope level-0 test project was found.` |
| Б, с локальной правкой I-08 | `Adds`, `Rows` × net10.0, net9.0 | то же | 0 из 10 | Valid |

`Seed` достигается и из `Adds`/`Rows`: инициализатор поля входит в каждый конструктор `Calc`. Это верно и для эталона.

## Закрытые замечания

- **I-01 — закрыто.** Вставка строится разбором текста (`ParseStatement`/`ParseExpression`) и нормализуется только
  она. Мутации метода и инициализатора поля компилируются, тесты падают, как ожидалось.
- **I-02 — закрыто.** `testId` → FQN берётся из всех `TestExecution.StatsChange` и `LastRunTests` того же `--diag`.
  Неотображённых 0, O совпадает с ожидаемым, строки `Rows` сворачиваются в одну единицу.
- **I-03 — закрыто.** `MemberLocations` собирает все ключи, где объявлен член; F-\* и R-CHA объединяются. В
  варианте Б с правкой I-08 тесты `net9.0` доходят до узла `netstandard2.0`, `net10.0` — до узла `net10.0`, и оба
  попадают в предсказание.
- **I-05 — закрыто.** При негодной по процессу паре `judgment` не печатается.
- **I-06 — закрыто.** Ноль выполненных проверяется у обоих прогонов, есть причина `crash`, таймаут — 3× контроля
  этого TFM, не меньше 5 мин. Ограничение: `crash` ставится, только если артефактов нет; падение testhost после
  частичной записи TRX уходит в `missing results`. Пара всё равно негодна, отдельным замечанием не считаю.
- **I-07 — закрыто.** Модель-исполнитель указана.
- **I-04 — код исправлен, проверка не проходит из-за I-08.** Отдельный `dotnet test -f` на каждый TFM со своими
  `diag`/`trx` и отображение только на единицы этого TFM работают — это видно в прогоне Б с локальной правкой.

## I-08, Blocker — имя multi-TFM проекта без пробела

`Model/ProjectKey.cs` (`TargetFrameworkOf`) и `Graph/RootIndexBuilder.cs` (`NameMatches`) ищут суффикс `" ("`.
MSBuildWorkspace (Roslyn 5.9.0) называет такие проекты **без пробела**. Проверено на solution варианта Б:

```
[Prod(netstandard2.0)]
[Prod(net10.0)]
[Tests(net10.0)]
[Tests(net9.0)]
```

Последствия:

- `--test-project Tests` не совпадает ни с `Tests(net10.0)`, ни с `Tests(net9.0)`. Корней нет: `summary` даёт
  `tests: 0`, `oracle` — `No in-scope level-0 test project was found.` Любой multi-TFM тестовый проект выпадает из
  scope; на Roslyn это значит, что замер на таких проектах не запустится.
- `TargetFrameworkOf` возвращает `unspecified` для всех TFM, `ProjectMap` разводит коллизию суффиксом
  `unspecified#<guid>`. Ключи и `outsideScope` получают бессмысленный TFM, а `DotNetTest` передал бы
  `-f unspecified#<guid>`.

Тесты этого не ловят: `MultiTargetPredictionTests` заводит in-memory проекты с именами `"Lib (netstandard2.0)"`, то
есть с форматом, которого MSBuildWorkspace не выдаёт.

Нужно:

- разбирать суффикс `(tfm)` с пробелом и без, в одном общем помощнике для `ProjectKey` и `NameMatches`. Локальная
  правка в две строки (`LastIndexOf('(')` + `TrimEnd()`) дала результат из последней строки таблицы выше; в
  репозиторий она не вносилась;
- в `MultiTargetPredictionTests` использовать реальный формат `Lib(net10.0)`, плюс тест разбора обоих форм;
- в сквозной тест добавить `Prod` с `netstandard2.0;net10.0`: это работает и с одним установленным runtime и
  проверяет, что TFM в ключах и `targets` — настоящие, а не `unspecified`. Если на машине есть второй runtime или
  разрешён `RollForward=Major`, — ещё и multi-TFM тестовый проект.
