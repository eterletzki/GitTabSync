using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitTabSync.Git;
using GitTabSync.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace GitTabSync
{
    /// <summary>
    /// The settings window's view. Glue only: it binds a <see cref="SettingsViewModel"/> built in
    /// Core, follows the open solution, and marshals branch changes onto the UI thread.
    /// </summary>
    public partial class SettingsWindowControl : UserControl
    {
        private GitTabSyncPackage? _package;
        private RepositorySyncSession? _session;
        private SettingsViewModel? _model;

        public SettingsWindowControl()
        {
            InitializeComponent();
        }

        public void Attach(GitTabSyncPackage? package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Detach();

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
            DataContext = null;
        }

        private void OnSessionChanged(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Rebind();
        }

        /// <summary>
        /// Rebuilds the view model for whatever repository is open now, or shows the placeholder
        /// when there is none.
        /// </summary>
        private void Rebind()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            UnsubscribeFromSession();

            _session = _package?.CurrentSession;
            if (_session is null)
            {
                _model = null;
                DataContext = null;
                NoRepository.Visibility = Visibility.Visible;
                Body.Visibility = Visibility.Collapsed;
                return;
            }

            _session.BranchChanged += OnBranchChanged;

            _model = new SettingsViewModel(_session.Settings, _session.CurrentContext, NameOf(_session.Repository));
            DataContext = _model;
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

        private static string NameOf(GitRepository repository)
        {
            var name = Path.GetFileName(
                repository.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            return string.IsNullOrEmpty(name) ? repository.WorkingDirectory : name;
        }
    }
}
