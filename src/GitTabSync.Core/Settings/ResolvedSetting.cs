namespace GitTabSync.Settings
{
    /// <summary>
    /// A setting's value together with where it came from.
    /// </summary>
    /// <remarks>
    /// The origin is not decoration. The question a user actually has about an automatic action is
    /// "why is it doing this here", and a bare true/false cannot answer it — with five levels in
    /// the cascade, the level that decided is the useful half of the answer.
    /// </remarks>
    public sealed class ResolvedSetting
    {
        public ResolvedSetting(SyncSetting setting, bool value, SettingScope origin, bool isExplicit)
        {
            Setting = setting;
            Value = value;
            Origin = origin;
            IsExplicit = isExplicit;
        }

        public SyncSetting Setting { get; }

        public bool Value { get; }

        /// <summary>
        /// The scope that supplied <see cref="Value"/>, or <see cref="SettingScope.Global"/> when
        /// nothing was set anywhere and the built-in default applied.
        /// </summary>
        public SettingScope Origin { get; }

        /// <summary>
        /// True when a user set this somewhere; false when it is the built-in default. Both report
        /// a global origin, so this is what tells "Off" apart from "Off, because you said so".
        /// </summary>
        public bool IsExplicit { get; }

        /// <summary>True when the value came from a scope narrower than the one asked about.</summary>
        public bool IsInherited(SettingScope scope) => !Origin.Equals(scope);
    }
}
