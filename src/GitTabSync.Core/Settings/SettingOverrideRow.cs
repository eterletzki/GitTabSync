namespace GitTabSync.Settings
{
    /// <summary>One row in the "overrides in this repository" list.</summary>
    public sealed class SettingOverrideRow
    {
        public SettingOverrideRow(SettingOverrideInfo info)
        {
            Scope = info.Scope;
            Setting = info.Setting;
            ScopeText = SettingScopeLabel.For(info.Scope);
            SettingText = SyncSettingCatalog.DisplayNameOf(info.Setting);
            ValueText = info.Value ? "on" : "off";
            IsActive = SyncSettingCatalog.IsSettableAt(info.Setting, info.Scope.Kind);
        }

        /// <summary>
        /// False for an override stored at a scope the setting no longer reaches — hand-edited, or
        /// written by a build whose rules differed. It is still listed, and still clearable: the
        /// whole point of this list is that an override nobody can see is an override nobody can
        /// undo, and one that is stored but ignored is the worst of both.
        /// </summary>
        public bool IsActive { get; }

        /// <summary>Empty when the override applies, so a UI can bind it unconditionally.</summary>
        public string NoteText => IsActive ? string.Empty : " — stored, but not used at this level";

        public SettingScope Scope { get; }

        public SyncSetting Setting { get; }

        public string ScopeText { get; }

        public string SettingText { get; }

        public string ValueText { get; }

        public override string ToString() => ScopeText + ": " + SettingText + " " + ValueText;
    }
}
