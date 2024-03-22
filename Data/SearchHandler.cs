using StackExchange.Redis;

namespace SearchCacher
{
	internal class SearchHandler : ISearcher
	{
		public event Action<string>? CurrentSearchDir;

		RedisDB _dbInterface;

		private string[] _blacklist;

		public SearchHandler(string connectionString, string[]? blacklist = null)
		{
			_blacklist = blacklist ?? Array.Empty<string>();

			_dbInterface = new RedisDB();
			lock (_dbInterface.LockObj)
				if (!_dbInterface.Connect(connectionString))
					throw new Exception("Could not connect or init redis");
		}

		public void DelDB()
		{
			lock (_dbInterface.LockObj)
				_dbInterface.FlushAll();
		}

		public Task InitDB(string path)
		{
			return Task.Run(() =>
			{
				lock (_dbInterface.LockObj)
				{
					List<string> extensions = new List<string>();
					Recursive(path, ref extensions);
					_dbInterface.SADD("ext", extensions.ToArray());
				}
			});

			void Recursive(string newPath, ref List<string> extensions)
			{
				Console.WriteLine(newPath);
				CurrentSearchDir?.Invoke(newPath);
				string[] innerDirs = Directory.GetDirectories(newPath);

				foreach (string dir in innerDirs)
				{
					string lowerCaseDir = dir.ToLower();

					if (_blacklist.Any(x => x == lowerCaseDir))
						continue;

					string? dirName = new DirectoryInfo(lowerCaseDir).Name;
					if (string.IsNullOrWhiteSpace(dirName))
						continue;

					// Only get the first char and only the lower variant of it
					char startChar = dirName[0];

					// Create an entry for the directories starting with the first letter, in lowercase
					_dbInterface.SADD("ld" + startChar, lowerCaseDir);

					Recursive(dir, ref extensions);
				}

				string[] innerFiles = Directory.GetFiles(newPath);

				foreach (string file in innerFiles)
				{
					string lowerCaseFile = file.ToLower();
					string fileExt       = Path.GetExtension(lowerCaseFile).ToLower();
					_dbInterface.SADD(fileExt, lowerCaseFile);

					if (!extensions.Contains(fileExt))
						extensions.Add(fileExt);

					string? fileName = Path.GetFileName(lowerCaseFile);
					if (string.IsNullOrWhiteSpace(fileName))
						continue;

					// Only get the first char and only the lower variant of it
					char startChar = fileName.ToLower()[0];

					// Create an entry for the directories starting with the first letter, in lowercase
					_dbInterface.SADD("lf" + startChar, lowerCaseFile);
				}
			}
		}

		public void AddPath(string path)
		{
		}

		public void UpdatePath(string oldPath, string newPath)
		{
		}

		public void DeletePath(string path)
		{
		}

		public string?[] Search(SearchSettings settings)
		{
			List<string?> results = new List<string?>();

			lock (_dbInterface.LockObj)
			{
				if (settings.SearchOnlyFileExt)
				{
					if (!_dbInterface.KeyExists(settings.Pattern))
						return Array.Empty<string>();

					return _dbInterface.SMEMBERS(settings.Pattern);
				}

				List<Task<string?[]>[]> searchQueries = new List<Task<string?[]>[]>();

				// Search directories
				if (settings.SearchDirs)
				{
					var dirKeys = _dbInterface.SCAN("ld*");

					if (dirKeys is not null)
						searchQueries.Add(_Search(dirKeys, settings.Pattern));
				}

				// Search files
				if (settings.SearchFiles)
				{
					var fileKeys = _dbInterface.SCAN("lf*");

					if (fileKeys is not null)
						searchQueries.Add(_Search(fileKeys, settings.Pattern));
				}

				// Assemble results
				foreach (Task<string?[]>[] tasks in searchQueries)
				{
					Task.WaitAll(tasks);

					foreach (Task<string?[]> task in tasks)
					{
						if (task.Status == TaskStatus.RanToCompletion)
							results.AddRange(task.Result);
					}
				}
			}

			return results.ToArray();	// _dbInterface.SCAN(settings.Pattern).Select(key => key.ToString()).ToArray();
		}

		private Task<string?[]>[] _Search(List<RedisKey> keys, string pattern)
		{
			Task<string?[]>[] searchQueries = new Task<string?[]>[keys.Count];
			for (int i = 0; i < keys.Count; i++)
				searchQueries[i] = _Search(keys[i].ToString(), pattern);

			return searchQueries;
		}

		private Task<string?[]> _Search(string? key, string pattern) => Task.Run(() => _dbInterface.SSCAN(key, pattern));
	}
}
