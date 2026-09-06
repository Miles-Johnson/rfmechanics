# Build, test and release

This is prerelease development. Building, pushing development source and publishing a release are separate operations. ModDB and GitHub releases require the maintainer's express approval of the exact version, source commit and ZIP checksum.

## Branches

- `dev`: ongoing development; default branch until the first accepted stable release.
- `release/<version>`: frozen candidate, containing release fixes only.
- `stable`: latest approved release source. Create this branch from the first approved candidate, not an arbitrary old commit.
- `v<version>`: immutable annotated tag at the exact approved commit.

Keep existing historical branches. Use feature/fix branches and PRs into dev where useful. Do not force-push stable or move published tags. Normal pushes only run metadata validation; they do not deploy or publish.

## Build locally

Windows PowerShell and, for code mods, .NET 10 SDK plus a legitimate Vintage Story installation are required. Set `VINTAGE_STORY` to the game installation directory. Game DLLs are references only and must not be committed or packaged.

From this repository, run:

```powershell
.\Build.ps1
```

This builds the mod and creates a Release ZIP plus SHA-256 sidecar in `artifacts/`. It does not change your client or contact a server. `dotnet build -c Release` in a code mod only compiles; the separate build wrapper also packages. Content mods need no compiler.

Use `-Configuration Debug` for local staged development output. Release archives refuse changed content under an existing version, including changed DLLs and notices. Increment the prerelease version for a new shared candidate. Keep generated output out of Git.

## Freeze a candidate

Commit only reviewed, intended source/config/assets/docs. From dev, create `release/<version>`, preferably in a separate worktree. Finalize the manifest and changelog, then run:

```powershell
.\Build.ps1 -ReleaseCandidate
```

This requires a clean Git checkout and known full SHA. The ZIP includes full source revision, Git tree, source-file hash and DLL hash in `build-info.json`; its sidecar records the ZIP hash. A source archive without Git metadata can build for development but cannot certify a candidate.

Install the exact candidate on the active local client, verify its files, then update the test server only after that verification succeeds. The workspace's deployment tool accepts a pinned artifact manifest; do not choose a different ZIP by timestamp. Relaunch the client if it was running when files changed. An installed file is not proof the running client loaded it.

Use the actual game and intended test server for acceptance. Check startup, join/rejoin, race selection, applicable mechanics and food/config reload cases. Do not use standalone test applications. Existing-save installation/removal is unverified unless separately tested on disposable save copies.

## After express release approval

Record the user's approval alongside version, full SHA, ZIP hash, game/dependency versions, playtest results and known limitations. Fast-forward stable to that same commit (or create stable there for the first release), create the annotated version tag, then upload the already approved ZIP manually. Never rebuild silently after approval. Make stable the default after the first accepted release.

A hotfix starts from the last released tag/stable; test and approve it through the same process, then merge it back into dev. Branch integrity controls do not establish gameplay acceptance or replace human release approval.
