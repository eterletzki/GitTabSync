using System.ComponentModel;
using System.Runtime.InteropServices;
using GitTabSync.Sync;
using Microsoft.VisualStudio.Shell;

namespace GitTabSync
{
    /// <summary>
    /// Tools &gt; Options &gt; Git Tab Sync.
    /// </summary>
    [Guid("8d41b7c2-9e35-4f06-a1d8-53b2c7e94f61")]
    [ComVisible(true)]
    public sealed class GitTabSyncOptionsPage : DialogPage
    {
        [Category("Branch switching")]
        [DisplayName("Close tabs on an unvisited branch")]
        [Description(
            "When switching to a branch that has no remembered tabs, close everything instead of " +
            "leaving the current tabs open. Off by default, because every branch is unvisited the " +
            "first time you use the extension.")]
        [DefaultValue(false)]
        public bool CloseTabsWhenBranchHasNoSession { get; set; }

        [Category("Branch switching")]
        [DisplayName("Restore tabs when a solution opens")]
        [Description("Restore the remembered tabs for the current branch as soon as a solution is opened.")]
        [DefaultValue(true)]
        public bool RestoreOnStartup { get; set; } = true;

        public TabSyncOptions ToOptions() => new TabSyncOptions
        {
            CloseTabsWhenBranchHasNoSession = CloseTabsWhenBranchHasNoSession,
            RestoreOnStartup = RestoreOnStartup,
        };
    }
}
