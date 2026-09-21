# Явный раннер тестов

Дата: 2026-09-22. Статус: **план, код не начат**.

Агент по-прежнему вызывает `run_dotnet_test`, `run_specific_test` и `run_test_by_filter`. Версию xUnit он не выбирает. Ось — раннер, которым `dotnet test` печатает итог.

| Значение | Кто так запускается |
|---|---|
| `auto` (молчание параметра то же самое) | Раннер читается из проекта. Неясный или смешанный набор — ошибка до процесса, без запасного прогона |
| `vstest` | xUnit v2. xUnit v3 с `xunit.runner.visualstudio` и без Microsoft Testing Platform (`xunit.v3.mtp-off` и тот же режим). NUnit и MSTest |
| `mtp` | Стоковый `xunit.v3` и линейка 4.x, где раннер — Microsoft Testing Platform |

v3 бывает и тем, и другим. Параметра «версия пакета» в этой серии нет. `auto` не подставляет v3 или v4: он выбирает раннер по правилам эпохи либо останавливается.

Канон эпохи: [epoch-1-explicit-runner.md](epoch-1-explicit-runner.md).

Тестовый проект этого репозитория остаётся на xUnit v2 (`xunit` 2.9.3). Эта серия его на Microsoft Testing Platform не переводит. Для этого репозитория `auto` обязан сойтись на VSTest.

## Что серия не делает

- Не оценивает условия MSBuild и не идёт по произвольным `Import`.
- Не гоняет один `.sln` / `.slnx` двумя раннерами и не склеивает два отчёта в один `Passed`.
- Не запускает `.sln` на MTP. Однородный MTP-набор — ошибка с просьбой передать один `.csproj`.
- Не меняет `get_test_list`, `generate_test_method_stub`, шарды `.github/workflows/test-suite.yml` и пакеты `RoslynMcpServer.Tests`.
