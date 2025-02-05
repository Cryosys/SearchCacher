using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace SearchCacher
{
	[JsonObject("Config")]
	internal class Config
	{
		/// <summary>
		///	True to allow only localhost/127.0.0.1 access to the settings page.
		/// </summary>
		[JsonProperty("AllowOnlyLocalSettingsAccess")]
		internal bool AllowOnlyLocalSettingsAccess = false;

		/// <summary>
		/// Gets or sets a value indicating whether the automatic save is enabled.
		/// </summary>
		[JsonProperty("AutoSaveEnabled")]
		internal bool AutoSaveEnabled { get; set; } = true;

		/// <summary> Gets or sets the auto save interval in minutes. </summary>
		[JsonProperty("AutoSaveInterval")]
		internal int AutoSaveInterval { get; set; } = 5;

		[JsonProperty("DBConfigs")]
		internal List<DBConfig> DBConfigs { get; set; } = [];

		public Config()
		{
		}

		public Config(WebConfigModel cfg)
		{
			AutoSaveEnabled  = cfg.AutoSaveEnabled;
			AutoSaveInterval = cfg.AutoSaveInterval;

			foreach (WebDBConfigModel webDBConfig in cfg.DBConfigs)
				DBConfigs.Add(new DBConfig(webDBConfig));
		}
	}

	[JsonObject("DBConfig")]
	internal class DBConfig
	{
		/// <summary>
		/// Gets or initializes the identifier.
		/// </summary>
		[JsonProperty("ID")]
		internal string ID { get; init; }

		/// <summary>
		/// Gets or sets the full path of the search.
		/// This value describes the path for the cacher to monitor and search on.
		/// </summary>
		[JsonProperty("RootPath")]
		internal string RootPath { get; set; } = "";

		/// <summary>
		/// Gets or sets the list of paths to ignore in the search.
		/// </summary>
		[JsonProperty("IgnoreList")]
		internal List<string> IgnoreList { get; set; } = [];

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

		/// <summary> Gets or sets the username for a network share. Can include a domain if written like domain\username </summary>
		[JsonProperty("UserName")]
		internal string UserName { get; set; } = "";

		/// <summary> Gets or sets the password for a network share. </summary>
		[JsonProperty("Password")]
		internal string Password { get; set; } = "";

		public DBConfig()
		{
			ID = Guid.NewGuid().ToString();
		}

		public DBConfig(WebDBConfigModel cfg)
		{
			ID             = cfg.ID;
			FileDBPath     = cfg.FileDBPath;
			RootPath       = cfg.RootPath;
			IgnoreList     = cfg.IgnoreList.ToList();
			WatchDogFilter = cfg.WatchDogFilter;
			UserName       = cfg.UserName;

			// Here we take the password of the webmodel as it may be a new one
			Password = cfg.Password;
		}
	}

	internal class WebConfigModel
	{
		internal bool AutoSaveEnabled { get; set; } = true;

		internal int AutoSaveInterval { get; set; } = 5;

		internal List<WebDBConfigModel> DBConfigs { get; set; } = [];

		public WebConfigModel(Config cfg)
		{
			AutoSaveEnabled  = cfg.AutoSaveEnabled;
			AutoSaveInterval = cfg.AutoSaveInterval;
			DBConfigs        = cfg.DBConfigs.Select(x => new WebDBConfigModel(x)).ToList();
		}
	}

	internal class WebDBConfigModel
	{
		/// <summary>
		/// Gets or initializes the identifier.
		/// </summary>
		internal string ID { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether this config is selected for the search.
		/// This value is a Web UI only property as it is used to prepare the search settings.
		/// </summary>
		internal bool IsSelected { get; set; } = true;

		/// <summary>
		/// Gets or sets a value indicating whether this config should use the ignore list for the search.
		/// This value is a Web UI only property as it is used to prepare the search settings.
		/// </summary>
		internal bool UseIgnoreList { get; set; } = true;

		/// <summary>
		/// Gets or sets the path to search on, this is the actual path for the user search. * is equivalent to <see cref="RootPath"/>.
		/// This value is a Web UI only property as it is used to prepare the search settings.
		/// </summary>
		internal string SearchPath { get; set; } = "*";

		/// <summary>
		/// Gets or sets the full path of the search.
		/// This value describes the path for the cacher to monitor and search on.
		/// </summary>
		internal string RootPath { get; set; } = "";

		/// <summary>
		/// Gets or sets the full path of the database file.
		/// It should (if possible) be stored on a drive that is decently fast on write, read does not matter too much, only the startup will be slower.
		/// The file can get really large depending on the amount of folders/files to cache on.
		/// </summary>
		internal string? FileDBPath { get; set; }

		/// <summary>   Gets or sets the username for a network share. Can include a domain if written like domain\username </summary>
		internal string UserName { get; set; } = "";

		/// <summary>   Gets or sets the password for a network share. </summary>
		internal string Password { get; set; } = "";

		/// <summary>
		/// *.* is the default filter for everything
		/// </summary>
		internal string? WatchDogFilter { get; set; } = "*.*";

		/// <summary>
		/// Gets or sets the paths to ignore.
		/// </summary>
		internal ObservableCollection<string> IgnoreList { get; set; } = [];

		/// <summary>
		/// Gets or sets the path to add to the ignore list.
		/// This property is only used for the UI to represent.
		/// </summary>
		internal string IgnorePathToAdd { get; set; } = "";

		public WebDBConfigModel(DBConfig cfg)
		{
			RootPath       = cfg.RootPath;
			IgnoreList     = new(cfg.IgnoreList);
			FileDBPath     = cfg.FileDBPath;
			WatchDogFilter = cfg.WatchDogFilter;
			UserName       = cfg.UserName;
			ID             = cfg.ID;

			// We do not copy the password for safety reasons as every user could potentially see the password in the web interface.
			// But we still need the password in the WebConfigModel in order to set it to the actual config later
			// Password = cfg.Password;
		}

		public WebDBConfigModel()
		{
			ID = Guid.NewGuid().ToString();
		}
	}
}
