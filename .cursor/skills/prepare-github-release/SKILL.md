---
name: prepare-github-release
description: >-
  Prepares a GitHub Release for RoslynMcpServer without git push or gh.
  Resolves the commit, csproj Version, commit messages since the previous v* tag
  (max 16), and the Actions Run workflow URL. Use when the user asks to
  выпусти релиз, пометь релизным, cut a release, tag a release commit, or
  prepare a GitHub Release.
---

# Prepare GitHub Release

The agent **cannot** authenticate `git push` (SSH key passphrase). Do not tag,
push, or run `gh release create`. Actions creates the tag and Release after a
human `workflow_dispatch`.

## When to run

Only when the user explicitly asked to release. Never after a normal merge,
bugfix, or docs commit.

| User says | `-CommitRef` |
|---|---|
| выпусти / текущий / HEAD | `HEAD` (default) |
| предыдущий / прошлый коммит | `HEAD~1` |
| SHA | that SHA |

## Steps

1. Read this skill. Run from the repo root (PowerShell; use `;` not `&&`):

```powershell
powershell -NoProfile -File .github/scripts/Get-ReleaseMetadata.ps1 -CommitRef HEAD
```

Pass `-Strict` after the commit is already on `origin/main` and the change is not docs-only.

2. Stop if the script throws or reports:
   - **On origin/main : False** — user must `git push` that commit themselves, then dispatch.
   - **Docs-only : True** — refuse a binary release (README in the zip is not a reason).
   - Tag `v{Version}` would collide with an already shipped version — they need a csproj bump first.

3. Reply with SHA, `v{Version}`, notes preview, and:

   GitHub → Actions → **Release** → Run workflow  
   - Branch: `main`  
   - `commit_sha`: the SHA from the script  

   Workflow URL is in the script output (`run_workflow_url`).

4. Do not create a local tag. Actions will create `v{Version}` on that SHA.

## Notes

- Changelog: commit subjects (and bodies) since the previous `v*` tag; **newest 16** if there are more.
- Batch bugfixes: wait until the last commit is on `main`, then dispatch that SHA once.
- `force=true` on the workflow is only for an intentional docs-only binary republish.
