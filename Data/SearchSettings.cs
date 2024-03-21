namespace SearchCacher
{
	internal class SearchSettings
	{
		public string Pattern { get; set; } = "*.*";

		public bool IsFileExt = false;

		public bool SearchOnFullPath = false;

		public bool SearchDirs = true;

		public bool SearchFiles = true;

		public SearchSettings(string pattern = "*.*", bool isFileExt = false, bool searchDirs = true, bool searchFiles = true, bool searchOnFullPath = false)
		{
			Pattern          = pattern;
			IsFileExt        = isFileExt;
			SearchDirs       = searchDirs;
			SearchFiles      = searchFiles;
			SearchOnFullPath = searchOnFullPath;
		}
	}
}
