using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace SearchCacher
{
	[JsonObject("Config")]
	internal class Config
	{
		/// <summary>
		/// Gets or sets the connection string.
		/// This value is only interesting for the redis connection.
		/// </summary>
		[JsonProperty("ConnectionString")]
		internal string ConnectionString { get; set; } = "localhost:6379";

		/// <summary>
		/// Gets or sets the full path of the search.
		/// This value describes the path for the cacher to monitor and search on.
		/// </summary>
		[JsonProperty("SearchPath")]
		internal string SearchPath { get; set; } = "";

		/// <summary>
		/// Gets or sets the list of paths to ignore in the search.
		/// </summary>
		[JsonProperty("IgnoreList")]
		internal List<string> IgnoreList { get; set; } = [];

		/// <summary>
		///	True to allow only localhost/127.0.0.1 access to the settings page.
		/// </summary>
		[JsonProperty("AllowOnlyLocalSettingsAccess")]
		internal bool AllowOnlyLocalSettingsAccess = false;

		/// <summary>
		/// Gets or sets the full path of the database file.
		/// It should (if possible) be stored on a drive that is decently fast on write, read does not matter too much, only the startup will be slower.
		/// The file can get really large depending on the amount of folders/files to cache on.
		/// </summary>
		[JsonProperty("FileDBPath")]
		internal string? FileDBPath { get; set; }

		/// <summary>
		/// *.* is the default filter for everything
		/// </summary>
		[JsonProperty("WatchDogFilter")]
		internal string? WatchDogFilter { get; set; } = "*.*";

		/// <summary>
		/// Gets or sets a value indicating whether the automatic save is enabled.
		/// </summary>
		[JsonProperty("AutoSaveEnabled")]
		internal bool AutoSaveEnabled { get; set; } = true;

		/// <summary> Gets or sets the auto save interval in minutes. </summary>
		[JsonProperty("AutoSaveInterval")]
		internal int AutoSaveInterval { get; set; } = 5;

		/// <summary> Gets or sets the username for a network share. Can include a domain if written like domain\username </summary>
		[JsonProperty("UserName")]
		internal string UserName { get; set; } = "";

		/// <summary> Gets or sets the password for a network share. </summary>
		[JsonProperty("Password")]
		internal string Password { get; set; } = "";

		public Config()
		{
		}

		public Config(WebConfigModel cfg)
		{
			ConnectionString = cfg.ConnectionString;
			SearchPath       = cfg.SearchPath;
			IgnoreList       = cfg.IgnoreList.ToList();
			FileDBPath       = cfg.FileDBPath;
			WatchDogFilter   = cfg.WatchDogFilter;
			AutoSaveEnabled  = cfg.AutoSaveEnabled;
			AutoSaveInterval = cfg.AutoSaveInterval;
			UserName         = cfg.UserName;
			Password         = cfg.Password;
		}
	}

	internal class WebConfigModel
	{
		/// <summary>
		/// Gets or sets the connection string.
		/// This value is only interesting for the redis connection.
		/// </summary>
		internal string ConnectionString { get; set; } = "localhost:6379";

		/// <summary>
		/// Gets or sets the full path of the search.
		/// This value describes the path for the cacher to monitor and search on.
		/// </summary>
		internal string SearchPath { get; set; } = "";

		/// <summary>
		/// Gets or sets the paths to ignore.
		/// </summary>
		internal ObservableCollection<string> IgnoreList { get; set; } = [];

		/// <summary>
		/// Gets or sets the full path of the database file.
		/// It should (if possible) be stored on a drive that is decently fast on write, read does not matter too much, only the startup will be slower.
		/// The file can get really large depending on the amount of folders/files to cache on.
		/// </summary>
		internal string? FileDBPath { get; set; }

		/// <summary>
		/// *.* is the default filter for everything
		/// </summary>
		internal string? WatchDogFilter { get; set; } = "*.*";

		internal bool AutoSaveEnabled { get; set; } = true;

		internal int AutoSaveInterval { get; set; } = 5;

		/// <summary>   Gets or sets the username for a network share. Can include a domain if written like domain\username </summary>
		internal string UserName { get; set; } = "";

		/// <summary>   Gets or sets the password for a network share. </summary>
		internal string Password { get; set; } = "";

		public WebConfigModel(Config cfg)
		{
			ConnectionString = cfg.ConnectionString;
			SearchPath       = cfg.SearchPath;
			IgnoreList       = new(cfg.IgnoreList);
			FileDBPath       = cfg.FileDBPath;
			WatchDogFilter   = cfg.WatchDogFilter;
			AutoSaveEnabled  = cfg.AutoSaveEnabled;
			AutoSaveInterval = cfg.AutoSaveInterval;
			UserName         = cfg.UserName;

			// We do not copy the password for safety reasons as every user could potentially see the password in the web interface.
			// But we still need the password in the WebConfigModel in order to set it to the actual config later
			// Password = cfg.Password;
		}
	}
}
