using Newtonsoft.Json;
using static SearchCacher.ISearcher;
using SearchCacher.Tools;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SearchCacher
{
	internal interface ISearcher
	{
		/// <summary>	Event queue for all listeners interested in CurrentSearchDir events. </summary>
		/// <seealso cref="FileDBSearcher.CurrentSearchDir"/>
		public event Action<string>? CurrentSearchDir;

		/// <summary>   Initializes the database. </summary>
		/// <param name="path"> Full path of the search folder. </param>
		/// <returns>   The init Task. </returns>
		/// <seealso cref="FileDBSearcher.InitDB(List<DBConfig>)"/>
		public Task InitDB(List<DBConfig> dbConfigs, CancellationToken globalCancellationToken);

		/// <summary>   Initializes the database. </summary>
		/// <param name="path"> Full path of the search folder. </param>
		/// <returns>   The init Task. </returns>
		/// <seealso cref="FileDBSearcher.InitDB(WebDBConfigModel)"/>
		public Task InitDB(WebDBConfigModel dbConfig);

		/// <summary>   Searches for all matches for the given search settings. </summary>
		/// <param name="settings"> Options for controlling the operation. </param>
		/// <returns>   The search result. </returns>
		/// <seealso cref="FileDBSearcher.Search(SearchSettings)"/>
		public IEnumerable<SearchResult> Search(SearchSettings settings);

		/// <summary>   Adds a path. </summary>
		/// <param name="path"> Full path to add. </param>
		/// <seealso cref="FileDBSearcher.AddPath(string, string)"/>
		public void AddPath(string rootPath, string path);

		/// <summary>   Updates the path. </summary>
		/// <param name="oldPath"> Full path of the old folder/file. </param>
		/// <param name="newPath"> Full path of the new folder/file. </param>
		/// <seealso cref="FileDBSearcher.UpdatePath(string, string, string)"/>
		public void UpdatePath(string rootPath, string oldPath, string newPath);

		/// <summary>   Deletes the path. </summary>
		/// <param name="path"> Full path to remove. </param>
		/// <seealso cref="FileDBSearcher.DeletePath(string, string)"/>
		public void DeletePath(string rootPath, string path);

		/// <summary>   Starts automatic save if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.StartAutoSave"/>
		public virtual void StartAutoSave() { }

		/// <summary>   Stops automatic save if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.StopAutoSave"/>
		public virtual void StopAutoSave() { }

		/// <summary>   Saves the DB if applicable to the DB type. </summary>
		/// <seealso cref="FileDBSearcher.SaveDB"/>
		public virtual void SaveDB() { }

		/// <summary>	Deletes the database. </summary>
		/// <seealso cref="FileDBSearcher.DelDB(string?)"/>
		public void DelDB(string? guid);

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

		internal long InitDirCount { get; private set; }
		internal long InitFileCount { get; private set; }

		private FileDBConfig[] _configs = [];

		private readonly MultiLock _dbLock = new MultiLock();

		private Thread? _autosaveThread;
		private CancellationTokenSource _autosaveCancellationTokenSource = new CancellationTokenSource();
		private readonly object _autosaveLock = new object();
		private int _autoSaveInterval;

		public FileDBSearcher(Config serviceConfig, int autoSaveInterval = 5)
		{
			_autoSaveInterval = autoSaveInterval;

			List<FileDBConfig> configs = new List<FileDBConfig>();

			foreach (var dbConfig in serviceConfig.DBConfigs)
			{
				if (System.IO.File.Exists(dbConfig.FileDBPath))
				{
					// Does not load all directories, as parent looped references are ignored
					using (FileStream fileStream = new FileStream(dbConfig.FileDBPath, FileMode.Open, FileAccess.Read))
					{
						var config = JsonExtensions.FromCryJson<FileDBConfig>(fileStream) ?? new FileDBConfig();
						config.FileSavePath = dbConfig.FileDBPath;
						config.IgnoreList   = dbConfig.IgnoreList;
						configs.Add(config);
					}
				}
			}

			_configs = configs.ToArray();

			// The parent relation needs to be restored as we do not serialize looped references.
			_RestoreParentRelation();

			Program.Log($"Got {InitDirCount.ToString("#,##0")} directories and {InitFileCount.ToString("#,##0")} files");
		}

		private void _RestoreParentRelation()
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();

			try
			{
				if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire master lock on file DB");

				if (_configs is null)
					return;

				InitDirCount  = 0;
				InitFileCount = 0;

				List<Task> recursiveTasks = new();
				foreach (var config in _configs)
					recursiveTasks.Add(Task.Run(() => Recursive(config.DB)));

				Task.WaitAll(recursiveTasks);

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
					dir.Hash   = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(dir.FullPath)));
					Recursive(dir);
				}

				InitDirCount += parentDir.Directories.Length;

				foreach (var file in parentDir.Files)
					file.Parent = parentDir;

				InitFileCount += parentDir.Files.Length;
			}
		}

		public void DelDB(string? guid = null)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire master lock on file DB");

				if (_configs is null)
					return;

				for (int i = 0; i < _configs.Length; i++)
					if (guid is null || _configs[i].ID == guid)
						_configs[i].DB = new Dir();

				_SaveDB(true);

				if (!_dbLock.ReleaseMasterLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not release master lock on file DB");
			}
			finally
			{
				cancelSource.Dispose();
			}
		}

		public Task InitDB(List<DBConfig> configs, CancellationToken globalCancellationToken)
		{
			return Task.Run(() =>
			{
				List<Task> initTasks = new();

				if (!_dbLock.RequestMasterLockAsync(globalCancellationToken).Result)
				{
					if (globalCancellationToken.IsCancellationRequested)
						return;

					throw new Exception("Could not acquire master lock on file DB");
				}

				object countLockObj = new object();
				InitDirCount        = 0;
				InitFileCount       = 0;

				DateTime preStartTime = DateTime.Now;

				_configs = new FileDBConfig[configs.Count];

				for (int i = 0; i < configs.Count; i++)
				{
					DBConfig config = configs[i];
					int index       = i;

					initTasks.Add(Task.Run(() =>
					{
						if (config is null)
							return;

						// Initiate the dir with no parent
						CancellationTokenSource initCancelSource = new CancellationTokenSource();
						try
						{
							Program.Log("Starting init for " + config.RootPath);

							var dbConfig    = new FileDBConfig(config.ID, config.RootPath, config.FileDBPath, new Dir(config.RootPath, null));
							_configs[index] = dbConfig;

							string[] baseDirPaths = Directory.GetDirectories(config.RootPath);
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

								Dir dir = new Dir(dirName, dbConfig.DB);
								tasks.Add(new Task(delegate(object? val)
								{
									try
									{
										(int index, Dir tmpDir) = (Tuple<int, Dir>)val;
										long dirCount           = 0, fileCount = 0;
										Recursive(baseDirPaths[index], tmpDir, initCancelSource.Token, ref dirCount, ref fileCount);

										lock (countLockObj)
										{
											InitDirCount  += dirCount;
											InitFileCount += fileCount;
										}
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
								dbConfig.DB.Directories = baseDirs.ToArray();

								string[] innerFiles = Directory.GetFiles(config.RootPath);
								dbConfig.DB.Files   = new File[innerFiles.Length];
								string file;

								for (int i = 0; i < innerFiles.Length; i++)
								{
									if (globalCancellationToken.IsCancellationRequested)
										return;

									file             = innerFiles[i];
									string? fileName = Path.GetFileName(file);
									if (string.IsNullOrWhiteSpace(fileName))
										continue;

									File fileEntry       = new File(fileName, dbConfig.DB);
									dbConfig.DB.Files[i] = fileEntry;
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
							_SaveDB(dbConfig, true);
						}
						catch (Exception ex)
						{
							CryLib.Core.LibTools.ExceptionManager.AddException(ex);
							throw;
						}
						finally
						{
							Program.Log("Finished init on " + config.RootPath);
							initCancelSource.Dispose();
						}
					}));
				}

				try
				{
					// We do not listen to the cancellation here as we handle it in the individual tasks
					Task.WaitAll(initTasks);
				}
				finally
				{
					CancellationTokenSource masterLockCancelSource = new CancellationTokenSource();

					// No matter what the cancellation state is, we wait for the master lock to be released
					if (!_dbLock.ReleaseMasterLockAsync(masterLockCancelSource.Token).Result)
						throw new Exception("Could not release master lock on file DB");

					masterLockCancelSource.Dispose();

					Program.Log("DB init took: " + (DateTime.Now - preStartTime).ToString("g"));
					Program.Log($"Got {InitDirCount.ToString("#,##0")} directories and {InitFileCount.ToString("#,##0")} files");
				}
			});

			void Recursive(string newPath, Dir parentDir, CancellationToken cancelToken, ref long dirCount, ref long fileCount)
			{
				if (cancelToken.IsCancellationRequested || globalCancellationToken.IsCancellationRequested)
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

						// We only count up in here as the dir can fail
						dirCount++;

						Recursive(dir, dirEntry, cancelToken, ref dirCount, ref fileCount);
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

				fileCount += innerFiles.Length;
			}
		}

		public Task InitDB(WebDBConfigModel webConfig)
		{
			return Task.CompletedTask;
		}

		public void AddPath(string rootPath, string path)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			bool acquiredLock                    = false;
			try
			{
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

				if (_configs is null || _configs.Length == 0)
					return;

				var config = _configs.FirstOrDefault(c => c.RootPath == rootPath);
				if (config is null)
					return;

				string subPath      = path.Replace(config.RootPath, "");
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

				Dir curDir = config.DB;

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

				config.IsDirty = true;
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

		public void UpdatePath(string rootPath, string oldPath, string newPath)
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

				// We can do everything else above as it does not concern the DB
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

				if (_configs is null || _configs.Length == 0)
					return;

				var config = _configs.FirstOrDefault(c => c.RootPath == rootPath);
				if (config is null)
					return;

				string subPath         = oldPath.Replace(config.RootPath, "");
				string[] pathSplits    = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);
				string[] newPathSplits = newPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				Dir curDir = config.DB;

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

				config.IsDirty = true;
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

		public void DeletePath(string rootPath, string path)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			bool acquiredLock                    = false;
			try
			{
				if (!(acquiredLock = _dbLock.RequestMasterLockAsync(cancelSource.Token).Result))
					throw new Exception("Could not acquire master lock on file DB");

				if (_configs is null || _configs.Length == 0)
					return;

				var config = _configs.FirstOrDefault(c => c.RootPath == rootPath);
				if (config is null)
					return;

				string subPath      = path.Replace(config.RootPath, "");
				string[] pathSplits = subPath.Split("\\", StringSplitOptions.RemoveEmptyEntries);

				Dir curDir = config.DB;

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

				config.IsDirty = true;
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

		public IEnumerable<ISearcher.SearchResult> Search(SearchSettings searchSettings)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			List<List<string>> results           = new List<List<string>>();
			List<string> dbResults               = new List<string>();

			BlockingCollection<ISearcher.SearchResult> searchResults = new BlockingCollection<ISearcher.SearchResult>();

			try
			{
				if (!_dbLock.RequestMultiLockAsync(cancelSource.Token).Result)
					throw new Exception("Could not acquire multi lock on file DB");

				Program.Log($"search path: {searchSettings}; pattern: {searchSettings.Pattern}");

				if (_configs is null || _configs.Length == 0)
					return [new ISearcher.SearchResult(false, Array.Empty<string>(), "DB is not initialized")];

				Task[] totalSearchTasks = new Task[_configs.Length];

				for (int i = 0; i < _configs.Length; i++)
				{
					FileDBConfig config = _configs[i];

					SearchPathSettings? searchPathSettings = null;

					// Only search paths that were selected
					if ((searchPathSettings = searchSettings.SearchPaths.Find(searchPath => searchPath.ID == config.ID)) == null)
					{
						totalSearchTasks[i] = Task.CompletedTask;
						continue;
					}

					totalSearchTasks[i] = Task.Run(() =>
					{
						Dir baseSearchDir = config.DB;

						if (searchPathSettings.SearchPath != "*")
						{
							// Search for the directory mentioned in the search settings
							string subPath      = searchPathSettings.SearchPath.Replace(config.RootPath, "");
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
									searchResults.Add(new ISearcher.SearchResult(false, Array.Empty<string>(), "Could not find base search path"));
							}
						}

						Task[] configSearchTasks = new Task[baseSearchDir.Directories.Length];

						for (int i = 0; i < baseSearchDir.Directories.Length; i++)
						{
							Dir dir = baseSearchDir.Directories[i];
							results.Add(new List<string>());

							if (searchPathSettings.UseIgnoreList)
								if (config.IgnoreList.Any(ignorePath => ignorePath.Hash == dir.Hash))
								{
									// Skip ignored path
									configSearchTasks[i] = Task.CompletedTask;
									continue;
								}

							Task searchTask = new Task(delegate(object? val)
							{
								if (val is null)
									return;

								RecursiveSearch(dir, results[(int) val], searchPathSettings.UseIgnoreList ? config.IgnoreList : null);
							}, i);

							searchTask.Start();
							configSearchTasks[i] = searchTask;
						}

						if (searchSettings.SearchDirs)
							foreach (var dir in baseSearchDir.Directories)
							{
								if (searchPathSettings.UseIgnoreList)
									if (config.IgnoreList.Any(ignorePath => ignorePath.Hash == dir.Hash))
										continue;

								if (searchSettings.SearchOnFullPath)
								{
									if (Regex.IsMatch(dir.FullPath, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
									{
										string fullPath = dir.FullPath;
										if (!fullPath.EndsWith("\\"))
											fullPath += "\\";

										dbResults.Add(fullPath);
									}
								}
								else if (Regex.IsMatch(dir.Name, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
								{
									string fullPath = dir.FullPath;
									if (!fullPath.EndsWith("\\"))
										fullPath += "\\";

									dbResults.Add(fullPath);
								}
							}

						if (searchSettings.SearchFiles)
						{
							foreach (var file in baseSearchDir.Files)
							{
								if (searchPathSettings.UseIgnoreList)
									if (config.IgnoreList.Any(ignorePath => ignorePath.Hash == file.Hash))
										continue;

								if (searchSettings.SearchOnlyFileExt && file.Extension == searchSettings.Pattern)
								{
									dbResults.Add(file.FullPath);
									continue;
								}

								if (searchSettings.SearchOnFullPath)
								{
									if (Regex.IsMatch(file.FullPath, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
										dbResults.Add(file.FullPath);
								}
								else if (Regex.IsMatch(file.Name, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
									dbResults.Add(file.FullPath);
							}
						}

						Task.WaitAll(configSearchTasks, TimeSpan.FromMinutes(10));
					});
				}

				Task.WaitAll(totalSearchTasks, TimeSpan.FromMinutes(10));
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

			ISearcher.SearchResult[] returnResults = new ISearcher.SearchResult[combinedList.Count > 0 ? searchResults.Count + 1 : searchResults.Count];
			for (int i = 0; i < searchResults.Count; i++)
				if (searchResults.TryTake(out SearchResult result))
					returnResults[i] = result;

			if (combinedList.Count > 0)
				returnResults[returnResults.Length - 1] = new ISearcher.SearchResult(true, combinedList.ToArray());

			return returnResults;

			void RecursiveSearch(Dir curDir, List<string> foundFiles, List<IgnoreListEntry>? ignoreList)
			{
				foreach (var dir in curDir.Directories)
				{
					if (ignoreList is not null)
						if (ignoreList.Any(ignorePath => ignorePath.Hash == dir.Hash))
							continue;

					if (searchSettings.SearchDirs && !searchSettings.SearchOnlyFileExt)
						if (searchSettings.SearchOnFullPath)
						{
							if (Regex.IsMatch(dir.FullPath, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
							{
								string fullPath = dir.FullPath;
								if (!fullPath.EndsWith("\\"))
									fullPath += "\\";

								foundFiles.Add(fullPath);
							}
						}
						else if (Regex.IsMatch(dir.Name, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
						{
							string fullPath = dir.FullPath;
							if (!fullPath.EndsWith("\\"))
								fullPath += "\\";

							foundFiles.Add(fullPath);
						}

					RecursiveSearch(dir, foundFiles, ignoreList);
				}

				if (searchSettings.SearchFiles)
					foreach (var file in curDir.Files)
					{
						if (ignoreList is not null)
							if (ignoreList.Any(ignorePath => ignorePath.Hash == file.Hash))
								continue;

						if (searchSettings.SearchOnlyFileExt && file.Extension == searchSettings.Pattern)
						{
							foundFiles.Add(file.FullPath);
							continue;
						}

						if (searchSettings.SearchOnFullPath)
						{
							if (Regex.IsMatch(file.FullPath, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
								foundFiles.Add(file.FullPath);
						}
						else if (Regex.IsMatch(file.Name, searchSettings.Pattern, searchSettings.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
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

					foreach (var config in _configs)
					{
						if (!config.IsDirty)
							continue;

						_SaveDB();
						config.IsDirty = false;
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

		private void _SaveDB(bool parentHasMaster = false, bool force = false)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				Program.Log("Saving DB");

				if (!parentHasMaster)
					if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
						throw new Exception("Could not acquire master lock on file DB");

				foreach (var config in _configs)
					if (config.IsDirty || force)
						using (FileStream fileStream = new FileStream(config.FileSavePath, FileMode.Create, FileAccess.ReadWrite))
							JsonExtensions.ToCryJson(config, fileStream);
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

		private void _SaveDB(FileDBConfig config, bool parentHasMaster = false)
		{
			CancellationTokenSource cancelSource = new CancellationTokenSource();
			try
			{
				Program.Log("Saving DB -> " + config.RootPath);

				if (!parentHasMaster)
					if (!_dbLock.RequestMasterLockAsync(cancelSource.Token).Result)
						throw new Exception("Could not acquire master lock on file DB");

				using (FileStream fileStream = new FileStream(config.FileSavePath, FileMode.Create, FileAccess.ReadWrite))
					JsonExtensions.ToCryJson(config, fileStream);
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
			[JsonProperty("ID")]
			internal string ID { get; init; }

			[JsonProperty("RootPath")]

			/// <summary> Gets or sets the root path of the search folder. </summary>
			internal string RootPath { get; set; }

			[JsonProperty("DB")]
			/// <summary> Gets or sets the root directory functioning as the database. </summary>
			internal Dir DB { get; set; }

			[JsonIgnore]

			/// <summary> Gets or sets a value indicating whether this object is dirty and needs to be saved. </summary>
			internal bool IsDirty { get; set; } = false;

			/// <summary> Gets or sets a value indicating where this object should be serialized and saved to. </summary>
			[JsonIgnore]
			internal string FileSavePath { get; set; }

			/// <summary> Gets or sets a lists of paths to ignore while searching. </summary>
			[JsonIgnore]
			internal List<IgnoreListEntry> IgnoreList { get; set; } = [];

			public FileDBConfig()
			{
				ID           = Guid.NewGuid().ToString();
				RootPath     = string.Empty;
				FileSavePath = string.Empty;
				DB           = new Dir();
			}

			public FileDBConfig(string guid, string rootPath, string fileSavePath, Dir db)
			{
				ID           = guid;
				RootPath     = rootPath;
				FileSavePath = fileSavePath;
				DB           = db;
			}
		}
	}

	[JsonObject("IgnoreListEntry")]
	internal class IgnoreListEntry
	{
		[JsonProperty("Path")]
		internal string Path { get; set; }

		[JsonProperty("Hash")]
		internal long Hash { get; set; }

		public IgnoreListEntry()
		{
			Path = "";
			Hash = 0;
		}

		public IgnoreListEntry(string path)
		{
			Path = path;
			Hash = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(path)));
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

		[JsonIgnore]
		internal long Hash
		{
			get;
			set;
		}

		[JsonProperty("Directories")]
		internal Dir[] Directories { get; set; } = Array.Empty<Dir>();

		[JsonProperty("Files")]
		internal File[] Files { get; set; } = Array.Empty<File>();

		[JsonProperty("Parent")]
		internal Dir? Parent { get; set; }

		public Dir()
		{
		}

		internal Dir(string orgName, Dir? parentDir)
		{
			Name   = orgName;
			Parent = parentDir;
			Hash   = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(FullPath)));
		}

		public override string ToString()
		{
			return FullPath;
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

		internal long Hash
		{
			get;
			set;
		}

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
			Hash      = BitConverter.ToInt64(MD5.HashData(Encoding.Unicode.GetBytes(FullPath)));
		}
	}
}
