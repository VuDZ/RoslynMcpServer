# 7a — Mixed non-C# project diagnostics

Pri **P2**. SemVer: **patch**. Независим. 7b не входит.
Разбор: [../port-candidates-1-2-3-4-7.md](../port-candidates-1-2-3-4-7.md#7-mixed-cc-и-отсутствующие-проекты).

## Цель

`.vcxproj` / «not associated with a language» не валит `load_workspace`.
Missing C# `.csproj` остаётся blocking.

## Файлы

- `Diagnostics/WorkspaceDiagnosticFormatter.cs`
- `RoslynMcpServer.Tests/WorkspaceDiagnosticFormatterTests.cs`

## Правка

`IsExpectedNonCSharpProjectAdvisory`: английская фраза + закавыченное
non-C# расширение (`vcx|cpp|wix|…`). **Без** blanket `Project file not found`.

Включить в `IsSoftWorkspaceAdvisory`. Classifier **сам** возвращает false при
`HasExplicitErrorToken` / `IsHardMsBuildLoadFailure` — soft в
`IsBlockingLoadFailure` проверяется раньше hard (строки 140–150).

Стиль regex — как в файле сейчас (`static readonly`, не обязательно
`[GeneratedRegex]`).

## Тесты

- true: «is not associated with a language», локализованный текст с `".vcxproj"`
- `Project file not found` + `.csproj` — blocking
- non-C# marker **и** `: error MSB` — blocking
- регресс hard: NETSDK1045, XMakeElements, missing Compile, NU/MSB/NETSDK

## Acceptance

Mixed C++/C# solution → загруженный C# workspace + видимые native diagnostics.
Missing C# project → по-прежнему Failure.
