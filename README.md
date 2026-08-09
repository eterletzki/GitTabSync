# Git Tab Sync for Visual Studio

Visual Studio does not remember open tabs per branch. If you work across several branches, every
switch throws away your working set and you rebuild it by hand.

Git Tab Sync remembers which documents were open on each branch and restores them when you switch
back — including when the branch is switched **outside** Visual Studio.

Tabs are the focus. Bookmarks and breakpoints are a possible later step, deliberately not started.

## Status

Early. The core — branch detection, storage, and the save/restore logic — is implemented and covered
by 82 tests, including tests that drive the real `git` executable.

The Visual Studio integration layer compiles and packages into an installable `.vsix`, but **has not
yet been exercised inside Visual Studio**. Treat it as unproven.

## How it works

### Detecting the branch

The branch can change from the command line, another git client, or a rebase in a different tool, so
the extension cannot wait to be told. It watches `.git/HEAD` with a `FileSystemWatcher` *and* polls it
on a slow timer.

The poll is not redundant. File watchers drop events when their buffer overflows and are unreliable on
network and virtualised paths, and a missed switch means silently saving one branch's tabs under
another branch's name. The poll makes the worst case *late* instead of *wrong*.

HEAD is read directly from disk rather than by running `git.exe` — cheap enough to poll, and it never
spawns a process on the UI thread. Linked worktrees, submodules and detached HEAD are all handled.

### Saving the right tabs

A branch switch is noticed *after* it has happened. By then Visual Studio may already have closed tabs
for files the checkout deleted, so reading the editor at that moment no longer describes the branch
you just left.

So the extension keeps a continuously refreshed snapshot of the open tabs and saves *that* against the
outgoing branch — never a reading taken at switch time.

### Restoring

Sessions are stored per (repository, branch) under `%LOCALAPPDATA%\GitTabSync`, outside the working
tree — anything kept inside it would be rewritten by the very checkout the session exists to survive.

On restore, documents that do not exist on the incoming branch are skipped, documents already open are
left alone rather than closed and reopened, and **documents with unsaved changes are never closed**.

Switching to a branch you have never visited leaves your tabs alone by default; there is an option to
close them instead.

## Building

Requires the .NET SDK and MSBuild. The *Visual Studio extension development* workload is **not**
required — the VSIX packaging targets come from NuGet.

```powershell
# Tests
dotnet test tests\GitTabSync.Core.Tests\GitTabSync.Core.Tests.csproj

# The extension
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    GitTabSync.slnx /p:Configuration=Release /t:Rebuild /restore
```

The package lands at `src\GitTabSync.Vsix\bin\Release\net472\GitTabSync.Vsix.vsix`. It targets Visual
Studio 2022 and 2026 (`[17.0, 19.0)`).

## Layout

| Project | Role |
|---|---|
| `src/GitTabSync.Core` | Branch detection, storage, sync logic. No Visual Studio dependency. |
| `src/GitTabSync.Vsix` | The extension: adapts the Visual Studio shell to the core. |
| `tests/GitTabSync.Core.Tests` | Tests for the core, including real-`git` integration tests. |

Everything that can be tested without Visual Studio is deliberately kept out of the VSIX, so the
interesting decisions are covered by fast tests that need nothing installed.

## Known gaps

- The Visual Studio layer has never been run in Visual Studio.
- Tab *order* is approximate, and split panes and tab groups are not captured.
- Open Folder mode is not handled — solutions only.
