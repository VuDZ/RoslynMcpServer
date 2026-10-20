# Task-07 — Причины нарушений snapshot-плана и различий XML

Дата: **2026-10-10**. Проверка read-only: измеренные JSON этой серии, исходные XML
clone PowerToys commit `7b6789c576b304c7e1eda8ce571cc1b0afd280ab` и декомпиляция
публичного пакета `Microsoft.CodeAnalysis.Workspaces.MSBuild` **5.9.0**.
Новых загрузок, evaluation и изменений clone не выполнялось.

## Snapshot хранит compilation references, а не все зависимости загрузки

В `src/modules/CropAndLock/Tests/CropAndLock.UITests/CropAndLock.UITests.csproj:26`
есть `ProjectReference` на `../CropAndLock.TestApp/CropAndLock.TestApp.csproj` с
`ReferenceOutputAssembly="false"`. В baseline-снимке ProjectReferences этого
UITests содержит только `UITestAutomation.Next`; TestApp отсутствует.

В `MSBuildProjectLoader.Worker.ResolveReferencesAsync` после проверки уже
известного проекта ветка `ReferenceOutputAssembly=false` вызывает
`LoadProjectInfosFromPathAsync`, затем продолжает цикл без добавления compilation
reference. Таким образом, отсутствие ребра в `Project.ProjectReferences`
совместимо с фактической загрузкой TestApp. Это конкретная неполнота источника
snapshot для планирования BuildHost ownership, а не случайная вариация.

Snapshot N=2 запрашивает TestApp как отдельный root группы **0**, а UITests —
группы **1**. Progress обеих групп содержит TestApp; в ядре его нет. Получены два
violation: повтор пути вне ядра и общий каталог между группами. Snapshot N=4
повторяет CropAndLock.TestApp в группах **0/3** и Workspaces.TestApp в **1/3**,
поэтому имеет четыре violation с учётом общих каталогов.

Аналогичное XML-ребро находится в
`src/modules/Workspaces/Tests/Workspaces.UITests.Next/Workspaces.UITests.Next.csproj:24`,
также `ReferenceOutputAssembly="false"`. Baseline ProjectReferences UITests.Next
содержит только `UITestAutomation.Next`. В snapshot-плане TestApp и UITests.Next
снова становятся независимыми roots.

Оба UITests имеют дополнительные `MSBuild` tasks для fixture package после
Build, но соответствующие targets ограничены условием
`'$(DesignTimeBuild)' != 'true'`. Они не нужны для объяснения наблюдаемой загрузки:
публичная ветка ResolveReferencesAsync уже загружает ROA=false зависимости.
Утверждать запуск этих package targets по данному evidence нельзя.

XML-граф сохраняет буквальные ProjectReference, включая ROA=false; snapshot-план
не дополнялся этим ребром и после нарушений не перепланировался. Нарушения
сохранены как отказ стратегии, включая measured прогоны.

Для возможного E1 compilation snapshot недостаточен: нужен полный evaluated
граф зависимостей **загрузки**, включая ROA=false и иные обнаруженные build edges,
либо отказ/ordinary fallback при нарушении ownership. Это требование к будущему
источнику графа, не разрешение исправить текущий эксперимент задним числом.

## XML: реальные пути output изменились

Во всех девяти measured XML-прогонах PowerToys (N=2/4/8, n=1/2/3)
неизменённый компаратор после baseline 1/2 даёт одинаковые unexplained категории:

- **OutputFilePath: 15** на каждый прогон.
- **Diagnostics: 94** на каждый прогон; все 94 — baseline-only Failure
  `Cannot open project ... .vcxproj ... file extension ... not associated with a language`.
- **Documents/AdditionalDocuments/AnalyzerConfigDocuments: 0 unexplained**.

Это не потеря generated documents и не регистр Windows paths. Два примера:

- AdvancedPaste.UnitTests: baseline
  `<solution>/x64/Debug/tests/AdvancedPaste.UnitTests/AdvancedPaste.UnitTests.dll`,
  XML N4
  `<solution>/src/modules/AdvancedPaste/AdvancedPaste.UnitTests/x64/Debug/tests/AdvancedPaste.UnitTests/AdvancedPaste.UnitTests.dll`.
- ImageResizer.UnitTests: baseline
  `<solution>/x64/Debug/tests/ImageResizer.Test/net10.0-windows10.0.26100.0/win-x64/ImageResizer.Test.dll`,
  XML N4
  `<solution>/src/modules/imageresizer/tests/x64/Debug/tests/ImageResizer.Test/net10.0-windows10.0.26100.0/win-x64/ImageResizer.Test.dll`.

Полный inventory 15 проектов:
AdvancedPaste.UnitTests, Awake.UnitTests, HostsEditor.UnitTests,
ImageResizer.UnitTests, KeyboardManagerEditorUI.UnitTests, LightSwitch.UITests,
ScreenRuler.UITests, MouseUtils.UITests, Peek.Common.UnitTests,
PowerDisplay.Cli.UnitTests, PowerDisplay.Contracts.UnitTests,
PowerDisplay.Ipc.UnitTests, PowerDisplay.Lib.UnitTests, PowerRename.UITests,
ShortcutGuide.UnitTests.

Например, AdvancedPaste.UnitTests.csproj:12 задаёт OutputPath через
`$(SolutionDir)$(Platform)/$(Configuration)/tests/...`. Декомпилированный
LoadSolutionInfoAsync вызывает SetSolutionProperties исходного solution, который
добавляет абсолютный `SolutionDir` в Properties. LoadProjectInfoAsync этого
вызова не делает. Поэтому загрузка того же проекта как discovered dependency
агрегатора может иметь другую evaluation context и вычислять относительный
OutputPath от каталога самого проекта. Наблюдаемые пути ровно так и изменились.
Это объяснение механизма, **но расхождения остаются unexplained для gate**:
текущий spike не сохраняет solution-level context групп и не доказал его
эквивалентное воспроизведение.

Возможный E1 должен явно воспроизводить исходный SolutionDir и остальные
solution-level properties, а также исходный requested inventory и
diagnostic-reporting semantics native/unloadable проектов. После этого нужна
новая полная проверка эквивалентности. Текущие 94 diagnostics не исключаются из
сравнения, изменение OutputFilePath не классифицируется как case-only.

Полные before/after остаются в
`artifacts/parallel-load/task-07-evidence/task07-comparisons.json`; сводка и
SHA-256 inventory лежат рядом с итоговым отчётом. Измеренные исходники сохранены
отдельно в `measured-source-sha256.json`; последующие исправления failure handling
не выдаются за код, на котором была снята эта матрица.
