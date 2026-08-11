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
