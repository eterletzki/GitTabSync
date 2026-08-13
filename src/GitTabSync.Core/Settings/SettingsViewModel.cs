using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace GitTabSync.Settings
{
    /// <summary>
    /// Backs the settings window: which scopes can be picked here and now, what each setting is at
    /// the picked one, and every override stored in this repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In Core, with no WPF and no Visual Studio references, so all of it is testable — which
    /// matters more here than usual, because nothing in the VSIX has coverage at all. The window
    /// itself should stay thin enough to be obviously right by inspection.
    /// </para>
    /// <para>
    /// Not thread-safe, and not meant to be: it feeds data bindings, so it belongs to the UI
    /// thread. <c>BranchChanged</c> arrives on a timer thread, so whatever calls
    /// <see cref="UpdateContext"/> has to marshal first.
    /// </para>
    /// </remarks>
    public sealed class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsResolver _resolver;

        // Assigned through the build methods the constructor calls; the compiler cannot see that.
        private SyncContext _context = null!;
        private SettingScopeChoice _selectedScope = null!;
        private IReadOnlyList<SettingScopeChoice> _scopes = null!;
        private IReadOnlyList<SyncSettingRow> _rows = null!;
        private IReadOnlyList<SettingOverrideRow> _overrides = null!;

        public SettingsViewModel(SettingsResolver resolver, SyncContext context, string repositoryName)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            RepositoryName = repositoryName ?? string.Empty;

            Rebuild(context ?? throw new ArgumentNullException(nameof(context)), preserveSelectedKind: false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string RepositoryName { get; private set; }

        /// <summary>The branch, or a short commit id when HEAD is detached.</summary>
        public string HeadText => SettingScopeLabel.ForHead(_context.HeadKey);

        /// <summary>Broadest first, so the picker reads Defaults → repository → branch → project.</summary>
        public IReadOnlyList<SettingScopeChoice> Scopes => _scopes;

        public SettingScopeChoice SelectedScope
        {
            get => _selectedScope;
            set
            {
                if (value is null || ReferenceEquals(value, _selectedScope))
                {
                    return;
                }

                _selectedScope = value;
                Raise(nameof(SelectedScope));
                BuildRows();
            }
        }

        public IReadOnlyList<SyncSettingRow> Rows => _rows;

        public IReadOnlyList<SettingOverrideRow> Overrides => _overrides;

        public bool HasOverrides => _overrides.Count != 0;

        /// <summary>
        /// Points the window at a new situation — a branch switch, or a different active document.
        /// </summary>
        public void UpdateContext(SyncContext context, string? repositoryName = null)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (repositoryName is not null)
            {
                RepositoryName = repositoryName;
                Raise(nameof(RepositoryName));
            }

            // Staying on the same *kind* of scope across a switch is what a user expects: having
            // chosen to look at branch settings, they want the new branch's, not to be dropped
            // back to the repository level.
            Rebuild(context, preserveSelectedKind: true);
        }

        /// <summary>Re-reads everything, for after settings changed somewhere else.</summary>
        public void Refresh() => Rebuild(_context, preserveSelectedKind: true);

        /// <summary>
        /// Removes one stored override, including on branches other than the current one — which
        /// is the whole reason the list exists.
        /// </summary>
        public void ClearOverride(SettingOverrideRow row)
        {
            if (row is null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            _resolver.Set(row.Scope, row.Setting, null);
            BuildRows();
            BuildOverrides();
        }

        private void Rebuild(SyncContext context, bool preserveSelectedKind)
        {
            var previousKind = preserveSelectedKind ? _selectedScope?.Kind : null;

            _context = context;
            BuildScopes(previousKind);
            BuildRows();
            BuildOverrides();

            Raise(nameof(HeadText));
        }

        private void BuildScopes(SettingScopeKind? preferredKind)
        {
            var choices = new List<SettingScopeChoice>();

            // The chain is narrowest first; a picker reads better the other way round.
            for (var i = _context.ScopeChain.Count - 1; i >= 0; i--)
            {
                choices.Add(new SettingScopeChoice(_context.ScopeChain[i]));
            }

            _scopes = choices;
            _selectedScope = Select(choices, preferredKind);

            Raise(nameof(Scopes));
            Raise(nameof(SelectedScope));
        }

        /// <summary>
        /// Defaults to the branch. Settings reach over a branch, so that is the level a user
        /// arriving at this window is nearly always asking about.
        /// </summary>
        private static SettingScopeChoice Select(
            IReadOnlyList<SettingScopeChoice> choices,
            SettingScopeKind? preferredKind)
        {
            if (preferredKind is not null)
            {
                foreach (var choice in choices)
                {
                    if (choice.Kind ==preferredKind.Value)
                    {
                        return choice;
                    }
                }
            }

            foreach (var choice in choices)
            {
                if (choice.Kind ==SettingScopeKind.Branch)
                {
                    return choice;
                }
            }

            // No readable HEAD: the chain is repository and defaults only.
            return choices[choices.Count - 1];
        }

        private void BuildRows()
        {
            var rows = new List<SyncSettingRow>(SyncSettingCatalog.All.Count);

            foreach (var setting in SyncSettingCatalog.All)
            {
                rows.Add(new SyncSettingRow(_resolver, _selectedScope.Scope, _context, setting, BuildOverrides));
            }

            _rows = rows;
            Raise(nameof(Rows));
        }

        private void BuildOverrides()
        {
            var rows = new List<SettingOverrideRow>();

            foreach (var info in _resolver.Overrides)
            {
                rows.Add(new SettingOverrideRow(info));
            }

            _overrides = rows;
            Raise(nameof(Overrides));
            Raise(nameof(HasOverrides));
        }

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
