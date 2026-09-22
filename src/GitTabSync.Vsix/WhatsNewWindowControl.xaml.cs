using System;
using System.Diagnostics;
using System.Windows.Controls;
using GitTabSync.Release;
using GitTabSync.Theming;
using Microsoft.VisualStudio.Shell;

namespace GitTabSync
{
    /// <summary>
    /// The What's New page's view. Glue only: it binds a <see cref="LandingPageViewModel"/> built
    /// in Core and dresses itself in the current theme.
    /// </summary>
    /// <remarks>
    /// Simpler than the settings window because there is nothing live about it. The page describes
    /// a release, which does not change while somebody is reading it, so there is no session to
    /// follow and no branch change to marshal — only the theme, which the user can switch from the
    /// other window while this one is open.
    /// </remarks>
    public partial class WhatsNewWindowControl : UserControl
    {
        private ThemeSelectionViewModel? _themes;

        public WhatsNewWindowControl()
        {
            InitializeComponent();

            // Before anything is bound, so the page is never painted unstyled and then re-dressed.
            // This window is shown during startup on an upgrade, which is the moment that would
            // show the most.
            AttachThemes();
            ApplyTheme();
        }

        public void Attach(GitTabSyncPackage? package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            AttachThemes();

            // The package decides which page this is: the one an upgrade queued, or the whole
            // history when somebody opened the window from the menu. Without a package there is
            // still a page to show — the changelog is embedded in Core and needs nothing from the
            // shell — so a window restored before initialisation finished is not left blank.
            Body.DataContext = package is not null
                ? package.CreateLandingPageModel()
                : new LandingPageViewModel(LandingPageDecision.OnRequest(ShippedRelease.Version, ShippedRelease.Notes));
        }

        public void Detach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Body.DataContext = null;

            // The theme library is one per process and outlives this window, so the subscription
            // has to be given back or a closed window goes on being notified for the life of the
            // IDE.
            if (_themes is not null)
            {
                _themes.ThemeChanged -= OnThemeChanged;
                _themes.Dispose();
                _themes = null;
            }
        }

        /// <summary>
        /// Idempotent, because <see cref="Attach"/> may run after the constructor has done it.
        /// </summary>
        /// <remarks>
        /// This window shows no theme picker — appearance belongs to the settings window — but it
        /// subscribes all the same, so that switching theme there re-dresses this page instead of
        /// leaving two windows disagreeing about how the extension looks.
        /// </remarks>
        private void AttachThemes()
        {
            if (_themes is not null)
            {
                return;
            }

            try
            {
                _themes = new ThemeSelectionViewModel(ThemeHost.Instance.Library);
                _themes.ThemeChanged += OnThemeChanged;
            }
            catch (Exception e)
            {
                // A page that cannot follow theme changes is still a readable page.
                ThemeHost.Instance.Log.Error("The What's New page could not follow theme changes.", e);
            }
        }

        private void OnThemeChanged(object sender, EventArgs e) => ApplyTheme();

        private void ApplyTheme()
        {
            try
            {
                ThemeHost.Instance.ApplyTo(this);
            }
            catch (Exception e)
            {
                ThemeHost.Instance.Log.Error("The theme could not be applied.", e);
            }
        }

        /// <remarks>
        /// Given a body rather than an expression, because an expression-bodied handler that
        /// reaches into the shell trips VSTHRD010 — the analyser cannot see the thread affinity
        /// that <see cref="ThreadHelper.ThrowIfNotOnUIThread"/> asserts.
        /// </remarks>
        private void OnOpenSettings(object sender, System.Windows.RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            GitTabSyncPackage.Instance?.ShowSettingsWindow();
        }

        /// <inheritdoc cref="OnOpenSettings"/>
        private void OnOpenProject(object sender, System.Windows.RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // UseShellExecute is the default on .NET Framework, which is what opens a URL in
                // the user's browser rather than trying to execute it.
                Process.Start(LandingPageViewModel.ProjectUrl);
            }
            catch (Exception exception)
            {
                ThemeHost.Instance.Log.Error(
                    "Failed to open " + LandingPageViewModel.ProjectUrl + ".", exception);
            }
        }
    }
}
