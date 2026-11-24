# Этап 0. Версии, зависимости и воспроизводимый baseline

- Рекомендуемые модели: gpt-6.2-astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 3/5.
- Риск ошибки: средний — версия NuGet не определяет автоматически runtime дочернего host и выбранный SDK.
- Необходимые способности: анализ NuGet/MSBuild, протокола процессов, CI и измерений.
- Статус: planned, 2026-11-18.
- Depends on: нет.
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.
- Принятый scope и дата приёмки: —.

Общий контракт: [план перехода](README.md). На этом этапе код и версии пакетов не меняются.

## Работа

1. Зафиксировать SHA, чистоту checkout, OS/architecture, `dotnet --info`,
   `--list-sdks`, `--list-runtimes`, workloads, global.json контрольных решений,
   NuGet sources без credentials и настройки A/B/C/GC. Не использовать dirty checkout
   с несвязанными изменениями как воспроизводимый baseline.
2. Скачать выбранные стабильные пакеты Roslyn (кандидат 5.12.0), записать версии,
   package hash и source commit из nuspec. Убедиться в наличии согласованного набора
   CSharp, CSharp.Workspaces, CSharp.Features, Features, Workspaces.MSBuild и их closure.
   Проверить TFM, dependencies, release notes и breaking changes.
3. Сопоставить BuildHost/Contracts и client с текущим upstream:
   RPC methods/DTO, новые linked compiler files, runtimeconfig/deps, TFM host,
   roll-forward, MSBuild reference assemblies и Locator. Проверить все процессы
   на SDK 10 и 11, не выводить совместимость из TFM одного пакета.
4. Проверить минимум Microsoft.Build.Framework >=18.7.1 нового пакета против
   прямых pins 18.6.3 в сервере и тестах. Составить точный согласованный набор
   Microsoft.Build*, StringTools и Locator; runtime MSBuild по-прежнему поставляет SDK.
   Сохранить `ExcludeAssets=runtime`/PrivateAssets и security pin Pkcs, если
   их изменение не подтверждено составом новой closure и audit.
5. Составить список всех потребителей версий/TFM: сервер, Tests, LifecycleTestHost,
   DeadlockWitnessHost, sample plugin, forks/build, spikes/BuildHostFork,
   package/version assertions, SDK selectors, build/test CLI, CI, release metadata,
   smoke fixture, publish-and-verify, README/ARCHITECTURE и актуальные инструкции.
   Отдельно отметить пользовательские fixtures, где net10/старый SDK сохраняются намеренно.
6. Проверить риски: SQLite identity/native assets, MEF/reflection/internal Roslyn API,
   checksum fallback, MissingSourceFileTextLoader, UnregisteredBuildHostReaper,
   snapshot codec/version fingerprints, source generators, плагины.
   Для каждого обходного пути — доказательство нужности либо тест для безопасного удаления.
7. Подтвердить наличие исправления `2624f5c156` в выбранном client по исходникам/истории
   и реальному early-cancel тесту. До выполнения теста ничего не удалять и не закрывать
   [старую task-00b](../../workspace-load-performance/research-buildhost-fork/task-00b-early-cancel-buildhost-leak-low.md).
8. Выполнить baseline Release build, SourceStructure, main и lifecycle с фильтрами
   и платформами [общей validation](README.md#общая-обязательная-validation).
   Записать существующие отказы отдельно от будущих регрессий.
9. Снять B0 и определить доступность публичных/офисного контролей.
   Сохранить предыдущие проверенные бинарники и SHA для rollback.

## Результат и gate

Отчёт содержит inventory затрагиваемых файлов, точные package/source/runtime версии,
baseline tests/замеры, ссылки на raw evidence и решение о порядке этапов.

Если новый host/MSBuild действительно требует runtime 11 раньше сервера, сначала описать
минимальное изменение порядка/границ этапов с причиной. Нельзя получить зелёный gate
сменой RPC guard, скрытым runtime requirement или отключением старого SDK-сценария.
Установленный SDK 11 не доказывает работу на машине с одним SDK 10.

Gate пройден, когда зависимости и протокол подтверждены, B0 воспроизводим,
обязательные исходные проверки имеют реальные результаты, а все блокирующие
проблемы перечислены с решением. Дальнейшая реализация не начата на этом этапе.
Изменившиеся версии upstream требуют повторной проверки затронутых фактов.
