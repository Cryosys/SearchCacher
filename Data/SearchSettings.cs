namespace SearchCacher
{
	internal class SearchSettings
	{
		public string Pattern { get; set; } = "*.*";

		public bool SearchOnlyFileExt = false;

		public bool SearchOnFullPath = false;

		public bool SearchDirs = true;

		public bool SearchFiles = true;

		public bool CaseSensitive = false;

		public string SearchPath = "*";

		public SearchSettings(string pattern = "*.*", bool searchOnlyFileExt = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false, bool caseSensitive = false, string searchPath = "*")
		{
			Pattern           = pattern;
			SearchOnlyFileExt = searchOnlyFileExt;
			SearchDirs        = searchDirs;
			SearchFiles       = searchFiles;
			SearchOnFullPath  = searchOnFullPath;
			CaseSensitive     = caseSensitive;
			SearchPath        = searchPath;
		}
	}
}
