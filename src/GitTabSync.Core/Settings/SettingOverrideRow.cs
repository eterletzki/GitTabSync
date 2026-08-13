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
        }

        public SettingScope Scope { get; }

        public SyncSetting Setting { get; }

        public string ScopeText { get; }

        public string SettingText { get; }

        public string ValueText { get; }

        public override string ToString() => ScopeText + ": " + SettingText + " " + ValueText;
    }
}
