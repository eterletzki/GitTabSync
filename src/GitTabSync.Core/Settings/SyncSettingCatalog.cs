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
