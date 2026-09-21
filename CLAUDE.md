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
| `tests/GitTabSync.Core.Tests` | net9.0 | 224 tests, incl. real-`git` and real-timer timing tests. |

Core targets netstandard2.0 specifically so one assembly is consumable by both the .NET Framework
VSIX and the modern test project.

### Where things live

```
Core/Git/       GitRepository (discovery + HEAD read), GitHead (parse/identity), BranchMonitor
Core/Model/     TabSession, TabEntry — the persisted shape, DataContract-annotated
Core/Settings/  SyncSetting + SyncSettingCatalog (names/defaults/labels), SettingScope(Kind),
                SyncContext (the cascade), ScopedSettings (persisted), UiPreferences (not
                scoped — the theme), ISettingsStore/FileSettingsStore, ISyncSettings,
                SettingsResolver, ResolvedSetting, and the window's model: SettingsViewModel,
                SyncSettingRow, SettingScopeChoice, SettingOverrideRow, SettingScopeLabel
Core/Storage/   ISessionStore, FileSessionStore, StorageKey (internal),
                RecoverableStorageFailure (internal, shared swallow policy)
Core/Sync/      TabSyncCoordinator (the decisions), TabSessionMapper, IEditorTabs, EditorTab,
                CaptureScheduler (when the editor is read), TabSyncOptions, ITabSyncLog
Core/Theming/   ThemeLibrary (discovery, seeding, the choice), ThemeInfo, BuiltInTheme (the
                VSIX supplies the markup), ThemeFile (internal, reads the name out of the XML),
                ThemeSelectionViewModel
Vsix/           GitTabSyncPackage (autoload, solution events, the menu command),
                RepositorySyncSession (RDT subscription, one per open repo; owns the resolver),
                VsEditorTabs (the only file touching editor windows), GitTabSyncToolWindow +
                SettingsWindowControl.xaml(.cs), GitTabSyncPackage.vsct, OutputWindowLog
Vsix/Theming/   ThemeHost (parses a theme, dresses a control), and the shipped themes:
                VisualStudio.xaml (default, holds the key contract), Compact.xaml, Plain.xaml
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

### Scoped settings

Settings cascade over five levels. A branch is the unit settings reach over; solution and project
narrow *within* a branch rather than standing beside it, so a project override is a statement about
that project **on that branch**.

| Asked | Scope | Key |
|---|---|---|
| 1st | `Project` | `branch/release/1.0.1｜src/…/Core.csproj` |
| 2nd | `Solution` | `branch/release/1.0.1｜GitTabSync.slnx` |
| 3rd | **`Branch`** | `branch/release/1.0.1` — the default granularity |
| 4th | `Repository` | (one settings file per repo, so no key) |
| 5th | `Global` | the defaults |

Four things here are load-bearing.

**A setting declares how far down it reaches.** `SyncSettingCatalog.NarrowestScopeFor` is the rule;
everything else asks `IsSettableAt`. `CloseTabsWhenBranchHasNoSession` stops at `Repository`, because
a value for it at a narrower scope could be stored and could never fire: it only acts on arrival at a
branch with *nothing stored*, and the only branch the window can set anything for is the one you are
on, which by then has a session. That is the same trap `IsImplemented` exists to keep out of the
window, so it gets the same treatment — the row stays, the toggle does not.

The rule is enforced in three places and each one matters. `Resolve` **skips** out-of-reach scopes
rather than obeying them, because a settings file can be hand-edited and the window must not say one
thing while the coordinator does another. `Set` throws for a *value* at such a scope but always
allows `null`: clearing has to keep working, or an override stored before the rule existed could
never be removed. And `SettingOverrideRow.IsActive` marks a stranded override in the list instead of
hiding it — stored but silently ignored, with nothing saying so, is the worst of the three states.
Deleting stored values to make the rule true is deliberately *not* done.

**Absence is the third state.** A stored override is present or it is not; there is no `false`
meaning "unset". "Off here" and "not set here" have to stay distinguishable or the cascade collapses
into whichever level was written last. `SettingsResolver.Set(scope, setting, null)` clears, `Resolve`
returns the built-in default with `IsExplicit = false`, and several tests in `SettingsResolverTests`
exist only to hold that line — `Off_at_a_narrow_scope_survives_on_at_a_broad_one` and
`A_value_set_to_the_same_as_the_default_is_still_explicit` are the two that matter.

**Settings are resolved per decision, never held.** `TabSyncCoordinator` takes an `ISyncSettings` and
calls it with a `SyncContext` built from *the head that decision is about*. Resolving once and
keeping the answer is not a performance question: `OnBranchChanged` is the one code path where the
outgoing and incoming heads are guaranteed to differ, so a held answer applies the wrong branch's
configuration on every switch. `Settings_are_resolved_at_each_switch_not_captured_when_the_session_starts`
is the test; it changes a setting mid-session from another branch and expects the next switch to obey.

**Scope identity follows the same case split as `StorageKey`.** Head keys compare case-*sensitively*
(git refs), paths case-*insensitively* (Windows). `SettingScope` lower-cases the path half of its key
at construction so ordinal comparison is the whole rule afterwards.

Storage: defaults in `%LOCALAPPDATA%\GitTabSync\settings.json`, everything else in
`repos\<repo-key>\settings.json` beside the sessions. The split is enforced on load as well as on
save — a `Branch` scope hand-written into the defaults file is ignored, or one repository's branch
override would apply to every repository with a branch of that name. Scope kinds and setting names
are stored as text, never as enum ordinals, so the enums can be reordered; an unrecognised value is
skipped rather than guessed at.

`TabSyncOptions` implements `ISyncSettings` as the degenerate zero-scope case — one answer
everywhere, ignoring the context. Nothing in the VSIX passes it any more; it survives as
`TabSyncCoordinator`'s null-object default and as the convenient shorthand in tests that are not
about scoping. Replacing it with a `SyncSettingCatalog`-backed default object would be tidier and
would touch a lot of test arrange code for no behaviour change.

### The settings window

`SettingsViewModel` is in **Core**, with no WPF and no VS references, so scope selection, the
tri-state values, the origin text and the override list are all covered by `dotnet test`. That is
deliberate: the VS layer has no coverage at all, so the rule is that the window holds no decision
worth testing. `SettingsWindowControl.xaml.cs` binds, follows the solution, and marshals — nothing
else belongs there.

Three details are easy to undo by accident:

- **The tri-state binds directly.** `SyncSettingRow.Value` is `bool?` and the check box is
  `IsThreeState="True"`; `null` is inherit on both sides, so there is no converter and no place for
  the third state to be flattened. Introducing a `bool` anywhere in that chain re-creates exactly
  the bug the cascade exists to avoid.
- **`EffectiveText` distinguishes inherited from overridden.** A value from a *narrower* scope than
  the selected one reads "overridden on …", not "inherited from …". Getting this backwards is how a
  user concludes a toggle is broken when it is being overruled from below.
- **The window reaches the package through `GitTabSyncPackage.Instance`, set in the constructor.**
  Not through `ToolWindowPane.Package`, and not from `InitializeAsync`: a docked window is restored
  at startup and can be constructed while the package is still initialising.

`RepositorySyncSession` owns the `SettingsResolver` (it needs the working directory, which only
exists after `GitRepository.Discover`) and hands the same instance to both the coordinator and the
window, which is why a toggle needs no change notification to take effect — the coordinator re-reads
at every decision. The one thing that does need a nudge is applying a setting to the branch you are
already on, which is what `RestoreCurrent`/the **Restore tabs now** button is for. Doing it
implicitly on a settings change would rearrange the editor while somebody was reading a checkbox.

**Tools > Options is gone.** `GitTabSyncOptionsPage` was deleted along with `[ProvideOptionPage]`:
once the window can edit the `Global` scope, a property grid that silently ignores scope is a second
and weaker way to set the same things. Nothing shipped to a Marketplace, so there were no stored
preferences to migrate.

### Theming

The window's appearance is a file, not code. `SettingsWindowControl.xaml` contains no colour, font
or margin: every one is a `DynamicResource` lookup against a `ResourceDictionary` that `ThemeHost`
parses out of `%LOCALAPPDATA%\GitTabSync\themes` at run time. The key contract is written out at the
top of [VisualStudio.xaml](src/GitTabSync.Vsix/Theming/VisualStudio.xaml), which is the file to
update when a key is added.

The split follows the `IEditorTabs` rule. Core owns everything testable — which files exist, what
they are called, which one is chosen, what is written out when one is missing — and the VSIX owns
the one thing only it can do, parsing markup. `BuiltInTheme` is the seam: Core writes theme *text*
it never interprets, and the VSIX supplies that text from an embedded resource.

Five things here are load-bearing.

- **`DynamicResource`, never `StaticResource`, in the control.** Static would bind to whichever
  theme was loaded when the window was built, so switching would do nothing until the window was
  reopened — and it would *throw* on a key a user's theme omits instead of falling back. Dynamic
  makes a partial theme a valid theme. This is the whole reason theme switching needs no rebuild of
  the window.
- **Themes are always loaded from disk, including the built-in ones.** They are compiled into the
  assembly only so `UseWPF` type-checks them at build time and so there is text to write out; the
  loading path is the same one a user's theme takes. A built-in that loaded from a pack URI would
  be the path that is exercised on every run, and users' themes would be the path that is not.
- **A file that is already there is never overwritten**, so an edit to a shipped theme survives an
  upgrade. The cost is that an improvement to a shipped theme does not reach somebody who edited it;
  deleting the file is how they ask for the new one, and the README written into the folder says so.
- **The fallback chain is chosen → default → `Plain` → nothing.** `Plain.xaml` references no Visual
  Studio types at all, so it is the one most likely to load when another did not; "nothing" is an
  empty dictionary, which renders unstyled rather than failing. A broken theme must never be able to
  stop the window opening — it is a file the user is invited to edit.
- **`ThemeHost` passes a `ParserContext` with `BaseUri`.** That is what lets one theme merge another
  by file name — `Compact.xaml` is `VisualStudio.xaml` plus a few overrides, and a user's theme can
  do the same. Without it a relative `Source` in a theme has nothing to resolve against.

The theme is deliberately **not** in the settings cascade. Everything in that cascade is a
three-state boolean answering "what should happen on this branch"; appearance is neither boolean nor
per-branch, and a window that changed colour on checkout would be a bug, not a feature. It lives in
`UiPreferences` in `ui.json` beside the defaults, and `ThemeLibrary` is one per process rather than
one per session, because the question is answerable with no solution open — which is also why the
Appearance section sits outside `Body` in the XAML and stays visible on the "no repository"
placeholder.

Selection stores an id, not a path, and an id naming no file resolves to the default **without being
cleared**: a theme file can be missing for a moment because it is being edited, and treating that as
"they changed their mind" would silently discard a choice nobody withdrew.

### Decisions worth knowing before changing them

- **Storage lives in `%LOCALAPPDATA%\GitTabSync`**, never in the working tree — anything inside it
  would be rewritten by the very checkout the session exists to survive, and would show as a pending
  change on every switch. Layout: `repos\<repo-key>\<head-key>.json`, plus `settings.json` (the
  defaults), `ui.json` (the theme) and `themes\` at the root. The same rule covers themes: a theme
  file in the working tree would let a checkout change how the IDE looks.
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
- **The settings window has never been opened in Visual Studio.** It compiles, the `.vsct` compiles,
  and the pkgdef registers both `Menus.ctmenu` and the tool window GUID. What is *not* only "it
  builds": the control's markup and all three themes have been loaded into a real WPF runtime
  outside VS, with the VS resource keys stubbed, and rendered with stand-in view models — so the
  layout, the bindings, the shared-size columns, the three-state check boxes, the collapsing note
  text and the merge in `Compact.xaml` are known to work. That harness is not in the repo; it lived
  in a scratch directory, and it stubbed exactly the things only VS can supply.
  Still unverified, and only `devenv /rootsuffix Exp` can settle it: whether the command appears
  under View > Other Windows, whether the pane hosts the control, whether the real `VsBrushes` and
  `VsResourceKeys` styles resolve in both IDE themes (the stub proves the *markup* resolves them,
  not that the shell provides them to a tool window), and whether `GitTabSyncPackage.Instance` is
  populated when a docked window is restored at startup.
- **Nothing supplies a project scope.** The window offers Defaults, repository, branch and — when a
  solution is open — solution. `SettingScopeKind.Project` resolves, persists and is tested, but
  `RepositorySyncSession.CurrentContext` passes no project path, so the level is unreachable from
  the UI. See the attribution work below.
- **`SyncBookmarks` and `SyncBreakpoints` are defined but read by nothing.** They exist so the
  storage format does not have to change when the features land; `SettingsStoreTests` proves they
  round-trip. The window shows them **disabled**, with the reason, via
  `SyncSettingCatalog.IsImplemented` — a toggle wired to nothing must not look like a working one.
  Deleting that entry is the only change the UI needs when a feature arrives.
- **Making the project scope reachable** means `EditorTab.ProjectPath`, a `TabEntry` member at
  `Order = 5` (the `pinned` member is the precedent for appending without a schema bump), an
  `IVsHierarchy` lookup in `VsEditorTabs`, and a rule for documents owned by no project and for
  linked/shared files owned by several. It also makes a stored session a *partial* record: a
  project-scoped `SyncTabs = off` means the session no longer describes every open tab, so the
  closing decided by `CloseTabsWhenBranchHasNoSession` — which is resolved at the repository level,
  not per project — must then leave excluded projects' tabs alone. That needs its own test before
  the feature is believable. Both ends have to change together:
  `TabSyncCoordinator.ContextFor` and `RepositorySyncSession.CurrentContext` must name the same
  project, or the window will set a scope the coordinator never reads. That trap was live for the
  solution scope during this work and is what
  `A_solution_scoped_override_is_honoured_by_the_coordinator` now guards.
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
- Nothing ever prunes `%LOCALAPPDATA%\GitTabSync`; deleted branches leave their session files — and
  now their branch-scoped overrides — behind.
- **Themes are re-read only on demand.** `ThemeLibrary.Reload` is wired to the window's **Reload
  themes** button and nothing else, so a theme edited in place is picked up when asked for rather
  than on save. Watching the folder is the obvious next step and was left out deliberately: a file
  being written is a normal state while somebody is editing one, and re-dressing the window on
  every keystroke in their editor would make authoring a theme harder. If it is added, it wants
  `BranchMonitor`'s shape — a watcher plus a debounce — not a bare `FileSystemWatcher`.
- Bookmarks and breakpoints, per the README's staging.
