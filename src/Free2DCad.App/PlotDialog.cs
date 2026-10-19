using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Free2DCad.Rendering;
using SkiaSharp;
using Point = Avalonia.Point;

namespace Free2DCad.App;

/// <summary>
/// 세션 동안 기억할 플롯 설정.
/// </summary>
public sealed class PlotSessionSettings
{
	public string PrinterName { get; set; } = "PDF로 저장 (Free 2D CAD)";
	public string PaperSizeName { get; set; } = "A3";
	public string PlotArea { get; set; } = "범위";
	public bool Center { get; set; } = true;
	public bool FitToPaper { get; set; } = true;
	public double ScaleMm { get; set; } = 1.0;
	public double ScaleUnits { get; set; } = 1.0;
	public PlotColorMode Colors { get; set; } = PlotColorMode.Color;
	public bool IsLandscape { get; set; } = true;
	public double LineWidthMm { get; set; } = 0.18;
	public Bounds? WindowBounds { get; set; }
}

/// <summary>
/// 기존 CAD 스타일 플롯(인쇄) 대화상자.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PlotDialog : Window
{
	private static PlotSessionSettings? s_lastSettings;

	private readonly Scene _modelScene;
	private readonly LayoutScene? _layoutScene;
	private readonly ViewTransform? _canvasView;
	private readonly string _docName;
	private readonly string _spaceName;
	private readonly Func<Task<Bounds?>> _windowPicker;
	private readonly Action<string>? _onPlotCompleted;
	private readonly Action? _onPlotStarted;

	private Bounds? _windowBounds;
	private bool _updatingUi;
	private CancellationTokenSource? _previewCts;

	// UI 컨트롤
	private readonly ComboBox _cboPrinter = new();
	private readonly ComboBox _cboPaperSize = new();
	private readonly ComboBox _cboPlotArea = new();
	private readonly Button _btnWindowPick = new();
	private readonly CheckBox _chkCenter = new();
	private readonly CheckBox _chkFit = new();
	private readonly TextBox _txtScaleMm = new();
	private readonly TextBox _txtScaleUnits = new();
	private readonly TextBlock _lblEffectiveScale = new();
	private readonly ComboBox _cboColorStyle = new();
	private readonly RadioButton _rbLandscape = new();
	private readonly RadioButton _rbPortrait = new();
	private readonly TextBox _txtLineWidth = new();

	private readonly Image _previewImage = new();
	private readonly Border _previewPaper = new();
	private readonly TextBlock _previewInfo = new();

	public PlotDialog(
		Scene modelScene,
		LayoutScene? layoutScene,
		ViewTransform? canvasView,
		string docName,
		string spaceName,
		Func<Task<Bounds?>> windowPicker,
		Action<string>? onPlotCompleted,
		Action? onPlotStarted)
	{
		this._modelScene = modelScene;
		this._layoutScene = layoutScene;
		this._canvasView = canvasView;
		this._docName = string.IsNullOrEmpty(docName) ? "Drawing1" : docName;
		this._spaceName = string.IsNullOrEmpty(spaceName) ? "Model" : spaceName;
		this._windowPicker = windowPicker;
		this._onPlotCompleted = onPlotCompleted;
		this._onPlotStarted = onPlotStarted;

		bool isLayout = layoutScene != null;
		this.Title = $"플롯 - {(isLayout ? "배치" : "모형")}";
		this.Width = 840;
		this.Height = 600;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.CanResize = false;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(12) };

		// 하단 버튼 모음
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 메인 본문: 좌측 설정 + 우측 미리보기
		var centerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, 300"),
		};

		var settingsScroll = new ScrollViewer
		{
			Content = this.BuildSettingsPanel(isLayout),
			Margin = new Thickness(0, 0, 12, 0),
		};
		Grid.SetColumn(settingsScroll, 0);
		centerGrid.Children.Add(settingsScroll);

		var previewPanel = this.BuildPreviewPanel();
		Grid.SetColumn(previewPanel, 1);
		centerGrid.Children.Add(previewPanel);

		root.Children.Add(centerGrid);
		this.Content = root;

		// 설정 초기화 및 세션 복원
		this.InitSettings(isLayout);
		this.UpdatePreview();
	}

	private Control BuildBottomBar()
	{
		var bar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
			Margin = new Thickness(0, 10, 0, 0),
		};

		var btnPreview = new Button
		{
			Content = "미리보기(P)...",
			Padding = new Thickness(14, 6),
		};
		btnPreview.Click += (_, _) => this.ShowLargePreview();
		Grid.SetColumn(btnPreview, 0);
		bar.Children.Add(btnPreview);

		var rightStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Right,
		};

		var btnOk = new Button
		{
			Content = "확인",
			Padding = new Thickness(16, 6),
			IsDefault = true,
		};
		btnOk.Click += async (_, _) => await this.ExecutePlotAsync();
		rightStack.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소",
			Padding = new Thickness(16, 6),
			IsCancel = true,
		};
		btnCancel.Click += (_, _) => this.Close();
		rightStack.Children.Add(btnCancel);

		Grid.SetColumn(rightStack, 2);
		bar.Children.Add(rightStack);

		return bar;
	}

	private Control BuildSettingsPanel(bool isLayout)
	{
		var panel = new StackPanel { Spacing = 8 };

		// 1. 프린터/플로터
		var grpPrinter = CreateGroupBox("프린터 / 플로터");
		var pGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("95,*") };
		pGrid.Children.Add(new TextBlock { Text = "이름(N):", VerticalAlignment = VerticalAlignment.Center });
		this._cboPrinter.HorizontalAlignment = HorizontalAlignment.Stretch;
		this._cboPrinter.FontSize = 11;
		this._cboPrinter.Background = Brushes.White;
		this._cboPrinter.BorderBrush = CadTheme.DropdownBorder;
		Grid.SetColumn(this._cboPrinter, 1);
		pGrid.Children.Add(this._cboPrinter);
		grpPrinter.Child = pGrid;
		panel.Children.Add(grpPrinter);

		// 2. 용지 크기
		var grpPaper = CreateGroupBox("용지 크기");
		this._cboPaperSize.HorizontalAlignment = HorizontalAlignment.Stretch;
		this._cboPaperSize.FontSize = 11;
		this._cboPaperSize.Background = Brushes.White;
		this._cboPaperSize.BorderBrush = CadTheme.DropdownBorder;
		grpPaper.Child = this._cboPaperSize;
		panel.Children.Add(grpPaper);

		// 3. 플롯 영역
		var grpArea = CreateGroupBox("플롯 영역");
		var aGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("95,*,Auto"),
		};
		aGrid.Children.Add(new TextBlock { Text = "플롯 대상(A):", VerticalAlignment = VerticalAlignment.Center });
		this._cboPlotArea.HorizontalAlignment = HorizontalAlignment.Stretch;
		this._cboPlotArea.FontSize = 11;
		this._cboPlotArea.Background = Brushes.White;
		this._cboPlotArea.BorderBrush = CadTheme.DropdownBorder;
		Grid.SetColumn(this._cboPlotArea, 1);
		aGrid.Children.Add(this._cboPlotArea);

		this._btnWindowPick.Content = "윈도우 <";
		this._btnWindowPick.Padding = new Thickness(8, 2);
		this._btnWindowPick.Margin = new Thickness(6, 0, 0, 0);
		this._btnWindowPick.FontSize = 11;
		this._btnWindowPick.Click += async (_, _) => await this.OnWindowPickClicked();
		Grid.SetColumn(this._btnWindowPick, 2);
		aGrid.Children.Add(this._btnWindowPick);

		grpArea.Child = aGrid;
		panel.Children.Add(grpArea);

		// 4. 플롯 간격띄우기 & 방향
		var grpOffsetDir = CreateGroupBox("플롯 간격띄우기 및 도면 방향");
		var odGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,*"),
		};

		var offsetStack = new StackPanel { Spacing = 4 };
		this._chkCenter.Content = "플롯의 중심(C)";
		this._chkCenter.FontSize = 11;
		offsetStack.Children.Add(this._chkCenter);
		Grid.SetColumn(offsetStack, 0);
		odGrid.Children.Add(offsetStack);

		var dirStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
		this._rbLandscape.Content = "가로(L)";
		this._rbLandscape.FontSize = 11;
		this._rbPortrait.Content = "세로(O)";
		this._rbPortrait.FontSize = 11;
		dirStack.Children.Add(this._rbLandscape);
		dirStack.Children.Add(this._rbPortrait);
		Grid.SetColumn(dirStack, 1);
		odGrid.Children.Add(dirStack);

		grpOffsetDir.Child = odGrid;
		panel.Children.Add(grpOffsetDir);

		// 5. 플롯 축척
		var grpScale = CreateGroupBox("플롯 축척");
		var sStack = new StackPanel { Spacing = 6 };
		this._chkFit.Content = "용지에 맞춤(F)";
		this._chkFit.FontSize = 11;
		sStack.Children.Add(this._chkFit);

		// 축척: [ 1 ] mm = [ 1 ] 단위 — 칸과 글자가 한 줄 가운데에 맞도록 고정 높이 24
		var customScaleGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,64,Auto,64,Auto"),
			Height = 26,
		};
		var lblScale = new TextBlock { Text = "축척:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
		customScaleGrid.Children.Add(lblScale);
		StyleSmallBox(this._txtScaleMm);
		Grid.SetColumn(this._txtScaleMm, 1);
		customScaleGrid.Children.Add(this._txtScaleMm);

		var lblMm = new TextBlock { Text = "mm  =", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
		Grid.SetColumn(lblMm, 2);
		customScaleGrid.Children.Add(lblMm);

		StyleSmallBox(this._txtScaleUnits);
		Grid.SetColumn(this._txtScaleUnits, 3);
		customScaleGrid.Children.Add(this._txtScaleUnits);

		var lblUnits = new TextBlock { Text = "단위", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
		Grid.SetColumn(lblUnits, 4);
		customScaleGrid.Children.Add(lblUnits);

		sStack.Children.Add(customScaleGrid);

		this._lblEffectiveScale.FontSize = 11;
		this._lblEffectiveScale.Foreground = Brushes.DimGray;
		sStack.Children.Add(this._lblEffectiveScale);

		grpScale.Child = sStack;
		panel.Children.Add(grpScale);

		// 6. 플롯 스타일 테이블 및 선 굵기
		var grpStyle = CreateGroupBox("플롯 스타일 테이블 (펜 지정) 및 선가중치");
		var stGrid = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto, Auto"),
			ColumnDefinitions = new ColumnDefinitions("120,*"),
		};

		stGrid.Children.Add(new TextBlock { Text = "플롯 스타일 테이블:", VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
		this._cboColorStyle.HorizontalAlignment = HorizontalAlignment.Stretch;
		this._cboColorStyle.FontSize = 11;
		this._cboColorStyle.Background = Brushes.White;
		this._cboColorStyle.BorderBrush = CadTheme.DropdownBorder;
		Grid.SetColumn(this._cboColorStyle, 1);
		stGrid.Children.Add(this._cboColorStyle);

		var lblLw = new TextBlock { Text = "기본 선 굵기 (mm):", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
		Grid.SetRow(lblLw, 1);
		Grid.SetColumn(lblLw, 0);
		stGrid.Children.Add(lblLw);

		StyleSmallBox(this._txtLineWidth);
		this._txtLineWidth.HorizontalAlignment = HorizontalAlignment.Left;
		this._txtLineWidth.Margin = new Thickness(0, 6, 0, 0);
		Grid.SetRow(this._txtLineWidth, 1);
		Grid.SetColumn(this._txtLineWidth, 1);
		stGrid.Children.Add(this._txtLineWidth);

		grpStyle.Child = stGrid;
		panel.Children.Add(grpStyle);

		return panel;
	}

	private Control BuildPreviewPanel()
	{
		var panel = new Border
		{
			Background = CadTheme.DialogBg,
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(4),
			Padding = new Thickness(8),
		};

		var dock = new DockPanel();

		var title = new TextBlock
		{
			Text = "부분 미리보기",
			FontWeight = FontWeight.Bold,
			FontSize = 11,
			Margin = new Thickness(0, 0, 0, 6),
		};
		DockPanel.SetDock(title, Dock.Top);
		dock.Children.Add(title);

		this._previewInfo.FontSize = 10;
		this._previewInfo.Foreground = Brushes.DimGray;
		this._previewInfo.HorizontalAlignment = HorizontalAlignment.Center;
		this._previewInfo.Margin = new Thickness(0, 6, 0, 0);
		DockPanel.SetDock(this._previewInfo, Dock.Bottom);
		dock.Children.Add(this._previewInfo);

		this._previewPaper.Background = Brushes.White;
		this._previewPaper.BorderBrush = Brushes.Gray;
		this._previewPaper.BorderThickness = new Thickness(1);
		this._previewPaper.HorizontalAlignment = HorizontalAlignment.Center;
		this._previewPaper.VerticalAlignment = VerticalAlignment.Center;
		this._previewPaper.BoxShadow = new BoxShadows(new BoxShadow { Blur = 6, Spread = 1, Color = Avalonia.Media.Color.FromArgb(60, 0, 0, 0) });

		this._previewImage.Stretch = Stretch.Uniform;
		this._previewPaper.Child = this._previewImage;

		dock.Children.Add(this._previewPaper);
		panel.Child = dock;
		return panel;
	}

	private static Border CreateGroupBox(string header)
	{
		var border = new Border
		{
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD8, 0xD8, 0xD8)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(4),
			Background = Brushes.White,
			Padding = new Thickness(10, 8),
		};

		var dock = new DockPanel();
		var lbl = new TextBlock
		{
			Text = header,
			FontWeight = FontWeight.SemiBold,
			FontSize = 11,
			Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x15, 0x65, 0xC0)),
			Margin = new Thickness(0, 0, 0, 6),
		};
		DockPanel.SetDock(lbl, Dock.Top);
		dock.Children.Add(lbl);

		return new Border
		{
			Child = dock,
		};
	}

	private void InitSettings(bool isLayout)
	{
		this._updatingUi = true;

		// 1. 프린터 목록
		var printers = new List<string> { "PDF로 저장 (Free 2D CAD)" };
		try
		{
			foreach (string p in PrinterSettings.InstalledPrinters)
			{
				printers.Add(p);
			}
		}
		catch { }
		this._cboPrinter.ItemsSource = printers;

		// 2. 용지 크기
		this._cboPaperSize.ItemsSource = PlotRenderer.PaperSizes.Select(p => p.Name).ToList();

		// 3. 플롯 영역
		var areas = new List<string> { "화면표시", "범위", "윈도우" };
		if (isLayout)
		{
			areas.Insert(0, "배치");
		}
		this._cboPlotArea.ItemsSource = areas;

		// 4. 스타일
		this._cboColorStyle.ItemsSource = new[] { "acad.ctb (컬러)", "monochrome.ctb (흑백)" };

		// 세션 기억값 또는 기본값 적용
		var s = s_lastSettings ?? new PlotSessionSettings
		{
			PlotArea = isLayout ? "배치" : "범위",
		};

		this._cboPrinter.SelectedItem = printers.Contains(s.PrinterName) ? s.PrinterName : printers[0];
		this._cboPaperSize.SelectedItem = PlotRenderer.PaperSizes.Any(p => p.Name == s.PaperSizeName) ? s.PaperSizeName : "A3";
		this._cboPlotArea.SelectedItem = areas.Contains(s.PlotArea) ? s.PlotArea : areas[0];
		this._chkCenter.IsChecked = s.Center;
		this._chkFit.IsChecked = s.FitToPaper;
		this._txtScaleMm.Text = $"{s.ScaleMm:0.####}";
		this._txtScaleUnits.Text = $"{s.ScaleUnits:0.####}";
		this._cboColorStyle.SelectedIndex = (s.Colors == PlotColorMode.Monochrome) ? 1 : 0;
		this._rbLandscape.IsChecked = s.IsLandscape;
		this._rbPortrait.IsChecked = !s.IsLandscape;
		this._txtLineWidth.Text = $"{s.LineWidthMm:0.##}";
		this._windowBounds = s.WindowBounds;

		this._updatingUi = false;

		// 이벤트 연동
		this._cboPrinter.SelectionChanged += (_, _) => this.OnSettingChanged();
		this._cboPaperSize.SelectionChanged += (_, _) => this.OnSettingChanged();
		this._cboPlotArea.SelectionChanged += (_, _) => this.OnSettingChanged();
		this._chkCenter.IsCheckedChanged += (_, _) => this.OnSettingChanged();
		this._chkFit.IsCheckedChanged += (_, _) => this.OnSettingChanged();
		this._cboColorStyle.SelectionChanged += (_, _) => this.OnSettingChanged();
		this._rbLandscape.IsCheckedChanged += (_, _) => this.OnSettingChanged();
		this._rbPortrait.IsCheckedChanged += (_, _) => this.OnSettingChanged();

		this._txtScaleMm.TextChanged += (_, _) => this.OnSettingChanged();
		this._txtScaleUnits.TextChanged += (_, _) => this.OnSettingChanged();
		this._txtLineWidth.TextChanged += (_, _) => this.OnSettingChanged();
	}

	private void OnSettingChanged()
	{
		if (this._updatingUi) return;
		this.UpdateScaleUi();
		this.UpdatePreview();
	}

	private void UpdateScaleUi()
	{
		bool fit = this._chkFit.IsChecked == true;
		this._txtScaleMm.IsEnabled = !fit;
		this._txtScaleUnits.IsEnabled = !fit;

		var o = this.BuildOptions();
		double eff = PlotRenderer.EffectiveMmPerUnit(o);
		if (eff > 0)
		{
			this._lblEffectiveScale.Text = $"계산된 축척: 1 mm = {1.0 / eff:F4} 단위";
		}
		else
		{
			this._lblEffectiveScale.Text = "계산된 축척: -";
		}
	}

	private async Task OnWindowPickClicked()
	{
		this.Hide();
		try
		{
			var bounds = await this._windowPicker();
			if (bounds != null)
			{
				this._windowBounds = bounds;
				this._updatingUi = true;
				this._cboPlotArea.SelectedItem = "윈도우";
				this._updatingUi = false;
				this.UpdatePreview();
			}
		}
		finally
		{
			this.Show();
		}
	}

	private PlotOptions BuildOptions()
	{
		// 1. 용지 크기
		string paperName = (this._cboPaperSize.SelectedItem as string) ?? "A3";
		var paperInfo = PlotRenderer.PaperSizes.FirstOrDefault(p => p.Name == paperName);
		if (paperInfo == default) paperInfo = ("A3", 297.0, 420.0);

		bool isLandscape = this._rbLandscape.IsChecked == true;
		double paperWidth = isLandscape ? paperInfo.Height : paperInfo.Width;
		double paperHeight = isLandscape ? paperInfo.Width : paperInfo.Height;

		// 2. 플롯 영역
		string areaType = (this._cboPlotArea.SelectedItem as string) ?? "범위";
		Bounds area;
		if (areaType == "화면표시" && this._canvasView != null)
		{
			var (x1, y1) = this._canvasView.ToWorld(0, this._canvasView.Height);
			var (x2, y2) = this._canvasView.ToWorld(this._canvasView.Width, 0);
			area = new Bounds(Math.Min(x1, x2), Math.Max(x1, x2), Math.Min(y1, y2), Math.Max(y1, y2));
		}
		else if (areaType == "배치" && this._layoutScene != null)
		{
			area = this._layoutScene.Paper;
		}
		else if (areaType == "윈도우" && this._windowBounds != null)
		{
			area = this._windowBounds.Value;
		}
		else
		{
			// "범위"
			if (this._layoutScene != null)
			{
				area = this._layoutScene.Paper;
			}
			else
			{
				area = this._modelScene.Extents;
			}
		}

		if (area.Width <= 1e-6 || area.Height <= 1e-6)
		{
			area = new Bounds(0, 420, 0, 297);
		}

		// 3. 축척
		bool fit = this._chkFit.IsChecked == true;
		double mm = double.TryParse(this._txtScaleMm.Text, out var vMm) && vMm > 0 ? vMm : 1.0;
		double units = double.TryParse(this._txtScaleUnits.Text, out var vU) && vU > 0 ? vU : 1.0;
		double mmPerUnit = mm / units;

		// 4. 스타일 & 선 굵기
		var colorMode = (this._cboColorStyle.SelectedIndex == 1) ? PlotColorMode.Monochrome : PlotColorMode.Color;
		double lineWidth = double.TryParse(this._txtLineWidth.Text, out var vLw) && vLw >= 0 ? vLw : 0.18;
		bool center = this._chkCenter.IsChecked == true;

		return new PlotOptions(
			area,
			paperWidth,
			paperHeight,
			MarginMm: 5,
			FitToPaper: fit,
			MmPerUnit: mmPerUnit,
			Center: center,
			Colors: colorMode,
			LineWidthMm: lineWidth);
	}

	private void UpdatePreview()
	{
		this._previewCts?.Cancel();
		var cts = new CancellationTokenSource();
		this._previewCts = cts;

		var o = this.BuildOptions();

		// 용지 테두리 비율 설정
		double maxW = 260;
		double maxH = 260;
		double scale = Math.Min(maxW / o.PaperWidthMm, maxH / o.PaperHeightMm);
		this._previewPaper.Width = Math.Max(40, o.PaperWidthMm * scale);
		this._previewPaper.Height = Math.Max(40, o.PaperHeightMm * scale);
		this._previewInfo.Text = $"{o.PaperWidthMm:0} x {o.PaperHeightMm:0} mm ({(this._rbLandscape.IsChecked == true ? "가로" : "세로")})";

		Task.Run(() =>
		{
			if (cts.IsCancellationRequested) return;
			try
			{
				using var skBmp = PlotRenderer.RenderBitmap(o, 40, this._modelScene, this._layoutScene);
				if (cts.IsCancellationRequested) return;

				using var ms = new MemoryStream();
				skBmp.Encode(ms, SKEncodedImageFormat.Png, 80);
				ms.Position = 0;
				var bmp = new Bitmap(ms);

				Dispatcher.UIThread.Post(() =>
				{
					if (!cts.IsCancellationRequested)
					{
						this._previewImage.Source = bmp;
					}
				});
			}
			catch { }
		}, cts.Token);
	}

	private void ShowLargePreview()
	{
		var o = this.BuildOptions();
		var dlg = new PlotPreviewDialog(o, this._modelScene, this._layoutScene);
		dlg.ShowDialog(this);
	}

	private void SaveCurrentSettingsToSession(PlotOptions o)
	{
		s_lastSettings = new PlotSessionSettings
		{
			PrinterName = (this._cboPrinter.SelectedItem as string) ?? "PDF로 저장 (Free 2D CAD)",
			PaperSizeName = (this._cboPaperSize.SelectedItem as string) ?? "A3",
			PlotArea = (this._cboPlotArea.SelectedItem as string) ?? "범위",
			Center = o.Center,
			FitToPaper = o.FitToPaper,
			ScaleMm = double.TryParse(this._txtScaleMm.Text, out var v1) ? v1 : 1.0,
			ScaleUnits = double.TryParse(this._txtScaleUnits.Text, out var v2) ? v2 : 1.0,
			Colors = o.Colors,
			IsLandscape = this._rbLandscape.IsChecked == true,
			LineWidthMm = o.LineWidthMm,
			WindowBounds = this._windowBounds,
		};
	}

	private async Task ExecutePlotAsync()
	{
		var o = this.BuildOptions();
		this.SaveCurrentSettingsToSession(o);

		string printer = (this._cboPrinter.SelectedItem as string) ?? "PDF로 저장 (Free 2D CAD)";

		if (printer.StartsWith("PDF로 저장", StringComparison.OrdinalIgnoreCase))
		{
			// PDF 파일 저장 대화상자
			string defName = $"{this._docName}-{this._spaceName}.pdf";
			var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = "PDF로 저장",
				SuggestedFileName = defName,
				DefaultExtension = "pdf",
				FileTypeChoices = new[]
				{
					new FilePickerFileType("PDF 문서 (*.pdf)") { Patterns = new[] { "*.pdf" } }
				}
			});

			if (file == null) return;
			string path = file.Path.LocalPath;

			this.Close();

			try
			{
				PlotRenderer.ExportPdf(path, o, this._modelScene, this._layoutScene);
				this._onPlotCompleted?.Invoke(path);
			}
			catch (Exception ex)
			{
				this._onPlotCompleted?.Invoke($"오류: PDF 출력 실패 ({ex.Message})");
			}
		}
		else
		{
			// Windows 프린터 출력
			this.Close();
			this._onPlotStarted?.Invoke();

			bool isLandscape = this._rbLandscape.IsChecked == true;
			string paperName = (this._cboPaperSize.SelectedItem as string) ?? "A3";

			_ = Task.Run(() =>
			{
				try
				{
					var pd = new PrintDocument();
					pd.PrinterSettings.PrinterName = printer;
					pd.DefaultPageSettings.Landscape = isLandscape;

					foreach (PaperSize ps in pd.PrinterSettings.PaperSizes)
					{
						if (ps.PaperName.Contains(paperName, StringComparison.OrdinalIgnoreCase))
						{
							pd.DefaultPageSettings.PaperSize = ps;
							break;
						}
					}

					pd.PrintPage += (_, ev) =>
					{
						using var skBmp = PlotRenderer.RenderBitmap(o, 300, this._modelScene, this._layoutScene);
						using var ms = new MemoryStream();
						skBmp.Encode(ms, SKEncodedImageFormat.Png, 100);
						ms.Position = 0;
						using var gdiImg = System.Drawing.Image.FromStream(ms);

						float hardX = ev.PageSettings.HardMarginX;
						float hardY = ev.PageSettings.HardMarginY;
						float w = ev.PageBounds.Width;
						float h = ev.PageBounds.Height;

						ev.Graphics?.DrawImage(gdiImg, -hardX, -hardY, w, h);
					};

					pd.Print();
					this._onPlotCompleted?.Invoke($"프린터 \"{printer}\" 출력 완료");
				}
				catch (Exception ex)
				{
					this._onPlotCompleted?.Invoke($"오류: 프린터 출력 실패 ({ex.Message})");
				}
			});
		}
	}

	/// <summary>
	/// 작은 숫자 입력 칸. Fluent 기본 최소 높이(32)·안쪽 여백 때문에 칸이 글자와 겹치고 글자가 위로 붙었다.
	/// </summary>
	private static void StyleSmallBox(TextBox box)
	{
		box.Width = 64;
		box.Height = 24;
		box.MinHeight = 0;
		box.MinWidth = 0;
		box.FontSize = 12;
		box.Padding = new Thickness(4, 0);
		box.VerticalAlignment = VerticalAlignment.Center;
		box.VerticalContentAlignment = VerticalAlignment.Center;
		box.TextAlignment = TextAlignment.Right;
	}
}
