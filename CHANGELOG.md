# Changelog

Notable changes to Git Tab Sync, newest first.

This file is read by the extension itself: it is embedded in `GitTabSync.Core` and parsed by
`ChangelogParser` to build the **What's New** page. Only three things in it are parsed —

- `## <version> [separator] [date]` begins a release. The separator may be a dash, an em dash, a
  colon or nothing at all, and the date is free text.
- `### <heading>` begins a group of changes within that release.
- `-`, `*` or `+` begins one change. A line indented under one continues it, and the two are
  joined with a space.

— and **everything else here is prose that only people read**, including this paragraph. Write
normally around the structure.

Two rules matter when editing it:

- **An `## Unreleased` section is skipped, with everything under it.** It carries no version, so
  it cannot be matched against the build the user is running, and every item in it is a promise
  the installed build cannot keep. Releasing means renaming that heading to the version and date.
- **The version in a heading must eventually match a shipped build.** A test asserts that this
  file has an entry for the version in `Directory.Build.props`, so a release whose notes were
  never written fails the build rather than shipping a page with nothing on it.

## Unreleased

### Added

- A **What's New** page, shown once after a first install or an upgrade and reopenable from
  View > Other Windows > Git Tab Sync: What's New. It reads this file, shows the releases between
  the version whose notes were last seen and the one now running, and records that it has been
  shown — but only if that record can be read back afterwards, since a page that cannot remember
  being dismissed would open on every startup for the life of the install.
- `state.json` beside `settings.json` and `ui.json`, holding the version whose release notes have
  been shown.
- A build check that the assembly version and the VSIX manifest version agree. They are read by
  different things — the manifest by Visual Studio's installer, the assembly by the What's New
  page — and a build where they disagree announces a version nobody installed.

### Fixed

- The What's New page no longer deadlocks Visual Studio. Showing it from inside the package's
  initialisation hung the IDE on the first solution opened after an upgrade: the shell will not
  create a tool window until the package has finished initialising, and the package cannot finish
  while it is blocked creating one. The page is now shown after the shell has started, through the
  asynchronous API, so nothing waits on the UI thread.

## 1.0.1

The first build worth installing. Everything below is implemented and covered by tests in the
core; the Visual Studio layer compiles and packages but has not yet been run inside the IDE.

### Added

- Per-branch tab sessions. The documents open on a branch are remembered and restored when you
  switch back to it, including caret position and pinned state.
- Branch detection that does not depend on Visual Studio. `.git/HEAD` is watched *and* polled, so
  a branch switched from a terminal, GitHub Desktop or any other git client is noticed just the
  same — which is the whole reason the extension exists.
- Detached HEAD, linked worktrees and submodules, each keyed separately so a branch named after a
  commit cannot collide with the detached state at that commit.
- Scoped settings, cascading over Defaults, repository, branch, solution and project. Each setting
  at each level is on, off or inherit, and inherit is a real third state: "off on this branch" and
  "nothing set on this branch" stay distinguishable.
- A settings window under View > Other Windows > Git Tab Sync, showing the value that applies at
  the selected scope, which level decided it, and every override stored in the repository.
- Themes. The window's colours, fonts and spacing are a `ResourceDictionary` read at run time from
  `%LOCALAPPDATA%\GitTabSync\themes`; three ship, and copying one is how you make your own.
- A **Git Tab Sync** pane in the Output window recording every decision the extension makes,
  which is the only way to see why an automatic action happened.

### Notes

- Sessions, settings and themes are stored under `%LOCALAPPDATA%\GitTabSync`, never in the working
  tree — anything kept there would be rewritten by the very checkout the session exists to survive.
- Documents with unsaved changes are never closed, and a document whose dirty state cannot be
  determined is assumed dirty.
- Arriving on a branch with nothing remembered leaves your tabs alone by default, because every
  branch is unvisited the first time you use the extension.
