namespace GitTabSync.Sync
{
    public sealed class TabSyncOptions
    {
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
    }
}
