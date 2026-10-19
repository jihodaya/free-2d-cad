using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free2DCad.App;

/// <summary>
/// 시작 로고 창: 프로그램을 켜면 메인 창이 준비될 때까지 "Free 2D CAD" 로고·버전·불러오는 중을 보여 준다.
/// </summary>
public sealed class SplashWindow : Window
{
	public SplashWindow()
	{
		this.Title = "Free 2D CAD";
		this.Width = 480;
		this.Height = 270;
		this.CanResize = false;
		this.WindowDecorations = WindowDecorations.None;
		this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		this.Background = new SolidColorBrush(Color.FromRgb(0x24, 0x28, 0x2C));

		var accent = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));
		var gray = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));

		var title = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 16,
			HorizontalAlignment = HorizontalAlignment.Center,
			Children =
			{
				IconHelper.AppLogo(72),
				new StackPanel
				{
					VerticalAlignment = VerticalAlignment.Center,
					Spacing = 2,
					Children =
					{
						new TextBlock { Text = "Free 2D CAD", FontSize = 38, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
						new TextBlock { Text = "무료 2D CAD (DWG·DXF)", FontSize = 13, Foreground = gray },
					},
				},
			},
		};

		var bar = new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4, Foreground = accent, Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x45)) };
		var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		footer.Children.Add(new TextBlock { Text = "불러오는 중...", FontSize = 12, Foreground = gray });
		var ver = new TextBlock { Text = $"버전 {MainWindow.AppVersion}", FontSize = 12, Foreground = gray };
		Grid.SetColumn(ver, 1);
		footer.Children.Add(ver);

		var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), Margin = new Thickness(32, 28, 32, 22) };
		root.Children.Add(new Border { Child = title, VerticalAlignment = VerticalAlignment.Center });
		Grid.SetRow(bar, 1);
		bar.Margin = new Thickness(0, 0, 0, 10);
		root.Children.Add(bar);
		Grid.SetRow(footer, 2);
		root.Children.Add(footer);

		// 테두리 (어두운 바탕에서 창 경계가 보이게) + 위쪽 파란 띠
		this.Content = new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x55, 0x5C)),
			BorderThickness = new Thickness(1),
			Child = new DockPanel
			{
				Children =
				{
					new Border { Height = 4, Background = accent, [DockPanel.DockProperty] = Dock.Top },
					root,
				},
			},
		};
	}
}
