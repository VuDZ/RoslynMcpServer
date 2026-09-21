# 7b — Missing C# project (DEFER)

Не входит в текущий ship. После 7a.

`Project file not found` на **C#** `.csproj` может значить дырявый graph
(типы/ссылки пропали). Обычный success это скрывает.

Нужен отдельный контракт: `loaded (degraded)`, список пропущенных путей,
влияние на solution-wide results. Не смягчать blanket-фразой форка.
