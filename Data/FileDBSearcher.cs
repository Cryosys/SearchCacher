using Newtonsoft.Json;
using SearchCacher.Tools;
using System.Text.RegularExpressions;

namespace SearchCacher
{
	internal interface ISearcher
	{
		/// <summary>	Event queue for all listeners interested in CurrentSearchDir events. </summary>
		/// <seealso cref="FileDBSearcher.CurrentSearchDir"/>
		public event Action<string>? CurrentSearchDir;

		/// <summary>	Deletes the database. </summary>
		/// <seealso cref="FileDBSearcher.DelDB"/>
		public void DelDB();

		/// <summary>   Initializes the database. </summary>
		/// <param name="path"> Full path of the search folder. </param>
		/// <returns>   The init Task. </returns>
		/// <seealso cref="FileDBSearcher.InitDB(string)"/>
		public Task InitDB(string path);

		/// <summary>   Searches for all matches for the given search settings. </summary>
		/// <param name="settings"> Options for controlling the operation. </param>
		/// <returns>   The search result. </returns>
		/// <seealso cref="FileDBSearcher.Search(SearchSettings)"/>
		public SearchResult Search(SearchSettings settings);

		/// <summary>   Adds a path. </summary>
		/// <param name="path"> Full path to add. </param>
		/// <seealso cref="FileDBSearcher.AddPath(string)"/>
		public void AddPath(string path);

		/// <summary>   Updates the path. </summary>
		/// <param name="oldPath"> Full path of the old folder/file. </param>
		/// <param name="newPath"> Full path of the new folder/file. </param>
		/// <seealso cref="FileDBSearcher.UpdatePath(string, string)"/>
		public void UpdatePath(string oldPath, string newPath);

		/// <summary>   Deletes the path. </summary>
		/// <param name="path"> Full path to remove. </param>
		/// <seealso cref="FileDBSearcher.DeletePath(string)"/>
		public void DeletePath(string path);

		/// <summary>   Starts automatic save if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.StartAutoSave"/>
		public virtual void StartAutoSave() { }

		/// <summary>   Stops automatic save if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.StopAutoSave"/>
		public virtual void StopAutoSave() { }

		/// <summary>   Saves the DB if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.SaveDB"/>
		public virtual void SaveDB() { }

		public struct SearchResult
		{
			public bool Success { get; }

			public string[] Result { get; }

			public string Error { get; }

			public SearchResult(bool success, string[] result, string error = "")
			{
				Success = success;
				Result  = result;
				Error   = error;
			}
		}
	}

	internal class FileDBSearcher : ISearcher
	{
		public event Action<string>? CurrentSearchDir;

		internal readonly string DBPath;

		private FileDBConfig _config;

		private Dir _DB;
		private readonly MultiLock _dbLock = new MultiLock();

		private Thread? _autosaveThread;
		private CancellationTokenSource _autosaveCancellationTokenSource = new CancellationTokenSource();
		private readonly object _autosaveLock = new object();
		private int _autoSaveInterval;

		private bool _IsDirty = false;

		public FileDBSearcher(string dbPath, int autoSaveInterval = 5)
		{
			DBPath            = dbPath;
			_autoSaveInterval = autoSaveInterval;

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
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire master lock on file DB");

				Recursive(_DB);

				if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not release master lock on file DB");
			}
			finally
			{
				cancelSource.Dispose();
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
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire master lock on file DB");

				_config.DB = new Dir();
				_DB        = _config.DB;

				_SaveDB(true);

				if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not release master lock on file DB");
			}
			finally
			{
				cancelSource.Dispose();
			}
		}

		public Task InitDB(string path)
		{
			return Task.Run(() =>
			{
				DateTime preStartTime = DateTime.Now;

				// Initiate the dir with no parent
				CancellationTokenSource masterLockCancelSource = new CancellationTokenSource();
				CancellationTokenSource initCancelSource       = new CancellationTokenSource();
				try
				{
					Program.Log("Starting init");

					if (!_dbLock.RequestMasterLockAsync(masterLockCancelSource.Token).Result)
						throw new Exception("Could not acquire master lock on file DB");

					_config = new FileDBConfig(path, new Dir(path, null));
					_DB     = _config.DB;

					string[] baseDirPaths = Directory.GetDirectories(path);
					List<Task> tasks      = new List<Task>();
					List<Dir> baseDirs    = new List<Dir>();

					// Here we do some simple threading where we can check multiple directories at the same time
					// This does not consider that the root may contain only 1 folder and then splits
					for (int i = 0; i < baseDirPaths.Length; i++)
					{
						string? dirName = new DirectoryInfo(baseDirPaths[i]).Name;
						if (string.IsNullOrWhiteSpace(dirName))
						{
							// In this case we do not create a path for this directory and just mark it as finished, but never add it to the final dirs.
							continue;
						}

						Dir dir = new Dir(dirName, _DB);
						tasks.Add(new Task(delegate(object? val)
						{
							try
							{
								(int index, Dir tmpDir) = (Tuple<int, Dir>)val;
								Recursive(baseDirPaths[index], tmpDir, initCancelSource.Token);
							}
							catch (Exception ex)
							{
								CryLib.Core.LibTools.ExceptionManager.AddException(ex);
								throw;
							}
						}, new Tuple<int, Dir>(i, dir)));
						baseDirs.Add(dir);
					}

					foreach (Task task in tasks)
						task.Start();

					try
					{
						// Before we wait for the tasks we can do some work in this thread and check the files in the base directory
						// Now we form the actual DB
						_DB.Directories = baseDirs.ToArray();

						string[] innerFiles = Directory.GetFiles(path);
						_DB.Files           = new File[innerFiles.Length];
						string file;

						for (int i = 0; i < innerFiles.Length; i++)
						{
							file             = innerFiles[i];
							string? fileName = Path.GetFileName(file);
							if (string.IsNullOrWhiteSpace(fileName))
								continue;

							File fileEntry = new File(fileName, _DB);
							_DB.Files[i]   = fileEntry;
						}
					}
					catch (Exception ex)
					{
						CryLib.Core.LibTools.ExceptionManager.AddException(ex);
						initCancelSource.Cancel();

						// We still have to await the cancel.
						// We give it a hard stop time, it should never get to this as the process to check for the cancel should be rather fast.
						Task.WaitAll(tasks.ToArray(), 60000);
						throw;
					}

					Task.WaitAll(tasks.ToArray(), initCancelSource.Token);
					_SaveDB(true);
				}
				catch (Exception ex)
				{
					CryLib.Core.LibTools.ExceptionManager.AddException(ex);
					throw;
				}
				finally
				{
					if (!_dbLock.ReleaseMasterLockAsync(masterLockCancelSource.Token).Result)
						throw new Exception("Could not release master lock on file DB");

					Program.Log("Finished init");
					masterLockCancelSource.Dispose();
					initCancelSource.Dispose();

					Program.Log("DB init took: " + (DateTime.Now - preStartTime).ToString("g"));
				}
			});

			void Recursive(string newPath, Dir parentDir, CancellationToken cancelToken)
			{
				if (cancelToken.IsCancellationRequested)
					return;

				CurrentSearchDir?.Invoke(newPath);
				string[] innerDirs = Directory.GetDirectories(newPath);
				List<Dir> dirs     = new List<Dir>();
				parentDir.Directories = new Dir[innerDirs.Length];
				string dir;

				for (int i = 0; i < innerDirs.Length; i++)
				{
					// Accessing the directory may fail as we cannot guaranty that the directory will still exist once we get to cache it
					try
					{
						dir = innerDirs[i];
						string? dirName = new DirectoryInfo(dir).Name;
						if (string.IsNullOrWhiteSpace(dirName))
							continue;

						Dir dirEntry = new Dir(dirName, parentDir);
						dirs.Add(dirEntry);

						Recursive(dir, dirEntry, cancelToken);
					}
					catch { }
				}

				parentDir.Directories = dirs.ToArray();

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
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			bool acquiredLock                    = false;
			try
			{
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

				string subPath      = path.Replace(_config.RootPath, "");
				string[] pathSplits = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				bool isFile = false;

				try
				{
					isFile = !System.IO.File.GetAttributes(path).HasFlag(FileAttributes.Directory);
				}
				catch (Exception ex) when(ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException)
				{
					// This may happen if the file or directory does not exist anymore
					// At this point we just return and assume everything is fine
					return;
				}

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
			finally
			{
				try
				{
					// We can only release the lock if we also took it
					if (acquiredLock)
						if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
							throw new Exception("Could not release master lock on file DB");
				}
				catch { }

				cancelSource.Dispose();

				// Event though this looks weird in the log, we log the add after releasing the log so that another thread can acquire it faster
				Program.Log("Added " + path);
			}
		}

		public void UpdatePath(string oldPath, string newPath)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			bool acquiredLock                    = false;
			try
			{
				// Has to be the new path as the old folder or file does not exist anymore at this point
				bool isFile = false;

				try
				{
					isFile = !System.IO.File.GetAttributes(newPath).HasFlag(FileAttributes.Directory);
				}
				catch (Exception ex) when(ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is IOException)
				{
					// This may happen if the file or directory does not exist anymore
					// At this point we just return and assume everything is fine
					return;
				}

				// First we need to check if the old path and new path match as a file.
				// It can happen that the old path provided by the watchdog is invalid as the file was renamed on creation.
				// In this case we do not need to do anything as a event would preceed this one
				// We can simply check it by subtracting the new paths folder path and the old paths folder path, if the resulting path is empty we know that it was a faulty event.
				{
					string newDirPath = Path.GetDirectoryName(newPath) ?? string.Empty;
					if (!newDirPath.EndsWith("\\"))
						newDirPath += "\\";

					if (string.IsNullOrWhiteSpace(oldPath.Replace(newDirPath, "")))
						return;
				}

				string subPath         = oldPath.Replace(_config.RootPath, "");
				string[] pathSplits    = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);
				string[] newPathSplits = newPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				// We can do everything else above as it does not concern the DB
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

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

				string lastSplit = newPathSplits.LastOrDefault() ?? newPath;

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
			finally
			{
				try
				{
					// We can only release the lock if we also took it
					if (acquiredLock)
						if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
							throw new Exception("Could not release master lock on file DB");
				}
				catch { }

				cancelSource.Dispose();

				// Event though this looks weird in the log, we log the update after releasing the log so that another thread can acquire it faster
				Program.Log("Update from " + oldPath + " to " + newPath);
			}
		}

		public void DeletePath(string path)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			bool acquiredLock                    = false;
			try
			{
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

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
			finally
			{
				try
				{
					// We can only release the lock if we also took it
					if (acquiredLock)
						if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
							throw new Exception("Could not release master lock on file DB");
				}
				catch { }

				cancelSource.Dispose();

				// Event though this looks weird in the log, we log the delete after releasing the log so that another thread can acquire it faster
				Program.Log("Removed " + path);
			}
		}

		public ISearcher.SearchResult Search(SearchSettings settings)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			List<List<string>> results           = new List<List<string>>();
			List<string> dbResults               = new List<string>();

			try
			{
				if (!_dbLock.RequestMultiLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire multi lock on file DB");

				Program.Log($"search path: {settings}; pattern: {settings.Pattern}");

				Dir baseSearchDir = _DB;

				if (settings.SearchPath != "*")
				{
					// Search for the directory mentioned in the search settings
					string subPath      = settings.SearchPath.Replace(_config.RootPath, "");
					string[] pathSplits = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);
					int searchDepth     = pathSplits.Length;
					Dir? fittingDir;
					for (int i = 0; i < searchDepth; i++)
					{
						if ((fittingDir = baseSearchDir.Directories.FirstOrDefault(dir => dir.Name == pathSplits[i])) != null)
						{
							baseSearchDir = fittingDir;
							continue;
						}
						else
							return new ISearcher.SearchResult(false, Array.Empty<string>(), "Could not find base search path");
					}
				}

				Task[] searchTasks = new Task[baseSearchDir.Directories.Length];

				for (int i = 0; i < baseSearchDir.Directories.Length; i++)
				{
					Dir dir = baseSearchDir.Directories[i];
					results.Add(new List<string>());
					Task searchTask = new Task(delegate(object? val)
					{
						RecursiveSearch(dir, results[(int) val]);
					}, i);

					searchTask.Start();
					searchTasks[i] = searchTask;
				}

				if (settings.SearchDirs)
					foreach (var dir in baseSearchDir.Directories)
					{
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(dir.FullPath, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
							{
								string fullPath = dir.FullPath;
								if (!fullPath.EndsWith("\\"))
									fullPath += "\\";

								dbResults.Add(fullPath);
							}
						}
						else if (Regex.IsMatch(dir.Name, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
						{
							string fullPath = dir.FullPath;
							if (!fullPath.EndsWith("\\"))
								fullPath += "\\";

							dbResults.Add(fullPath);
						}
					}

				if (settings.SearchFiles)
					foreach (var file in baseSearchDir.Files)
					{
						if (settings.SearchOnlyFileExt && file.Extension == settings.Pattern)
						{
							dbResults.Add(file.FullPath);
							continue;
						}

						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(file.FullPath, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
								dbResults.Add(file.FullPath);
						}
						else if (Regex.IsMatch(file.Name, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
							dbResults.Add(file.FullPath);
					}

				Task.WaitAll(searchTasks, TimeSpan.FromMinutes(3));
			}
			finally
			{
				if (!_dbLock.ReleaseMultiLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not release master lock on file DB");

				cancelSource.Dispose();
			}

			results.Add(dbResults);

			List<string> combinedList = new List<string>();
			foreach (var result in results)
				combinedList.AddRange(result);

			return new ISearcher.SearchResult(true, combinedList.ToArray());

			void RecursiveSearch(Dir curDir, List<string> foundFiles)
			{
				foreach (var dir in curDir.Directories)
				{
					if (settings.SearchDirs && !settings.SearchOnlyFileExt)
						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(dir.FullPath, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
							{
								string fullPath = dir.FullPath;
								if (!fullPath.EndsWith("\\"))
									fullPath += "\\";

								foundFiles.Add(fullPath);
							}
						}
						else if (Regex.IsMatch(dir.Name, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
						{
							string fullPath = dir.FullPath;
							if (!fullPath.EndsWith("\\"))
								fullPath += "\\";

							foundFiles.Add(fullPath);
						}

					RecursiveSearch(dir, foundFiles);
				}

				if (settings.SearchFiles)
					foreach (var file in curDir.Files)
					{
						if (settings.SearchOnlyFileExt && file.Extension == settings.Pattern)
						{
							foundFiles.Add(file.FullPath);
							continue;
						}

						if (settings.SearchOnFullPath)
						{
							if (Regex.IsMatch(file.FullPath, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
								foundFiles.Add(file.FullPath);
						}
						else if (Regex.IsMatch(file.Name, settings.Pattern, settings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
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

				if (!_autosaveCancellationTokenSource.TryReset())
					_autosaveCancellationTokenSource = new CancellationTokenSource();

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

				if (!_autosaveCancellationTokenSource.IsCancellationRequested)
					_autosaveCancellationTokenSource.Cancel();

				_autosaveThread.Join();
				_autosaveThread = null;
			}
		}

		public void SaveDB() => _SaveDB();

		private void _AutoSave()
		{
			try
			{
				while (!_autosaveCancellationTokenSource.IsCancellationRequested)
				{
					Task.Delay(TimeSpan.FromMinutes(_autoSaveInterval), _autosaveCancellationTokenSource.Token).Wait(_autosaveCancellationTokenSource.Token);

					if (!_IsDirty)
						continue;

					_SaveDB();
					_IsDirty = false;
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

		private void _SaveDB(bool parentHasMaster = false)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				Program.Log("Saving DB");

				if (!parentHasMaster)
					if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
						throw new Exception("Could not acquire master lock on file DB");

				using (FileStream fileStream = new FileStream(DBPath, FileMode.Create, FileAccess.ReadWrite))
					JsonExtensions.ToCryJson(_config, fileStream);
			}
			catch (Exception ex)
			{
				CryLib.Core.LibTools.ExceptionManager.AddException(ex);
			}
			finally
			{
				if (!parentHasMaster)
					if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
						throw new Exception("Could not release master lock on file DB");

				Program.Log("Saved DB");
				cancelSource.Dispose();
			}
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
		[JsonProperty("Name")]
		internal string Name = "";

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
		#if LARGEARRAY_DEBUG
			internal CryLib.Core.LargeArray<Dir> Directories = new CryLib.Core.LargeArray<Dir>(0);
		#else
			internal Dir[] Directories = Array.Empty<Dir>();
		#endif

		[JsonProperty("Files")]
		internal File[] Files = Array.Empty<File>();

		[JsonProperty("Parent")]
		internal Dir? Parent;

		public Dir()
		{
		}

		internal Dir(string orgName, Dir? parentDir)
		{
			Name   = orgName;
			Parent = parentDir;
		}
	}

	[JsonObject("File")]
	internal class File
	{
		[JsonProperty("Name")]
		internal string Name;

		[JsonProperty("Extension")]
		internal string Extension;

		[JsonIgnore]
		internal string FullPath => Path.Combine(Parent.FullPath, Name);

		[JsonProperty("Parent")]
		internal Dir Parent;

		public File()
		{
			Name      = "";
			Extension = "";
		}

		public File(string name, Dir parent)
		{
			Name      = name;
			Parent    = parent;
			Extension = Path.GetExtension(name);
		}
	}
}
