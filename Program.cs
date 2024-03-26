using SearchCacher.Data;
using Syncfusion.Blazor;
using System.Reflection;

namespace SearchCacher
{
	public class Program
	{
		private static readonly string ConfigPath = Path.Combine(CryLib.Core.Paths.ExecuterPath, "config.cfg");

		private static SearchService _service;

		public static void Main(string[] args)
		{
			Console.Title = Assembly.GetExecutingAssembly().GetName().Name + " ver. 1.0.1.0";

			CryLib.Core.LibTools.ExceptionManager.bAllowCollection = true;
			CryLib.Core.LibTools.ExceptionManager.bLogExceptions   = true;

			if (!System.IO.File.Exists(ConfigPath))
			{
				System.IO.File.WriteAllText(ConfigPath, new Config().ToCryJson());
				return;
			}

			Config? cfg = System.IO.File.ReadAllText(ConfigPath).FromCryJson<Config>();

			if (cfg is null)
				throw new Exception("Config is invalid, fix or delete it");

			Watchdog dog = new Watchdog();
			dog.Watched += Dog_Watched;
			dog.Init(cfg.SearchPath);

			var builder = WebApplication.CreateBuilder(args);

			_service = new SearchService(cfg);

			// Add services to the container.
			builder.Services.AddRazorPages();
			builder.Services.AddServerSideBlazor();
			builder.Services.AddSyncfusionBlazor();
			builder.Services.AddSingleton<SearchService>(_service);

			dog.Start();

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

			app.Run();

			_service?.CleanUp();

			// Save the DB just in case
			_service?.SaveDB();
		}

		private static void Dog_Watched(Watchdog.WatchedEventArgs data)
		{
			Task.Run(() =>
			{
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
								Console.WriteLine($"Got changetype: {data.ChangeType}, we do nothing in this case");
								break;
							}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex);
					CryLib.Core.LibTools.ExceptionManager.AddException(ex);
				}
			});
		}
	}
}
