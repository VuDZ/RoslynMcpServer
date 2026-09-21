<#
.SYNOPSIS
  Resolve GitHub release metadata for a commit already intended for main.

.DESCRIPTION
  Does not tag, push, or call the GitHub API. Safe for a local agent that cannot
  authenticate git-over-SSH. Writes notes (commit messages since the previous
  v* tag, newest 16) and optional GITHUB_OUTPUT keys.
#>
[CmdletBinding()]
param(
    [string]$CommitRef = 'HEAD',
    [string]$RepoRoot,
    [int]$MaxCommits = 16,
    [string]$NotesPath,
    [string]$GitHubOutput,
    [switch]$AsJson,
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'

if ($MaxCommits -lt 1) {
    throw 'MaxCommits must be >= 1.'
}

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
else {
    $RepoRoot = (Resolve-Path $RepoRoot).Path
}

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]]$GitArgs)
    $out = & git -C $RepoRoot @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        $detail = ($out | Out-String).Trim()
        throw "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE): $detail"
    }
    return $out
}

function Test-IsReleaseRelevantPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $p = $Path.Replace('\', '/').TrimStart('/')
    if ($p -match '^(docs/|\.cursor/|\.github/)') {
        return $false
    }

    if ($p -match '\.(md|mdc)$') {
        return $false
    }

    if ($p -eq '.gitignore' -or $p -eq 'LICENSE') {
        return $false
    }

    return $true
}

function Get-GitHubRepoSlug {
    $url = (Invoke-Git @('remote', 'get-url', 'origin') | Out-String).Trim()
    if ($url -match 'github\.com[:/]([^/]+)/([^/]+?)(?:\.git)?$') {
        return '{0}/{1}' -f $Matches[1], $Matches[2]
    }

    return $null
}

function Get-CsprojVersion {
    $csproj = Join-Path $RepoRoot 'RoslynMcpServer.csproj'
    if (-not (Test-Path -LiteralPath $csproj)) {
        throw "RoslynMcpServer.csproj not found: $csproj"
    }

    $raw = Get-Content -LiteralPath $csproj -Raw
    $match = [regex]::Match($raw, '<Version>([^<]+)</Version>')
    if (-not $match.Success) {
        throw 'Version property missing in RoslynMcpServer.csproj.'
    }

    $version = $match.Groups[1].Value.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$version' is not a three-part number."
    }

    return $version
}

function Get-PreviousReleaseTag {
    param([string]$Sha)
    $tags = @(Invoke-Git @('tag', '--list', 'v*', '--merged', $Sha, '--sort=-version:refname') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^v\d+\.\d+\.\d+$' })

    foreach ($tag in $tags) {
        $tagSha = (Invoke-Git @('rev-list', '-n', '1', $tag) | Out-String).Trim()
        if ($tagSha -ne $Sha) {
            return $tag
        }
    }

    return $null
}

function Get-GitLine {
    param([string[]]$GitArgs)
    $text = (Invoke-Git $GitArgs | Out-String).TrimEnd()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ''
    }

    return $text
}

function Get-CommitRecords {
    param(
        [string]$Range,
        [int]$Limit
    )

    $hashes = @(Invoke-Git @(
            '-c', 'core.quotepath=false',
            '--no-pager',
            'log',
            '-n', "$Limit",
            '--format=%H',
            $Range
        ) | ForEach-Object { $_.Trim() } | Where-Object { $_ })

    $records = @()
    foreach ($hash in $hashes) {
        $subject = (Get-GitLine @('-c', 'core.quotepath=false', '--no-pager', 'log', '-1', '--format=%s', $hash)).Trim()
        $body = (Get-GitLine @('-c', 'core.quotepath=false', '--no-pager', 'log', '-1', '--format=%b', $hash)).Trim()
        if ($body.Length -gt 800) {
            $body = $body.Substring(0, 800).TrimEnd() + [char]0x2026
        }

        if ([string]::IsNullOrWhiteSpace($subject)) {
            continue
        }

        $records += [pscustomobject]@{
            Sha     = $hash
            Subject = $subject
            Body    = $body
        }
    }

    return @($records)
}

function ConvertTo-NotesMarkdown {
    param(
        [object[]]$Commits,
        [int]$TotalCount,
        [string]$PreviousTag,
        [string]$Version,
        [string]$ScZip,
        [string]$FddZip,
        [string]$RepoSlug
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add('## Downloads')
    [void]$lines.Add('')
    [void]$lines.Add(('- **Self-contained** (`{0}`): includes the .NET 10 runtime. Use this unless this machine already has a compatible .NET 10 Runtime.' -f $ScZip))
    [void]$lines.Add(('- **Framework-dependent** (`{0}`): smaller; requires a .NET 10 Runtime (or SDK 10) for win-x64.' -f $FddZip))
    [void]$lines.Add('')
    [void]$lines.Add('The server still locates an installed MSBuild/SDK for the **consumer project**. The bundled runtime does not replace that SDK.')
    if (-not [string]::IsNullOrWhiteSpace($RepoSlug)) {
        [void]$lines.Add('')
        [void]$lines.Add('## Provenance')
        [void]$lines.Add('')
        [void]$lines.Add('Each zip has a GitHub Artifact Attestation from the Release workflow. Optional check (not Authenticode / SmartScreen):')
        [void]$lines.Add('')
        [void]$lines.Add('```')
        [void]$lines.Add(('gh attestation verify {0} -R {1}' -f $ScZip, $RepoSlug))
        [void]$lines.Add(('gh attestation verify {0} -R {1}' -f $FddZip, $RepoSlug))
        [void]$lines.Add('```')
    }
    [void]$lines.Add('')
    [void]$lines.Add('## Changes')
    [void]$lines.Add('')

    if ($TotalCount -le 0) {
        [void]$lines.Add('_No commits listed (first release or empty range)._')
        return ($lines -join "`n")
    }

    if ($PreviousTag) {
        if ($TotalCount -gt $Commits.Count) {
            [void]$lines.Add(('Showing the {0} most recent of {1} commits since `{2}`.' -f $Commits.Count, $TotalCount, $PreviousTag))
        }
        else {
            [void]$lines.Add(('Commits since `{0}`:' -f $PreviousTag))
        }
    }
    else {
        if ($TotalCount -gt $Commits.Count) {
            [void]$lines.Add(('Showing the {0} most recent of {1} commits (no previous `v*` tag).' -f $Commits.Count, $TotalCount))
        }
        else {
            [void]$lines.Add('Commits in this first tagged release:')
        }
    }

    [void]$lines.Add('')
    foreach ($c in $Commits) {
        $escaped = $c.Subject
        [void]$lines.Add('- ' + $escaped)
        if (-not [string]::IsNullOrWhiteSpace($c.Body)) {
            foreach ($bodyLine in ($c.Body -split '\r?\n')) {
                [void]$lines.Add('  ' + $bodyLine.TrimEnd())
            }
        }
    }

    [void]$lines.Add('')
    [void]$lines.Add(('Version `{0}` from `RoslynMcpServer.csproj` at this commit.' -f $Version))
    return ($lines -join "`n")
}

$sha = (Invoke-Git @('rev-parse', $CommitRef) | Out-String).Trim()
$originMain = $null
try {
    $originMain = (Invoke-Git @('rev-parse', '--verify', 'origin/main') | Out-String).Trim()
}
catch {
    $originMain = $null
}

$onOriginMain = $false
if ($originMain) {
    & git -C $RepoRoot merge-base --is-ancestor $sha origin/main 2>$null | Out-Null
    $onOriginMain = ($LASTEXITCODE -eq 0)
}

$version = Get-CsprojVersion
$tag = 'v' + $version
$previousTag = Get-PreviousReleaseTag -Sha $sha

if ($previousTag) {
    $range = '{0}..{1}' -f $previousTag, $sha
}
else {
    $range = $sha
}

$totalCount = [int](Get-GitLine @('rev-list', '--count', $range))
$selected = @(Get-CommitRecords -Range $range -Limit $MaxCommits)

# git log is newest-first; show chronological (oldest of the selected window first).
[array]::Reverse($selected)

$docsOnly = $false
$relevantFiles = @()
if ($previousTag) {
    $changed = @(Invoke-Git @('diff', '--name-only', $previousTag, $sha) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ })
    $relevantFiles = @($changed | Where-Object { Test-IsReleaseRelevantPath $_ })
    $docsOnly = ($relevantFiles.Count -eq 0)
}

$scZip = 'RoslynMcpServer-{0}-win-x64-self-contained.zip' -f $version
$fddZip = 'RoslynMcpServer-{0}-win-x64-framework-dependent.zip' -f $version
$slug = Get-GitHubRepoSlug
$runWorkflowUrl = $null
if ($slug) {
    $runWorkflowUrl = 'https://github.com/{0}/actions/workflows/release.yml' -f $slug
}

$notes = ConvertTo-NotesMarkdown `
    -Commits $selected `
    -TotalCount $totalCount `
    -PreviousTag $previousTag `
    -Version $version `
    -ScZip $scZip `
    -FddZip $fddZip `
    -RepoSlug $slug

if ($NotesPath) {
    $notesDir = Split-Path -Parent $NotesPath
    if ($notesDir -and -not (Test-Path -LiteralPath $notesDir)) {
        New-Item -ItemType Directory -Path $notesDir -Force | Out-Null
    }

    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($NotesPath, $notes.Replace("`n", "`r`n"), $utf8)
}

$result = [ordered]@{
    sha              = $sha
    version          = $version
    tag              = $tag
    on_origin_main   = $onOriginMain
    docs_only        = $docsOnly
    previous_tag     = $previousTag
    commit_count     = $totalCount
    notes_commit_count = $selected.Count
    truncated        = ($totalCount -gt $selected.Count)
    sc_zip           = $scZip
    fdd_zip          = $fddZip
    run_workflow_url = $runWorkflowUrl
    relevant_files   = $relevantFiles
    notes            = $notes
}

if ($GitHubOutput) {
    @(
        "sha=$sha"
        "version=$version"
        "tag=$tag"
        "on_origin_main=$onOriginMain"
        "docs_only=$docsOnly"
        "previous_tag=$previousTag"
        "commit_count=$totalCount"
        "truncated=$($result.truncated)"
        "sc_zip=$scZip"
        "fdd_zip=$fddZip"
    ) | Add-Content -LiteralPath $GitHubOutput -Encoding utf8
}

$report = @"
GitHub release prep (no tag, no push)
  SHA              : $sha
  Version / tag    : $version / $tag
  On origin/main   : $onOriginMain  (local origin/main only; this script does not fetch)
  Docs-only        : $docsOnly
  Previous tag     : $(if ($previousTag) { $previousTag } else { '(none)' })
  Commits in range : $totalCount (notes include $($selected.Count); cap $MaxCommits)
  Run workflow     : $(if ($runWorkflowUrl) { $runWorkflowUrl } else { '(set origin to github.com)' })

Next:
  1. Push this commit to origin/main yourself if On origin/main is False.
  2. GitHub -> Actions -> Release -> Run workflow
     - branch: main
     - commit_sha: $sha
"@

Write-Host $report
Write-Host ''
Write-Host '--- notes preview ---'
Write-Host $notes

if ($AsJson) {
    $result | ConvertTo-Json -Depth 5
}

if ($Strict) {
    if (-not $onOriginMain) {
        throw "Commit $sha is not an ancestor of local origin/main. Push main first, then run the workflow."
    }

    if ($docsOnly) {
        throw "Changes since $(if ($previousTag) { $previousTag } else { 'the previous tag' }) are docs/rules only. Do not dispatch a binary release (pass workflow input force=true only if you mean it)."
    }
}
