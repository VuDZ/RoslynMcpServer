# Roslyn BuildHost fork

Контракт: [BuildHost-исследование](../../docs/workspace-load-performance/research-buildhost-fork/README.md),
[task-00](../../docs/workspace-load-performance/research-buildhost-fork/task-00-buildhost-fork-baseline-hi.md).
Источник и хеши: [UPSTREAM.md](UPSTREAM.md).

Форк процесса BuildHost, которым `MSBuildWorkspace` из Roslyn 5.9.0 загружает проекты.
Клиент (`Microsoft.CodeAnalysis.Workspaces.MSBuild.dll` в сервере) не форкается: он запускает тот
BuildHost, что лежит в папке `BuildHost-netcore` рядом с ним, и общается с ним JSON по каналу.
Поэтому форк подменяется копированием своей сборки в эту папку, пока протокол не меняется.

- `upstream/` — дословная копия исходников Roslyn на ревизии пакета; наши изменения — отдельными
  коммитами поверх неё.
- `build/` — проекты для обычного .NET SDK: `Contracts/Contracts.Fork.csproj` и
  `BuildHost/BuildHost.Fork.csproj` (только netcore, net8.0, `rollForward: Major`, как в пакете).
- Код следует стилю и `.editorconfig` Roslyn, а не `docs/code-style.md` этого репозитория: изменения
  должны уходить в upstream с минимальным диффом (см. `AGENTS.md`).
- Production сервера форк не использует. Эксперименты подключают его явно:
  `spikes/BuildHostFork/Invoke-BuildHostForkBaseline.ps1` сравнивает оригинал и форк на одном решении.

Сборка:

```text
dotnet build forks/roslyn-buildhost/build/BuildHost/BuildHost.Fork.csproj -c Release
```

Обновление Roslyn: заново скопировать перечисленные в `UPSTREAM.md` пути с ревизии нового пакета,
перенести поверх них изменения форка и повторить сравнение task-00.
