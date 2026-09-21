namespace GitTabSync.Settings
{
    /// <summary>
    /// Answers "is this on, here?".
    /// </summary>
    /// <remarks>
    /// The context is a parameter rather than something the implementation is constructed with,
    /// and that is the whole point: settings can differ per branch, so an answer is only valid for
    /// the branch it was asked about. Anything that resolves a setting once and keeps the answer
    /// will apply the outgoing branch's configuration to the incoming one.
    /// </remarks>
    public interface ISyncSettings
    {
        bool IsEnabled(SyncSetting setting, SyncContext context);
    }
}
