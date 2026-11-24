# Этап 2. Runtime .NET 11 и язык сервера C# 15

- Рекомендуемые модели: gpt-6.2-astra.
- Рекомендуемый reasoning: high.
- Примерная сложность: 4/5.
- Риск ошибки: высокий — TFM затрагивает test hosts, плагины, SDK discovery и требования к поставке.
- Необходимые способности: .NET SDK/runtime, dependency loading, cross-platform tests, performance.
- Статус: planned, 2026-11-18.
- Depends on: [этап 1](task-01-roslyn-and-buildhost-hi.md).
- Модель, реализовавшая задачу: —.
- Модели, проводившие ревью: —.
- Количество раундов ревью: —.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: —.
- Принятый scope и дата приёмки: —.

Общий контракт: [план перехода](README.md). Версия Roslyn и патчи форка фиксируются
на результате этапа 1, чтобы B2/B1 измерял смену runtime, а не ещё одно обновление compiler.

## Реализация

1. Перед C# изменениями прочитать [code-style.md](../../code-style.md) полностью.
   Перевести сервер на `net11.0`; согласовать TFM Tests, LifecycleTestHost
   и DeadlockWitnessHost, иначе project references/testhost окажутся несовместимы.
   Проверить sample plugin и его shape/load tests: старый net10 plugin должен продолжать
   загружаться в новом host при прежнем API; sample, ссылающийся на сервер, собирается совместимо.
2. Использовать штатный стабильный C# 15 от SDK 11 без `LangVersion=latest/preview`.
   Сначала добиться корректной сборки без синтаксической переработки. Новые конструкции
   применять только когда они сокращают конкретное изменяемое место и понятны SourceStructure.
   Memory-safety preview и массовая миграция моделей/циклов не входят в scope.
3. Установить SDK 11 для CI; сохранить нужные SDK/runtime 10 для compatibility tests
   и прежних fixtures. Выбрать точный стабильный SDK 11 для release pin
   после проверки доступности на всех трёх платформах (кандидат `11.0.100`).
   Не добавлять root global.json только ради release: pin сейчас создаётся в release checkout.
4. Проверить зависимости Microsoft.Extensions.*, MCP, Serilog, test SDK и SQLite
   на runtime 11. Повышать версии только при доказанной необходимости; проверить
   NuGet audit, transitive conflicts, assembly load и native assets.
   Учесть runtime/SDK/Hosting breaking changes, включая поведение shutdown и watchers.
5. Обновить только собственные output paths и ожидания runtime: build/test discovery,
   host launch, sample plugin tests, publish-and-verify, CI/release scripts и инструкции.
   Прочитать все найденные `net10.0`/SDK 10 места; не делать глобальную замену:
   пользовательские multi-TFM/pinned/older-framework fixtures сохраняют свой смысл.
6. BuildHost остаётся отдельным процессом с подтверждённым upstream TFM/roll-forward.
   Не переводить его на net11 просто вместе с сервером. Если это обязательное изменение,
   выделить причину и сравнение, повторить RPC/scenario gates этапа 1.

## Проверки

- Полная [матрица совместимости](README.md#матрица-совместимости) после смены TFM:
  прежний net10 проект, новый net11/C# 15 проект, generators на поддерживаемых TFM,
  старые и новые plugin assemblies, build/test через MCP и reload.
- SDK 10 по точному pin при установленном runtime 11; SDK 11; обе версии рядом;
  чужой cwd, PATH с x86 dotnet на Windows, native ARM64 dotnet на macOS.
  Runtime сервера не должен заставить selector проигнорировать SDK пользовательского проекта.
- AnalyzerLifecycle с opt-in, full SourceStructure и точным inventory limits:
  новые синтаксические формы не могут молча выпасть из обхода transitive calls/lambdas.
  Также focused build/test runner, SDK diagnostics, disk sync/watchers,
  process cancellation/cleanup, index storage и snapshot roundtrip.
- Release build и полный main/lifecycle на трёх native платформах.
  Проверить новые предупреждения, не глушить их общим NoWarn.
- B2 против B1 на одинаковом SDK проекта и тех же настройках GC/A/B/C;
  раздельные измерения server/BuildHost runtime и памяти.
  Если сравниваются разные runtime host, отдельно указать этот фактор.
- Проверить минимальные OS/CPU/runtime требования по официальным
  [breaking changes .NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11).
  Любое повышение требований отражается в документации и следующем этапе smoke.

## Приёмка и откат

Gate — поддерживаемый stable TFM/язык, корректные dependency/assembly loads,
сохранённые SDK 10 сценарии, все обязательные tests/замеры в пределах порогов.
Нет новых публичных возможностей только по факту успешного `dotnet build`.

При откате вернуть одновременно server/test-host/sample TFM, CI/release SDK,
output paths и связанные dependencies к checkpoint этапа 1.
Не оставлять net11 test host с net10 тестами или C# 15 исходники, которые старый SDK не соберёт.
Приёмка исходников этого этапа не означает готовность публичного выпуска: её закрывает этап 3.
