using static SearchCacher.ISearcher;

namespace SearchCacher.Data
{
	internal interface ISearchService
	{
		public const string Version = "1.0.6.0";

		public string SearchPath { get; }

		bool AllowOnlyLocalSettingsAccess();

		Task<IEnumerable<SearchResult>> GetSearchResult(SearchSettings settings);

		Task InitDB(CancellationToken token);

		Task InitDB(string configID, CancellationToken token);

		void DelDB(string? searchPath = null);

		void AddPath(string searchPath, string path);

		void UpdatePath(string searchPath, string oldPath, string newPath);

		void DeletePath(string searchPath, string path);

		void SaveDB();

		void CleanUp();

		WebConfigModel GetWebConfigModel();

		WebStatisticsModel GetFileStatistics();

        IgnoreListEntry[] GetIgnoreList(WebDBConfigModel cfg);

		bool SetIgnoreList(string guid, List<IgnoreListEntry> ignoreList);
	}

	internal class SearchService : ISearchService
	{
		static List<WeakReference<Action<string>>> initDirChangedHandlers = new List<WeakReference<Action<string>>>();
		static object handlerLock = new object();

		public string SearchPath => string.Join(",", _cfg?.DBConfigs.Select(x => "\"" + x.RootPath + "\"").ToArray() ??[]);

		private ISearcher _searchHandler;
		private Config _cfg;

		internal SearchService(Config cfg)
		{
			_cfg = cfg;

			if (_cfg.DBConfigs.Count == 0)
				_cfg.DBConfigs.Add(new DBConfig() { RootPath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "fileDB") });

			_searchHandler                   = new FileDBSearcher(_cfg, cfg.AutoSaveInterval);
			_searchHandler.InitDir += _searchHandler_InitDir;

			if (cfg.AutoSaveEnabled)
				_searchHandler.StartAutoSave();
		}

		public bool AllowOnlyLocalSettingsAccess() => _cfg.AllowOnlyLocalSettingsAccess;

		public Task<IEnumerable<SearchResult>> GetSearchResult(SearchSettings settings) => Task.FromResult(_searchHandler.Search(settings));

		public Task InitDB(CancellationToken token) => _searchHandler.InitDB(_cfg.DBConfigs, token);

		public Task InitDB(string configID, CancellationToken token) => _searchHandler.InitDB(configID, token, false);

		public void DelDB(string? guid) => _searchHandler.DelDB(guid);

		public void AddPath(string searchPath, string path) => _searchHandler.AddPath(searchPath, path);

		public void UpdatePath(string searchPath, string oldPath, string newPath) => _searchHandler.UpdatePath(searchPath, oldPath, newPath);

		public void DeletePath(string searchPath, string path) => _searchHandler.DeletePath(searchPath, path);

		public void SaveDB() => _searchHandler.SaveDB();

		public void CleanUp() => _searchHandler.Shutdown();

		public WebConfigModel GetWebConfigModel() => new WebConfigModel(_cfg);

        public WebStatisticsModel GetFileStatistics() => new WebStatisticsModel(_searchHandler.GetStatistics());

        public IgnoreListEntry[] GetIgnoreList(WebDBConfigModel cfg) => cfg.IgnoreList.ToArray();

		public bool SetIgnoreList(string guid, List<IgnoreListEntry> ignoreList)
		{
			var config = _cfg.DBConfigs.Find(x => x.ID == guid);
			if (config is null)
				return false;

			config.IgnoreList = ignoreList;
			Program.SetNewConfig(_cfg, false);
			return true;
		}

		internal static void SubscribeToInitDir(Action<string> callback)
		{
			lock (handlerLock)
				initDirChangedHandlers.Add(new WeakReference<Action<string>>(callback));
		}

		private void _searchHandler_InitDir(string dir)
		{
			lock (handlerLock)
			{
				for (int i = initDirChangedHandlers.Count - 1; i >= 0; i--)
				{
					WeakReference<Action<string>> handler = initDirChangedHandlers[i];
					if (!handler.TryGetTarget(out var action))
						initDirChangedHandlers.RemoveAt(i);

					action?.Invoke(dir);
				}
			}
		}
    }

    internal class DummySearchService : ISearchService
    {
        public string SearchPath => "";

        public bool AllowOnlyLocalSettingsAccess() => false;

        public Task<IEnumerable<SearchResult>> GetSearchResult(SearchSettings settings) => Task.FromResult<IEnumerable<SearchResult>>([new ISearcher.SearchResult(false, Array.Empty<string>(), "Cannot run search, searcher not initialized. Most likely because of an invalid config")]);

        public Task InitDB(CancellationToken token) => Task.CompletedTask;

        public Task InitDB(string configID, CancellationToken token) => Task.CompletedTask;

        public void DelDB(string? searchPath) { }

        public void AddPath(string searchPath, string path) { }

        public void UpdatePath(string searchPath, string oldPath, string newPath) { }

        public void DeletePath(string searchPath, string path) { }

        public void SaveDB() { }

        public void CleanUp() { }

        public WebConfigModel GetWebConfigModel() => new WebConfigModel(new Config());

        public WebStatisticsModel GetFileStatistics() => new WebStatisticsModel([]);

        public IgnoreListEntry[] GetIgnoreList(WebDBConfigModel cfg) => [];

        public bool SetIgnoreList(string guid, List<IgnoreListEntry> ignoreList) => true;
    }
}
