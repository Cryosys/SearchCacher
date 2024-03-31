using Newtonsoft.Json;

namespace SearchCacher
{
	[JsonObject("Config")]
	internal class Config
	{
		[JsonProperty("ConnectionString")]
		internal string ConnectionString { get; set; } = "localhost:6379";

		[JsonProperty("SearchPath")]
		internal string SearchPath { get; set; } = "";

		[JsonProperty("FileDBPath")]
		internal string? FileDBPath { get; set; }

		/// <summary>
		/// *.* is the default filter for everything
		/// </summary>
		[JsonProperty("WatchDogFilter")]
		internal string? WatchDogFilter { get; set; } = "*.*";
	}
}
