using System;
using System.IO;
using System.Threading;

namespace GitTabSync.Git
{
    /// <summary>
    /// Actively watches a repository's HEAD and raises <see cref="BranchChanged"/> when it moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because a branch switch can originate anywhere — the command line, another
    /// git client, a rebase in a different tool — so the extension cannot wait to be told.
    /// </para>
    /// <para>
    /// Two independent detectors run together. A <see cref="FileSystemWatcher"/> gives a
    /// near-instant signal in the normal case, and a slow poll is the safety net: watchers drop
    /// events under buffer overflow and are unreliable on network and virtualised paths, and a
    /// missed switch here means silently saving one branch's tabs under another branch's name.
    /// The poll makes the worst case "late" instead of "wrong".
    /// </para>
    /// <para>
    /// Both detectors funnel into a debounce, because a single checkout touches HEAD more than
    /// once and the intermediate states are not worth reacting to.
    /// </para>
    /// </remarks>
    public sealed class BranchMonitor : IDisposable
    {
        public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);
        public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

        private readonly GitRepository _repository;
        private readonly TimeSpan _debounce;
        private readonly object _gate = new object();

        private FileSystemWatcher? _watcher;
        private Timer? _debounceTimer;
        private Timer? _pollTimer;
        private GitHead? _current;
        private bool _started;
        private bool _disposed;

        public BranchMonitor(GitRepository repository, TimeSpan? debounce = null, TimeSpan? pollInterval = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _debounce = debounce ?? DefaultDebounce;
            PollInterval = pollInterval ?? DefaultPollInterval;
        }

        /// <summary>
        /// Raised after HEAD has settled on a value different from the last observed one.
        /// Raised on a background thread; consumers on the UI thread must marshal.
        /// </summary>
        public event EventHandler<BranchChangedEventArgs>? BranchChanged;

        public TimeSpan PollInterval { get; }

        public GitRepository Repository => _repository;

        /// <summary>The most recently observed HEAD, or <c>null</c> before the first read.</summary>
        public GitHead? Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        /// <summary>
        /// Begins watching. The HEAD present at this moment becomes the baseline and does
        /// <em>not</em> raise <see cref="BranchChanged"/>.
        /// </summary>
        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_started)
                {
                    return;
                }

                _started = true;
                _current = _repository.ReadHead();

                _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);

                if (PollInterval > TimeSpan.Zero)
                {
                    _pollTimer = new Timer(OnPollElapsed, null, PollInterval, PollInterval);
                }

                TryStartWatcher();
            }
        }

        private void TryStartWatcher()
        {
            try
            {
                var watcher = new FileSystemWatcher(_repository.GitDirectory)
                {
                    // Git replaces HEAD by writing HEAD.lock and renaming it over the top, so
                    // the rename is the event that matters as much as the write.
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                };

                watcher.Changed += OnHeadFileEvent;
                watcher.Created += OnHeadFileEvent;
                watcher.Renamed += OnHeadFileEvent;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;

                _watcher = watcher;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // No watcher available (deleted directory, permissions, exotic filesystem).
                // Polling alone still gives correct, if slower, detection.
                _watcher = null;
            }
        }

        private void OnHeadFileEvent(object sender, FileSystemEventArgs e)
        {
            if (!IsHeadFile(e.Name))
            {
                return;
            }

            ScheduleCheck();
        }

        private static bool IsHeadFile(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            // e.Name is relative to the watched directory; compare the leaf only.
            var leaf = Path.GetFileName(name);
            return string.Equals(leaf, "HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // The watcher's buffer overflowed and events were lost; re-read rather than trust
            // that nothing happened.
            ScheduleCheck();
        }

        private void OnPollElapsed(object? state) => CheckNow();

        private void OnDebounceElapsed(object? state) => CheckNow();

        private void ScheduleCheck()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                // Restarting the one-shot timer collapses a burst of file events into a single
                // check once HEAD has been quiet for the debounce interval.
                _debounceTimer?.Change(_debounce, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Reads HEAD immediately and raises <see cref="BranchChanged"/> if it moved.
        /// Exposed so callers can force a check at moments a watcher cannot see, such as a
        /// solution being opened.
        /// </summary>
        public void CheckNow()
        {
            GitHead? previous;
            GitHead current;

            lock (_gate)
            {
                if (_disposed || !_started)
                {
                    return;
                }

                var head = _repository.ReadHead();
                if (head is null)
                {
                    // Unreadable HEAD is treated as "no new information". Deliberately not
                    // reported as a change: doing so would attribute the open tabs to a branch
                    // we are not sure about.
                    return;
                }

                if (head.Equals(_current))
                {
                    return;
                }

                previous = _current;
                current = head;
                _current = head;
            }

            // Raised outside the lock: handlers do real work (saving and restoring tabs) and
            // must not be able to deadlock the monitor.
            BranchChanged?.Invoke(this, new BranchChangedEventArgs(previous, current));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BranchMonitor));
            }
        }

        public void Dispose()
        {
            FileSystemWatcher? watcher;
            Timer? debounceTimer;
            Timer? pollTimer;

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                watcher = _watcher;
                debounceTimer = _debounceTimer;
                pollTimer = _pollTimer;
                _watcher = null;
                _debounceTimer = null;
                _pollTimer = null;
            }

            if (watcher is not null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnHeadFileEvent;
                watcher.Created -= OnHeadFileEvent;
                watcher.Renamed -= OnHeadFileEvent;
                watcher.Error -= OnWatcherError;
                watcher.Dispose();
            }

            debounceTimer?.Dispose();
            pollTimer?.Dispose();
        }
    }
}
