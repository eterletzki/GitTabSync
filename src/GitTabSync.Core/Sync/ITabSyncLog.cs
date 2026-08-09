using System;

namespace GitTabSync.Sync
{
    /// <summary>
    /// Minimal sink so the core can report what it did without referencing Visual Studio.
    /// </summary>
    public interface ITabSyncLog
    {
        void Info(string message);

        void Error(string message, Exception? exception);
    }

    /// <summary>Discards everything. Used when no log is supplied.</summary>
    public sealed class NullTabSyncLog : ITabSyncLog
    {
        public static readonly NullTabSyncLog Instance = new NullTabSyncLog();

        private NullTabSyncLog()
        {
        }

        public void Info(string message)
        {
        }

        public void Error(string message, Exception? exception)
        {
        }
    }
}
