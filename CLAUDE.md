# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Visual Studio extension that remembers which documents were open on each git branch and restores
them when you switch back — including branch switches made outside Visual Studio. Tabs are the only
feature in scope; bookmarks and breakpoints are a deliberate later step (see [README.md](README.md)).

## Commands

`dotnet build` cannot build the whole solution: the VSIX packaging targets need full MSBuild.
Use `dotnet` for the Core/test loop and MSBuild when you need the `.vsix`.

```powershell
# Tests — the main development loop. Core has no Visual Studio dependency, so this is fast
# and needs nothing installed beyond the .NET SDK.
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj

# A single test / class / trait
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj --filter "FullyQualifiedName~Detects_a_real_git_checkout"
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj --filter "FullyQualifiedName~BranchMonitorTests"
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj --filter "Category!=Integration"

# Core only
dotnet build src\GitTabSync.Core\GitTabSync.Core.csproj

# Full solution and the .vsix (MSBuild from either installed VS)
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" GitTabSync.slnx /p:Configuration=Release /t:Rebuild /restore
# -> src\GitTabSync.Vsix\bin\Release\net472\GitTabSync.Vsix.vsix
```

**The "Visual Studio extension development" workload is not installed on this machine and is not
required.** `VSSDKBuildToolsAutoSetup=true` in
[GitTabSync.Vsix.csproj](src/GitTabSync.Vsix/GitTabSync.Vsix.csproj) makes `Microsoft.VSSDK.BuildTools`
import the packaging targets from the NuGet package (it defaults to `false`), so the whole build comes
from NuGet + MSBuild. Do not "fix" a VSIX build problem by telling the user to install the workload
before checking that property.

To try the extension, build the `.vsix` and install it, or launch an experimental instance:
`devenv /rootsuffix Exp`. There is no automated coverage of the Visual Studio layer — see below.

## Architecture

Three projects, split along one line: **anything that can be tested without Visual Studio is kept out
of the VSIX.**

| Project | Target | Role |
|---|---|---|
| `src/GitTabSync.Core` | netstandard2.0 | Git detection, storage, and all sync decisions. No VS references. |
| `src/GitTabSync.Vsix` | net472 | Thin shell adapter: VS APIs in, `IEditorTabs` out. |
| `tests/GitTabSync.Core.Tests` | net9.0 | 82 tests, incl. real-`git` integration tests. |

Core targets netstandard2.0 specifically so one assembly is consumable by both the .NET Framework
VSIX and the modern test project.

### The two constraints that shape everything

**1. Branch changes originate outside Visual Studio.** This is the problem the project exists for.
[BranchMonitor](src/GitTabSync.Core/Git/BranchMonitor.cs) runs a `FileSystemWatcher` on `.git/HEAD`
*and* a slow poll. The poll is not redundant: watchers drop events on buffer overflow and are
unreliable on network/virtualised paths, and a missed switch means silently saving one branch's tabs
under another branch's name. The poll makes the worst case *late* rather than *wrong*. Both feed a
debounce, because one checkout touches HEAD several times.

HEAD is read from disk rather than by shelling out to `git.exe` — cheap enough to poll and never
spawns a process on the UI thread. Reads use `FileShare.ReadWrite` and retry, because git holds a
write handle while swapping HEAD, and unparseable content is treated as "no information" rather than
as a change.

**2. A switch is observed *after* it happened.** By then VS may already have closed tabs for files
the checkout deleted, so asking the editor at that moment no longer describes the branch being left.
[TabSyncCoordinator](src/GitTabSync.Core/Sync/TabSyncCoordinator.cs) therefore keeps a *continuously
refreshed* snapshot (fed by RDT document events) and persists **that** against the outgoing branch —
never a reading taken at switch time. `TabSyncCoordinatorTests` pins this behaviour; if you change
how snapshots are taken, that test is the one that matters.

The same reason drives the `_applying` flag: a restore opens documents, which raises document events,
which would otherwise overwrite the snapshot with a half-applied state.

### Decisions worth knowing before changing them

- **Storage lives in `%LOCALAPPDATA%\GitTabSync`**, never in the working tree — anything inside it
  would be rewritten by the very checkout the session exists to survive, and would show as a pending
  change on every switch.
- **Session keys are `readable prefix + hash`** ([StorageKey](src/GitTabSync.Core/Storage/StorageKey.cs)).
  Branch names contain characters illegal in filenames, and sanitising alone maps `feature/foo` and
  `feature-foo` onto one file. Repo keys are case-*insensitive* (Windows paths); head keys are
  case-*sensitive* (git refs). Both halves must match that, prefix included.
- **Serialisation is `DataContractJsonSerializer`**, chosen over Newtonsoft/System.Text.Json so the
  VSIX ships no extra assemblies and cannot hit binding-redirect conflicts with the copies VS already
  has loaded. It escapes `/` as `\/` — valid JSON, left alone deliberately.
- **Missing files are dropped on restore.** A branch switch is exactly what makes files appear and
  disappear, so a stored session routinely names files absent on the target branch.
- **Dirty documents are never closed** ([VsEditorTabs](src/GitTabSync.Vsix/VsEditorTabs.cs)), and
  unknown dirty state is assumed dirty — the cost of being wrong the other way is discarding edits.
- **Unvisited branches leave tabs alone by default.** Closing would be "purer", but every branch is
  unvisited on first use, so the default would wipe the user's tabs the first time they tried it.
- Storage and restore failures are swallowed and logged, not thrown: losing a remembered tab set
  beats failing a branch switch.

### Threading

Core raises `BranchChanged` on a **timer thread**. `VsEditorTabs` marshals to the UI thread itself
via `JoinableTaskFactory`; callers do not. Timer callbacks in Core and the VSIX both catch broadly —
an escaped exception on a timer thread takes the process down.

## Testing notes

Tests use real temp directories rather than a mocked filesystem, because the behaviour that matters
(file locking, rename-over-the-top, path casing) is exactly what a mock would not reproduce.

`RealGitIntegrationTests` shells out to real `git` — a real checkout, a real detached HEAD, and a real
linked worktree. The unit tests write HEAD themselves, which proves parsing but assumes how git
updates the file; only these prove the watcher subscribes to the events that actually fire. They skip
if git is not on PATH. **Keep them passing** — they cover the project's central risk.

`Directory.Build.props` sets `TreatWarningsAsErrors`. The VSIX project opts out (the VS SDK reference
assemblies are not nullable-annotated); Core does not — keep it warning-clean.

## Not built yet

- No coverage of the Visual Studio layer at all. `VsEditorTabs`, `RepositorySyncSession` and
  `GitTabSyncPackage` compile and package but **have never been run inside Visual Studio**. Treat
  their behaviour as unverified.
- Tab *order* is approximate: `IVsUIShell.GetDocumentWindowEnum` does not promise tab order, and
  restore reopens documents rather than rebuilding the layout. Split panes and tab groups are not
  captured. If layout fidelity becomes the goal, `IVsUIShellDocumentWindowMgr`
  (`SaveDocumentWindowPositions`/`ReopenDocumentWindows`) persists the real layout as an opaque blob —
  the tradeoff is losing the ability to filter out files missing on the target branch.
- Open Folder mode is not handled; only solutions (`SolutionExists` autoload).
- Bookmarks and breakpoints, per the README's staging.
