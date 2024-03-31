using System.Collections.Concurrent;

namespace SearchCacher
{
	internal class Watchdog
	{
		internal event Action<WatchedEventArgs>? Watched;

		internal string CurrentWatchPath { get; private set; } = string.Empty;

		private FileSystemWatcher? _watcher;
		private BlockingCollection<WatchedEventArgs>? _blocks;
		private Thread? _eventThread;

		public Watchdog()
		{
		}

		internal void Init(string watchpath, Config config)
		{
			CurrentWatchPath = watchpath;
			_watcher         = new FileSystemWatcher(CurrentWatchPath);
			_watcher.BeginInit();
			_watcher.Filter       = config.WatchDogFilter ?? "*.*";
			_watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.DirectoryName;
			_watcher.Created     += Watcher_Created;
			_watcher.Deleted     += Watcher_Deleted;
			_watcher.Renamed     += Watcher_Renamed;
			_watcher.Error       += Watcher_Error;

			// 64 KB is max described by https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.internalbuffersize?view=net-8.0
			// However, apparently you can set values higher than 64KB?
			_watcher.InternalBufferSize = 64 * 1024;
			_watcher.EndInit();

			_watcher.IncludeSubdirectories = true;
		}

		internal void Start()
		{
			if (_watcher is null)
				throw new NullReferenceException(nameof(_watcher));

			_blocks      = new BlockingCollection<WatchedEventArgs>();
			_eventThread = new Thread(EventInvokerThread);
			_eventThread.Start();

			_watcher.EnableRaisingEvents = true;
		}

		internal void Stop()
		{
			if (_watcher is null)
				throw new NullReferenceException(nameof(_watcher));

			_watcher.EnableRaisingEvents = false;

			_blocks?.CompleteAdding();
			_eventThread?.Join();
		}

		private void EventInvokerThread()
		{
			while (!_blocks.IsAddingCompleted || _blocks.Count > 0)
				if (_blocks.TryTake(out var e, 1000))
					Watched?.Invoke(e);
		}

		private void Watcher_Created(object sender, FileSystemEventArgs e)
		{
			if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, e.FullPath)))
				Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath}");
		}

		private void Watcher_Deleted(object sender, FileSystemEventArgs e)
		{
			if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, e.FullPath)))
				Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath}");
		}

		private void Watcher_Renamed(object sender, RenamedEventArgs e)
		{
			if (!_blocks.TryAdd(new WatchedEventArgs(e.ChangeType, e.FullPath, e.OldFullPath)))
				Program.Log($"Unable to add watchdog event: {e.ChangeType} -> {e.FullPath};{e.OldFullPath}");
		}

		private static void Watcher_Error(object sender, System.IO.ErrorEventArgs e) => CryLib.Core.LibTools.ExceptionManager.AddException(e.GetException());

		internal struct WatchedEventArgs
		{
			internal WatcherChangeTypes ChangeType { get; }

			internal string FullPath { get; }

			internal string OldFullPath { get; }

			public WatchedEventArgs(WatcherChangeTypes changeType, string fullPath, string oldFullPath = "")
			{
				ChangeType  = changeType;
				FullPath    = fullPath;
				OldFullPath = oldFullPath;
			}
		}
	}
}
