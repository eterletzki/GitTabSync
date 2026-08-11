namespace GitTabSync.Settings
{
    /// <summary>
    /// One on/off decision the extension makes, resolvable at any <see cref="SettingScope"/>.
    /// </summary>
    /// <remarks>
    /// Members are written to disk by name (see <see cref="SyncSettingCatalog.NameOf"/>) and never
    /// by ordinal, so this list can be reordered or extended without reinterpreting settings files
    /// that already exist.
    /// </remarks>
    public enum SyncSetting
    {
        /// <summary>Save and restore open documents at all. Off means the tabs are left alone.</summary>
        SyncTabs,

        /// <summary>
        /// Reserved for the bookmark feature, which does not exist yet. Nothing reads this: it is
        /// defined now so the storage format does not have to change when the feature lands.
        /// </summary>
        SyncBookmarks,

        /// <summary>Reserved for breakpoints, on the same terms as <see cref="SyncBookmarks"/>.</summary>
        SyncBreakpoints,

        /// <summary>Close everything when arriving on a branch that has no stored session.</summary>
        CloseTabsWhenBranchHasNoSession,

        /// <summary>Restore when a repository is first observed, not only on a later branch switch.</summary>
        RestoreOnStartup,
    }
}
