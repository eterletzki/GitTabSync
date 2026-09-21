namespace GitTabSync.Settings
{
    /// <summary>
    /// One stored override: what was set, where, and to what.
    /// </summary>
    /// <remarks>
    /// Exists so the settings UI can list every override in a repository. An override nobody can
    /// see is an override nobody can undo, and a per-branch setting made months ago on a branch
    /// you have since forgotten is exactly the kind of thing that reads as a bug.
    /// </remarks>
    public sealed class SettingOverrideInfo
    {
        public SettingOverrideInfo(SettingScope scope, SyncSetting setting, bool value)
        {
            Scope = scope;
            Setting = setting;
            Value = value;
        }

        public SettingScope Scope { get; }

        public SyncSetting Setting { get; }

        public bool Value { get; }

        public override string ToString() =>
            Scope + " " + SyncSettingCatalog.NameOf(Setting) + "=" + (Value ? "on" : "off");
    }
}
