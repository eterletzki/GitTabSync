# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Visual Studio extension that remembers which documents were open on each git branch and restores
them when you switch back — including branch switches made outside Visual Studio. Tabs are the only
feature in scope; bookmarks and breakpoints are a deliberate later step.

[README.md](README.md) is the user-facing description (settings, storage format, troubleshooting).
This file is the working notes: build commands, why the code is shaped the way it is, and what is
unverified. Keep both accurate when behaviour changes — the README's status table and the test count
in both files are the things that go stale first.

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
| `tests/GitTabSync.Core.Tests` | net9.0 | 118 tests, incl. real-`git` and real-timer timing tests. |

Core targets netstandard2.0 specifically so one assembly is consumable by both the .NET Framework
VSIX and the modern test project.

### Where things live

```
Core/Git/       GitRepository (discovery + HEAD read), GitHead (parse/identity), BranchMonitor
Core/Model/     TabSession, TabEntry — the persisted shape, DataContract-annotated
Core/Storage/   ISessionStore, FileSessionStore, StorageKey (internal)
Core/Sync/      TabSyncCoordinator (the decisions), TabSessionMapper, IEditorTabs, EditorTab,
                CaptureScheduler (when the editor is read), TabSyncOptions, ITabSyncLog
Vsix/           GitTabSyncPackage (autoload + solution events), RepositorySyncSession (RDT
                subscription, one per open repo), VsEditorTabs (the only file touching editor
                windows), GitTabSyncOptionsPage, OutputWindowLog
```

The seam is `IEditorTabs`: Core never sees a VS type, the VSIX never makes a sync decision. New
logic belongs in Core with a test; only shell plumbing belongs in the VSIX.

### Flow of one branch switch

`.git/HEAD` changes → `BranchMonitor` (watcher or poll) → debounce → `CheckNow` reads and compares →
`BranchChanged` on a **timer thread** → `TabSyncCoordinator.OnBranchChanged` saves the *stored
snapshot* under the outgoing head, then loads the incoming head's session, drops entries whose files
are missing, and calls `IEditorTabs.ApplyTabs` → `VsEditorTabs` marshals to the UI thread, closes
what is not wanted (never a dirty document), opens the rest, applies pin state and restores carets.

Meanwhile, independently: RDT document events → `RepositorySyncSession.ScheduleCapture` →
`CaptureScheduler.Schedule` (300 ms debounce, because events arrive in bursts while documents are
still half-open) → `TabSyncCoordinator.CaptureSnapshot` → reads the editor and replaces the
snapshot.

The Pin Tab command calls `CaptureScheduler.CaptureNow` instead, which **skips the debounce** — one
settled action rather than a burst, and the delay is precisely what would lose a pin to a branch
switch made right after it. That subscription is best-effort and has never been observed firing;
what actually guarantees the pin is saved is `TabSyncCoordinator.RefreshPinnedState`, below.

### Delays, and where they are tested

Every wait in the system is deliberate, and each one is pinned by a test against real timers
(`CaptureSchedulerTests`, `BranchMonitorTests.Timing`) — a fake clock would prove only that the
arithmetic is right. `CaptureScheduler` lives in Core rather than the VSIX for exactly this reason:
timing logic that cannot be tested is timing logic that drifts.

| Wait | Where | Why |
|---|---|---|
| 250 ms | `BranchMonitor.DefaultDebounce` | One checkout touches HEAD several times; the intermediate values are real but meaningless. |
| 5 s | `BranchMonitor.DefaultPollInterval` | Safety net for a watcher that dropped its events. Checks directly, without the debounce. |
| 300 ms | `CaptureScheduler.DefaultDelay` | Document events arrive while the document is still half-open. |
| none | `CaptureScheduler.CaptureNow` | The Pin Tab command. Waiting is what loses the pin.  |

The tests are one-sided on purpose: "not yet" is asserted at a fraction of the delay, "eventually"
waits ten seconds. A loaded machine is allowed to be slow, not wrong. If you change a number, the
first test in `CaptureSchedulerTests` fails until the docs above agree with the code.

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

**The one deliberate exception is `RefreshPinnedState`**, which does read the editor as the session
is written. It is safe because it changes no membership and no order: it walks the snapshot and
updates *only* the pinned flag, *only* for paths the editor still reports as open. A tab the
checkout already closed keeps its last known flag; a tab the checkout opened is ignored. It exists
because pinning is reported by nothing — the snapshot's pinned flags have no lower bound on how
stale they are, unlike the tab set, which document events do keep current. Caret position is
pointedly *not* refreshed the same way: a checkout reloads changed files and can move the caret, so
the live value there may be worse than the snapshot's. Three tests in `TabSyncCoordinatorTests`
hold this line — if you widen the refresh beyond the pinned flag, they are the ones that will fail,
and they are right.

### Decisions worth knowing before changing them

- **Storage lives in `%LOCALAPPDATA%\GitTabSync`**, never in the working tree — anything inside it
  would be rewritten by the very checkout the session exists to survive, and would show as a pending
  change on every switch. Layout: `repos\<repo-key>\<head-key>.json`.
- **Session keys are `readable prefix + hash`** ([StorageKey](src/GitTabSync.Core/Storage/StorageKey.cs)).
  Branch names contain characters illegal in filenames, and sanitising alone maps `feature/foo` and
  `feature-foo` onto one file. Repo keys are case-*insensitive* (Windows paths); head keys are
  case-*sensitive* (git refs). Both halves must match that, prefix included.
- **`GitHead.SessionKey` is prefixed** (`branch/` or `detached/`) so a branch literally named after a
  commit id cannot collide with the detached state at that commit.
- **Serialisation is `DataContractJsonSerializer`**, chosen over Newtonsoft/System.Text.Json so the
  VSIX ships no extra assemblies and cannot hit binding-redirect conflicts with the copies VS already
  has loaded. It escapes `/` as `\/` — valid JSON, left alone deliberately. Adding a field means a
  `[DataMember(Order = …)]`; an incompatible reshape means bumping `TabSession.CurrentSchemaVersion`
  (a newer schema on disk is ignored, not half-read).
- **Paths are stored repository-relative with `/` separators** where possible, so sessions survive
  the repo being moved or re-cloned; files outside the repo are stored absolute.
- **Saves are write-temp-then-replace.** A crash mid-write must not leave a truncated file where a
  good session was.
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
an escaped exception on a timer thread takes the process down. `OutputWindowLog` uses
`OutputStringThreadSafe` for the same reason (with a deliberate `VSTHRD010` suppression).

### Diagnostics

Everything the extension decides goes to the **Git Tab Sync** pane in the VS Output window via
`ITabSyncLog`. It is the only way to see why an automatic action happened, so prefer adding a log
line over adding silence when you extend the sync path.

## Testing notes

Tests use real temp directories rather than a mocked filesystem, because the behaviour that matters
(file locking, rename-over-the-top, path casing) is exactly what a mock would not reproduce.
`FakeEditorTabs` and `InMemorySessionStore` in `TestSupport/` stand in for the two interfaces.

### `HostWiringTests`, and why it exists

`FakeEditorTabs` is a passive store: what a test puts in is what comes out. That made a whole class
of bug invisible. Pinned-tab support shipped broken three times with a green suite, because the
coordinator tests called `CaptureSnapshot()` by hand before switching branches — supplying the one
input the production path could not produce. **A test that arranges the broken step cannot fail
because of it.**

[HostWiringTests](tests/GitTabSync.Core.Tests/HostWiringTests.cs) closes that gap. `FakeHostEditor`
models Visual Studio's *reporting* behaviour, not just its state: `OpenAndReport` and
`ActivateAndReport` raise an event, while `PinSilently`, `MoveCaretSilently` and `CloseSilently`
change the editor and raise nothing — which is what really happens. Tests wire that event to a real
`CaptureScheduler` exactly as `RepositorySyncSession` does, and **nothing in that file may call
`CaptureSnapshot` directly.** The only route into the snapshot is a real notification through a real
debounce, so the tests fail if the wiring is wrong or if the extension starts depending on being
told about something the host never reports.

Three of them fail against the pre-fix coordinator; I checked. If you add a sync behaviour that
depends on a host notification, add it here rather than to `TabSyncCoordinatorTests`, and resist
the urge to "just capture" in the arrange step.

Two things in that file are load-bearing and easy to undo by accident. The `Reporting(...)` helper
takes its read-count baseline **before** running the host action; taking it afterwards races the
scheduler, and a capture that already ran leaves the test waiting ten seconds for one that is never
coming, then blaming the wiring. And in the pin tests the baseline is taken *after* the silent pin,
so whichever capture satisfies the wait has provably seen it — wait for "some capture" without that
ordering and the test passes against an unpinned snapshot, proving nothing.

### The failure paths

"Storage and restore failures are swallowed and logged, not thrown" is a design rule, and the five
`catch` blocks in `TabSyncCoordinator` are what implement it. The failure tests at the bottom of
`TabSyncCoordinatorTests` drive each one by making `FakeEditorTabs`/`InMemorySessionStore` throw on
demand (`FailOnSave`, `FailOnLoad`, `FailOnGetOpenTabs`, `FailOnApplyTabs`). They assert behaviour
rather than which handler ran, so they survive the handling being moved — but delete the handling
and they fail.

The one worth understanding is `A_failing_restore_does_not_stop_later_captures`, with
`A_restore_leaves_the_capture_path_working` as its counterpart through the real wiring. `_applying`
is set before `ApplyTabs` and cleared in a `finally`; if a throw could leave it set, **every capture
from then on is dropped in silence** and the extension goes on saving whatever was open at the
moment of that restore. A permanently stuck `_applying` was run against the whole suite: only these
two tests noticed.

`RealGitIntegrationTests` shells out to real `git` — a real checkout, a real detached HEAD, and a real
linked worktree. The unit tests write HEAD themselves, which proves parsing but assumes how git
updates the file; only these prove the watcher subscribes to the events that actually fire. They skip
if git is not on PATH. **Keep them passing** — they cover the project's central risk.

`Directory.Build.props` sets `TreatWarningsAsErrors`. The VSIX project opts out (the VS SDK reference
assemblies are not nullable-annotated); Core does not — keep it warning-clean.

## Not built yet

- No coverage of the Visual Studio layer at all. `VsEditorTabs`, `RepositorySyncSession` and
  `GitTabSyncPackage` compile and package but **have never been run inside Visual Studio**. Treat
  their behaviour as unverified. This is the next thing that matters.
- Options are read once, when a session starts. Changing them in Tools > Options does not affect the
  open solution — there is no `DialogPage` change subscription.
- **`GitRepository.ReadHead`'s retry loop is untested**, and cannot be tested honestly as written:
  5 attempts × 20 ms hard-codes a ~80 ms window, so any test of it is a race that a loaded machine
  loses — "slow" becomes "wrong", which is the one thing the timing tests here refuse to be. Making
  the attempt count and delay constructor parameters, the way `CaptureScheduler` takes its delay,
  would make it testable; that is the reason to do it.
- **`TabSyncCoordinator.OnBranchChanged` claims a serialisation it does not provide.** The comment
  says a rapid sequence of switches cannot interleave a save of one branch with a restore of
  another, but the `lock` is released after the `_disposed` check and the save and restore both run
  outside it. The poll timer and the debounce timer are separate threads, so two transitions in
  quick succession genuinely can overlap. Either the lock should span the handler or the comment
  should stop promising it. No test covers this, and the fake editor is synchronous enough that
  none currently could.
- `RepositorySyncSession.CheckForBranchChange()` and `ISessionStore.Delete` exist with no production
  caller (only tests). They are hooks for work not done, not dead code to delete blindly.
- Tab *order* is approximate: `IVsUIShell.GetDocumentWindowEnum` does not promise tab order, and
  restore reopens documents rather than rebuilding the layout. Split panes and tab groups are not
  captured. If layout fidelity becomes the goal, `IVsUIShellDocumentWindowMgr`
  (`SaveDocumentWindowPositions`/`ReopenDocumentWindows`) persists the real layout as an opaque blob —
  the tradeoff is losing the ability to filter out files missing on the target branch.
- Open Folder mode is not handled; only solutions (`SolutionExists` autoload).
- Only caret line/column and pinned state are persisted per tab — no scroll position, selection or
  folding.
- **The Pin Tab command subscription is unverified and no longer load-bearing.** Pin state is a
  frame property (`__VSFPROPID5.VSFPROPID_IsPinned`); toggling it raises no RDT document event, and
  `IVsWindowFrameEvents` 1/2/3 have no pin callback, so `RepositorySyncSession` subscribes to the
  command itself (`VSStd11CmdID.PinTab`, filtered — DTE also allows an unfiltered subscription that
  fires for every command in the IDE). It has never been observed firing, and pins were still lost
  with it in place, which is why `RefreshPinnedState` exists. Kept because it costs one filtered
  subscription and makes the snapshot right earlier when it does work; the log line it writes is
  how to tell whether it ever does. Deleting it should change no behaviour — if it does, that is
  worth knowing.
- Nothing ever prunes `%LOCALAPPDATA%\GitTabSync`; deleted branches leave their session files behind.
- Bookmarks and breakpoints, per the README's staging.
