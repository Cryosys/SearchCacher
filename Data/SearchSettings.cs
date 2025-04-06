namespace SearchCacher
{
	internal class SearchSettings
	{
		public string Pattern { get; set; }

		public bool SearchOnlyFileExt { get; set; }

		public bool SearchInFiles { get; set; }

		public bool SearchOnFullPath { get; set; }

		public bool SearchDirs { get; set; }

		public bool SearchFiles { get; set; }

		public bool CaseSensitive { get; set; }

		public string[] FileExtensions { get; } = [];

		public List<SearchPathSettings> SearchPaths { get; } = [];

		public SearchSettings(string pattern = "*.*", bool searchOnlyFileExt = false, bool searchInFiles = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false, bool caseSensitive = false, string[]? fileExtensions = null)
		{
			Pattern           = pattern;
			SearchOnlyFileExt = searchOnlyFileExt;
			SearchInFiles     = searchInFiles;
			SearchDirs        = searchDirs;
			SearchFiles       = searchFiles;
			SearchOnFullPath  = searchOnFullPath;
			CaseSensitive     = caseSensitive;
			FileExtensions    = fileExtensions ??[];
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
