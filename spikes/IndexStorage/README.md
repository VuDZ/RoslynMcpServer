# IndexStorage — persistent storage индексов Roslyn (spike)

Контракт: [task-14](../../docs/workspace-load-performance/epoch-0-stage-measurement/task-14-roslyn-index-storage-spike-hi.md).

Проект компилирует исходники `spikes/ParallelSearch` без изменений и добавляет только
`SQLite3MC.PCLRaw.bundle` 2.3.5. Именно эту сборку `SQLitePCLRaw.batteries_v2`
(версия 2.3.5.0, ключ `df579d6b11135638`) ждёт Roslyn 5.9.0. Пакеты `SQLitePCLRaw.*`
2.x и 3.x не подходят: у них другая линия версий и другой ключ подписи. С этой
библиотекой Roslyn открывает `SQLitePersistentStorage` и сохраняет индексы между
процессами в `%LOCALAPPDATA%\Microsoft\VisualStudio\Roslyn\Cache\dotnet-*\<решение>-*`.

## Матрица на Windows

Стенд и подготовка — как для `spikes/ParallelSearch` (Roslyn.slnx на commit
протокола, SDK 10.0.301). Запуск из дерева ветки `research/workspace-load-performance`:

```powershell
powershell -ExecutionPolicy Bypass -File .\spikes\IndexStorage\Invoke-IndexStorageMatrix.ps1 -Solution D:\src\roslyn\Roslyn.slnx
```

Матрица: 2 запроса × (прогрев + 5 раундов) × 3 режима = 36 новых процессов,
то есть около 35–40 минут.
- `none` — без SQLite;
- `cold` — кеш этого решения удалён перед прогоном;
- `warm` — кеш, который только что заполнил `cold`.

Удаляются только папки `<решение>-*` внутри `dotnet-*`. Кеши Visual Studio лежат в папках
других процессов и не затрагиваются.

Результаты пишутся в `spikes\IndexStorage\results\storage-<время>`, эта папка не
попадает в Git. Папку нужно упаковать и передать ссылкой. Пороги вердикта заданы
в task-14 до измерений.
