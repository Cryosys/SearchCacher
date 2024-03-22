namespace SearchCacher
{
	internal class SearchSettings
	{
		public string Pattern { get; set; } = "*.*";

		public bool SearchOnlyFileExt = false;

		public bool SearchOnFullPath = false;

		public bool SearchDirs = true;

		public bool SearchFiles = true;

		public SearchSettings(string pattern = "*.*", bool searchOnlyFileExt = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false)
		{
			Pattern           = pattern;
			SearchOnlyFileExt = searchOnlyFileExt;
			SearchDirs        = searchDirs;
			SearchFiles       = searchFiles;
			SearchOnFullPath  = searchOnFullPath;
		}
	}
}
