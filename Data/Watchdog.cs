using System.Collections.Concurrent;

namespace SearchCacher
{
    internal class Watchdog
    {
        internal event Action<Watchdog, List<WatchedEventArgs>>? Watched;

        internal List<string> CurrentWatchPaths { get; private set; } = [];

        private List<FileSystemWatcher> _watchers = [];
        private BlockingCollection<WatchedEventArgs> _blocks = new();
        private Thread? _eventThread;

        private int _maxBulkActionSize = 15;

        public Watchdog(int maxBulkActionSize)
        {
            _maxBulkActionSize = maxBulkActionSize;
        }

        internal void AddWatcher(string searchPath, string? filter = "*.*")
        {
            if (_watchers.Any(x => x.Path == searchPath))
                return;

            var watcher = new FileSystemWatcher(searchPath);
            watcher.BeginInit();
            watcher.Filter = filter ?? "*.*";
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.DirectoryName;
            watcher.Created += Watcher_Created;
            watcher.Deleted += Watcher_Deleted;
            watcher.Renamed += Watcher_Renamed;
            watcher.Error += Watcher_Error;

            // 64 KB is max described by https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.internalbuffersize?view=net-8.0
            // However, apparently you can set values higher than 64KB?
            watcher.InternalBufferSize = 64 * 1024;
            watcher.EndInit();

            watcher.IncludeSubdirectories = true;

            _watchers.Add(watcher);
            CurrentWatchPaths.Add(searchPath);
        }

        internal void RemoveWatcher(string searchPath)
        {
            var watcher = _watchers.Find(x => x.Path == searchPath);
            if (watcher is null)
                return;

            _watchers.Remove(watcher);
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();

            CurrentWatchPaths.Remove(searchPath);
        }

        internal void Start()
        {
            if (_watchers is null)
                throw new NullReferenceException(nameof(_watchers));

            _eventThread = new Thread(EventInvokerThread);
            _eventThread.Start();

            foreach (var watcher in _watchers)
                watcher.EnableRaisingEvents = true;
        }

        internal void Stop()
        {
            if (_watchers is null)
                throw new NullReferenceException(nameof(_watchers));

            foreach (var watcher in _watchers)
                watcher.EnableRaisingEvents = false;

            _blocks.CompleteAdding();
            _eventThread?.Join();
        }

        private void EventInvokerThread()
        {
            // Check that no more work is left to do, otherwise complete it first before stopping the thread
            while (!_blocks.IsAddingCompleted || _blocks.Count > 0)
            {
                List<WatchedEventArgs> events = [];

                // Get UP TO x events and send them to the DB to process.
                // This improves throughput due to the locking mechanism of the master lock,
                // less locking in total.
                do
                {
                    if (!_blocks.TryTake(out var e, 1000))
                        break;

                    events.Add(e);
                }
                while (events.Count < _maxBulkActionSize);

                // Only invoke if we actualy collected events
                if (events.Count > 0)
                    Watched?.Invoke(this, events);
            }
        }

        private void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, ((FileSystemWatcher)sender).Path, e.FullPath)))
                Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath}");
        }

        private void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, ((FileSystemWatcher)sender).Path, e.FullPath)))
                Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath}");
        }

        private void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, ((FileSystemWatcher)sender).Path, e.FullPath, e.OldFullPath)))
                Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath};{e.OldFullPath}");
        }

        private static void Watcher_Error(object sender, System.IO.ErrorEventArgs e) => CryLib.Core.LibTools.ExceptionManager.AddException(e.GetException());

        internal struct WatchedEventArgs
        {
            internal WatcherChangeTypes ChangeType { get; }

            internal string FullPath { get; }

            internal string OldFullPath { get; }

            internal string SearchPath { get; }

            public WatchedEventArgs(WatcherChangeTypes changeType, string searchPath, string fullPath, string oldFullPath = "")
            {
                ChangeType = changeType;
                SearchPath = searchPath;
                FullPath = fullPath;
                OldFullPath = oldFullPath;
            }
        }
    }
}
