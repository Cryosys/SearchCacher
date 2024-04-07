using CryLib.Core;
using SearchCacher.Data;
using Syncfusion.Blazor;
using System.Reflection;

namespace SearchCacher
{
	public class Program
	{
		private static readonly string ConfigPath = Path.Combine(Paths.ExecuterPath, "config.cfg");

		private static SearchService _service;
		private static Watchdog _dog;
		private static AdaptiveLogHandler<LogTypes> _logHandler = new AdaptiveLogHandler<LogTypes>(Path.Combine(Paths.AppPath, "Logs"));

		public static void Main(string[] args)
		{
			// Add exception handler for unhandled exception in other threads or the GUI thread
			AppDomain.CurrentDomain.UnhandledException += _CurrentDomain_UnhandledException;
			TaskScheduler.UnobservedTaskException      += _TaskScheduler_UnobservedTaskException;

			_logHandler.LoggedEntry += _logHandler_LoggedEntry;
			_logHandler.AddLog(LogTypes.Log);
			_logHandler.StartHandler();

			Console.Title = Assembly.GetExecutingAssembly().GetName().Name + " ver. " + SearchService.Version;

			LibTools.ExceptionManager.ExceptionCaught += ExceptionManager_ExceptionCaught;
			LibTools.ExceptionManager.bAllowCollection = true;
			LibTools.ExceptionManager.bLogExceptions   = true;

			if (!System.IO.File.Exists(ConfigPath))
			{
				System.IO.File.WriteAllText(ConfigPath, new Config().ToCryJson());
				_logHandler.StopHandler();
				return;
			}

			Config? cfg = System.IO.File.ReadAllText(ConfigPath).FromCryJson<Config>();

			if (cfg is null)
				throw new Exception("Config is invalid, fix or delete it");

			_dog          = new Watchdog();
			_dog.Watched += Dog_Watched;
			_dog.Init(cfg);

			var builder = WebApplication.CreateBuilder(args);

			_service = new SearchService(cfg);

			// Add services to the container.
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddSyncfusionBlazor();
			builder.Services.AddSingleton<SearchService>(_service);

			_dog.Start();

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

			_dog.Stop();
			_service.CleanUp();

			// Save the DB just in case
			_service.SaveDB();

			_logHandler.StopHandler();
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
				Log(ex.ToString());
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
