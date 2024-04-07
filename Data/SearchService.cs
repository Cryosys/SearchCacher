namespace SearchCacher.Data
{
	internal class SearchService
	{
		public const string Version = "1.0.3.1";

		static List<WeakReference<Action<string>>> currentSearchDirChangedHandlers = new List<WeakReference<Action<string>>>();
		static object handlerLock = new object();

		public string SearchPath => _cfg?.SearchPath ?? string.Empty;

		private ISearcher _searchHandler;
		private Config _cfg;

		internal SearchService(Config cfg)
		{
			_cfg = cfg;

			// _searchHandler                   = new SearchHandler(_cfg.ConnectionString);
			_searchHandler                   = new FileDBSearcher(_cfg.FileDBPath ?? Path.Combine(CryLib.Core.Paths.ExecuterPath, "fileDB"), cfg.AutoSaveInterval);
			_searchHandler.CurrentSearchDir += _searchHandler_CurrentSearchDir;

			if (cfg.AutoSaveEnabled)
				_searchHandler.StartAutoSave();
		}

		internal Task<ISearcher.SearchResult> GetSearchResult(SearchSettings settings) => Task.FromResult(_searchHandler.Search(settings));

		internal Task InitDB() => _searchHandler.InitDB(_cfg.SearchPath);

		internal void DelDB() => _searchHandler.DelDB();

		internal void AddPath(string path) => _searchHandler.AddPath(path);

		internal void UpdatePath(string oldPath, string newPath) => _searchHandler.UpdatePath(oldPath, newPath);

		internal void DeletePath(string path) => _searchHandler.DeletePath(path);

		internal void SaveDB() => _searchHandler.SaveDB();

		internal void CleanUp() => _searchHandler.StopAutoSave();

		internal static void SubscribeToCurrentSearchDir(Action<string> callback)
		{
			lock (handlerLock)
				currentSearchDirChangedHandlers.Add(new WeakReference<Action<string>>(callback));
		}

		private static void _searchHandler_CurrentSearchDir(string dir)
		{
			lock (handlerLock)
			{
				for (int i = currentSearchDirChangedHandlers.Count - 1;  i >= 0; i--)
				{
					WeakReference<Action<string>> handler = currentSearchDirChangedHandlers[i];
					if (!handler.TryGetTarget(out var action))
						currentSearchDirChangedHandlers.RemoveAt(i);

					action?.Invoke(dir);
				}
			}
		}
	}
}
