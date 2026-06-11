using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tts.App.Configuration;
using Tts.App.HostedServices;
using Tts.App.Services;
using Tts.App.Services.Tray;
using Tts.App.ViewModels;

namespace Tts.App;

public partial class App : System.Windows.Application
{
	private IHost? _host;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		ShutdownMode = ShutdownMode.OnExplicitShutdown;

		_host = Host.CreateDefaultBuilder(e.Args)
			.ConfigureServices(services =>
			{
				services.AddSingleton<AppPaths>();
				services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
				services.AddHostedService<SettingsWarmupService>();
				services.AddSingleton<TrayLifecycleService>();
				services.AddSingleton<SettingsWindowViewModel>();
				services.AddTransient<SettingsWindow>();
			})
			.Build();

		try
		{
			await _host.StartAsync();

			var trayLifecycle = _host.Services.GetRequiredService<TrayLifecycleService>();
			trayLifecycle.Initialize();
			trayLifecycle.ShowSettingsWindow();
		}
		catch (Exception exception)
		{
			System.Windows.MessageBox.Show(
				exception.Message,
				"Speech-to-Text Daemon startup failed",
				MessageBoxButton.OK,
				MessageBoxImage.Error);

			Shutdown(1);
		}
	}

	protected override async void OnExit(ExitEventArgs e)
	{
		if (_host is not null)
		{
			await _host.StopAsync(TimeSpan.FromSeconds(5));
			_host.Dispose();
		}

		base.OnExit(e);
	}
}

