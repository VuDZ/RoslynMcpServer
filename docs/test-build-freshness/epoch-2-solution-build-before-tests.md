# Эпоха 2. Сборка через Solution перед запуском DLL

Статус: **не начата**. Требует [эпохи 1](_archive/epoch-1-session-state.md) и согласованного решения F-01.

## Цель

Три тестовых инструмента используют общую автоматическую проверку актуальности
на DLL-маршруте и предварительную сборку целевого проекта через Solution.

## Работа

- Сохранить существующее однозначное сопоставление `.csproj` + `binariesPath`
  → `AssemblyName.dll`; не угадывать проект только по имени чужой DLL.
  Неоднозначный project target или отсутствующий Solution — ошибка до процесса.
- Вынести общий этап ensure-build-current, вызываемый `run_dotnet_test`,
  `run_specific_test`, `run_test_by_filter`. Учесть разные исторические
  defaults `noBuild` при реализации выбранного публичного контракта.
- Dirty/unknown собирается через загруженный `.sln`/`.slnx` с project target.
  Прямой build `.csproj` как запасной маршрут не использовать: сценарий требует
  solution context. Передавать фактические Configuration/Platform, TFM и
  значимые session buildArgs без расхождения ключа и команды.
- Тестовому процессу передавать DLL и режим без build. `noRestore` относится
  к согласованной команде предварительной сборки; restore/test steps не должны
  запускаться вторично из-за автопроверки. Сохранять SDK pinning и workdir rules.
- На build failure/timeout/cancel завершать ответ без тестового запуска.
  Общий `timeoutSeconds` расходуется на проверку, build и tests; retry не
  получает новый полный бюджет.
- После success сверять ожидаемый путь outputs и input revisions. Не
  подменять требуемую DLL случайным существующим файлом другой конфигурации.
  Сверять общий pull snapshot своей generation, membership revision и pending
  events; callback subscriber для этого не нужен. Unknown coverage сохраняет
  always-build для следующего запуска даже после успешного fallback build.
- Прогресс и отчёт объясняют решение: unknown/dirty/context changed/output
  missing/coverage unknown либо подтверждённый reuse, цель solution build
  и реально выбранную DLL. Парсер тестов не получает build log.

## Приёмка

- Первый DLL-запуск строит solution target даже при существующей DLL.
- Повторный запуск в неизменной подтверждённой сессии с complete coverage
  и валидированными inputs/outputs пропускает build. При unknown — снова build.
- Изменение target/dependency/shared inputs вызывает build нужного target.
- Project-only build fixture намеренно неработоспособен; solution-target
  build успешен, после него tests идут по DLL. Проверяются команды и реальный
  результат, а не только совпадение строки аргументов.
- Ошибка/неоднозначность mapping, неизвестный context, отсутствующие outputs
  и build failure не запускают тесты по старой DLL.
- Отдельно проверены все три адаптера, filters и вариант `binariesPath`.
- XAML edit до первого freshness consumer сохранился в общем snapshot, даже
  после semantic sync. Bootstrap/прочтение snapshot не обнуляет revisions.
  Отсутствующий reverse-index entry и неполный metadata-only graph дают
  консервативное решение, а не skip.

## Результат

Рабочий DLL-маршрут с общей проверкой состояния. Исторические non-DLL маршруты
и отдельная серия выбора раннера сохраняют заявленный контракт. Реализация
ещё требует race/fault приёмки эпохи 3 перед заявлением о завершении серии.
