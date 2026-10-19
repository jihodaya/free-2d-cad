using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Free2DCad.Rendering;
using SkiaSharp;

namespace Free2DCad.App;

/// <summary>
/// 150dpi 고해상도 플롯 전체 화면 미리보기 대화상자.
/// </summary>
public sealed class PlotPreviewDialog : Window
{
	public PlotPreviewDialog(PlotOptions options, Scene modelScene, LayoutScene? layoutScene)
	{
		this.Title = "플롯 미리보기";
		this.Width = 960;
		this.Height = 700;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x60, 0x60, 0x60));

		var root = new DockPanel();

		// 상단 툴바
		var toolBar = new Border
		{
			Height = 36,
			Background = CadTheme.DialogBg,
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(8, 4),
		};

		var btnClose = new Button
		{
			Content = "미리보기 닫기(_C)",
			Padding = new Thickness(12, 4),
		};
		btnClose.Click += (_, _) => this.Close();
		toolBar.Child = btnClose;
		DockPanel.SetDock(toolBar, Dock.Top);
		root.Children.Add(toolBar);

		// 중앙 스크롤 뷰어 + 용지 이미지
		var image = new Image
		{
			Stretch = Stretch.Uniform,
			Margin = new Thickness(20),
		};

		var paperBorder = new Border
		{
			Background = Brushes.White,
			BorderBrush = Brushes.Black,
			BorderThickness = new Thickness(1),
			BoxShadow = new BoxShadows(new BoxShadow { Blur = 10, Spread = 2, Color = Avalonia.Media.Color.FromArgb(120, 0, 0, 0) }),
			Child = image,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		var scroll = new ScrollViewer
		{
			Content = paperBorder,
			HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
		};
		root.Children.Add(scroll);
		this.Content = root;

		// 150dpi 비트맵 렌더링
		try
		{
			using var skBmp = PlotRenderer.RenderBitmap(options, 150, modelScene, layoutScene);
			using var ms = new MemoryStream();
			skBmp.Encode(ms, SKEncodedImageFormat.Png, 95);
			ms.Position = 0;
			image.Source = new Bitmap(ms);
		}
		catch (Exception ex)
		{
			paperBorder.Child = new TextBlock
			{
				Text = $"미리보기 생성 실패: {ex.Message}",
				Foreground = Brushes.Red,
				Margin = new Thickness(20),
			};
		}
	}
}
