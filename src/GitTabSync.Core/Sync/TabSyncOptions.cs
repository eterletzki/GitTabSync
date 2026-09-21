using GitTabSync.Settings;

namespace GitTabSync.Sync
{
    /// <summary>
    /// The flat, unscoped form of the settings: one answer per setting, the same everywhere.
    /// </summary>
    /// <remarks>
    /// This is the degenerate case of <see cref="ISyncSettings"/> — a cascade with no levels in it.
    /// It backs Tools &gt; Options, which has no way to express a per-branch value, and it is what
    /// the VSIX still passes. <see cref="SettingsResolver"/> is the real implementation; when the
    /// settings UI can set scopes, this type has nothing left to do.
    /// </remarks>
    public sealed class TabSyncOptions : ISyncSettings
    {
        /// <summary>
        /// Save and restore open documents at all.
        /// </summary>
        public bool SyncTabs { get; set; } = true;

        /// <summary>
        /// What to do when switching to a branch with no stored session.
        /// </summary>
        /// <remarks>
        /// Off by default. Closing everything would be the "pure" behaviour, but the first switch
        /// to any branch after installing the extension has no stored session, so defaulting to
        /// true would wipe the user's tabs the first time they used it. Leaving them open only
        /// costs some tab bleed between branches, which the next save corrects.
        /// </remarks>
        public bool CloseTabsWhenBranchHasNoSession { get; set; }

        /// <summary>
        /// Restore tabs when a repository is first observed (solution open), rather than only on
        /// a subsequent branch switch.
        /// </summary>
        public bool RestoreOnStartup { get; set; } = true;

        /// <summary>
        /// Ignores the context, deliberately: this type predates scoped settings and has nowhere
        /// to put a per-branch answer. Settings it does not model report their built-in default.
        /// </summary>
        public bool IsEnabled(SyncSetting setting, SyncContext context)
        {
            switch (setting)
            {
                case SyncSetting.SyncTabs:
                    return SyncTabs;
                case SyncSetting.CloseTabsWhenBranchHasNoSession:
                    return CloseTabsWhenBranchHasNoSession;
                case SyncSetting.RestoreOnStartup:
                    return RestoreOnStartup;
                default:
                    return SyncSettingCatalog.DefaultFor(setting);
            }
        }
    }
}
