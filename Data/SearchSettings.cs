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

		public bool UseIgnoreList { get; set; } = true;

		public string[] IgnoreList { get; set; } = [];

		public string SearchPath { get; set; } = "*";

		public SearchSettings(string pattern = "*.*", bool searchOnlyFileExt = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false, bool caseSensitive = false, bool useIgnoreList = true, string[]? ignoreList = null, string searchPath = "*")
		{
			Pattern           = pattern;
			SearchOnlyFileExt = searchOnlyFileExt;
			SearchDirs        = searchDirs;
			SearchFiles       = searchFiles;
			SearchOnFullPath  = searchOnFullPath;
			CaseSensitive     = caseSensitive;
			UseIgnoreList     = useIgnoreList;
			IgnoreList        = ignoreList ??[];
			SearchPath        = searchPath;
		}
	}
}
