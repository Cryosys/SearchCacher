using CryLib.Core;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Hosting.WindowsServices;
using SearchCacher.Data;
using Syncfusion.Blazor;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace SearchCacher
{
	public class Program
	{
		private static readonly string ConfigPath = Path.Combine(Paths.ExecuterPath, "config.cfg");

		private static ISearchService? _service;
		private static Watchdog? _dog;
		private static AdaptiveLogHandler<LogTypes> _logHandler = new AdaptiveLogHandler<LogTypes>(Path.Combine(Paths.AppPath, "Logs"));

		private static bool _newConfigSet     = false;
		private static Config? _serviceConfig = null;

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
				System.IO.File.WriteAllText(ConfigPath, new Config().ToCryJson());

			_serviceConfig = System.IO.File.ReadAllText(ConfigPath).FromCryJson<Config>();

			// We only init the network share and watchdog if the config is valid
			List<NetworkShareConnector> shareConnectors = [];

			if (_serviceConfig is null)
				throw new Exception("Config is invalid, fix or delete it");

			if (_serviceConfig.DBConfigs.Count == 0)
			{
				Log("Config.cfg SearchPath has to be filled with a valid path");
				_service = new DummySearchService();
			}
			else
			{
				_dog          = new Watchdog();
				_dog.Watched += Dog_Watched;

				foreach (var cfg in _serviceConfig.DBConfigs)
				{
					// We only need the share if the path is a network path and if the configs user is set
					if (cfg.RootPath.StartsWith(@"\\") && !string.IsNullOrWhiteSpace(cfg.UserName))
					{
						// Split the domain and username
						string[] usernameSplit = cfg.UserName.Split(@"\", StringSplitOptions.RemoveEmptyEntries);
						if (usernameSplit.Length == 1)
							// In case there is not domain we use an empty string
							usernameSplit = ["", usernameSplit[0]];

						NetworkCredential xCred = new NetworkCredential(usernameSplit[1], cfg.Password, usernameSplit[0]);

						shareConnectors.Add(new NetworkShareConnector(cfg.RootPath, xCred));
						_ = Directory.GetDirectories(cfg.RootPath);
					}

					_dog.AddWatcher(cfg.RootPath);
				}

				_service = new SearchService(_serviceConfig);
			}

			var builder = WebApplication.CreateBuilder(new WebApplicationOptions()
			{
				ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
				Args            = args
			});

			// Add services to the container.
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddHttpContextAccessor();
			builder.Services.AddSyncfusionBlazor();
			builder.Services.AddSingleton<ISearchService>(_service);
			builder.Services.AddWindowsService();

			bool useHttpsRedirect = false;
			int sslPort           = 443;

			foreach (string arg in args)
			{
				if (arg.StartsWith("--thumb="))
				{
					string[] thumbArgs = arg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);

					if (thumbArgs.Length != 2)
						continue;

					X509Certificate2? cert = null;

					using (X509Store store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
					{
						store.Open(OpenFlags.ReadOnly);

						for (int iIndex = 0; iIndex < store.Certificates.Count; iIndex++)
						{
							var xCert = store.Certificates[iIndex];
							if (xCert.Thumbprint.ToUpper() == thumbArgs[1].ToUpper())
							{
								cert = xCert;
								Console.WriteLine("Found cert: " + cert.Subject);
								break;
							}
						}
					}

					if (cert == null)
					{
						Console.WriteLine("Could find not find cert in certificate store.");
						throw new ArgumentException("Could find not find cert in certificate store.");
					}

					Console.WriteLine("Assigning cert...");

					builder.WebHost.ConfigureKestrel((context, serverOptions) =>
					{
						serverOptions.ConfigureHttpsDefaults((listeningOptions) =>
						{
							listeningOptions.ServerCertificate = cert;
						});
					});
				}
				else if (arg.StartsWith("--sslport="))
				{
					string[] portArgs = arg.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);

					if (portArgs.Length != 2)
						continue;

					if (!int.TryParse(portArgs[1], out sslPort))
					{
						Console.WriteLine($"'{portArgs[1]}' is not a valid port");
						throw new ArgumentException($"'{portArgs[1]}' is not a valid port");
					}
				}
				else if (arg.StartsWith("--httpsredirect"))
					useHttpsRedirect = true;
			}

			_dog?.Start();

			var app = builder.Build();

			// Register a shutdown callback so that we can stop all services and save the DB.
			// We only really only need this register if the service runs as a windows service.
			// For a web service doing everything after the run would be fine timeout wise.
			// Windows Services however have a stop timeout which we need to request more time for when saving the DB.

			if (WindowsServiceHelpers.IsWindowsService())
				app.Lifetime.ApplicationStopping.Register(() =>
				{
					Console.WriteLine("Stopping service...");

					_dog?.Stop();
					_service.CleanUp();

					Console.WriteLine("Done clean-up");

					// Request more time for the service to stop,
					// we need it to save the DB. We just request 3 minutes, but it will stop sooner if the service returns sooner.
					WindowsServiceLifetime? winServiceLifeTime = app.Services.GetService(typeof(WindowsServiceLifetime)) as WindowsServiceLifetime;
					if (winServiceLifeTime is not null)
						winServiceLifeTime.RequestAdditionalTime(TimeSpan.FromMinutes(3));

					Console.WriteLine("Saving DB");

					// Save the DB just in case
					if (!_newConfigSet)
						_service.SaveDB();
					else if (System.IO.File.Exists(ConfigPath))
						_service.DelDB();

					Console.WriteLine("Saved DB");

					foreach (var shareConnector in shareConnectors)
						shareConnector?.Dispose();

					Console.WriteLine("Disposed connectors");
					_logHandler.StopHandler();
					Console.WriteLine("Stopped logger");
					app.Lifetime.StopApplication();
				});

			if (useHttpsRedirect)
				app.UseHttpsRedirection();
			// app.Urls.Add($"https://*:{sslPort}");

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment())
			{
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			app.UseStaticFiles();

			app.UseRouting();

			app.MapBlazorHub();
			app.MapFallbackToPage("/_Host");

			// This call blocks the main thread
			app.Run();

			if (WindowsServiceHelpers.IsWindowsService())
				return;

			Log("Stopping service...");

			_dog?.Stop();
			_service.CleanUp();

			// Save the DB just in case
			if (!_newConfigSet)
				_service.SaveDB();
			else if (System.IO.File.Exists(ConfigPath))
				_service.DelDB();

			foreach (var shareConnector in shareConnectors)
				shareConnector?.Dispose();

			_logHandler.StopHandler();
		}

		internal static bool SetNewConfig(WebConfigModel cfg, bool forceDBDelete = true) => SetNewConfig(new Config(cfg));

		internal static bool SetNewConfig(Config cfg, bool forceDBDelete = true)
		{
			try
			{
				if (_serviceConfig is not null)
				{
					foreach (var dbConfig in _serviceConfig.DBConfigs)
					{
						var config = cfg.DBConfigs.Find(x => x.ID == dbConfig.ID);

						// In case the password it empty we take the old one
						if (config is not null && string.IsNullOrWhiteSpace(config.Password))
							config.Password = dbConfig.Password;
					}
				}

				System.IO.File.WriteAllText(ConfigPath, cfg.ToCryJson(Newtonsoft.Json.Formatting.Indented));
				if (forceDBDelete)
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
				if (_service is null)
				{
					Log("Service is null, but we got watchdog events, something is wrong...");
					return;
				}

				switch (data.ChangeType)
				{
					case WatcherChangeTypes.Created:
					{
						_service.AddPath(data.SearchPath, data.FullPath);
						break;
					}
					case WatcherChangeTypes.Deleted:
					{
						_service.DeletePath(data.SearchPath, data.FullPath);
						break;
					}
					case WatcherChangeTypes.Renamed:
					{
						_service.UpdatePath(data.SearchPath, data.OldFullPath, data.FullPath);
						break;
					}
					default:
					{
						Log($"Got changetype: {data.ChangeType}, we do nothing in this case");
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
