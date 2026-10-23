# D/E0/task-15 — Persistent storage индексов Roslyn в сервере

- Дата постановки: 2026-10-23.
- Рекомендуемые модели: Claude Opus 5.5; альтернатива: GPT-6.1 Sol.
- Рекомендуемый reasoning: GPT: medium.
- Примерная сложность: 2/5; класс `med`.
- Риск ошибки: средний:
  - обновление Roslyn может сменить ожидаемую сборку SQLite, и кеш молча перестанет работать;
  - тесты могут копить кеш временных решений в профиле пользователя.
- Необходимые способности: `семантика_roslyn_msbuild`, `проектирование_проверок`.
- Статус: **implemented, 2026-10-23.** Сервер 1.5.14, решение владельца.
  - Офисный контроль пройден: первый поиск на server GC с тёплым кешем 16,1 → 13,6 с,
    остальные 141 проект 8,4 → 5,5 с; холодный прогрев 16,1 с, кеш 15 МБ
    ([офисный прогон](evidence/office-first-search-2026-10-23.md#третий-прогон-сервер-1514-с-кешем-индексов-20182029)).
  - Независимое ревью pending.
- Depends on: [task-14](task-14-roslyn-index-storage-spike-hi.md).
- Модель, реализовавшая задачу: Claude Opus 5.5.
- Модели, проводившие ревью: —.
- Количество раундов ревью: 0.
- Количество исправлений после ревью: —.
- Отчёт о реализации, ревью и validation: [evidence/task-15-roslyn-index-storage-in-server.md](evidence/task-15-roslyn-index-storage-in-server.md).

## Вопрос

Включить в сервере persistent storage индексов Roslyn, проверенное в task-14 (go),
не меняя ответов инструментов.

## Решение владельца, 2026-10-23

«Да, делай task-15». Вопрос владельца, что будет, если исходники изменятся, пока
сервер выключен. Ответ: Roslyn хранит каждый индекс вместе с контрольной суммой
документа. При следующей загрузке изменённые файлы индексируются заново, а
неизменённые берутся из кеша. Это проверено в task-14 между процессами и в тесте
этой задачи между сессиями.

## Реализация

- `RoslynMcpServer.csproj`: `SQLite3MC.PCLRaw.bundle` 2.3.5 — сборка, на которую
  ссылается Roslyn 5.9.0. Версия 1.5.14.
- В Roslyn 5.9.0 нет публичного выключателя хранилища и способа сменить место кеша.
  Внутренние `IPersistentStorageConfiguration` и `IWorkspaceConfigurationService` не
  дают ни того, ни другого. Поэтому хранилище включено всегда, как в Visual Studio,
  а в `PRIVACY.md` описаны место кеша и безопасное удаление.
- Тесты:
  - `RoslynIndexStorageTests.Shipped_sqlite_is_the_build_Roslyn_references` сравнивает
    версию и ключ поставляемой `SQLitePCLRaw.batteries_v2` со ссылкой Roslyn и упадёт
    при обновлении Roslyn;
  - `Indexes_are_stored_and_a_later_session_sees_changed_sources`: база создаётся;
    после правки файла новая сессия видит изменение;
  - `RoslynIndexCache` удаляет кеш процесса тестов при загрузке тестовой сборки и при
    выходе, чтобы временные решения не копились в `LocalApplicationData`.
- `scripts/Measure-WorkspaceLoad.ps1` пишет в окружение отчёта `indexCacheBefore` и
  `indexCacheAfter`: число и размер папок кеша этого решения от любых установок
  `RoslynMcpServer`. Так видно, был ли прогрев холодным.
- `PRIVACY.md`, CHANGELOG, `docs/ARCHITECTURE.md`.

## Ограничения

- Нет кеша для workspace, открытого из `.csproj` без решения: Roslyn строит место
  по пути файла решения.
- Кеш привязан к пути исполняемого файла:
  - новая папка установки начинает с холодного кеша;
  - сервер без SQLite, запущенный из того же пути, удаляет кеш.
- Кеш не очищается, около 34 МБ на решение размера Roslyn.

## Офисный контроль

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Invoke-FirstSearchDiagnostics.ps1 -Workspace <путь>\App.sln -SymbolName MainViewModel -Platform x64
```

На 1.5.14 при `indexCacheBefore: 0 folders` прогрев первого прохода холодный,
а замеры тёплые. Сравнение — с офисным 1.5.13: 16,1 с на server GC.
