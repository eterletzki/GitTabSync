using System;
using System.Threading;

namespace GitTabSync.Sync
{
    /// <summary>
    /// Decides <em>when</em> the open tabs are read, between the host's notifications and
    /// <see cref="TabSyncCoordinator.CaptureSnapshot"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two kinds of notification arrive, and they want opposite treatment. Document events come in
    /// bursts while a document is still half-open or half-closed, so <see cref="Schedule"/>
    /// collapses them and reads a settled editor once the burst stops. A pinned tab, by contrast,
    /// is one finished action reported by exactly one command, so <see cref="CaptureNow"/> reads
    /// immediately — waiting is precisely what would lose the pin to a branch switch made a moment
    /// later.
    /// </para>
    /// <para>
    /// This lives in the core rather than in the VSIX because none of it needs Visual Studio: it is
    /// timing logic, and timing logic that cannot be tested is timing logic that drifts.
    /// </para>
    /// </remarks>
    public sealed class CaptureScheduler : IDisposable
    {
        /// <summary>
        /// How long the editor must be quiet before a scheduled capture runs. Long enough to
        /// outlast the burst of events one document open produces, short enough to be finished
        /// well before anybody switches a branch by hand.
        /// </summary>
        public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

        private readonly Action _capture;
        private readonly ITabSyncLog _log;
        private readonly Timer _timer;
        private readonly object _gate = new object();

        private bool _disposed;

        public CaptureScheduler(Action capture, TimeSpan? delay = null, ITabSyncLog? log = null)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _log = log ?? NullTabSyncLog.Instance;
            Delay = delay ?? DefaultDelay;
            _timer = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public TimeSpan Delay { get; }

        /// <summary>
        /// Asks for a capture once things have been quiet for <see cref="Delay"/>. Calling this
        /// again before then replaces the pending capture rather than adding one.
        /// </summary>
        public void Schedule()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _timer.Change(Delay, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>
        /// Captures right now, on the calling thread, and drops any pending scheduled capture —
        /// what it would have read is what was just read.
        /// </summary>
        public void CaptureNow()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                }
            }

            Run();
        }

        private void Run()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                _capture();
            }
            catch (Exception e)
            {
                // Reached from a timer thread, where an escaping exception takes the whole process
                // down — Visual Studio, with the user's unsaved work in it.
                _log.Error("Failed to capture the open documents.", e);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _timer.Dispose();
        }
    }
}
