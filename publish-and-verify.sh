#!/usr/bin/env bash
# Linux counterpart of scripts/publish-and-verify.ps1: publishes RoslynMcpServer for this machine and
# checks that the Claude Code MCP launcher will start the new build.
#
# The launcher (~/.local/share/roslyn-mcp/launch.sh) copies the newest build output under
# $ROSLYN_MCP_REPO/bin into ~/.cache/roslyn-mcp/snapshots/ and runs that copy. Running servers
# therefore never lock bin/, and nothing has to be stopped before publishing; they keep the old
# snapshot until the MCP client reconnects.
set -euo pipefail

usage() {
    cat <<'EOF'
Usage: ./publish-and-verify.sh [options]

  -r, --rid RID         Runtime identifier (default: detected, e.g. linux-x64).
      --pack            Also pack artifacts/RoslynMCP-<version>-<rid>.tar.gz
                        (root folder RoslynMCP, README.md and LICENSE, no logs/).
      --kill            Stop running RoslynMcpServer processes started from snapshots
                        or this repository (SIGTERM, then report survivors).
      --mcp-name NAME   MCP server name in ~/.claude.json (default: roslyn).
      --min-tools N     Tool count to expect from get_mcp_server_info (default: 54).
  -h, --help            Show this help.
EOF
}

die() { echo "error: $*" >&2; exit 1; }
warn() { echo "WARNING: $*" >&2; }

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)
project="$repo_root/src/RoslynMcpServer/RoslynMcpServer.csproj"
cache_dir="${ROSLYN_MCP_CACHE:-$HOME/.cache/roslyn-mcp}"
launcher="${ROSLYN_MCP_LAUNCHER:-$HOME/.local/share/roslyn-mcp/launch.sh}"
claude_config="$HOME/.claude.json"
rid=""
pack=0
kill_old=0
mcp_name="roslyn"
min_tools=54

while [ $# -gt 0 ]; do
    case "$1" in
        -r|--rid) [ $# -ge 2 ] || die "$1 needs a value"; rid="$2"; shift 2 ;;
        --pack) pack=1; shift ;;
        --kill) kill_old=1; shift ;;
        --mcp-name) [ $# -ge 2 ] || die "$1 needs a value"; mcp_name="$2"; shift 2 ;;
        --min-tools) [ $# -ge 2 ] || die "$1 needs a value"; min_tools="$2"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; die "unknown option: $1" ;;
    esac
done

[ -f "$project" ] || die "project not found: $project"

detect_rid() {
    local arch libc=""
    case "$(uname -m)" in
        x86_64|amd64) arch=x64 ;;
        aarch64|arm64) arch=arm64 ;;
        *) die "unsupported architecture $(uname -m); pass --rid" ;;
    esac
    if ldd --version 2>&1 | grep -qi musl; then
        libc="musl-"
    fi
    echo "linux-$libc$arch"
}

# SDK version pinned by global.json (rollForward is disabled), empty if there is none.
pinned_sdk() {
    local global_json="$repo_root/global.json"
    [ -f "$global_json" ] || return 0
    sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$global_json" | head -n 1
}

# Does the dotnet host in this directory have the SDK pinned by global.json?
has_pinned_sdk() {
    [ -x "$1/dotnet" ] || return 1
    [ -z "$sdk_version" ] || [ -d "$1/sdk/$sdk_version" ]
}

# First dotnet host that can build this repository: PATH, $DOTNET_ROOT, ~/.dotnet, ~/.dotnet11.
resolve_dotnet() {
    local candidate dir
    for candidate in "$(command -v dotnet 2>/dev/null || true)" "${DOTNET_ROOT:-}/dotnet" \
                     "$HOME/.dotnet/dotnet" "$HOME/.dotnet11/dotnet"; do
        [ -n "$candidate" ] && [ -x "$candidate" ] || continue
        dir=$(dirname "$(readlink -f "$candidate")")
        if has_pinned_sdk "$dir"; then
            echo "$dir/dotnet"
            return
        fi
    done
    die "no dotnet host with SDK ${sdk_version:-from global.json} on PATH, in \$DOTNET_ROOT, ~/.dotnet or ~/.dotnet11. Install the SDK pinned in global.json."
}

# Version of a build output folder, taken from its deps.json ("RoslynMcpServer/1.6.2").
output_version() {
    local deps="$1/RoslynMcpServer.deps.json"
    [ -f "$deps" ] || { echo "unknown"; return; }
    grep -o '"RoslynMcpServer/[0-9][^"]*"' "$deps" | head -n 1 | tr -d '"' | cut -d/ -f2 || echo "unknown"
}

# Same rule as launch.sh: the folder whose RoslynMcpServer.dll has the newest mtime, in any TFM.
launcher_source() {
    local repo="$1" dll mtime newest=0 src=""
    for dll in "$repo"/bin/{Release,Debug}/net*/RoslynMcpServer.dll \
               "$repo"/bin/{Release,Debug}/net*/*/publish/RoslynMcpServer.dll; do
        [ -f "$dll" ] || continue
        mtime=$(stat -c %Y "$dll")
        if [ "$mtime" -gt "$newest" ]; then
            newest=$mtime
            src=$(dirname "$dll")
        fi
    done
    echo "$src"
}

# Value of an env variable from the MCP entry in ~/.claude.json (user scope), empty if absent.
mcp_env() {
    [ -n "$have_config" ] || return 0
    jq -r --arg n "$mcp_name" --arg k "$1" '.mcpServers[$n].env[$k] // empty' "$claude_config" 2>/dev/null || true
}

# PID and server path ("snap/RoslynMcpServer" or ".../RoslynMcpServer.dll") of running servers
# that were started from a snapshot or from this repository.
running_servers() {
    local proc pid exe path arg
    for proc in /proc/[0-9]*; do
        pid=${proc#/proc/}
        exe=$(readlink "$proc/exe" 2>/dev/null) || continue
        path=""
        if [ "$(basename "$exe")" = "RoslynMcpServer" ]; then
            path="$exe"
        else
            while IFS= read -r -d '' arg; do
                case "$arg" in
                    */RoslynMcpServer.dll) path="$arg"; break ;;
                esac
            done 2>/dev/null < "$proc/cmdline" || true
        fi
        [ -n "$path" ] || continue
        case "$path" in
            "$cache_dir"/snapshots/*|"$repo_root"/*) echo "$pid $path" ;;
        esac
    done
}

[ -n "$rid" ] || rid=$(detect_rid)
sdk_version=$(pinned_sdk)
dotnet=$(resolve_dotnet)
tfm=$(grep -o '<TargetFramework>[^<]*</TargetFramework>' "$project" | head -n 1 | sed 's/<[^>]*>//g')
[ -n "$tfm" ] || die "no single TargetFramework in $project"
publish_dir="$repo_root/bin/Release/$tfm/$rid/publish"

# Build tools follow TMPDIR; /tmp on this kind of machine is often a small tmpfs.
if [ -z "${TMPDIR:-}" ]; then
    export TMPDIR="$HOME/tmp"
fi
mkdir -p "$TMPDIR"

echo "dotnet host  : $dotnet"
"$dotnet" --info | grep -E '^\s*(RID|Architecture|Base Path)\s*:' | sed 's/^\s*//' || true
echo "SDK          : ${sdk_version:-not pinned}"
echo "TFM          : $tfm"
echo "RID          : $rid"

echo "Publishing RoslynMcpServer..."
"$dotnet" publish "$project" -c Release -r "$rid"

host="$publish_dir/RoslynMcpServer"
[ -f "$publish_dir/RoslynMcpServer.dll" ] || die "publish output not found: $publish_dir/RoslynMcpServer.dll"
[ -x "$host" ] || die "publish host is missing or not executable: $host"
if grep -q '"includedFrameworks"' "$publish_dir/RoslynMcpServer.runtimeconfig.json" 2>/dev/null \
    && [ ! -f "$publish_dir/libcoreclr.so" ]; then
    die "self-contained publish is missing libcoreclr.so: $publish_dir"
fi

version=$(output_version "$publish_dir")
csproj_version=$(grep -o '<Version>[^<]*</Version>' "$project" | head -n 1 | sed 's/<[^>]*>//g')
[ "$version" = "$csproj_version" ] || die "published version '$version' does not match csproj Version '$csproj_version'"

echo "Binary       : $host"
echo "Version      : $version"
echo "LastWriteTime: $(date -d "@$(stat -c %Y "$publish_dir/RoslynMcpServer.dll")" --iso-8601=seconds)"
echo "Size         : $(stat -c %s "$publish_dir/RoslynMcpServer.dll") bytes (RoslynMcpServer.dll)"

if [ "$pack" -eq 1 ]; then
    for f in README.md LICENSE; do
        [ -f "$repo_root/$f" ] || die "$f not found: $repo_root/$f"
    done
    archive="$repo_root/artifacts/RoslynMCP-$version-$rid.tar.gz"
    mkdir -p "$repo_root/artifacts"
    staging=$(mktemp -d "$TMPDIR/RoslynMcpPublish_XXXXXX")
    trap 'rm -rf "$staging"' EXIT
    mkdir -p "$staging/RoslynMCP"
    rsync -a --exclude /logs "$publish_dir/" "$staging/RoslynMCP/"
    cp "$repo_root/README.md" "$repo_root/LICENSE" "$staging/RoslynMCP/"
    chmod a+x "$staging/RoslynMCP/RoslynMcpServer"
    rm -f "$archive"
    tar -czf "$archive" -C "$staging" RoslynMCP

    names=$(tar -tzf "$archive")
    bad_root=$(grep -v '^RoslynMCP\(/\|$\)' <<<"$names" | head -n 5 || true)
    [ -z "$bad_root" ] || die "archive must have root folder RoslynMCP; unexpected entries: $bad_root"
    ! grep -q '^RoslynMCP/logs\(/\|$\)' <<<"$names" || die "archive must not include publish logs/"
    for required in RoslynMCP/README.md RoslynMCP/LICENSE RoslynMCP/RoslynMcpServer; do
        grep -qx "$required" <<<"$names" || die "archive is missing $required"
    done
    echo "Artifact     : $archive ($(stat -c %s "$archive") bytes)"
fi

# Does the Claude Code MCP entry start this build?
echo ""
have_config=""
if [ -f "$claude_config" ] && command -v jq >/dev/null 2>&1 \
    && jq -e --arg n "$mcp_name" '.mcpServers[$n]' "$claude_config" >/dev/null 2>&1; then
    have_config=1
fi

mcp_repo="$repo_root"
if [ -z "$have_config" ]; then
    warn "no user-scope MCP server '$mcp_name' in $claude_config (or jq is missing); launcher checks assume ROSLYN_MCP_REPO=$repo_root"
else
    mcp_command=$(jq -r --arg n "$mcp_name" '.mcpServers[$n].command // empty' "$claude_config")
    echo "MCP command  : $mcp_command"
    [ "$mcp_command" = "$launcher" ] \
        || warn "MCP '$mcp_name' does not run the snapshot launcher $launcher; the checks below may not apply"
    configured_repo=$(mcp_env ROSLYN_MCP_REPO)
    if [ -n "$configured_repo" ]; then
        mcp_repo="$configured_repo"
    elif [ -f "$launcher" ]; then
        mcp_repo=$(sed -n 's/^REPO="\${ROSLYN_MCP_REPO:-\(.*\)}"$/\1/p' "$launcher" | head -n 1)
        [ -n "$mcp_repo" ] || mcp_repo="$repo_root"
    fi
fi
echo "MCP repo     : $mcp_repo"

# The server loads workspaces with the MCP environment's dotnet. Without the pinned SDK it cannot
# open this repository (global.json disables roll-forward), and a framework-dependent build cannot start.
mcp_dotnet_root=$(mcp_env DOTNET_ROOT)
[ -n "$mcp_dotnet_root" ] || mcp_dotnet_root="${DOTNET_ROOT:-}"
if [ -n "$mcp_dotnet_root" ]; then
    echo "MCP dotnet   : $mcp_dotnet_root"
    has_pinned_sdk "$mcp_dotnet_root" \
        || warn "MCP DOTNET_ROOT $mcp_dotnet_root has no SDK $sdk_version; install it there or point DOTNET_ROOT and PATH of '$mcp_name' at $(dirname "$dotnet")"
fi

[ -f "$launcher" ] || warn "launcher not found: $launcher"

mcp_repo_real=$(cd "$mcp_repo" 2>/dev/null && pwd -P || true)
if [ -z "$mcp_repo_real" ]; then
    warn "ROSLYN_MCP_REPO '$mcp_repo' does not exist: the MCP server cannot start."
    echo "  Point it at this repository:" >&2
    echo "    claude mcp remove $mcp_name -s user" >&2
    echo "    claude mcp add-json $mcp_name '<same JSON with ROSLYN_MCP_REPO=$repo_root>' -s user" >&2
elif [ "$mcp_repo_real" != "$repo_root" ]; then
    warn "MCP uses another repository ($mcp_repo); this publish does not update it."
else
    picked=$(launcher_source "$repo_root")
    if [ "$picked" = "$publish_dir" ]; then
        echo "Launcher     : will snapshot $publish_dir"
    else
        warn "launcher will pick a newer build: $picked (version $(output_version "$picked"))"
    fi
fi

# Servers still running an old snapshot.
mapfile -t servers < <(running_servers)
if [ "${#servers[@]}" -eq 0 ]; then
    echo "Running      : no RoslynMcpServer from snapshots or this repository"
else
    echo "Running      : ${#servers[@]} RoslynMcpServer process(es):"
    for s in "${servers[@]}"; do
        pid=${s%% *}
        path=${s#* }
        echo "  PID $pid  version $(output_version "$(dirname "$path")")  $path"
    done
    if [ "$kill_old" -eq 1 ]; then
        for s in "${servers[@]}"; do
            kill -TERM "${s%% *}" 2>/dev/null || warn "failed to signal PID ${s%% *}"
        done
        for _ in $(seq 1 32); do
            sleep 0.25
            mapfile -t servers < <(running_servers)
            [ "${#servers[@]}" -gt 0 ] || break
        done
        if [ "${#servers[@]}" -eq 0 ]; then
            echo "  stopped."
        else
            warn "still running after SIGTERM: $(printf '%s ' "${servers[@]%% *}")"
        fi
    fi
fi

log_dir=$(mcp_env ROSLYN_MCP_LOG_DIR)
[ -n "$log_dir" ] || log_dir="${ROSLYN_MCP_LOG_DIR:-}"
if [ -z "$log_dir" ]; then
    server_home=$(mcp_env ROSLYN_MCP_HOME)
    [ -n "$server_home" ] || server_home="${ROSLYN_MCP_HOME:-}"
    if [ -n "$server_home" ]; then
        log_dir="$server_home/logs"
    else
        log_dir="${XDG_STATE_HOME:-$HOME/.local/state}/roslyn-mcp/logs"
    fi
fi

echo ""
echo "Next steps:"
echo "  1. Claude Code: /mcp -> $mcp_name -> Reconnect (running servers keep their old snapshot until then)"
echo "  2. Call get_mcp_server_info (expect >= $min_tools tools, version $version)"
echo "  3. Logs: $log_dir/mcp-*.log"
[ "$pack" -eq 1 ] && echo "  4. Artifact: $archive"
exit 0
