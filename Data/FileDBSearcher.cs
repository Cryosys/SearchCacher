using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace SearchCacher
{
	internal interface ISearcher
	{
		public event Action<string>? CurrentSearchDir;

		public void DelDB();

		public Task InitDB(string path);

		public string?[] Search(SearchSettings settings);

		public void AddPath(string path);

		public void UpdatePath(string oldPath, string newPath);

		public void DeletePath(string path);

		public virtual void StartAutoSave() { }

		public virtual void StopAutoSave() { }

		public virtual void Save() { }
	}

	internal class FileDBSearcher : ISearcher
	{
		public event Action<string>? CurrentSearchDir;

		internal readonly string DBPath;

		private FileDBConfig _config;

		private Dir _DB;
		private readonly object _dbLock = new object();

		private Thread? _autosaveThread;
		private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
		private readonly object _autosaveLock                    = new object();

		private bool _IsDirty = false;

		public FileDBSearcher(string dbPath)
		{
			DBPath = dbPath;

			if (System.IO.File.Exists(DBPath))
			{
				// Does not load all directories, as parent looped references are ignored
				using (FileStream fileStream = new FileStream(DBPath, FileMode.Open, FileAccess.Read))
				{
					_config = JsonExtensions.FromCryJson<FileDBConfig>(fileStream) ?? new FileDBConfig();
					_DB     = _config.DB;
				}

				// The parent relation needs to be restored as we do not serialize looped references.
				_RestoreParentRelation();
			}
			else
			{
				_config = new FileDBConfig();
				_DB     = _config.DB;
			}
		}

		private void _RestoreParentRelation()
		{
			lock (_dbLock)
			{
				Recursive(_DB);
			}

			void Recursive(Dir parentDir)
			{
				foreach (var dir in parentDir.Directories)
				{
					dir.Parent = parentDir;
					Recursive(dir);
				}

				foreach (var file in parentDir.Files)
					file.Parent = parentDir;
			}
		}

		public void DelDB()
		{
			lock (_dbLock)
			{
				_config.DB = new Dir();
				_DB        = _config.DB;
			}
		}

		public Task InitDB(string path)
		{
			return Task.Run(() =>
			{
				// Initiate the dir with no parent
				lock (_dbLock)
				{
					_config = new FileDBConfig(path, new Dir(path, null));
					_DB     = _config.DB;

					Recursive(path, _DB);
					_SaveDB();
				}
			});

			void Recursive(string newPath, Dir parentDir)
			{
				Console.WriteLine(newPath);
				CurrentSearchDir?.Invoke(newPath);
				string[] innerDirs = Directory.GetDirectories(newPath);
				parentDir.Directories = new Dir[innerDirs.Length];
				string dir;

				for (int i = 0; i < innerDirs.Length; i++)
				{
					dir = innerDirs[i];
					string? dirName = new DirectoryInfo(dir).Name;
					if (string.IsNullOrWhiteSpace(dirName))
						continue;

					Dir dirEntry = new Dir(dirName, parentDir);
					parentDir.Directories[i] = dirEntry;

					Recursive(dir, dirEntry);
				}

				string[] innerFiles = Directory.GetFiles(newPath);
				parentDir.Files = new File[innerFiles.Length];
				string file;

				for (int i = 0; i < innerFiles.Length; i++)
				{
					file = innerFiles[i];
					string? fileName = Path.GetFileName(file);
					if (string.IsNullOrWhiteSpace(fileName))
						continue;

					File fileEntry = new File(fileName, parentDir);
					parentDir.Files[i] = fileEntry;
				}
			}
		}

		public void AddPath(string path)
		{
			lock (_dbLock)
			{
				string subPath      = path.Replace(_config.RootPath, "");
				string[] pathSplits = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				bool isFile = !System.IO.File.GetAttributes(path).HasFlag(FileAttributes.Directory);

				Dir curDir = _DB;

				// Always size - 1 as we create the file and folder entry at the end
				// Our first step is to find the directory mapping where to add either the directory or file
				int searchDepth = pathSplits.Length - 1;

				Dir? fittingDir;
				for (int i = 0; i < searchDepth; i++)
				{
					if ((fittingDir = curDir.Directories.FirstOrDefault(dir => dir.Name == pathSplits[i])) != null)
					{
						curDir = fittingDir;
						continue;
					}
					else
					{
						for (; i < searchDepth; i++)
						{
							fittingDir         = new Dir(pathSplits[i], curDir);
							curDir.Directories = curDir.Directories.Append(fittingDir).ToArray();
							curDir             = fittingDir;
						}
					}
				}

				if (isFile)
					curDir.Files = curDir.Files.Append(new File(pathSplits.Last(), curDir)).ToArray();
				else
					curDir.Directories = curDir.Directories.Append(new Dir(pathSplits.Last(), curDir)).ToArray();

				_IsDirty = true;
			}

			Console.WriteLine("Added " + path);
		}

		public void UpdatePath(string oldPath, string newPath)
		{
			lock (_dbLock)
			{
				string subPath         = oldPath.Replace(_config.RootPath, "");
				string[] pathSplits    = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);
				string[] newPathSplits = newPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				// Has to be the new path as the old folder or file does not exist anymore at this point
				bool isFile = !System.IO.File.GetAttributes(newPath).HasFlag(FileAttributes.Directory);

				Dir curDir = _DB;

				// Always size - 1 as we create the file and folder entry at the end
				// Our first step is to find the directory mapping where to add either the directory or file
				int searchDepth = pathSplits.Length - 1;

				Dir? fittingDir;
				for (int i = 0; i < searchDepth; i++)
				{
					if ((fittingDir = curDir.Directories.FirstOrDefault(dir => dir.Name == pathSplits[i])) != null)
					{
						curDir = fittingDir;
						continue;
					}
					else
					{
						for (; i < searchDepth; i++)
						{
							fittingDir         = new Dir(pathSplits[i], curDir);
							curDir.Directories = curDir.Directories.Append(fittingDir).ToArray();
							curDir             = fittingDir;
						}
					}
				}

				if (isFile)
				{
					File? foundFile = curDir.Files.FirstOrDefault(f => f.Name == pathSplits.Last());
					if (foundFile == null)
						curDir.Files = curDir.Files.Append(new File(newPathSplits.Last(), curDir)).ToArray();
					else
						foundFile.Name = newPathSplits.Last();
				}
				else
				{
					Dir? foundDir = curDir.Directories.FirstOrDefault(d => d.Name == pathSplits.Last());
					if (foundDir == null)
						curDir.Directories = curDir.Directories.Append(new Dir(newPathSplits.Last(), curDir)).ToArray();
					else
						foundDir.Name = newPathSplits.Last();
				}

				_IsDirty = true;
			}

			Console.WriteLine("Update from " + oldPath + " to " + newPath);
		}

		public void DeletePath(string path)
		{
			lock (_dbLock)
			{
				string subPath      = path.Replace(_config.RootPath, "");
				string[] pathSplits = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				Dir curDir = _DB;

				// Always size - 1 as we create the file and folder entry at the end
				// Our first step is to find the directory mapping where to add either the directory or file
				int searchDepth = pathSplits.Length - 1;

				Dir? fittingDir;
				for (int i = 0; i < searchDepth; i++)
				{
					if ((fittingDir = curDir.Directories.FirstOrDefault(dir => dir.Name == pathSplits[i])) != null)
					{
						curDir = fittingDir;
						continue;
					}
					else
					{
						// If it does not exist we do not need to remove it
						return;
					}
				}

				int lastCount = curDir.Files.Length;

				List<File> files = new List<File>();

				foreach (File file in curDir.Files)
				{
					if (file.Name != pathSplits.Last())
						files.Add(file);
				}

				curDir.Files = files.ToArray();

				// Just a simple performance trick to do as little work as possible
				if (lastCount != files.Count)
					return;

				List<Dir> dirs = new List<Dir>();

				foreach (Dir dir in curDir.Directories)
				{
					if (dir.Name != pathSplits.Last())
						dirs.Add(dir);
				}

				curDir.Directories = dirs.ToArray();

				_IsDirty = true;
			}

			Console.WriteLine("Removed " + path);
		}

		public string?[] Search(SearchSettings settings)
		{
			lock (_dbLock)
			{
				Console.WriteLine(settings.Pattern);
				List<List<string>> results = new List<List<string>>();
				Task[] searchTasks         = new Task[_DB.Directories.Length];

				for (int i = 0; i < _DB.Directories.Length; i++)
				{
					Dir dir = _DB.Directories[i];
					results.Add(new List<string>());
					Task searchTask = new Task(delegate(object? val)
					{
						RecursiveSearch(dir, results[(int) val]);
					}, i);

					searchTask.Start();
					searchTasks[i] = searchTask;
				}

				List<string> dbResults = new List<string>();

				if (settings.SearchDirs)
					foreach (var dir in _DB.Directories)
					{
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(dir.FullPath, settings.Pattern, RegexOptions.IgnoreCase))
								dbResults.Add(dir.FullPath);
						}
						else if (Regex.IsMatch(dir.Name, settings.Pattern, RegexOptions.IgnoreCase))
							dbResults.Add(dir.FullPath);
					}

				if (settings.SearchFiles)
					foreach (var file in _DB.Files)
					{
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(file.FullPath, settings.Pattern, RegexOptions.IgnoreCase))
								dbResults.Add(file.FullPath);
						}
						else if (Regex.IsMatch(file.Name, settings.Pattern, RegexOptions.IgnoreCase))
							dbResults.Add(file.FullPath);
					}

				Task.WaitAll(searchTasks, TimeSpan.FromSeconds(60));
				results.Add(dbResults);

				List<string> combinedList = new List<string>();
				foreach (var result in results)
					combinedList.AddRange(result);

				return combinedList.ToArray();
			}

			void RecursiveSearch(Dir curDir, List<string> foundFiles)
			{
				foreach (var dir in curDir.Directories)
				{
					if (settings.SearchDirs)
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(dir.FullPath, settings.Pattern, RegexOptions.IgnoreCase))
								foundFiles.Add(dir.FullPath);
						}
						else if (Regex.IsMatch(dir.Name, settings.Pattern, RegexOptions.IgnoreCase))
							foundFiles.Add(dir.FullPath);

					RecursiveSearch(dir, foundFiles);
				}

				if (settings.SearchFiles)
					foreach (var file in curDir.Files)
					{
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(file.FullPath, settings.Pattern, RegexOptions.IgnoreCase))
								foundFiles.Add(file.FullPath);
						}
						else if (Regex.IsMatch(file.Name, settings.Pattern, RegexOptions.IgnoreCase))
							foundFiles.Add(file.FullPath);
					}
			}
		}

		public void StartAutoSave()
		{
			lock (_autosaveLock)
			{
				if (_autosaveThread is not null)
					return;

				if (!_cancellationTokenSource.TryReset())
					_cancellationTokenSource = new CancellationTokenSource();

				_autosaveThread = new Thread(_AutoSave);
				_autosaveThread.Start();
			}
		}

		public void StopAutoSave()
		{
			lock (_autosaveLock)
			{
				if (_autosaveThread is null)
					return;

				_autosaveThread.Join();
			}
		}

		private void Save() => _SaveDB();

		private void _AutoSave()
		{
			try
			{
				while (!_cancellationTokenSource.IsCancellationRequested)
				{
					Thread.Sleep(TimeSpan.FromMinutes(5));
					lock (_dbLock)
					{
						if (!_IsDirty)
							continue;

						_SaveDB();

						_IsDirty = false;
					}
				}
			}
			catch (OperationCanceledException)
			{
				// Ignore that exception as this is caused by the cancellation request
			}
			catch (Exception ex)
			{
				CryLib.Core.LibTools.ExceptionManager.AddException(ex);
			}
		}

		private void _SaveDB()
		{
			lock (_dbLock)
				using (FileStream fileStream = new FileStream(DBPath, FileMode.Create, FileAccess.Write))
					JsonExtensions.ToCryJson(_config, fileStream);
		}

		[JsonObject("FileDBConfig")]
		private class FileDBConfig
		{
			[JsonProperty("RootPath")]
			internal string RootPath;

			[JsonProperty("DB")]
			internal Dir DB;

			public FileDBConfig()
			{
				RootPath = string.Empty;
				DB       = new Dir();
			}

			public FileDBConfig(string rootPath, Dir db)
			{
				RootPath = rootPath;
				DB       = db;
			}
		}
	}

	[JsonObject("Dir")]
	internal class Dir
	{
		[JsonProperty("NameLowerCase")]
		internal string NameLowerCase = "";

		[JsonProperty("Name")]
		internal string Name = "";

		[JsonIgnore]
		internal string FullPathLowerCase
		{
			get
			{
				string path    = "";
				Dir? parentDir = Parent;
				while (parentDir != null)
				{
					path      = parentDir.NameLowerCase + "\\" + path;
					parentDir = parentDir.Parent;
				}

				return path += NameLowerCase;
			}
		}

		[JsonIgnore]
		internal string FullPath
		{
			get
			{
				string path    = "";
				Dir? parentDir = Parent;
				while (parentDir != null)
				{
					path      = parentDir.Name + "\\" + path;
					parentDir = parentDir.Parent;
				}

				return path += Name;
			}
		}

		[JsonProperty("Directories")]
		internal Dir[] Directories = { };

		[JsonProperty("Files")]
		internal File[] Files = { };

		[JsonProperty("Parent")]
		internal Dir? Parent;

		public Dir()
		{
		}

		internal Dir(string orgName, Dir? parentDir)
		{
			Name          = orgName;
			NameLowerCase = orgName.ToLower();
			Parent        = parentDir;
		}
	}

	[JsonObject("File")]
	internal class File
	{
		[JsonProperty("Name")]
		internal string Name = "";

		[JsonProperty("NameLowerCase")]
		internal string NameLowerCase = "";

		[JsonIgnore]
		internal string FullPath => Path.Combine(Parent.FullPath, Name);

		[JsonIgnore]
		internal string FullPathLowerCase => Path.Combine(Parent.FullPathLowerCase, NameLowerCase);

		[JsonProperty("Parent")]
		internal Dir Parent;

		public File()
		{
			Name = "";
		}

		public File(string name, Dir parent)
		{
			Name          = name;
			NameLowerCase = name.ToLower();
			Parent        = parent;
		}
	}
}
