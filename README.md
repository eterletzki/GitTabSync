# Git Tab Sync for Visual Studio

Remembers which documents were open on each git branch and restores them when you switch back —
including when the branch is switched **outside** Visual Studio.

- [The problem](#the-problem)
- [Status](#status)
- [Installing](#installing)
- [Settings](#settings)
- [How it works](#how-it-works)
- [What gets stored, and where](#what-gets-stored-and-where)
- [Building from source](#building-from-source)
- [Repository layout](#repository-layout)
- [Tests](#tests)
- [Troubleshooting](#troubleshooting)
- [Limitations](#limitations)
- [Roadmap](#roadmap)

## The problem

Visual Studio does not remember open tabs per branch. If you work across several branches, every
switch throws away your working set and you rebuild it by hand — and if you switch branches from a
terminal or another git client, Visual Studio does not even notice until files start changing
underneath it.

Git Tab Sync watches the repository itself, so it works regardless of *how* the branch changed:
`git switch` in a terminal, GitHub Desktop, a rebase in another tool, or Visual Studio's own Git
tooling.

Tabs are the focus. Bookmarks and breakpoints are a possible later step, deliberately not started.

## Status

**Early — the core is proven, the Visual Studio layer is not.**

| Part | State |
|---|---|
| Branch detection, storage, save/restore decisions | Implemented, 82 passing tests, including tests that drive the real `git` executable. |
| Visual Studio integration | Compiles and packages into an installable `.vsix`, but **has never been run inside Visual Studio**. Treat it as unproven. |

## Installing

There is no Marketplace release yet. Build the `.vsix` ([below](#building-from-source)) and
double-click it, or install it from the command line:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\VSIXInstaller.exe" `
    src\GitTabSync.Vsix\bin\Release\net472\GitTabSync.Vsix.vsix
```

Targets Visual Studio 2022 and 2026 (`[17.0, 19.0)`), amd64, Community/Professional/Enterprise.

The extension loads when a solution is opened and does nothing at all if that solution is not inside
a git repository.

## Settings

**Tools → Options → Git Tab Sync → General**

| Setting | Default | Effect |
|---|---|---|
| Close tabs on an unvisited branch | Off | When switching to a branch with no remembered tabs, close everything instead of leaving the current tabs open. |
| Restore tabs when a solution opens | On | Restore the remembered tabs for the current branch as soon as a solution is opened, rather than waiting for a branch switch. |

Closing tabs on an unvisited branch is off by default because *every* branch is unvisited the first
time you use the extension — defaulting it on would wipe your tabs the first time you tried it. The
cost of leaving it off is some tab bleed between branches, which the next save corrects.

## How it works

### Detecting the branch

The branch can change from anywhere, so the extension cannot wait to be told. It watches `.git/HEAD`
with a `FileSystemWatcher` *and* polls it on a slow timer (5 s by default). Both feed a debounce
(250 ms), because a single checkout touches HEAD more than once.

The poll is not redundant. File watchers drop events when their buffer overflows and are unreliable
on network and virtualised paths, and a missed switch means silently saving one branch's tabs under
another branch's name. **The poll makes the worst case *late* instead of *wrong*.**

HEAD is read directly from disk rather than by running `git.exe` — cheap enough to poll, and it never
spawns a process on the UI thread. Reads share the file with git's own write handle and retry
briefly, because git swaps HEAD by renaming `HEAD.lock` over the top. Content that cannot be parsed
is treated as "no information" rather than as a change.

Handled states:

- **Attached HEAD** — `ref: refs/heads/feature/foo` → the branch `feature/foo`.
- **Detached HEAD** — a bare SHA-1 or SHA-256 object id, as left by a rebase, bisect or
  `git checkout <sha>`. Sessions are keyed separately from a branch of the same name.
- **Linked worktrees and submodules** — a `.git` *file* redirects to the real git directory, and the
  worktree's own HEAD is read, not the main repository's.

### Saving the right tabs

A branch switch is noticed *after* it has happened. By then Visual Studio may already have closed
tabs for files the checkout deleted, so reading the editor at that moment no longer describes the
branch you just left.

So the extension keeps a continuously refreshed snapshot of the open tabs — driven by document
open/close/activate events — and saves *that* against the outgoing branch, never a reading taken at
switch time. Its own restores are excluded from the snapshot, so a half-applied state can never be
mistaken for what you had open.

Pinning a tab is the one thing that produces no document event at all, so the extension listens for
the Pin Tab command itself and refreshes the snapshot immediately, without the short delay the
document events go through. Pinning a tab and switching branches a moment later keeps the pin.

Sessions are also saved when the solution closes and when Visual Studio shuts down, since neither
produces a branch change to react to.

### Restoring

On switching to a branch, the stored session for the incoming branch is applied:

- **Documents missing on the incoming branch are skipped.** A branch switch is exactly what makes
  files appear and disappear, so a stored session routinely names files that do not exist on the
  target branch; opening them would mean "file not found" dialogs on every switch.
- **Documents already open are left alone**, not closed and reopened — that would lose undo history
  and scroll position.
- **Documents with unsaved changes are never closed.** A branch switch is not a reason to discard
  edits or to throw a save prompt at someone who did not ask for one. When the dirty state cannot be
  determined, the document is assumed dirty.
- **Caret position is restored** where it can be, and silently skipped where it cannot (the file may
  be shorter on this branch).
- **Pinned tabs are restored pinned**, and tabs that were not pinned on the incoming branch are
  unpinned — pin state belongs to the branch, so a tab pinned on one branch does not stay pinned
  after you switch to a branch that never pinned it.
- **Branches with no stored session leave your tabs alone** by default — see [Settings](#settings).

Storage and restore failures are logged rather than thrown: losing a remembered tab set is a much
better outcome than failing a branch switch.

## What gets stored, and where

Sessions live under `%LOCALAPPDATA%\GitTabSync`, deliberately **outside the working tree** —
anything kept inside it would be rewritten by the very checkout the session exists to survive, and
would show as a pending change on every branch switch.

```
%LOCALAPPDATA%\GitTabSync\
└── repos\
    └── gittabsync-3f9a1c7d5e2b40a8\        one directory per repository
        ├── branch_main-8c1d02e4f7a9b365.json
        ├── branch_feature_login-2b7e4a10c9d8f3e6.json
        └── detached_9f2c1d645f7a4a3b8c2e6d4b0e1a7c5-5a3e7b092c14d8f6.json
```

Each name is a readable prefix plus a hash of the original: branch names legitimately contain
characters that are illegal in file names, and sanitising alone would map `feature/foo` and
`feature-foo` onto the same file. Repository keys are case-*insensitive* (Windows paths); HEAD keys
are case-*sensitive* (git refs — `Feature` and `feature` are different branches).

A session file:

```json
{"schema":1,"head":"branch\/main","savedAtUtc":"2026-08-09T10:14:32.1174820Z","activeIndex":1,
 "tabs":[{"path":"src\/GitTabSync.Core\/Git\/GitHead.cs","relative":true,"line":42,"column":9,"pinned":true},
         {"path":"README.md","relative":true,"line":1,"column":1,"pinned":false}]}
```

Paths inside the repository are stored relative with `/` separators, so sessions survive the
repository being moved or re-cloned elsewhere; files outside it are stored absolute. Writes go to a
temporary file and are renamed over the top, so a crash mid-write cannot leave a truncated file
where a good session used to be. A file written by a newer schema version is ignored rather than
half-understood.

Deleting the directory, or any file in it, is safe — it just forgets.

## Building from source

Requires the **.NET SDK** and **MSBuild**. The *Visual Studio extension development* workload is
**not** required: `VSSDKBuildToolsAutoSetup=true` makes the VSIX packaging targets come from the
`Microsoft.VSSDK.BuildTools` NuGet package, so a fresh clone and a CI agent can both produce a
`.vsix`.

```powershell
# Tests — the main development loop. Fast, and needs nothing but the .NET SDK.
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj

# Core only
dotnet build src\GitTabSync.Core\GitTabSync.Core.csproj

# The extension. `dotnet build` cannot do this — VSIX packaging needs full MSBuild.
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    GitTabSync.slnx /p:Configuration=Release /t:Rebuild /restore
```

The package lands at `src\GitTabSync.Vsix\bin\Release\net472\GitTabSync.Vsix.vsix`.

To try it without installing into your main IDE, install it into the experimental instance and
launch that: `devenv /rootsuffix Exp`.

## Repository layout

| Project | Target | Role |
|---|---|---|
| `src/GitTabSync.Core` | netstandard2.0 | Branch detection, storage, and every sync decision. No Visual Studio references. |
| `src/GitTabSync.Vsix` | net472 | The extension: a thin adapter from the Visual Studio shell to the core. |
| `tests/GitTabSync.Core.Tests` | net9.0 | 86 tests, including real-`git` integration tests. |

The split follows one rule: **anything that can be tested without Visual Studio is kept out of the
VSIX**, so the interesting decisions are covered by fast tests that need nothing installed. Core
targets netstandard2.0 so a single assembly is consumable by both the .NET Framework VSIX and the
modern test project.

Notable types:

| File | Role |
|---|---|
| [GitRepository.cs](src/GitTabSync.Core/Git/GitRepository.cs) | Finds the repository from a path inside it; reads HEAD from disk. |
| [GitHead.cs](src/GitTabSync.Core/Git/GitHead.cs) | Parses HEAD; models attached and detached states. |
| [BranchMonitor.cs](src/GitTabSync.Core/Git/BranchMonitor.cs) | Watcher + poll + debounce; raises `BranchChanged`. |
| [TabSyncCoordinator.cs](src/GitTabSync.Core/Sync/TabSyncCoordinator.cs) | Holds the snapshot; decides what to save and restore. |
| [TabSessionMapper.cs](src/GitTabSync.Core/Sync/TabSessionMapper.cs) | Editor tabs ⇄ persisted session; drops missing files. |
| [FileSessionStore.cs](src/GitTabSync.Core/Storage/FileSessionStore.cs) | JSON sessions under `%LOCALAPPDATA%`. |
| [VsEditorTabs.cs](src/GitTabSync.Vsix/VsEditorTabs.cs) | The only file that touches editor windows. |
| [RepositorySyncSession.cs](src/GitTabSync.Vsix/RepositorySyncSession.cs) | Everything alive while one repository is open. |

Serialisation uses `DataContractJsonSerializer`, chosen over Newtonsoft and System.Text.Json so the
VSIX ships no extra assemblies and cannot hit binding-redirect conflicts with the copies Visual
Studio already has loaded in-process.

## Tests

```powershell
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj

# One class, one test, or everything but the integration tests
dotnet test tests\... --filter "FullyQualifiedName~BranchMonitorTests"
dotnet test tests\... --filter "FullyQualifiedName~Detects_a_real_git_checkout"
dotnet test tests\... --filter "Category!=Integration"
```

Tests use real temp directories rather than a mocked filesystem, because the behaviour that matters
— file locking, rename-over-the-top, path casing — is exactly what a mock would not reproduce.

`RealGitIntegrationTests` shells out to real `git` for a real checkout, a real detached HEAD and a
real linked worktree. The unit tests write HEAD themselves, which proves the parsing but assumes how
git updates the file; only these prove the watcher is subscribed to the events that actually fire.
They skip when git is not on PATH.

## Troubleshooting

The extension acts without being asked, so when it does something surprising the log is the way to
find out why: **View → Output**, then pick **Git Tab Sync** in the drop-down. It records the
repository and branch being watched, every HEAD move, how many tabs were saved and restored, how
many stored tabs did not exist on the incoming branch, and any document left open because it had
unsaved changes.

If nothing is logged at all, the solution is probably not inside a git repository, or the extension
did not load — check **Extensions → Manage Extensions**.

## Limitations

- **The Visual Studio layer has never been run in Visual Studio.** Everything in
  `src/GitTabSync.Vsix` is unverified.
- **Tab order is approximate.** `IVsUIShell.GetDocumentWindowEnum` does not promise tab order, and
  restore reopens documents rather than rebuilding the layout. Split panes and tab groups are not
  captured.
- **Open Folder mode is not handled** — the extension loads on `SolutionExists` only.
- **Documents without a file on disk are ignored** — designers, option pages and unsaved new files
  have nothing that could be reopened on another branch.
- Only the caret line and column and the pinned state are remembered per tab; scroll position,
  selection and folding are not.
- **Pinning is noticed through the Pin Tab command, not a document event.** Pinning changes no
  document, so nothing in the running document table reports it; the extension subscribes to the
  command instead. Anything that changes pin state without going through that command would not be
  seen until the next document event.

## Roadmap

1. Run the extension in Visual Studio and fix whatever the shell layer gets wrong. Nothing else
   matters until this is done.
2. Higher-fidelity layout, if it proves worth it. `IVsUIShellDocumentWindowMgr`
   (`SaveDocumentWindowPositions` / `ReopenDocumentWindows`) persists the real layout as an opaque
   blob — the tradeoff is losing the ability to filter out files missing on the target branch.
3. Open Folder support.
4. Bookmarks, then breakpoints.
