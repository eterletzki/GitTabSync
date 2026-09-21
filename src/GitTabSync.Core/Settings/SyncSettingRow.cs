using System;
using System.ComponentModel;

namespace GitTabSync.Settings
{
    /// <summary>
    /// One setting, as shown at one selected scope: what is set here, what actually applies, and
    /// which scope decided.
    /// </summary>
    public sealed class SyncSettingRow : INotifyPropertyChanged
    {
        private readonly SettingsResolver _resolver;
        private readonly SyncContext _context;
        private readonly Action _changed;

        internal SyncSettingRow(
            SettingsResolver resolver,
            SettingScope scope,
            SyncContext context,
            SyncSetting setting,
            Action changed)
        {
            _resolver = resolver;
            _context = context;
            _changed = changed;
            Scope = scope;
            Setting = setting;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public SyncSetting Setting { get; }

        /// <summary>The scope this row edits — whatever the picker has selected.</summary>
        public SettingScope Scope { get; }

        public string DisplayName => SyncSettingCatalog.DisplayNameOf(Setting);

        public string Description => SyncSettingCatalog.DescriptionOf(Setting);

        public bool IsImplemented => SyncSettingCatalog.IsImplemented(Setting);

        /// <summary>
        /// Whether this setting reaches the selected scope. False on a row that is perfectly real
        /// but is set somewhere broader — see
        /// <see cref="SyncSettingCatalog.NarrowestScopeFor"/>.
        /// </summary>
        public bool IsSettableHere => SyncSettingCatalog.IsSettableAt(Setting, Scope.Kind);

        /// <summary>
        /// Whether the toggle accepts input. The row itself stays enabled either way: it still
        /// reports what applies here, and greying the whole row would grey the sentence saying why
        /// the toggle is not available along with it.
        /// </summary>
        public bool IsEditable => IsImplemented && IsSettableHere;

        /// <summary>Why the toggle is disabled, or <c>null</c> when it is not.</summary>
        /// <remarks>
        /// "Not implemented" wins over "not available here", because a setting that does nothing
        /// anywhere is the more useful thing to say first.
        /// </remarks>
        public string? UnavailableReason =>
            !IsImplemented
                ? "Not implemented yet — this setting is stored but does nothing."
                : SyncSettingCatalog.ScopeLimitReasonFor(Setting, Scope.Kind);

        /// <summary>
        /// On, off, or inherit. <c>null</c> is inherit, which binds straight onto a three-state
        /// check box's <c>IsChecked</c> without a converter.
        /// </summary>
        public bool? Value
        {
            get => _resolver.GetOverride(Scope, Setting);
            set
            {
                if (value == Value)
                {
                    return;
                }

                _resolver.Set(Scope, Setting, value);
                RaiseChanged();
                _changed();
            }
        }

        /// <summary>What actually applies right now, wherever it was decided.</summary>
        public bool EffectiveValue => _resolver.Resolve(Setting, _context).Value;

        public bool IsSetHere => Value is not null;

        /// <summary>
        /// The value and where it came from — "On, inherited from Defaults", "Off, set here".
        /// </summary>
        /// <remarks>
        /// A scope narrower than the selected one is reported as an override rather than as
        /// inheritance, because "inherited from" would suggest the selected scope is what decides
        /// when it is being overruled. That distinction is the difference between a user believing
        /// a toggle did nothing and understanding why.
        /// </remarks>
        public string EffectiveText
        {
            get
            {
                var resolved = _resolver.Resolve(Setting, _context);
                var state = resolved.Value ? "On" : "Off";

                if (!resolved.IsExplicit)
                {
                    return state + ", default";
                }

                if (resolved.Origin.Equals(Scope))
                {
                    return state + ", set here";
                }

                return (int)resolved.Origin.Kind > (int)Scope.Kind
                    ? state + ", overridden on " + SettingScopeLabel.For(resolved.Origin)
                    : state + ", inherited from " + SettingScopeLabel.For(resolved.Origin);
            }
        }

        /// <summary>Returns this row to inheriting from a broader scope.</summary>
        public void Clear() => Value = null;

        private void RaiseChanged()
        {
            Raise(nameof(Value));
            Raise(nameof(EffectiveValue));
            Raise(nameof(EffectiveText));
            Raise(nameof(IsSetHere));
        }

        private void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
