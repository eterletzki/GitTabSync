namespace GitTabSync.Settings
{
    /// <summary>One selectable level in the settings UI's scope picker.</summary>
    /// <remarks>
    /// Only scopes in the current <see cref="SyncContext.ScopeChain"/> are offered, so the picker
    /// always describes where the user actually is. Setting something for a branch you are not on
    /// is deliberately not possible here: the overrides list is how those are seen and removed.
    /// </remarks>
    public sealed class SettingScopeChoice
    {
        public SettingScopeChoice(SettingScope scope)
        {
            Scope = scope;
            Label = SettingScopeLabel.For(scope);
        }

        public SettingScope Scope { get; }

        public SettingScopeKind Kind => Scope.Kind;

        public string Label { get; }

        public override string ToString() => Label;
    }
}
