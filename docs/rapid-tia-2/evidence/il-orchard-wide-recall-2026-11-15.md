# Сверка `recall` на `orchard-wide`, 2026-11-15

Шаг 5 [rapid TIA 2](../README.md). Клон `LOCAL_ROOT/orchard-wide`,
SHA `b9c4b2f23e56ef11fbdbd28603c871d1b0fc9deb` (тег `v3.0.1`).
Спайк — `bdff432`. Сводка без имён: [recall-summary](il-orchard-wide-recall-2026-11-15-summary.json).
`recall-raw.json` и каталог `classes` остаются в `LOCAL_ROOT/orchard-class-2026-11-15`
и `LOCAL_ROOT/orchard-recall-2026-11-15`. Debug-вывод шага 4 не пересобирался.
`--bin` — `src` и `test` клона.

## Команда

`dotnet-coverage` 18.12.0 взят из `LOCAL_ROOT/tools`.
`--runner mtp` передаёт exe ключ `--filter-class`. Хост корпуса — xUnit.net v3 In-Process Runner 3.2.2,
он этот ключ не знает: пробные три класса завершились с кодом 3 и «0 files executed».

Рабочая строка — `--run`:

```text
dotnet-coverage collect -f cobertura -o "{report}" -- "{exe}" -class "{class}"
```

Пробный прогон `--limit 3` с этой строкой. `ThemeAttributeTests`: `Total: 8` при 40 тестах сборки
`OrchardCore.Abstractions.Tests`, «7 files executed».

Полный прогон, 8 мин 30 с:

```text
dotnet TestImpactIlProbe.dll class-coverage --root LOCAL_ROOT/orchard-wide --bin LOCAL_ROOT/src --bin LOCAL_ROOT/test --out LOCAL_ROOT/classes --run "dotnet-coverage collect -f cobertura -o "{report}" -- "{exe}" -class "{class}""
```

Повтор того же вызова: классов без отчёта 0. Затем `recall` с теми же `--root` и `--bin`
и `--classes` равным каталогу `--out`.

## Классы

В графе 177 тестовых классов, отчёт есть у 177. Три класса не исполнили ни одного production-файла:
`SubResourceIntegrityTests` (в отчёте только `CIFact.cs`), `AwsFileStoreTests` (только файл самого теста),
`TablePrefixGeneratorTests` (только файлы тестового проекта). Классов без отчёта нет.
В именах классов нет `+`; ключ `-class` вложенные классы этим прогоном не проверялся.

Сумма по логам: 2373 теста, 16 упали, 20 пропущены — те же числа, что в сверке покрытия шага 3.

- 16 падений — функциональные тесты, фикстура Playwright: браузер не установлен.
  Хост к этому моменту уже слушает порт, поэтому Cobertura всё равно записывает файлы старта:
  у классов CMS по 57 файлов, у `MvcTests` — 110. Тела тестов не исполнялись.
- 19 пропусков `AwsFileStoreTests` — нет `S3_EMULATOR_URL`.
- 1 пропуск `SavedSubResourceIntegritiesShouldMatchCurrentResources` — `CIFact`, локально без переменной `CI`.

## Recall

Исполняемых файлов 1572, пар «файл и исполняющий его класс» 24 734.
`recall` слоя `discoveryWithLoads` в сводке равен 1: одна потерянная пара из 24 734
на четырёх знаках округляется до 1. В таблице ниже у этого слоя `filesLosing` равен 1.

| Слой | recall | recallByMethods | filesLosing | filesWithEmptyAnswer | precision |
| --- | --- | --- | --- | --- | --- |
| уровень 0 | 87,58% | 87,35% | 188 | 188 | 2,91% |
| `references` | 37,77% | 37,60% | 1030 | 800 | 34,67% |
| `discovery` | 38,20% | 37,92% | 1016 | 785 | 23,68% |
| `wholeAssembly` | 39,16% | 38,99% | 1004 | 763 | 23,14% |
| `referencesWithLoads` | 99,98% | 99,98% | 3 | 0 | 10,69% |
| `discoveryWithLoads` | 100% | 100% | 1 | 0 | 6,98% |

`discovery` держит 38,2% пар. Пустой ответ остаётся у 785 исполняемых файлов,
ещё 231 файл имеет неполный ответ. Слои с загрузкой по имени поднимают recall до 100%
и опускают precision до 10,7% и 7,0%.

Чаще всего `discovery` теряет классы API-тестов сборки `OrchardCore.Tests`, которые поднимают хост.
Первые строки `lost.byTestClass`:

- `BlogPostTests` — 819 файлов;
- `ContentStepLuceneQueryTests` — 809;
- `BlogPostCreateDeploymentPlanTests` и `BlogPostUpdateDeploymentPlanTests` — по 801;
- `BlogPostContentStepIdempotentTests` — 797;
- `RecentBlogPostsQueryTests` — 752;
- `AccountControllerTests` — 745;
- `DefaultContentManagerTests` — 742.

Примеры в этих строках начинаются с `src\OrchardCore.Modules\OrchardCore.AdminMenu`:
список файлов класса идёт по пути. Среди файлов, которые теряют больше всего классов,
верх `lost.byFile` — сборки `OrchardCore.Users`, `OrchardCore.DisplayManagement.Liquid`,
`OrchardCore.Roles`, `OrchardCore.Localization.Core`, `OrchardCore.Settings`, `OrchardCore.Tenants`.

У `discoveryWithLoads` остаётся одна строка: `HttpsSettingsMigrationsTests`, один файл
`src\OrchardCore.Modules\OrchardCore.Https\Migrations\HttpsSettingsMigrations.cs`, сборка `OrchardCore.Https`.
