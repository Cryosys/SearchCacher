using Microsoft.Extensions.FileSystemGlobbing;

namespace SearchCacher
{
	internal class Watchdog
	{
		internal event Action<WatchedEventArgs>? Watched;

		FileSystemWatcher? _watcher;

		public Watchdog()
		{
		}

		internal void Init(string watchpath)
		{
			_watcher = new FileSystemWatcher(watchpath);
			_watcher.BeginInit();
			_watcher.Filter       = "*.*";
			_watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.DirectoryName;
			_watcher.Created     += Watcher_Created;
			_watcher.Deleted     += Watcher_Deleted;
			_watcher.Renamed     += Watcher_Renamed;
			_watcher.Error       += Watcher_Error;
			_watcher.EndInit();

			_watcher.IncludeSubdirectories = true;
		}

		internal void Start()
		{
			if (_watcher is null)
				throw new NullReferenceException(nameof(_watcher));

			_watcher.EnableRaisingEvents = true;
		}

		internal void Stop()
		{
			if (_watcher is null)
				throw new NullReferenceException(nameof(_watcher));

			_watcher.EnableRaisingEvents = false;
		}

		private void Watcher_Created(object sender, FileSystemEventArgs e)
		{
			Watched?.Invoke(new WatchedEventArgs(e.ChangeType, e.FullPath));
		}

		private void Watcher_Deleted(object sender, FileSystemEventArgs e)
		{
			Watched?.Invoke(new WatchedEventArgs(e.ChangeType, e.FullPath));
		}

		private void Watcher_Renamed(object sender, RenamedEventArgs e)
		{
			Watched?.Invoke(new WatchedEventArgs(e.ChangeType, e.FullPath, e.OldFullPath));
		}

		private static void Watcher_Error(object sender, System.IO.ErrorEventArgs e)
		{
			CryLib.Core.LibTools.ExceptionManager.AddException(e.GetException());
		}

		internal class WatchedEventArgs
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
