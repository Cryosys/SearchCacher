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

		void DelDB(string? searchPath = null);

		void AddPath(string searchPath, string path);

		void UpdatePath(string searchPath, string oldPath, string newPath);

		void DeletePath(string searchPath, string path);

		void SaveDB();

		void CleanUp();

		WebConfigModel GetWebConfigModel();

		IgnoreListEntry[] GetIgnoreList(WebDBConfigModel cfg);

		bool SetIgnoreList(string guid, List<IgnoreListEntry> ignoreList);
	}

	internal class DummySearchService : ISearchService
	{
		public string SearchPath => "";

		public bool AllowOnlyLocalSettingsAccess() => false;

		public Task<IEnumerable<SearchResult>> GetSearchResult(SearchSettings settings) => Task.FromResult<IEnumerable<SearchResult>>([new ISearcher.SearchResult(false, Array.Empty<string>(), "Cannot run search, searcher not initialized. Most likely because of an invalid config")]);

		public Task InitDB(CancellationToken token) => Task.CompletedTask;

		public void DelDB(string? searchPath) { }

		public void AddPath(string searchPath, string path) { }

		public void UpdatePath(string searchPath, string oldPath, string newPath) { }

		public void DeletePath(string searchPath, string path) { }

		public void SaveDB() { }

		public void CleanUp() { }

		public WebConfigModel GetWebConfigModel() => new WebConfigModel(new Config());

		public IgnoreListEntry[] GetIgnoreList(WebDBConfigModel cfg) => [];

		public bool SetIgnoreList(string guid, List<IgnoreListEntry> ignoreList) => true;
	}

	internal class SearchService : ISearchService
	{
		static List<WeakReference<Action<string>>> currentSearchDirChangedHandlers = new List<WeakReference<Action<string>>>();
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
			_searchHandler.CurrentSearchDir += _searchHandler_CurrentSearchDir;

			if (cfg.AutoSaveEnabled)
				_searchHandler.StartAutoSave();
		}

		public bool AllowOnlyLocalSettingsAccess() => _cfg.AllowOnlyLocalSettingsAccess;

		public Task<IEnumerable<SearchResult>> GetSearchResult(SearchSettings settings) => Task.FromResult(_searchHandler.Search(settings));

		public Task InitDB(CancellationToken token) => _searchHandler.InitDB(_cfg.DBConfigs, token);

		public Task InitDB(WebDBConfigModel dbConfig) => _searchHandler.InitDB(dbConfig);

		public void DelDB(string? guid) => _searchHandler.DelDB(guid);

		public void AddPath(string searchPath, string path) => _searchHandler.AddPath(searchPath, path);

		public void UpdatePath(string searchPath, string oldPath, string newPath) => _searchHandler.UpdatePath(searchPath, oldPath, newPath);

		public void DeletePath(string searchPath, string path) => _searchHandler.DeletePath(searchPath, path);

		public void SaveDB() => _searchHandler.SaveDB();

		public void CleanUp() => _searchHandler.StopAutoSave();

		public WebConfigModel GetWebConfigModel() => new WebConfigModel(_cfg);

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
