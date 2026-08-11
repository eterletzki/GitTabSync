using System;
using System.Collections.Generic;
using GitTabSync.Sync;

namespace GitTabSync.Tests.TestSupport
{
    /// <summary>
    /// Captures what the core logged. Locked, because the timing tests write to it from timer
    /// threads while the test thread reads.
    /// </summary>
    public sealed class RecordingTabSyncLog : ITabSyncLog
    {
        private readonly object _gate = new object();
        private readonly List<string> _messages = new List<string>();
        private readonly List<Exception?> _errors = new List<Exception?>();

        public IReadOnlyList<string> Messages
        {
            get { lock (_gate) { return _messages.ToArray(); } }
        }

        public IReadOnlyList<Exception?> Errors
        {
            get { lock (_gate) { return _errors.ToArray(); } }
        }

        public void Info(string message)
        {
            lock (_gate)
            {
                _messages.Add(message);
            }
        }

        public void Error(string message, Exception? exception)
        {
            lock (_gate)
            {
                _errors.Add(exception);
            }
        }
    }
}
