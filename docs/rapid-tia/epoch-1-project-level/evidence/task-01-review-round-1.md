# task-01 — независимое ревью, раунд 1

- Дата: 2026-11-03.
- Рецензент: Codex; точный runtime model ID средой не предоставлен.
- Версия: рабочее дерево после первоначального focused GREEN 31/31, до исправлений R1-001/R1-002; база `386c46b`.
- Scope: новый builder, три модели, каталог тестов, отчёт и RED/GREEN evidence task-01. Посторонний `.agents/skills/prepare-github-release/` исключён.
- Проверки координатора до ревью: Release solution build успешен, focused 31/31, SourceStructure 123/123, Windows main suite 1463/1463. Эти результаты не являются финальной validation после исправлений.
- Вердикт: **MUST-FIX**.

## R1-001 — потеря связи объявления с областью импортов

`Services/Testing/Baseline/BaselineDocumentSnapshotBuilder.cs:47–49`.

До:

```csharp
namespace N { using X = System.Int32; class C { X Value; } }
namespace N { using X = System.Int64; }
```

После:

```csharp
namespace N { using X = System.Int32; }
namespace N { using X = System.Int64; class C { X Value; } }
```

Read-only probe рецензента против Release DLL подтвердил одинаковые `Members` и все
классифицированные хеши при `HasUnparseableContent=false`; отличается только
`TextChecksum`. Тип `Value` меняется с `int` на `long`. Удаление всех членов из
namespace surfaces стирает связь объявления с областью using. Требуется сохранить
эту связь для обнаружения изменения потребителем task-02.

## R1-002 — комментарий в пустой неактивной ветви меняет хеш

`Services/Testing/Baseline/BaselineDocumentSnapshotBuilder.cs:130–132`.

Для `class C { void Run() {\n#if false\n#endif\n} }` вставка `// comment\n` либо
`   \n` перед `#endif` меняет `Run.SyntaxHash` и `DirectivesHash`. Подтверждено
read-only probe против Release DLL. `ParseTokens` добавляет искусственный EOF для
появившейся disabled trivia; пустая ветвь до правки такого токена не имеет.
Требуется исключить этот вклад EOF и добавить регрессии инвариантности комментариев
и пробелов.

## Передача исправлений

Оба замечания переданы первоначальному исполнителю. Исправления и итог перепроверки
фиксируются в отчёте реализации и следующем независимом раунде; этот файл сохраняет
исходный вердикт раунда 1.
