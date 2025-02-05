namespace SearchCacher
{
	internal class SearchSettings
	{
		public string Pattern { get; set; } = "*.*";

		public bool SearchOnlyFileExt { get; set; } = false;

		public bool SearchOnFullPath { get; set; } = false;

		public bool SearchDirs { get; set; } = true;

		public bool SearchFiles { get; set; } = true;

		public bool CaseSensitive { get; set; } = false;

		public List<SearchPathSettings> SearchPaths { get; } = [];

		public SearchSettings(string pattern = "*.*", bool searchOnlyFileExt = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false, bool caseSensitive = false)
		{
			Pattern           = pattern;
			SearchOnlyFileExt = searchOnlyFileExt;
			SearchDirs        = searchDirs;
			SearchFiles       = searchFiles;
			SearchOnFullPath  = searchOnFullPath;
			CaseSensitive     = caseSensitive;
		}
	}

	internal class SearchPathSettings
	{
		internal string ID { get; }

		public string SearchPath { get; }

		public bool UseIgnoreList { get; }

		public SearchPathSettings(string id, string searchPath, bool useIgnoreList)
		{
			ID            = id;
			SearchPath    = searchPath;
			UseIgnoreList = useIgnoreList;
		}
	}
}
