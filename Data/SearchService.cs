namespace SearchCacher.Data
{
	internal interface ISearchService
	{
		public const string Version = "1.0.5.0";

		public string SearchPath { get; }

		bool AllowOnlyLocalSettingsAccess();

		Task<ISearcher.SearchResult> GetSearchResult(SearchSettings settings);

		Task InitDB();

		void DelDB();

		void AddPath(string path);

		void UpdatePath(string oldPath, string newPath);

		void DeletePath(string path);

		void SaveDB();

		void CleanUp();

		WebConfigModel GetWebConfigModel();
	}

	internal class DummySearchService : ISearchService
	{
		public string SearchPath => string.Empty;

		public bool AllowOnlyLocalSettingsAccess() => false;

		public Task<ISearcher.SearchResult> GetSearchResult(SearchSettings settings) => Task<ISearcher.SearchResult>.FromResult(new ISearcher.SearchResult(false, Array.Empty<string>(), "Cannot run search, searcher not initialized. Most likely because of an invalid config"));

		public Task InitDB() => Task.CompletedTask;

		public void DelDB() { }

		public void AddPath(string path) { }

		public void UpdatePath(string oldPath, string newPath) { }

		public void DeletePath(string path) { }

		public void SaveDB() { }

		public void CleanUp() { }

		public WebConfigModel GetWebConfigModel() => new WebConfigModel(new Config());
	}

	internal class SearchService : ISearchService
	{
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

		public bool AllowOnlyLocalSettingsAccess() => _cfg.AllowOnlyLocalSettingsAccess;

		public Task<ISearcher.SearchResult> GetSearchResult(SearchSettings settings) => Task.FromResult(_searchHandler.Search(settings));

		public Task InitDB() => _searchHandler.InitDB(_cfg.SearchPath);

		public void DelDB() => _searchHandler.DelDB();

		public void AddPath(string path) => _searchHandler.AddPath(path);

		public void UpdatePath(string oldPath, string newPath) => _searchHandler.UpdatePath(oldPath, newPath);

		public void DeletePath(string path) => _searchHandler.DeletePath(path);

		public void SaveDB() => _searchHandler.SaveDB();

		public void CleanUp() => _searchHandler.StopAutoSave();

		public WebConfigModel GetWebConfigModel() => new WebConfigModel(_cfg);

		internal static void SubscribeToCurrentSearchDir(Action<string> callback)
		{
			lock (handlerLock)
				currentSearchDirChangedHandlers.Add(new WeakReference<Action<string>>(callback));
		}

		private static void _searchHandler_CurrentSearchDir(string dir)
		{
			lock (handlerLock)
			{
				for (int i = currentSearchDirChangedHandlers.Count - 1; i >= 0; i--)
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
