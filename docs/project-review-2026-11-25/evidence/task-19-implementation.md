# FIX-19. Частичная реализация и блокер

Дата — 2026-11-25. Основание — `1bb2e8c`. Проверка выполнена на Windows с SDK `11.0.100` и runtime `.NET 11.0.0` в Release.

## Реализованный объём

На исходном `ICSharpCode.Decompiler` 10.0.1.8346 продуктовый путь для `System.Diagnostics.Process.WaitForExitAsync` возвращал три `Stack underflow` и вызовы `AsyncHelpers.Await`. Поддержка runtime-async появилась в [ILSpy 10.1](https://github.com/icsharpcode/ILSpy/releases/tag/v10.1). После обновления до 10.1.1.8388 целевой метод снова выводится как `public async` с обоими `await`, без `Stack underflow` и `AsyncHelpers.Await`.

MCP помечает декомпиляцию как неполную, если ILSpy оставил `ErrorExpression` или бросаемое значение типа `System.Object`. Проверка использует узлы и типы AST. Обычный соседний метод `Process.Refresh` сохраняет прежний формат без предупреждения.

## Что остаётся открытым

В выводе для `WaitForExitAsync` ILSpy 10.1.1 оставляет `ExceptionDispatchInfo.Capture((obj as Exception) ?? throw obj).Throw()`, где `obj` объявлен как `object`. C# требует неявного преобразования операнда `throw` к `System.Exception`. У ILSpy есть [открытое обсуждение преобразования блоков catch для runtime-async](https://github.com/icsharpcode/ILSpy/issues/3745).

Пробная сборка с 11.2.0.10050 остановилась на MSB3027/MSB3021 при замене `ICSharpCode.Decompiler.dll` в `RoslynMcpServer.Tests/bin/Release/net11.0`. Файл удерживал запущенный Roslyn MCP после чтения сборки. Пробное обновление отменено. Осталось проверить версию, которая восстанавливает корректный `throw`, по тому же методу .NET 11 и соседнему контрольному методу.

## Независимое ревью

Автор — `gpt-6.2-sol` (`fix19_impl`). Рецензент — `gpt-6.2-sol` (`fix19_review`). Раунд 1 принял частичное исправление. Полный критерий корректного C# остаётся открытым.

## Проверка

- AssemblyResolve — 23/23 через Roslyn MCP. Тесты проверяют реальный `WaitForExitAsync`, обычный `Process.Refresh` и предупреждение для узла ошибки ILSpy.
- Финальная Release-сборка решения прошла. SourceStructure — 128/128.
- Основные группы CI — 149/149 в TiaIntegration и 2499/2499 в Unit.

## Проверка с ILSpy 11.2

Дата — 2026-11-25. Основание — `5f669a8`. Обновление до `ICSharpCode.Decompiler` 11.2.0.10050 прошло в Windows Release без остановки запущенного Roslyn MCP. Резолвер делегирует `BeginSnapshot` штатному резолверу ILSpy, а проверка предупреждений учитывает `throw;` без операнда.

Проверка вызвала продуктовый `get_decompiled_method_body` через тот же код MCP для двух установленных копий `System.Diagnostics.Process.dll`.

- .NET 11.0.0. `Process.WaitForExitAsync` выводится с `async` и обоими `await`, без `Stack underflow`. ILSpy 11.2 по-прежнему выводит `ExceptionDispatchInfo.Capture((obj as Exception) ?? throw obj).Throw()` с `obj` типа `object`. MCP сообщает о неполной декомпиляции.
- .NET 10.0.15. Для того же метода ILSpy 11.2 восстановил обычный `throw;`, без ошибки стека и предупреждения о неполной декомпиляции.

Автор обновления — `gpt-6.2-sol` (`ilspy112_impl`). Рецензент — `gpt-6.2-sol` (`ilspy112_review`). Независимое ревью приняло обновление за один раунд без обязательных замечаний.

- Целевой Release-прогон AssemblyResolve — 23/23, включая `WaitForExitAsync`, `Process.Refresh` и пометку для узла ошибки ILSpy.
- Release-сборка решения прошла. SourceStructure — 128/128. AnalyzerLifecycle с `ROSLYN_MCP_ANALYZER_LIFECYCLE=1` — 13/13.
- Основные группы CI — 149/149 в TiaIntegration и 2501/2501 в Unit.

FIX-19 остаётся открытой до исправления декомпиляции `throw` в ILSpy для воспроизведённого случая .NET 11.
