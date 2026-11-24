# Этап 3. Поставки, нативный smoke и итоговая приёмка

- Рекомендуемые модели: gpt-6.2-astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 4/5.
- Риск ошибки: высокий — локальная сборка не проверяет распакованный релиз и runtime дочернего host.
- Необходимые способности: publish/pack, MCP stdio, native CI, SDK/runtime isolation и rollback.
- Статус: planned, 2026-11-18.
- Depends on: [этап 2](task-02-runtime-and-language-hi.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.
- Принятый scope и дата приёмки: —.

Общий контракт: [план перехода](README.md). На этом этапе не добавляются новые RID,
новый test runner или другой способ поставки.

## Поставки

1. Из одного финального SHA и точного SDK собрать Release, оба publish-варианта и
   pack/tool package. Обычная сборка остаётся framework-dependent для project references;
   `PublishSingleFile=false`, ReadyToRun и GC defaults сохраняются.
2. Проверить шесть архивов: win-x64, linux-x64, osx-arm64 × self-contained/FDD.
   Имена/metadata/version/RID согласованы с Get-ReleaseMetadata.ps1 и release.yml;
   host executable mode и корень RoslynMCP сохранены, logs/secrets не упакованы.
3. Проверить содержимое build, publish и nupkg: новый форк BuildHost-netcore,
   нужные Contracts/deps/runtimeconfig, stock net472 того же пакета, license/notices,
   SQLite native assets. Не должно быть старых DLL, runtime MSBuild копий
   из compile-only references или случайной смеси 5.9/5.12.
4. Установить dotnet tool из локального package source в отдельный tool-path,
   выполнить initialize/load/semantic request/build/test, затем удалить только test tool-path.
   NuGet pack является отдельным gate, не следствием удачного publish.

## Native smoke

На Windows x64, Linux x64 и macOS ARM64 распаковать каждый из двух архивов
в свежую папку и запустить поставляемый executable, а не DLL из bin разработчика.
Использовать [Invoke-ReleaseSmoke.ps1](../../../.github/scripts/Invoke-ReleaseSmoke.ps1);
расширять только при необходимости подтвердить ещё не покрытый сценарий.

- `initialize`, `tools/list`, `load_workspace`, definition/reference, diagnostics,
  `run_dotnet_build`, `run_dotnet_test`, reload и штатный shutdown.
  Smoke тестового проекта должен реально выполнить хотя бы один тест; tools/list недостаточно.
- По очереди net10/C# 14 и net11/C# 15 проекты; fixture с новым синтаксисом
  должен дать правильный symbol/result, а не просто успешный ответ load.
- SC: доказать использование встроенного runtime 11; загрузить net10 проект в окружении
  с SDK 10 и без shared runtime 11, если это допускает подтверждённый контракт BuildHost.
  Отдельно SDK 11. Если дочернему host требуется новый shared runtime, это явное
  изменение требования к прежнему сценарию, а не «полностью standalone».
- FDD: runtime 11 + SDK 10, затем runtime/SDK 11; отрицательный тест без runtime 11.
  Не путать доступность SDK проекта и runtime сервера.
- Записать путь dotnet, server/host runtime, выбранный MSBuild/SDK и фактические PID.
  После cancellation, failed load, shutdown и обрыва stdin собственных host не остаётся;
  процессы других сессий сохраняются.
- Чистый `ROSLYN_MCP_HOME`, затем повторный запуск с данными предыдущей версии:
  config/plugins, index/state cache, логи и watcher не ломаются.
  Изоляция теста не должна удалять пользовательские данные.

Допустимы временные локальные artifacts или CI job без release publication.
Не запускать публикующий Release workflow только ради smoke feature-ветки:
он рассчитан на SHA в main и создаёт публичные assets.
Кросс-публикация macOS на Windows не закрывает native smoke.
Прежний pending macOS smoke отмечается отдельно от новых результатов.

## Итоговая приёмка и откат

1. На финальном SHA повторить [общую validation](README.md#общая-обязательная-validation):
   Release build, full SourceStructure/main/lifecycle и native smoke всех шести архивов;
   проверить отсутствие нулевых/пропущенных наборов и сохранить TRX/commands/environment.
2. Проверить checkpoint B0/B1/B2, полную RPC-equivalence нового форка и пороги
   load + first useful query/памяти. Вывод не опирается на результаты другого SHA.
3. Запустить предыдущий проверенный архив после нового на том же тестовом workspace
   и пользовательском каталоге. Нет повреждения конфигурации, plugins и кешей;
   несовместимый кеш безопасно отвергается. Не перезаписывать прежние release assets.
4. Обновить README/ARCHITECTURE, publish instructions, минимальные OS/CPU/runtime/SDK,
   release notes/CHANGELOG и версии согласованно в рамках code-реализации.
   Подготовка этого плана версии продукта не меняет.
5. Синхронизировать статусы всех этапов, этот индекс и docs/README.md:
   дата, принятый scope, финальный SHA, evidence, ограничения и фактическое ревью.
   При подтверждённом исправлении отдельно закрыть старую task-00b с её evidence.
6. Непройденные обязательные native gates остаются pending; такой результат не объявляется
   готовым к выпуску. Недоступный частный контроль фиксируется как ограничение,
   без выдуманной эквивалентности. Публикация — отдельное действие после готовности,
   по существующему порядку релизов и указанию пользователя.

Rollback report содержит проверенный прежний SHA/архив, точные действия восстановления,
совместимость пользовательского каталога и ограничения. Готовность — это проверенные
исходники вместе с артефактами, а не только успешный CI исходников.
