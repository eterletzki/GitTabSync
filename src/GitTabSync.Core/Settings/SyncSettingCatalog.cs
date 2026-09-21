using System;
using System.Collections.Generic;

namespace GitTabSync.Settings
{
    /// <summary>
    /// The stable names and built-in defaults of every <see cref="SyncSetting"/>.
    /// </summary>
    /// <remarks>
    /// The names are part of the on-disk format. Changing one silently discards every override
    /// users have already stored for that setting, because an unrecognised name is skipped on load
    /// rather than guessed at.
    /// </remarks>
    public static class SyncSettingCatalog
    {
        private static readonly SyncSetting[] AllSettings =
        {
            SyncSetting.SyncTabs,
            SyncSetting.SyncBookmarks,
            SyncSetting.SyncBreakpoints,
            SyncSetting.CloseTabsWhenBranchHasNoSession,
            SyncSetting.RestoreOnStartup,
        };

        public static IReadOnlyList<SyncSetting> All => AllSettings;

        public static string NameOf(SyncSetting setting)
        {
            switch (setting)
            {
                case SyncSetting.SyncTabs:
                    return "syncTabs";
                case SyncSetting.SyncBookmarks:
                    return "syncBookmarks";
                case SyncSetting.SyncBreakpoints:
                    return "syncBreakpoints";
                case SyncSetting.CloseTabsWhenBranchHasNoSession:
                    return "closeTabsWhenBranchHasNoSession";
                case SyncSetting.RestoreOnStartup:
                    return "restoreOnStartup";
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        }

        public static bool TryParse(string? name, out SyncSetting setting)
        {
            foreach (var candidate in AllSettings)
            {
                if (string.Equals(NameOf(candidate), name, StringComparison.Ordinal))
                {
                    setting = candidate;
                    return true;
                }
            }

            setting = default;
            return false;
        }

        /// <summary>The label a settings UI shows for this setting.</summary>
        public static string DisplayNameOf(SyncSetting setting)
        {
            switch (setting)
            {
                case SyncSetting.SyncTabs:
                    return "Tabs";
                case SyncSetting.SyncBookmarks:
                    return "Bookmarks";
                case SyncSetting.SyncBreakpoints:
                    return "Breakpoints";
                case SyncSetting.CloseTabsWhenBranchHasNoSession:
                    return "Close tabs on an unvisited branch";
                case SyncSetting.RestoreOnStartup:
                    return "Restore when a solution opens";
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        }

        public static string DescriptionOf(SyncSetting setting)
        {
            switch (setting)
            {
                case SyncSetting.SyncTabs:
                    return "Remember which documents are open and restore them on this branch.";
                case SyncSetting.SyncBookmarks:
                    return "Remember bookmarks per branch.";
                case SyncSetting.SyncBreakpoints:
                    return "Remember breakpoints per branch.";
                case SyncSetting.CloseTabsWhenBranchHasNoSession:
                    return "Arriving on a branch with nothing remembered closes the open tabs "
                        + "instead of leaving them.";
                case SyncSetting.RestoreOnStartup:
                    return "Restore as soon as a solution is opened, not only on a later switch.";
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        }

        /// <summary>
        /// False for settings whose feature does not exist yet.
        /// </summary>
        /// <remarks>
        /// The UI shows these, disabled and with the reason: a toggle wired to nothing is
        /// indistinguishable from a broken one, and hiding them entirely hides the shape of what
        /// the settings are for. Delete the entry here when the feature lands — the storage,
        /// cascade and persistence already work, so that is the only change needed.
        /// </remarks>
        public static bool IsImplemented(SyncSetting setting) =>
            setting != SyncSetting.SyncBookmarks && setting != SyncSetting.SyncBreakpoints;

        /// <summary>
        /// The narrowest scope this setting may be set at. Anything narrower neither stores nor
        /// resolves: <see cref="SettingsResolver"/> skips those scopes, and the window offers no
        /// toggle there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Most settings reach all the way down, which is what the cascade is for. The exception is
        /// <see cref="SyncSetting.CloseTabsWhenBranchHasNoSession"/>, which stops at
        /// <see cref="SettingScopeKind.Repository"/> because a value for it at a *narrower* scope
        /// could never be used. It only has an effect on arrival at a branch with nothing stored,
        /// and the only branch the window can set anything for is the one you are on — which, by
        /// the time you could set it, has a session. A branch, solution or project override of it
        /// is therefore a toggle that can be stored and can never fire, which is the same trap
        /// <see cref="IsImplemented"/> exists to keep out of the window.
        /// </para>
        /// <para>
        /// This is a rule about *where*, not about *whether*: broader scopes are unaffected, and an
        /// override already stored at a scope that is now out of reach stays listed so it can be
        /// cleared. It is skipped when resolving rather than deleted, because deleting a user's
        /// stored value to make a rule true is not a trade this project makes.
        /// </para>
        /// </remarks>
        public static SettingScopeKind NarrowestScopeFor(SyncSetting setting) =>
            setting == SyncSetting.CloseTabsWhenBranchHasNoSession
                ? SettingScopeKind.Repository
                : SettingScopeKind.Project;

        /// <summary>
        /// Whether this setting may be set at this scope kind. Relies on
        /// <see cref="SettingScopeKind"/> being ordered broadest to narrowest.
        /// </summary>
        public static bool IsSettableAt(SyncSetting setting, SettingScopeKind kind) =>
            (int)kind <= (int)NarrowestScopeFor(setting);

        /// <summary>
        /// Why a setting cannot be set at <paramref name="kind"/>, or <c>null</c> when it can.
        /// </summary>
        public static string? ScopeLimitReasonFor(SyncSetting setting, SettingScopeKind kind)
        {
            if (IsSettableAt(setting, kind))
            {
                return null;
            }

            return NarrowestScopeFor(setting) == SettingScopeKind.Repository
                ? "Set for the whole repository, or in Defaults — on one branch it could only "
                    + "apply the first time you arrived there."
                : "Not available at this level.";
        }

        /// <summary>
        /// The value used when nothing has been set at any scope.
        /// </summary>
        /// <remarks>
        /// <see cref="SyncSetting.CloseTabsWhenBranchHasNoSession"/> is off because every branch is
        /// unvisited the first time the extension runs, so the "pure" behaviour would wipe the
        /// user's tabs on first use. The two reserved features are off because they do not exist.
        /// </remarks>
        public static bool DefaultFor(SyncSetting setting)
        {
            switch (setting)
            {
                case SyncSetting.SyncTabs:
                    return true;
                case SyncSetting.SyncBookmarks:
                    return false;
                case SyncSetting.SyncBreakpoints:
                    return false;
                case SyncSetting.CloseTabsWhenBranchHasNoSession:
                    return false;
                case SyncSetting.RestoreOnStartup:
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(setting));
            }
        }
    }
}
