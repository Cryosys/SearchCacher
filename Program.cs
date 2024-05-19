using CryLib.Core;
using Microsoft.Extensions.Hosting.WindowsServices;
using SearchCacher.Data;
using Syncfusion.Blazor;
using System.Net;
using System.Reflection;

namespace SearchCacher
{
	public class Program
	{
		private static readonly string ConfigPath = Path.Combine(Paths.ExecuterPath, "config.cfg");

		private static ISearchService _service;
		private static Watchdog? _dog;
		private static AdaptiveLogHandler<LogTypes> _logHandler = new AdaptiveLogHandler<LogTypes>(Path.Combine(Paths.AppPath, "Logs"));

		private static bool _newConfigSet = false;

		public static void Main(string[] args)
		{
			// Add exception handler for unhandled exception in other threads or the GUI thread
			AppDomain.CurrentDomain.UnhandledException += _CurrentDomain_UnhandledException;
			TaskScheduler.UnobservedTaskException      += _TaskScheduler_UnobservedTaskException;

			_logHandler.LoggedEntry += _logHandler_LoggedEntry;
			_logHandler.AddLog(LogTypes.Log);
			_logHandler.StartHandler();

			if (!WindowsServiceHelpers.IsWindowsService())
				Console.Title = Assembly.GetExecutingAssembly().GetName().Name + " ver. " + ISearchService.Version;

			LibTools.ExceptionManager.ExceptionCaught += ExceptionManager_ExceptionCaught;
			LibTools.ExceptionManager.bAllowCollection = true;
			LibTools.ExceptionManager.bLogExceptions   = true;

			Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(SecretsConfig.License);

			if (!System.IO.File.Exists(ConfigPath))
			{
				System.IO.File.WriteAllText(ConfigPath, new Config().ToCryJson());
			}

			Config? cfg = System.IO.File.ReadAllText(ConfigPath).FromCryJson<Config>();

			// We only init the network share and watchdog if the config is valid
			NetworkShareConnector? shareConnector = null;

			if (cfg is null)
				throw new Exception("Config is invalid, fix or delete it");

			if (string.IsNullOrWhiteSpace(cfg.SearchPath))
			{
				Log("Config.cfg SearchPath has to be filled with a valid path");
				_service = new DummySearchService();
			}
			else
			{
				// We only need the share if the path is a network path and if the configs user is set
				if (cfg.SearchPath.StartsWith(@"\\") && !string.IsNullOrWhiteSpace(cfg.UserName))
				{
					// Split the domain and username
					string[] usernameSplit = cfg.UserName.Split(@"\", StringSplitOptions.RemoveEmptyEntries);
					if (usernameSplit.Length == 1)
						// In case there is not domain we use an empty string
						usernameSplit = ["", usernameSplit[0]];

					NetworkCredential xCred = new NetworkCredential(usernameSplit[1], cfg.Password, usernameSplit[0]);

					shareConnector = new NetworkShareConnector(cfg.SearchPath, xCred);
					_              = Directory.GetDirectories(cfg.SearchPath);
				}

				_dog          = new Watchdog();
				_dog.Watched += Dog_Watched;
				_dog.Init(cfg);

				_service = new SearchService(cfg);
			}

			var builder = WebApplication.CreateBuilder(new WebApplicationOptions()
			{
				ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
				Args            = args
			});

			// Add services to the container.
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddSyncfusionBlazor();
			builder.Services.AddSingleton<ISearchService>(_service);
			builder.Host.UseWindowsService();

			_dog?.Start();

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			// app.UseHttpsRedirection();

			app.UseStaticFiles();

			app.UseRouting();

			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");

			// This call blocks the main thread
			app.Run();

			_dog?.Stop();
			_service.CleanUp();

			// Save the DB just in case
			if (!_newConfigSet)
				_service.SaveDB();
			else if (System.IO.File.Exists(ConfigPath))
				_service.DelDB();

			shareConnector?.Dispose();
			_logHandler.StopHandler();
		}

		internal static bool SetNewConfig(WebConfigModel cfg)
		{
			try
			{
				System.IO.File.WriteAllText(ConfigPath, new Config(cfg).ToCryJson());
				_newConfigSet = true;
				return true;
			}
			catch (Exception ex)
			{
				Log(ex.ToString());
				return false;
			}
		}

		private static void ExceptionManager_ExceptionCaught(ExceptionManager.ExceptionInfo exInfo)
		{
			// This function should never throw an exception as it could cause loops
			try
			{
				Log(exInfo.CaughtException.ToString());
			}
			catch { }
		}

		private static void _logHandler_LoggedEntry(LogTypes logType, string info)
		{
			// This function should never throw an exception as it could cause loops
			try
			{
				Console.WriteLine(info);
			}
			catch { }
		}

		public static void Log(string info)
		{
			// This function should never throw an exception as it could cause loops
			try
			{
				_logHandler.Log(LogTypes.Log, info);
			}
			catch { }
		}

		private static void Dog_Watched(Watchdog.WatchedEventArgs data)
		{
			// We block this event as the watchdog is threaded with a blocking collection
			// and we have to do the updates one after another
			try
			{
				switch (data.ChangeType)
				{
					case WatcherChangeTypes.Created:
					{
						_service.AddPath(data.FullPath);
						break;
					}
					case WatcherChangeTypes.Deleted:
					{
						_service.DeletePath(data.FullPath);
						break;
					}
					case WatcherChangeTypes.Renamed:
					{
						_service.UpdatePath(data.OldFullPath, data.FullPath);
						break;
					}
					default:
					{
						Program.Log($"Got changetype: {data.ChangeType}, we do nothing in this case");
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Log(ex.ToString() + Environment.NewLine + $"Oldpath: {data.OldFullPath}; Newpath: {data.FullPath}");
				LibTools.ExceptionManager.AddException(ex);
			}
		}

		private static void _CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
		{
			if (e.ExceptionObject is System.Exception ex)
				Log(ex.ToString());
		}

		private static void _TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
		{
			Log(e.Exception.ToString());
		}

		private enum LogTypes
		{
			Log
		}
	}
}
