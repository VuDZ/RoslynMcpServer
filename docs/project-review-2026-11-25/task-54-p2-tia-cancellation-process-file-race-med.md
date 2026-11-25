# P2-54. Синхронизировать публикацию файла процессов в тесте отмены

- Рекомендуемые модели: GPT-6.2 Sol.
- Рекомендуемый reasoning: medium.
- Примерная сложность: 2/5.
- Риск ошибки: средний. Гонка фикстуры срывает проверку отмены и даёт нестабильный результат CI на Windows.
- Необходимые способности: совместный доступ к файлам Windows, синхронизация тестов, чтение TRX.
- Статус: accepted, 2026-11-25. Общая фикстура публикует оба PID после закрытия файла. Проверки отмены и таймаута прошли. [Приёмка](evidence/tasks-49-54-validation.md#p2-54).
- Depends on: —.
- Отчёт о воспроизведении: [повторное ревью](evidence/review-f82d4ff.md#p2-54).

## Воспроизведение

В полном прогоне Windows тест `Tools_call_cancellation_kills_active_process_trees_and_skips_queued_projects(degree: 1)` упал на чтении `processes.txt` с `IOException` о совместном доступе. Целевой повтор обоих вариантов степени параллелизма прошёл.

Управляемая проверка воспроизводит тот же порядок событий без ожидания совпадения по времени.

~~~powershell
$path = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
$writer = [IO.File]::Open($path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes('123,456')
    $writer.Write($bytes, 0, $bytes.Length)
    $writer.Flush()
    [IO.File]::Exists($path)
    try {
        [IO.File]::ReadAllTextAsync($path).GetAwaiter().GetResult()
    } catch {
        $_.Exception.GetBaseException().Message
    }
} finally {
    $writer.Dispose()
}
[IO.File]::ReadAllTextAsync($path).GetAwaiter().GetResult()
~~~

Путь существует при открытом писателе. Чтение в этом окне получает `0x80070020`. После закрытия писателя тот же вызов возвращает `123,456`.

## Причина и требуемое поведение

Дочерний процесс записывает PID через `File.WriteAllText`. `WaitForProcessesAsync` считает появление пути сигналом готовности и один раз вызывает `File.ReadAllTextAsync`. Файл становится видимым раньше окончания записи и освобождения дескриптора. Фикстура должна публиковать готовые PID целиком или ждать возможности прочитать полную запись в пределах существующего таймаута.

## Критерии приёмки

- [x] При управляемой задержке пишущего процесса ожидание не падает от совместного доступа и получает два PID после публикации.
- [x] Варианты теста продолжают проверять остановку активных процессов и пропуск проектов из очереди.

## Где смотреть

- [TestImpactRunFixture.cs](../../RoslynMcpServer.Tests/Testing/TestImpactRunFixture.cs).
- [EstimateAffectedTestsCancellationTests.cs](../../RoslynMcpServer.Tests/Testing/EstimateAffectedTestsCancellationTests.cs).
- Связанные карточки — [P1-04](task-04-p1-dotnet-pipe-deadline-hi.md), [P2-24](task-24-p2-dotnet-timeout-output-hi.md).

[Реестр задач](README.md)
