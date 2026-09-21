using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitTabSync.Git;
using GitTabSync.Settings;
using GitTabSync.Theming;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace GitTabSync
{
    /// <summary>
    /// The settings window's view. Glue only: it binds a <see cref="SettingsViewModel"/> and a
    /// <see cref="ThemeSelectionViewModel"/> built in Core, follows the open solution, and marshals
    /// branch changes onto the UI thread.
    /// </summary>
    public partial class SettingsWindowControl : UserControl
    {
        private GitTabSyncPackage? _package;
        private RepositorySyncSession? _session;
        private SettingsViewModel? _model;
        private ThemeSelectionViewModel? _themes;

        public SettingsWindowControl()
        {
            InitializeComponent();

            // Before anything is bound, so the window is never painted unstyled and then
            // re-dressed. A docked window is restored at startup, which is the moment this costs
            // the most and shows the most.
            AttachThemes();
            ApplyTheme();
        }

        public void Attach(GitTabSyncPackage? package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Detach();
            AttachThemes();

            _package = package;
            if (_package is not null)
            {
                _package.SessionChanged += OnSessionChanged;
            }

            Rebind();
        }

        public void Detach()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_package is not null)
            {
                _package.SessionChanged -= OnSessionChanged;
                _package = null;
            }

            UnsubscribeFromSession();
            _model = null;
            Body.DataContext = null;

            // The theme library is one per process and outlives this window, so the subscription
            // has to be given back or a closed window goes on being notified for the life of the
            // IDE.
            if (_themes is not null)
            {
                _themes.ThemeChanged -= OnThemeChanged;
                _themes.Dispose();
                _themes = null;
                Appearance.DataContext = null;
            }
        }

        /// <summary>
        /// Idempotent, because <see cref="Attach"/> detaches first and the constructor has already
        /// done this once.
        /// </summary>
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
                Appearance.DataContext = _themes;
            }
            catch (Exception e)
            {
                // Reading the themes folder is the only thing that can fail here, and a window
                // that cannot offer themes is still a window that configures tab syncing.
                ThemeHost.Instance.Log.Error("The theme picker could not be set up.", e);
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

        private void OnSessionChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Rebind();
        }

        /// <summary>
        /// Rebuilds the view model for whatever repository is open now, or shows the placeholder
        /// when there is none. The Appearance section is left alone either way: a theme is the
        /// user's choice about the window, not about a repository.
        /// </summary>
        private void Rebind()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            UnsubscribeFromSession();

            _session = _package?.CurrentSession;
            if (_session is null)
            {
                _model = null;
                Body.DataContext = null;
                NoRepository.Visibility = Visibility.Visible;
                Body.Visibility = Visibility.Collapsed;
                return;
            }

            _session.BranchChanged += OnBranchChanged;

            _model = new SettingsViewModel(_session.Settings, _session.CurrentContext, NameOf(_session.Repository));
            Body.DataContext = _model;
            NoRepository.Visibility = Visibility.Collapsed;
            Body.Visibility = Visibility.Visible;
        }

        private void UnsubscribeFromSession()
        {
            if (_session is not null)
            {
                _session.BranchChanged -= OnBranchChanged;
                _session = null;
            }
        }

        /// <summary>
        /// Arrives on a <em>timer thread</em>: the branch monitor's watcher and poll both run off
        /// the UI thread, and WPF bindings may only be touched from it.
        /// </summary>
        private void OnBranchChanged(object sender, BranchChangedEventArgs e)
        {
            // VSSDK007 wants this task awaited or joined. There is nothing to wait for: the window
            // is refreshing itself, and if it is closed before the refresh runs the right outcome
            // is for it to be abandoned. FileAndForget is the documented way to say so.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory
                .RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (_session is not null && _model is not null)
                    {
                        _model.UpdateContext(_session.CurrentContext);
                    }
                })
                .FileAndForget("GitTabSync/SettingsWindow/BranchChanged");
#pragma warning restore VSSDK007
        }

        private void OnClearRow(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SyncSettingRow row)
            {
                row.Clear();
            }
        }

        private void OnClearOverride(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is SettingOverrideRow row)
            {
                _model?.ClearOverride(row);
            }
        }

        private void OnRestoreNow(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _session?.RestoreNow();
        }

        private void OnReloadThemes(object sender, RoutedEventArgs e)
        {
            // Re-reads the folder and re-applies, through the library's Changed event: a theme file
            // edited in place is picked up, and one dropped into the folder appears in the picker.
            _themes?.Reload();
            ApplyTheme();
        }

        private void OnOpenThemesFolder(object sender, RoutedEventArgs e)
        {
            var directory = _themes?.ThemesDirectory;
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            try
            {
                // The folder is created when the library loads, but a user who has just deleted it
                // is exactly the user clicking this.
                Directory.CreateDirectory(directory);
                Process.Start("explorer.exe", "\"" + directory + "\"");
            }
            catch (Exception exception)
            {
                ThemeHost.Instance.Log.Error("Failed to open the themes folder at " + directory + ".", exception);
            }
        }

        private static string NameOf(GitRepository repository)
        {
            var name = Path.GetFileName(
                repository.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return string.IsNullOrEmpty(name) ? repository.WorkingDirectory : name;
        }
    }
}
