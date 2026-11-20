# README examples: calls and responses

The Jellyfin examples in the [README](../../README.md) come from the calls in [`calls.json`](calls.json).
The responses are in [`output/`](output/), unedited except that the checkout path is shown as `<jellyfin>`.
Run times are left out on purpose: the machine had two cores and its timings say little about the server.

## Setup

- Server: release **v1.6.0**, `RoslynMcpServer-1.6.0-linux-x64-self-contained.tar.gz` from GitHub Releases.
- Solution: [Jellyfin](https://github.com/jellyfin/jellyfin) at commit
  [`821b849e61f6bd0fa760c76de2dc1f39400abd44`](https://github.com/jellyfin/jellyfin/commit/821b849e61f6bd0fa760c76de2dc1f39400abd44),
  `Jellyfin.slnx`, 41 projects. Jellyfin's own CI (`run-tests` on Ubuntu, macOS and Windows) passed on this commit,
  and it builds without errors here.
- .NET SDK 10.0.404 on Linux x64. Building Jellyfin at this commit needs a compiler at least as new as Roslyn 5.9,
  which its analyzer project references; SDK 10.0.114 is too old for that.
- Client: [`mcp_calls.py`](mcp_calls.py), a minimal stdio MCP client (Python 3, no packages) that sends `initialize` and then each call
  in order to one server process. A `!shell` step runs a command in the repository between calls, for edits and pauses.

```bash
git clone https://github.com/jellyfin/jellyfin.git && git -C jellyfin checkout 821b849e61f6bd0fa760c76de2dc1f39400abd44
dotnet restore jellyfin/Jellyfin.slnx
(cd jellyfin && dotnet build Jellyfin.slnx)   # before the rename
python3 mcp_calls.py RoslynMCP/RoslynMcpServer jellyfin calls.json out
git -C jellyfin diff --stat                   # the last call applies the rename
(cd jellyfin && dotnet build Jellyfin.slnx)   # after the rename
git -C jellyfin checkout .                    # undo it
```

The last entry in `calls.json` applies the rename with `previewOnly: false`, so it changes the checkout.
Remove it to keep the run read-only.

## What each README claim rests on

| README claim | Evidence |
|---|---|
| `GetItemById` appears on 278 lines of C# | `grep -rn --include='*.cs' -E '\bGetItemById\b' . \| wc -l` in the checkout |
| Four declaration groups: interface and `LibraryManager` with 4 overloads and 246 locations each, `ChannelManager` 12, extension method 10 | [01-find_usages-GetItemById.txt](output/01-find_usages-GetItemById.txt); signatures in [02-find_symbol_definition-GetItemById.txt](output/02-find_symbol_definition-GetItemById.txt) |
| `ChannelManager.GetItemById<T>` calls `_libraryManager.GetItemById(id)` in its body | `src/Jellyfin.LiveTv/Channels/ChannelManager.cs`, lines 948 and 957 |
| Rename preview of the `Guid` overload: 152 locations | [03-rename_symbol-preview.txt](output/03-rename_symbol-preview.txt) (`Affected locations: 152`; the response lists the first 100) |
| Applied rename: 50 files; generic overloads and `ChannelManager`'s helper keep their names; no additional build errors | [08-rename_symbol-apply.txt](output/08-rename_symbol-apply.txt), [09-git-diff-stat-after-rename.txt](output/09-git-diff-stat-after-rename.txt). `dotnet build Jellyfin.slnx` reports 0 errors both before and after the rename: [10-dotnet-build-before-rename.txt](output/10-dotnet-build-before-rename.txt), [11-dotnet-build-after-rename.txt](output/11-dotnet-build-after-rename.txt). The second build is incremental, so it lists fewer warnings |
| TMDbLib 3.0.0 behavior on 404, 401, 429 and other errors | [05-get_decompiled_method_body-SendInternal.txt](output/05-get_decompiled_method_body-SendInternal.txt); the type was found through [04-explore_assembly-TMDbLib.txt](output/04-explore_assembly-TMDbLib.txt). Jellyfin sets `ThrowApiExceptions = false` in `MediaBrowser.Providers/Plugins/Tmdb/TmdbClientManager.cs` |
| 20 scheduled tasks from four projects | [06-find_implementations-IScheduledTask.txt](output/06-find_implementations-IScheduledTask.txt) |
| `LibraryManager` has three `DeleteItem` overloads; the call graph resolves one | [07-get_call_graph-DeleteItem.txt](output/07-get_call_graph-DeleteItem.txt); declarations in `Emby.Server.Implementations/Library/LibraryManager.cs` |
