# task-04. Получение `charset` из EditorConfig

Дата: 2026-11-26. Проверено на Windows в Release с Roslyn 5.12.0 на SDK 11.0.100.

Тест `EditorConfig_charset_is_available_in_the_workspace_and_for_unregistered_paths` прошёл. У загруженного проекта `Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree)` вернул `charset = utf-8`. Вторая часть того же теста вычислила значение по пути ещё не созданного файла с помощью публичных `AnalyzerConfig.Parse` и `AnalyzerConfigSet.GetOptionsForSourcePath`. Для вложенной секции `[*.{cs,vb}]` получено `latin1`, а более поздняя точная секция вернула `unset`.

Для записи будет использоваться та же логика сопоставления Roslyn над файлами `.editorconfig` на диске. У загруженных и новых файлов будет один способ разрешения `charset`. Поиск поднимается от каталога файла до `root = true`, а разобранные файлы кэшируются по времени изменения и длине. Это позволяет учитывать появление, удаление и изменение объявления без `load_workspace` и даёт путь объявления для диагностики. `unset` снимает объявление `.editorconfig`, после чего применяется `sourceEncoding`, если он задан.

[Спецификация EditorConfig](https://spec.editorconfig.org/) · [Реализация `AnalyzerConfigSet` в Roslyn](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/CommandLine/AnalyzerConfigSet.cs)
