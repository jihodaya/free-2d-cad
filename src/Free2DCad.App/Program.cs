using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Free2DCad.App;

internal static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		// UI 밖(백그라운드 스레드 등)에서 난 예외는 막을 수 없으므로 최소한 로그를 남긴다
		AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("처리되지 않은 예외(종료)", e.ExceptionObject as Exception);
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			CrashLog.Write("관찰되지 않은 작업 예외", e.Exception);
			e.SetObserved();
		};
		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

internal sealed class App : Application
{
	public override void Initialize()
	{
		this.Styles.Add(new FluentTheme());
		AppStyles.Apply(this.Styles);
		this.RequestedThemeVariant = ThemeVariant.Dark;
	}

	/// <summary>실행 파일에 포함한 아이콘 (Assets\Free2DCad.ico).</summary>
	public static readonly Lazy<WindowIcon?> AppIcon = new(() =>
	{
		using var s = typeof(App).Assembly.GetManifestResourceStream("Free2DCad.App.Free2DCad.ico");
		return s == null ? null : new WindowIcon(s);
	});

	public override void OnFrameworkInitializationCompleted()
	{
		// 화면 처리 중 예외: 앱을 끄지 않고 로그 + 명령줄 알림 후 계속 (작업 중인 도면을 잃지 않도록)
		Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
		{
			CrashLog.Write("UI 예외(복구됨)", e.Exception);
			e.Handled = true;
			if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mw })
			{
				mw.ReportRecoveredError(e.Exception);
			}
		};

		// 치수 문자 배치에 실제 글꼴 크기를 쓴다 (문자와 치수선이 겹치지 않게, 좁은 치수 맞춤)
		ACadSharp.Entities.Dimension.TextMeasurer = (m, doc) => Free2DCad.Rendering.SceneBuilder.MeasureText(m, doc);

		// 모든 창(메인·대화상자)에 앱 아이콘: 창이 열릴 때 아이콘이 없으면 넣는다
		Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => w.Icon ??= AppIcon.Value);

		if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// 명령줄 첫 인자로 도면 경로를 주면 바로 연다
			string? path = desktop.Args is { Length: > 0 } a ? a[0] : null;
			// 시작 로고를 먼저 띄우고, 로고가 그려진 뒤 메인 창을 만든다 (메인 창 준비에 몇 초 걸림)
			var splash = new SplashWindow();
			desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
			desktop.MainWindow = splash;
			splash.Show();
			Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				MainWindow main;
				try
				{
					main = new MainWindow(path);
				}
				catch (Exception)
				{
					splash.Close();
					throw;
				}
				desktop.MainWindow = main;
				main.Opened += async (_, _) =>
				{
					await Task.Delay(250); // 메인 창이 그려진 뒤 로고를 닫는다 (깜박임 방지)
					splash.Close();
				};
				main.Show();
			}, Avalonia.Threading.DispatcherPriority.Background);
		}
		base.OnFrameworkInitializationCompleted();
	}
}

/// <summary>%LOCALAPPDATA%\Free2DCad\crash.log 에 예외를 기록한다.</summary>
internal static class CrashLog
{
	public static string FilePath { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Free2DCad", "crash.log");

	public static void Write(string kind, Exception? ex)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
			File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {kind}\n{ex}\n\n");
		}
		catch (Exception)
		{
			// 로그 기록 실패는 무시
		}
	}
}
