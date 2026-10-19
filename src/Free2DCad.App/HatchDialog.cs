using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Hatching;
using AcadColor = ACadSharp.Color;
using AvColor = Avalonia.Media.Color;
using Point = Avalonia.Point;

namespace Free2DCad.App;

public enum HatchDialogAction
{
	None,
	PickPoints,
	SelectObjects,
	ViewSelection,
	Preview,
	Inherit,
	Ok,
}

public sealed class HatchDialogState
{
	public HatchDialogAction Action { get; set; } = HatchDialogAction.None;
	public HatchSettings Settings { get; set; } = HatchCommand.LastSettings.Clone();
	public List<List<double[]>> Regions { get; set; } = new();
	public List<Entity> Objects { get; set; } = new();
	public bool RetainBoundaries { get; set; }
	public bool RightPanelExpanded { get; set; } = true;
	public int ActiveTab { get; set; } // 0: 해치, 1: 그라데이션
}

/// <summary>
/// 고립영역 표시 스타일 미리보기 (기존 CAD 그림과 같은 구성): 굵은 사각형 안에 오각형, 그 안에 원.
/// 보통 = 사각형~오각형 사이 + 원 안, 외부 = 사각형~오각형 사이만, 무시 = 사각형 전체에 사선.
/// 사선은 해당 영역 모양으로 잘라서 그린다 (짝홀 채움 규칙).
/// </summary>
public sealed class IslandStyleCanvas : Control
{
	private readonly HatchStyleType _style;

	public IslandStyleCanvas(HatchStyleType style)
	{
		this._style = style;
		this.Width = 58;
		this.Height = 50;
		this.ClipToBounds = true;
	}

	public override void Render(DrawingContext dc)
	{
		const double w = 58, h = 50;
		var square = new Rect(3, 3, w - 6, h - 6);
		double cx = w / 2, cy = h / 2 + 1;

		// 오각형 (꼭짓점이 위), 원은 가운데
		double pr = 20.5;
		var penta = new Point[5];
		for (int i = 0; i < 5; i++)
		{
			double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
			penta[i] = new Point(cx + pr * Math.Cos(a), cy + 2 + pr * Math.Sin(a) * 0.95);
		}
		double cr = 8;
		var circleRect = new Rect(cx - cr, cy + 2 - cr, cr * 2, cr * 2);

		// 사선을 칠할 영역
		var region = new StreamGeometry();
		using (var ctx = region.Open())
		{
			ctx.SetFillRule(FillRule.EvenOdd);
			ctx.BeginFigure(square.TopLeft, true);
			ctx.LineTo(square.TopRight);
			ctx.LineTo(square.BottomRight);
			ctx.LineTo(square.BottomLeft);
			ctx.EndFigure(true);
			if (this._style != HatchStyleType.Ignore)
			{
				ctx.BeginFigure(penta[0], true);
				for (int i = 1; i < 5; i++) ctx.LineTo(penta[i]);
				ctx.EndFigure(true);
			}
			if (this._style == HatchStyleType.Normal)
			{
				// 원 (베지어 4개)
				const double k = 0.5523;
				double x = circleRect.Center.X, y = circleRect.Center.Y;
				ctx.BeginFigure(new Point(x + cr, y), true);
				ctx.CubicBezierTo(new Point(x + cr, y + cr * k), new Point(x + cr * k, y + cr), new Point(x, y + cr));
				ctx.CubicBezierTo(new Point(x - cr * k, y + cr), new Point(x - cr, y + cr * k), new Point(x - cr, y));
				ctx.CubicBezierTo(new Point(x - cr, y - cr * k), new Point(x - cr * k, y - cr), new Point(x, y - cr));
				ctx.CubicBezierTo(new Point(x + cr * k, y - cr), new Point(x + cr, y - cr * k), new Point(x + cr, y));
				ctx.EndFigure(true);
			}
		}

		dc.DrawRectangle(Brushes.White, null, square);
		var hatchPen = new Pen(new SolidColorBrush(AvColor.FromRgb(0x30, 0x30, 0x30)), 1.0);
		using (dc.PushGeometryClip(region))
		{
			for (double d = -h; d < w + h; d += 4.2)
			{
				dc.DrawLine(hatchPen, new Point(d, h), new Point(d + h, 0));
			}
		}

		var outline = new Pen(Brushes.Black, 1.2);
		dc.DrawGeometry(null, outline, new PolylineGeometry(penta, isFilled: false) { });
		dc.DrawLine(outline, penta[4], penta[0]);
		dc.DrawEllipse(null, outline, circleRect.Center, cr, cr);
		dc.DrawRectangle(null, new Pen(Brushes.Black, 2.2), square);
	}
}

/// <summary>
/// 그라데이션 견본 렌더링 캔버스 (48x48).
/// </summary>
public sealed class GradientSwatchCanvas : Control
{
	public GradientShape Shape { get; }
	public AvColor Color1 { get; set; }
	public AvColor Color2 { get; set; }

	public GradientSwatchCanvas(GradientShape shape, AvColor c1, AvColor c2)
	{
		this.Shape = shape;
		this.Color1 = c1;
		this.Color2 = c2;
		this.Width = 48;
		this.Height = 48;
	}

	public override void Render(DrawingContext dc)
	{
		IBrush brush = this.Shape switch
		{
			GradientShape.Linear => new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color1, 0), new GradientStop(this.Color2, 1) },
			},
			GradientShape.Cylinder => new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color1, 0), new GradientStop(this.Color2, 0.5), new GradientStop(this.Color1, 1) },
			},
			GradientShape.InvCylinder => new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color2, 0), new GradientStop(this.Color1, 0.5), new GradientStop(this.Color2, 1) },
			},
			GradientShape.Spherical => new RadialGradientBrush
			{
				Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
				RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color2, 0), new GradientStop(this.Color1, 1) },
			},
			GradientShape.Hemispherical => new RadialGradientBrush
			{
				Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				GradientOrigin = new RelativePoint(0.3, 0.3, RelativeUnit.Relative),
				RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
				RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color2, 0), new GradientStop(this.Color1, 1) },
			},
			GradientShape.Curved => new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color1, 0), new GradientStop(this.Color2, 1) },
			},
			GradientShape.InvSpherical => new RadialGradientBrush
			{
				Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
				RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color1, 0), new GradientStop(this.Color2, 1) },
			},
			GradientShape.InvHemispherical => new RadialGradientBrush
			{
				Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
				GradientOrigin = new RelativePoint(0.7, 0.7, RelativeUnit.Relative),
				RadiusX = new RelativeScalar(0.8, RelativeUnit.Relative),
				RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color1, 0), new GradientStop(this.Color2, 1) },
			},
			_ => new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
				EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
				GradientStops = { new GradientStop(this.Color2, 0), new GradientStop(this.Color1, 1) },
			},
		};

		dc.DrawRectangle(brush, null, new Rect(0, 0, this.Width, this.Height));
	}
}

/// <summary>
/// 해치 대화상자 미리보기 견본 캔버스 (80x40).
/// </summary>
public sealed class HatchPreviewSwatchControl : Control
{
	private string _patternName = "ANSI31";
	private double _angle;
	private double _scale = 1.0;
	private IReadOnlyList<(double X1, double Y1, double X2, double Y2)> _segments = Array.Empty<(double, double, double, double)>();

	public HatchPreviewSwatchControl()
	{
		this.Width = 80;
		this.Height = 40;
		this.UpdateSegments();
	}

	public void SetPattern(string patternName, double angleDeg, double scale)
	{
		this._patternName = patternName;
		this._angle = angleDeg;
		this._scale = Math.Max(scale, 0.05);
		this.UpdateSegments();
		this.InvalidateVisual();
	}

	private void UpdateSegments()
	{
		this._segments = HatchPatternLibrary.PreviewSegments(this._patternName, this.Width, this.Height, 0);
	}

	public override void Render(DrawingContext dc)
	{
		var rect = new Rect(0, 0, this.Width, this.Height);
		dc.DrawRectangle(Brushes.White, new Pen(Brushes.Black, 1), rect);

		if (string.Equals(this._patternName, "SOLID", StringComparison.OrdinalIgnoreCase))
		{
			dc.DrawRectangle(Brushes.LightGray, null, rect);
			return;
		}

		var pen = new Pen(Brushes.Black, 1.0);
		foreach (var seg in this._segments)
		{
			double y1 = this.Height - seg.Y1;
			double y2 = this.Height - seg.Y2;
			dc.DrawLine(pen, new Point(seg.X1, y1), new Point(seg.X2, y2));
		}
	}
}

/// <summary>
/// 기존 CAD 스타일 해치 및 그라디언트 대화상자.
/// </summary>
public sealed class HatchDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly HatchDialogState _state;

	private readonly TabControl _mainTabs = new();
	private readonly Border _rightPanel = new();
	private readonly Button _btnToggleRight = new();

	// 해치 탭 컨트롤
	private readonly ComboBox _cboKind = new();
	private readonly ComboBox _cboPattern = new();
	private readonly HatchPreviewSwatchControl _swatchControl = new();
	private readonly ComboBox _cboColor = new();
	private readonly ComboBox _cboAngle = new();
	private readonly ComboBox _cboScale = new();
	private readonly CheckBox _chkDouble = new();
	private readonly TextBox _txtSpacing = new();

	// 원점
	private readonly RadioButton _rbCurrentOrigin = new();
	private readonly RadioButton _rbSpecifiedOrigin = new();
	private readonly TextBox _txtOriginX = new();
	private readonly TextBox _txtOriginY = new();
	private readonly CheckBox _chkBoundsDefault = new();
	private readonly ComboBox _cboBoundsCorner = new();

	// 옵션
	private readonly CheckBox _chkAssociative = new();
	private readonly CheckBox _chkSeparate = new();
	private readonly ComboBox _cboDrawOrder = new();
	private readonly ComboBox _cboLayer = new();
	private readonly ComboBox _cboTransparency = new();
	private readonly Slider _sliderTransparency = new();
	private readonly TextBox _txtTransparency = new();

	// 오른쪽 패널 컨트롤
	private readonly CheckBox _chkDetectIslands = new();
	private readonly RadioButton _rbIslandNormal = new();
	private readonly RadioButton _rbIslandOuter = new();
	private readonly RadioButton _rbIslandIgnore = new();
	private readonly CheckBox _chkRetainBoundaries = new();

	// 그라데이션 탭 컨트롤
	private readonly RadioButton _rbGradOneColor = new();
	private readonly RadioButton _rbGradTwoColors = new();
	private readonly Border _gradSwatch1 = new();
	private readonly Border _gradSwatch2 = new();
	private readonly Border[] _gradShapeBorders = new Border[9];
	private GradientShape _selectedGradientShape = GradientShape.Linear;
	private AcadColor _gradColor1 = new(0, 0, 255);
	private AcadColor _gradColor2 = new(255, 255, 0);
	private readonly CheckBox _chkGradCentered = new();
	private readonly ComboBox _cboGradAngle = new();

	// 하단 컨트롤
	private readonly TextBlock _lblBoundaryCount = new();
	private readonly Button _btnPreview = new();
	private readonly Button _btnOk = new();

	public HatchDialog(EditorDocument doc, HatchDialogState state)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._state = state ?? throw new ArgumentNullException(nameof(state));

		this.Title = "해치 및 그라디언트";
		this.Width = state.RightPanelExpanded ? 880 : 620;
		this.Height = 690;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(10) };

		// 하단 버튼 바
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 중앙 메인 영역 (좌측 탭 + 가운데 경계/옵션 + 우측 세부 설정)
		var centerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("310, 190, Auto"),
		};

		// 1. 좌측 탭 (해치 / 그라데이션)
		this._mainTabs.Background = Brushes.Transparent;
		this._mainTabs.Items.Add(new TabItem { Header = "해치", Content = this.BuildHatchTab() });
		this._mainTabs.Items.Add(new TabItem { Header = "그라데이션", Content = this.BuildGradientTab() });
		this._mainTabs.SelectedIndex = Math.Clamp(this._state.ActiveTab, 0, 1);
		Grid.SetColumn(this._mainTabs, 0);
		centerGrid.Children.Add(this._mainTabs);

		// 2. 가운데 (경계 + 옵션 + 속성 상속)
		var middlePanel = this.BuildMiddlePanel();
		Grid.SetColumn(middlePanel, 1);
		centerGrid.Children.Add(middlePanel);

		// 3. 우측 확장 패널 (고립영역, 경계 보존, 상속 등)
		this._rightPanel.Child = this.BuildRightPanel();
		this._rightPanel.IsVisible = this._state.RightPanelExpanded;
		Grid.SetColumn(this._rightPanel, 2);
		centerGrid.Children.Add(this._rightPanel);

		root.Children.Add(centerGrid);
		this.Content = root;

		// 설정값 복원 및 동기화
		this.LoadSettingsToUi();
		this.UpdateBoundaryState();
	}

	private Control BuildHatchTab()
	{
		var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 8, 0) };

		// 1. 유형 및 패턴
		var typeGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("100, *"),
			RowDefinitions = new RowDefinitions("26, 26, Auto, 26, 26"),
			Margin = new Thickness(0, 2),
		};

		// 유형
		typeGrid.Children.Add(new TextBlock { Text = "유형:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		this.StyleCombo(this._cboKind);
		this._cboKind.ItemsSource = new[] { "미리 정의됨", "사용자 정의" };
		this._cboKind.SelectedIndex = this._state.Settings.Kind == HatchKind.UserDefined ? 1 : 0;
		this._cboKind.SelectionChanged += (_, _) => this.OnKindChanged();
		Grid.SetColumn(this._cboKind, 1);
		typeGrid.Children.Add(this._cboKind);

		// 패턴 + ... 버튼
		var lblPat = new TextBlock { Text = "패턴:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblPat, 1);
		typeGrid.Children.Add(lblPat);

		var patRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 28") };
		this.StyleCombo(this._cboPattern);
		this._cboPattern.ItemsSource = HatchPatternLibrary.All.Select(p => p.Name).ToList();
		this._cboPattern.SelectedItem = this._state.Settings.PatternName;
		this._cboPattern.SelectionChanged += (_, _) =>
		{
			if (this._cboPattern.SelectedItem is string name)
			{
				this.UpdateSwatch();
			}
		};
		patRow.Children.Add(this._cboPattern);

		var btnPatDlg = new Button
		{
			Content = "...",
			Width = 26,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			Padding = new Thickness(0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Margin = new Thickness(2, 0, 0, 0),
		};
		btnPatDlg.Click += async (_, _) => await this.OpenPatternPaletteAsync();
		Grid.SetColumn(btnPatDlg, 1);
		patRow.Children.Add(btnPatDlg);

		Grid.SetRow(patRow, 1);
		Grid.SetColumn(patRow, 1);
		typeGrid.Children.Add(patRow);

		// 색상 견본
		var lblSwatch = new TextBlock { Text = "색상 견본:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4) };
		Grid.SetRow(lblSwatch, 2);
		typeGrid.Children.Add(lblSwatch);

		var swatchBorder = new Border
		{
			BorderBrush = Brushes.Gray,
			BorderThickness = new Thickness(1),
			Width = 80,
			Height = 40,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(0, 4),
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = this._swatchControl,
		};
		swatchBorder.PointerPressed += async (_, _) => await this.OpenPatternPaletteAsync();
		Grid.SetRow(swatchBorder, 2);
		Grid.SetColumn(swatchBorder, 1);
		typeGrid.Children.Add(swatchBorder);

		// 색상
		var lblColor = new TextBlock { Text = "색상:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblColor, 3);
		typeGrid.Children.Add(lblColor);

		var colorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 70") };
		this.StyleCombo(this._cboColor);
		this.PopulateColorCombo();
		colorRow.Children.Add(this._cboColor);

		var cboBg = new ComboBox
		{
			ItemsSource = new[] { "없음" },
			SelectedIndex = 0,
			IsEnabled = false,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			Margin = new Thickness(4, 0, 0, 0),
		};
		Grid.SetColumn(cboBg, 1);
		colorRow.Children.Add(cboBg);

		Grid.SetRow(colorRow, 3);
		Grid.SetColumn(colorRow, 1);
		typeGrid.Children.Add(colorRow);

		// 사용자 정의 패턴 (비활성)
		var lblCustom = new TextBlock { Text = "사용자 정의 패턴:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
		Grid.SetRow(lblCustom, 4);
		typeGrid.Children.Add(lblCustom);

		var cboCustom = new ComboBox { IsEnabled = false, Height = 24, MinHeight = 0, FontSize = 11 };
		Grid.SetRow(cboCustom, 4);
		Grid.SetColumn(cboCustom, 1);
		typeGrid.Children.Add(cboCustom);

		panel.Children.Add(CreateGroupBox("유형 및 패턴", typeGrid));

		// 2. 각도 및 축척
		var scaleGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("50, *, Auto, *"),
			RowDefinitions = new RowDefinitions("26, 26, 26, 26"),
			Margin = new Thickness(0, 2),
		};

		scaleGrid.Children.Add(new TextBlock { Text = "각도:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		this.StyleCombo(this._cboAngle);
		this._cboAngle.IsEditable = true;
		this._cboAngle.ItemsSource = new[] { "0", "15", "30", "45", "60", "75", "90", "105", "120", "135", "150", "165", "180", "225", "270", "315" };
		this._cboAngle.SelectedItem = $"{this._state.Settings.AngleDegrees:0}";
		this._cboAngle.SelectionChanged += (_, _) => this.UpdateSwatch();
		Grid.SetColumn(this._cboAngle, 1);
		scaleGrid.Children.Add(this._cboAngle);

		var lblScale = new TextBlock { Text = "축척:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
		Grid.SetColumn(lblScale, 2);
		scaleGrid.Children.Add(lblScale);

		this.StyleCombo(this._cboScale);
		this._cboScale.IsEditable = true;
		this._cboScale.ItemsSource = new[] { "0.25", "0.5", "0.75", "1", "1.25", "1.5", "1.75", "2" };
		this._cboScale.SelectedItem = $"{this._state.Settings.Scale:0.##}";
		this._cboScale.SelectionChanged += (_, _) => this.UpdateSwatch();
		Grid.SetColumn(this._cboScale, 3);
		scaleGrid.Children.Add(this._cboScale);

		// 이중, 종이 공간 관련
		this._chkDouble.Content = "이중";
		this._chkDouble.FontSize = 12;
		this._chkDouble.IsEnabled = this._state.Settings.Kind == HatchKind.UserDefined;
		Grid.SetRow(this._chkDouble, 1);
		Grid.SetColumn(this._chkDouble, 0);
		Grid.SetColumnSpan(this._chkDouble, 2);
		scaleGrid.Children.Add(this._chkDouble);

		var chkPaper = new CheckBox { Content = "종이 공간 관련", FontSize = 11, IsEnabled = false };
		Grid.SetRow(chkPaper, 1);
		Grid.SetColumn(chkPaper, 2);
		Grid.SetColumnSpan(chkPaper, 2);
		scaleGrid.Children.Add(chkPaper);

		// 간격
		var lblSpacing = new TextBlock { Text = "간격:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblSpacing, 2);
		scaleGrid.Children.Add(lblSpacing);

		this.StyleTextBox(this._txtSpacing);
		this._txtSpacing.Text = $"{this._state.Settings.Spacing:0.##}";
		this._txtSpacing.IsEnabled = this._state.Settings.Kind == HatchKind.UserDefined;
		Grid.SetRow(this._txtSpacing, 2);
		Grid.SetColumn(this._txtSpacing, 1);
		scaleGrid.Children.Add(this._txtSpacing);

		// ISO 펜 너비 (비활성)
		var lblIso = new TextBlock { Text = "ISO 펜 너비:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(6, 0, 0, 0) };
		Grid.SetRow(lblIso, 2);
		Grid.SetColumn(lblIso, 2);
		scaleGrid.Children.Add(lblIso);

		var cboIso = new ComboBox { IsEnabled = false, Height = 24, MinHeight = 0, FontSize = 11 };
		Grid.SetRow(cboIso, 2);
		Grid.SetColumn(cboIso, 3);
		scaleGrid.Children.Add(cboIso);

		panel.Children.Add(CreateGroupBox("각도 및 축척", scaleGrid));

		// 3. 해치 원점
		var originStack = new StackPanel { Spacing = 4 };
		this._rbCurrentOrigin.Content = "현재 원점 사용";
		this._rbCurrentOrigin.GroupName = "HatchOrigin";
		this._rbCurrentOrigin.FontSize = 12;
		this._rbCurrentOrigin.IsChecked = this._state.Settings.Origin == null;
		originStack.Children.Add(this._rbCurrentOrigin);

		this._rbSpecifiedOrigin.Content = "지정된 원점 사용";
		this._rbSpecifiedOrigin.GroupName = "HatchOrigin";
		this._rbSpecifiedOrigin.FontSize = 12;
		this._rbSpecifiedOrigin.IsChecked = this._state.Settings.Origin != null;
		originStack.Children.Add(this._rbSpecifiedOrigin);

		var originSubGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, 70, Auto, 70"),
			Margin = new Thickness(16, 2, 0, 2),
		};
		originSubGrid.Children.Add(new TextBlock { Text = "X:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
		this.StyleTextBox(this._txtOriginX);
		this._txtOriginX.Text = $"{this._state.Settings.Origin?.X ?? 0:0.##}";
		Grid.SetColumn(this._txtOriginX, 1);
		originSubGrid.Children.Add(this._txtOriginX);

		var lblY = new TextBlock { Text = "Y:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
		Grid.SetColumn(lblY, 2);
		originSubGrid.Children.Add(lblY);

		this.StyleTextBox(this._txtOriginY);
		this._txtOriginY.Text = $"{this._state.Settings.Origin?.Y ?? 0:0.##}";
		Grid.SetColumn(this._txtOriginY, 3);
		originSubGrid.Children.Add(this._txtOriginY);

		originStack.Children.Add(originSubGrid);

		this._chkBoundsDefault.Content = "경계 범위를 기본으로";
		this._chkBoundsDefault.FontSize = 11;
		this._chkBoundsDefault.Margin = new Thickness(16, 0, 0, 0);
		originStack.Children.Add(this._chkBoundsDefault);

		this.StyleCombo(this._cboBoundsCorner);
		this._cboBoundsCorner.ItemsSource = new[] { "왼쪽 아래", "오른쪽 아래", "왼쪽 위", "오른쪽 위", "중앙" };
		this._cboBoundsCorner.SelectedIndex = 0;
		this._cboBoundsCorner.Margin = new Thickness(16, 2, 0, 0);
		originStack.Children.Add(this._cboBoundsCorner);

		panel.Children.Add(CreateGroupBox("해치 원점", originStack));

		return panel;
	}

	private Control BuildGradientTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 8, 0) };

		// 1. 색상 선택
		var colorStack = new StackPanel { Spacing = 6 };
		var radioRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
		this._rbGradOneColor.Content = "하나의 색상";
		this._rbGradOneColor.GroupName = "GradColor";
		this._rbGradOneColor.FontSize = 12;
		this._rbGradOneColor.IsChecked = this._state.Settings.GradientOneColor;

		this._rbGradTwoColors.Content = "두 색상";
		this._rbGradTwoColors.GroupName = "GradColor";
		this._rbGradTwoColors.FontSize = 12;
		this._rbGradTwoColors.IsChecked = !this._state.Settings.GradientOneColor;

		radioRow.Children.Add(this._rbGradOneColor);
		radioRow.Children.Add(this._rbGradTwoColors);
		colorStack.Children.Add(radioRow);

		// 색상1 / 색상2 견본
		var swatchGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, *"),
			Margin = new Thickness(0, 4),
		};

		// 색상1
		var c1Stack = new StackPanel { Spacing = 2 };
		var c1Row = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 26") };
		this._gradSwatch1.Height = 24;
		this._gradSwatch1.BorderBrush = Brushes.Gray;
		this._gradSwatch1.BorderThickness = new Thickness(1);
		this._gradSwatch1.Background = new SolidColorBrush(AvColor.FromRgb(this._state.Settings.GradientColor1.R, this._state.Settings.GradientColor1.G, this._state.Settings.GradientColor1.B));
		c1Row.Children.Add(this._gradSwatch1);

		var btnPickC1 = new Button { Content = "...", Width = 24, Height = 24, MinHeight = 0, FontSize = 11, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0) };
		btnPickC1.Click += async (_, _) =>
		{
			var dlg = new ColorPickerDialog(this._gradColor1);
			var res = await dlg.ShowDialog<AcadColor?>(this);
			if (res != null)
			{
				this._gradColor1 = res.Value;
				this._gradSwatch1.Background = new SolidColorBrush(AvColor.FromRgb(res.Value.R, res.Value.G, res.Value.B));
				this.RefreshGradientSwatches();
			}
		};
		Grid.SetColumn(btnPickC1, 1);
		c1Row.Children.Add(btnPickC1);

		c1Stack.Children.Add(c1Row);
		c1Stack.Children.Add(new TextBlock { Text = "색상1", FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
		Grid.SetColumn(c1Stack, 0);
		swatchGrid.Children.Add(c1Stack);

		// 색상2
		var c2Stack = new StackPanel { Spacing = 2, Margin = new Thickness(8, 0, 0, 0) };
		var c2Row = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 26") };
		this._gradSwatch2.Height = 24;
		this._gradSwatch2.BorderBrush = Brushes.Gray;
		this._gradSwatch2.BorderThickness = new Thickness(1);
		this._gradSwatch2.Background = new SolidColorBrush(AvColor.FromRgb(this._state.Settings.GradientColor2.R, this._state.Settings.GradientColor2.G, this._state.Settings.GradientColor2.B));
		c2Row.Children.Add(this._gradSwatch2);

		var btnPickC2 = new Button { Content = "...", Width = 24, Height = 24, MinHeight = 0, FontSize = 11, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0) };
		btnPickC2.Click += async (_, _) =>
		{
			var dlg = new ColorPickerDialog(this._gradColor2);
			var res = await dlg.ShowDialog<AcadColor?>(this);
			if (res != null)
			{
				this._gradColor2 = res.Value;
				this._gradSwatch2.Background = new SolidColorBrush(AvColor.FromRgb(res.Value.R, res.Value.G, res.Value.B));
				this.RefreshGradientSwatches();
			}
		};
		Grid.SetColumn(btnPickC2, 1);
		c2Row.Children.Add(btnPickC2);

		c2Stack.Children.Add(c2Row);
		c2Stack.Children.Add(new TextBlock { Text = "색상2", FontSize = 11, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center });
		Grid.SetColumn(c2Stack, 1);
		swatchGrid.Children.Add(c2Stack);

		colorStack.Children.Add(swatchGrid);
		panel.Children.Add(CreateGroupBox("색상", colorStack));

		// 2. 9개 그라데이션 견본 (3x3)
		var shapeGrid = new UniformGrid
		{
			Columns = 3,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 4),
		};

		var avC1 = AvColor.FromRgb(this._state.Settings.GradientColor1.R, this._state.Settings.GradientColor1.G, this._state.Settings.GradientColor1.B);
		var avC2 = AvColor.FromRgb(this._state.Settings.GradientColor2.R, this._state.Settings.GradientColor2.G, this._state.Settings.GradientColor2.B);

		GradientShape[] shapes =
		{
			GradientShape.Linear, GradientShape.Cylinder, GradientShape.InvCylinder,
			GradientShape.Spherical, GradientShape.Hemispherical, GradientShape.Curved,
			GradientShape.InvSpherical, GradientShape.InvHemispherical, GradientShape.InvCurved,
		};

		for (int i = 0; i < 9; i++)
		{
			var s = shapes[i];
			var border = new Border
			{
				Width = 52,
				Height = 52,
				Margin = new Thickness(4),
				BorderBrush = (s == this._state.Settings.Gradient) ? Brushes.Black : Brushes.Gray,
				BorderThickness = new Thickness(s == this._state.Settings.Gradient ? 2 : 1),
				Cursor = new Cursor(StandardCursorType.Hand),
				Child = new GradientSwatchCanvas(s, avC1, avC2),
			};
			border.PointerPressed += (_, _) => this.SelectGradientShape(s);
			this._gradShapeBorders[i] = border;
			shapeGrid.Children.Add(border);
		}
		panel.Children.Add(shapeGrid);

		// 3. 방향
		var dirStack = new StackPanel { Spacing = 6 };
		var dirRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, *") };
		this._chkGradCentered.Content = "중앙";
		this._chkGradCentered.FontSize = 12;
		this._chkGradCentered.IsChecked = this._state.Settings.GradientCentered;
		dirRow.Children.Add(this._chkGradCentered);

		var lblAngle = new TextBlock { Text = "각도:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
		Grid.SetColumn(lblAngle, 2);
		dirRow.Children.Add(lblAngle);

		this.StyleCombo(this._cboGradAngle);
		this._cboGradAngle.ItemsSource = new[] { "0", "45", "90", "135", "180", "225", "270", "315" };
		this._cboGradAngle.SelectedItem = $"{this._state.Settings.GradientAngleDegrees:0}";
		Grid.SetColumn(this._cboGradAngle, 3);
		dirRow.Children.Add(this._cboGradAngle);

		dirStack.Children.Add(dirRow);
		panel.Children.Add(CreateGroupBox("방향", dirStack));

		return panel;
	}

	private Control BuildMiddlePanel()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 8, 0) };

		// 1. 경계
		var bndStack = new StackPanel { Spacing = 4 };

		var btnAddPoints = this.CreateBoundaryButton(IconHelper.New, "추가: 내부 점 선택", () =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.PickPoints;
			this.Close();
		});
		bndStack.Children.Add(btnAddPoints);

		var btnAddObjects = this.CreateBoundaryButton(IconHelper.SelectAll, "추가: 객체 선택", () =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.SelectObjects;
			this.Close();
		});
		bndStack.Children.Add(btnAddObjects);

		var btnClearBoundaries = this.CreateBoundaryButton(IconHelper.Erase, "경계 제거", () =>
		{
			this._state.Regions.Clear();
			this._state.Objects.Clear();
			this.UpdateBoundaryState();
		});
		bndStack.Children.Add(btnClearBoundaries);

		var btnRecreateBoundaries = this.CreateBoundaryButton(IconHelper.Redo, "경계 재생성", null);
		btnRecreateBoundaries.IsEnabled = false;
		bndStack.Children.Add(btnRecreateBoundaries);

		var btnViewSelection = this.CreateBoundaryButton(IconHelper.ZoomExtents, "선택 보기", () =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.ViewSelection;
			this.Close();
		});
		bndStack.Children.Add(btnViewSelection);

		this._lblBoundaryCount.FontSize = 11;
		this._lblBoundaryCount.FontWeight = FontWeight.SemiBold;
		this._lblBoundaryCount.Foreground = new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0));
		this._lblBoundaryCount.Margin = new Thickness(4, 4, 0, 0);
		bndStack.Children.Add(this._lblBoundaryCount);

		panel.Children.Add(CreateGroupBox("경계", bndStack));

		// 2. 옵션
		var optStack = new StackPanel { Spacing = 4 };

		var chkAnnotative = new CheckBox { Content = "주석", FontSize = 12, IsEnabled = false };
		optStack.Children.Add(chkAnnotative);

		this._chkAssociative.Content = "연관";
		this._chkAssociative.FontSize = 12;
		this._chkAssociative.IsChecked = this._state.Settings.Associative;
		optStack.Children.Add(this._chkAssociative);

		this._chkSeparate.Content = "별도 해치 생성";
		this._chkSeparate.FontSize = 12;
		this._chkSeparate.IsChecked = this._state.Settings.SeparateHatches;
		optStack.Children.Add(this._chkSeparate);

		// 그리기 순서
		optStack.Children.Add(new TextBlock { Text = "그리기 순서:", FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
		this.StyleCombo(this._cboDrawOrder);
		this._cboDrawOrder.ItemsSource = new[] { "경계 뒤로 보내기", "경계 앞으로 가져오기", "맨 뒤로 보내기", "맨 앞으로 가져오기", "지정 안 함" };
		this._cboDrawOrder.SelectedIndex = 0;
		optStack.Children.Add(this._cboDrawOrder);

		// 도면층
		optStack.Children.Add(new TextBlock { Text = "도면층:", FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
		this.StyleCombo(this._cboLayer);
		var layerList = new List<string> { "현재 사용" };
		foreach (var lyr in this._doc.Document.Layers)
		{
			layerList.Add(lyr.Name);
		}
		this._cboLayer.ItemsSource = layerList;
		this._cboLayer.SelectedItem = this._state.Settings.Layer ?? "현재 사용";
		optStack.Children.Add(this._cboLayer);

		// 투명도
		optStack.Children.Add(new TextBlock { Text = "투명도:", FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
		this.StyleCombo(this._cboTransparency);
		this._cboTransparency.ItemsSource = new[] { "현재 사용", "ByLayer", "ByBlock", "투명도 값 지정" };
		this._cboTransparency.SelectedIndex = this._state.Settings.Transparency.HasValue ? 3 : 0;
		optStack.Children.Add(this._cboTransparency);

		var transRow = new Grid { ColumnDefinitions = new ColumnDefinitions("44, *"), Margin = new Thickness(0, 2) };
		this.StyleTextBox(this._txtTransparency);
		this._txtTransparency.Text = $"{this._state.Settings.Transparency ?? 0}";
		transRow.Children.Add(this._txtTransparency);

		this._sliderTransparency.Minimum = 0;
		this._sliderTransparency.Maximum = 90;
		this._sliderTransparency.Value = this._state.Settings.Transparency ?? 0;
		this._sliderTransparency.VerticalAlignment = VerticalAlignment.Center;
		this._sliderTransparency.Margin = new Thickness(4, 0, 0, 0);
		this._sliderTransparency.ValueChanged += (_, e) =>
		{
			this._txtTransparency.Text = $"{(int)e.NewValue}";
		};
		this._txtTransparency.TextChanged += (_, _) =>
		{
			if (short.TryParse(this._txtTransparency.Text, out short v))
			{
				this._sliderTransparency.Value = Math.Clamp((int)v, 0, 90);
			}
		};
		Grid.SetColumn(this._sliderTransparency, 1);
		transRow.Children.Add(this._sliderTransparency);
		optStack.Children.Add(transRow);

		panel.Children.Add(CreateGroupBox("옵션", optStack));

		// 3. 속성 상속 버튼
		var btnInherit = new Button
		{
			Height = 28,
			MinHeight = 0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Background = Brushes.Transparent,
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6,
				HorizontalAlignment = HorizontalAlignment.Center,
				Children =
				{
					IconHelper.Create(IconHelper.MatchProp, 16, Brushes.RoyalBlue),
					new TextBlock { Text = "속성 상속", FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
				},
			},
		};
		btnInherit.Click += (_, _) =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.Inherit;
			this.Close();
		};
		panel.Children.Add(btnInherit);

		return panel;
	}

	private Control BuildRightPanel()
	{
		var panel = new StackPanel { Spacing = 6, Width = 260, Margin = new Thickness(4, 6, 0, 0) };

		// 1. 고립영역
		var islandStack = new StackPanel { Spacing = 4 };
		this._chkDetectIslands.Content = "고립영역 감지";
		this._chkDetectIslands.FontSize = 12;
		this._chkDetectIslands.IsChecked = this._state.Settings.DetectIslands;
		islandStack.Children.Add(this._chkDetectIslands);

		islandStack.Children.Add(new TextBlock { Text = "고립영역 표시 STYLE:", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 2) });

		var stylePreviewRow = new UniformGrid { Columns = 3 };
		stylePreviewRow.Children.Add(new IslandStyleCanvas(HatchStyleType.Normal));
		stylePreviewRow.Children.Add(new IslandStyleCanvas(HatchStyleType.Outer));
		stylePreviewRow.Children.Add(new IslandStyleCanvas(HatchStyleType.Ignore));
		islandStack.Children.Add(stylePreviewRow);

		var styleRadioRow = new UniformGrid { Columns = 3 };
		this._rbIslandNormal.Content = "보통";
		this._rbIslandNormal.GroupName = "IslandStyle";
		this._rbIslandNormal.FontSize = 11;
		this._rbIslandNormal.IsChecked = this._state.Settings.IslandStyle == HatchStyleType.Normal;
		styleRadioRow.Children.Add(this._rbIslandNormal);

		this._rbIslandOuter.Content = "외부";
		this._rbIslandOuter.GroupName = "IslandStyle";
		this._rbIslandOuter.FontSize = 11;
		this._rbIslandOuter.IsChecked = this._state.Settings.IslandStyle == HatchStyleType.Outer;
		styleRadioRow.Children.Add(this._rbIslandOuter);

		this._rbIslandIgnore.Content = "무시";
		this._rbIslandIgnore.GroupName = "IslandStyle";
		this._rbIslandIgnore.FontSize = 11;
		this._rbIslandIgnore.IsChecked = this._state.Settings.IslandStyle == HatchStyleType.Ignore;
		styleRadioRow.Children.Add(this._rbIslandIgnore);
		islandStack.Children.Add(styleRadioRow);

		panel.Children.Add(CreateGroupBox("고립영역", islandStack));

		// 2. 경계 보존
		var retainStack = new StackPanel { Spacing = 4 };
		this._chkRetainBoundaries.Content = "경계 유지";
		this._chkRetainBoundaries.FontSize = 12;
		this._chkRetainBoundaries.IsChecked = this._state.RetainBoundaries;
		retainStack.Children.Add(this._chkRetainBoundaries);

		var typeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto, *") };
		typeRow.Children.Add(new TextBlock { Text = "객체 유형:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
		var cboObjType = new ComboBox { ItemsSource = new[] { "폴리라인" }, SelectedIndex = 0, IsEnabled = false, Height = 22, MinHeight = 0, FontSize = 11 };
		Grid.SetColumn(cboObjType, 1);
		typeRow.Children.Add(cboObjType);
		retainStack.Children.Add(typeRow);
		panel.Children.Add(CreateGroupBox("경계 보존", retainStack));

		// 3. 경계 세트
		var setRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 40") };
		var cboSet = new ComboBox { ItemsSource = new[] { "현재 뷰포트" }, SelectedIndex = 0, IsEnabled = false, Height = 22, MinHeight = 0, FontSize = 11 };
		setRow.Children.Add(cboSet);
		var btnNewSet = new Button { Content = "새로운", IsEnabled = false, Height = 22, MinHeight = 0, FontSize = 11, Margin = new Thickness(4, 0, 0, 0) };
		Grid.SetColumn(btnNewSet, 1);
		setRow.Children.Add(btnNewSet);
		panel.Children.Add(CreateGroupBox("경계 세트", setRow));

		// 4. 갭 허용
		var gapRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
		gapRow.Children.Add(new TextBlock { Text = "허용 오차:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
		var txtGap = new TextBox { Text = "0", IsEnabled = false, Width = 40, Height = 22, MinHeight = 0, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center };
		gapRow.Children.Add(txtGap);
		gapRow.Children.Add(new TextBlock { Text = "단위", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
		panel.Children.Add(CreateGroupBox("갭 허용", gapRow));

		// 5. 상속 옵션
		var inheritOptStack = new StackPanel { Spacing = 4 };
		var rbInheritCurr = new RadioButton { Content = "현재 원점 사용", GroupName = "InheritOpt", FontSize = 11, IsChecked = true };
		var rbInheritSource = new RadioButton { Content = "소스 해치 원점 사용", GroupName = "InheritOpt", FontSize = 11 };
		inheritOptStack.Children.Add(rbInheritCurr);
		inheritOptStack.Children.Add(rbInheritSource);
		panel.Children.Add(CreateGroupBox("상속 옵션", inheritOptStack));

		return panel;
	}

	private Control BuildBottomBar()
	{
		var bar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, Auto, *, Auto, Auto, Auto"),
			Height = 36,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 10, 0, 0),
		};

		// 도움말 (?)
		var btnHelp = new Button
		{
			Content = "?",
			Width = 26,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			FontWeight = FontWeight.Bold,
			Padding = new Thickness(0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnHelp.Click += (_, _) =>
		{
			// 간단 도움말 팝업
			var helpTip = new Window
			{
				Title = "해치 도움말",
				Width = 320,
				Height = 160,
				WindowStartupLocation = WindowStartupLocation.CenterOwner,
				RequestedThemeVariant = ThemeVariant.Light,
				Background = Brushes.White,
				Content = new TextBlock
				{
					Text = "해치 및 그라데이션:\n- 추가: 내부 점 선택으로 닫힌 영역을 클릭하거나 객체를 직접 선택합니다.\n- 각도, 축척, 패턴을 지정한 후 확인을 누르면 해치가 작성됩니다.",
					Margin = new Thickness(14),
					FontSize = 12,
					TextWrapping = TextWrapping.Wrap,
				},
			};
			helpTip.ShowDialog(this);
		};
		Grid.SetColumn(btnHelp, 0);
		bar.Children.Add(btnHelp);

		// 미리보기(P)
		this._btnPreview.Content = "미리보기";
		this._btnPreview.Width = 80;
		this._btnPreview.Height = 26;
		this._btnPreview.MinHeight = 0;
		this._btnPreview.FontSize = 12;
		this._btnPreview.Margin = new Thickness(8, 0, 0, 0);
		this._btnPreview.Click += (_, _) =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.Preview;
			this.Close();
		};
		Grid.SetColumn(this._btnPreview, 1);
		bar.Children.Add(this._btnPreview);

		// 확인
		this._btnOk.Content = "확인";
		this._btnOk.Width = 68;
		this._btnOk.Height = 26;
		this._btnOk.MinHeight = 0;
		this._btnOk.FontSize = 12;
		this._btnOk.IsDefault = true;
		this._btnOk.Margin = new Thickness(0, 0, 6, 0);
		this._btnOk.Click += (_, _) =>
		{
			this.SaveUiToSettings();
			this._state.Action = HatchDialogAction.Ok;
			this.Close();
		};
		Grid.SetColumn(this._btnOk, 3);
		bar.Children.Add(this._btnOk);

		// 취소
		var btnCancel = new Button
		{
			Content = "취소",
			Width = 68,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsCancel = true,
			Margin = new Thickness(0, 0, 6, 0),
		};
		btnCancel.Click += (_, _) =>
		{
			this._state.Action = HatchDialogAction.None;
			this.Close();
		};
		Grid.SetColumn(btnCancel, 4);
		bar.Children.Add(btnCancel);

		// 우측 확장/축소 버튼 (< / >)
		this._btnToggleRight.Content = this._state.RightPanelExpanded ? "<" : ">";
		this._btnToggleRight.Width = 26;
		this._btnToggleRight.Height = 26;
		this._btnToggleRight.MinHeight = 0;
		this._btnToggleRight.FontSize = 11;
		this._btnToggleRight.Padding = new Thickness(0);
		this._btnToggleRight.Click += (_, _) =>
		{
			this._state.RightPanelExpanded = !this._state.RightPanelExpanded;
			this._rightPanel.IsVisible = this._state.RightPanelExpanded;
			this._btnToggleRight.Content = this._state.RightPanelExpanded ? "<" : ">";
			this.Width = this._state.RightPanelExpanded ? 880 : 620;
		};
		Grid.SetColumn(this._btnToggleRight, 5);
		bar.Children.Add(this._btnToggleRight);

		return bar;
	}

	private Button CreateBoundaryButton(string iconKey, string text, Action? onClick)
	{
		var btn = new Button
		{
			Height = 26,
			MinHeight = 0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Background = Brushes.Transparent,
			Padding = new Thickness(4, 2),
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6,
				Children =
				{
					IconHelper.Create(iconKey, 14, Brushes.RoyalBlue),
					new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
				},
			},
		};
		if (onClick != null)
		{
			btn.Click += (_, _) => onClick();
		}
		return btn;
	}

	private void PopulateColorCombo()
	{
		var colors = new List<string>
		{
			"BYLAYER",
			"BYBLOCK",
			"빨간색",
			"노란색",
			"초록색",
			"하늘색",
			"파란색",
			"자홍색",
			"흰색",
			"색상 선택...",
		};
		this._cboColor.ItemsSource = colors;
		if (this._state.Settings.Color == null || this._state.Settings.Color.Value.IsByLayer)
		{
			this._cboColor.SelectedIndex = 0;
		}
		else if (this._state.Settings.Color.Value.IsByBlock)
		{
			this._cboColor.SelectedIndex = 1;
		}
		else
		{
			short idx = this._state.Settings.Color.Value.Index;
			if (idx >= 1 && idx <= 7)
			{
				this._cboColor.SelectedIndex = idx + 1;
			}
			else
			{
				this._cboColor.SelectedIndex = 0;
			}
		}

		this._cboColor.SelectionChanged += async (_, _) =>
		{
			if (this._cboColor.SelectedItem is string str && str == "색상 선택...")
			{
				var dlg = new ColorPickerDialog(this._state.Settings.Color);
				var res = await dlg.ShowDialog<AcadColor?>(this);
				if (res != null)
				{
					this._state.Settings.Color = res.Value;
				}
				else
				{
					this._cboColor.SelectedIndex = 0;
				}
			}
		};
	}

	private void OnKindChanged()
	{
		bool isCustom = this._cboKind.SelectedIndex == 1;
		this._cboPattern.IsEnabled = !isCustom;
		this._chkDouble.IsEnabled = isCustom;
		this._txtSpacing.IsEnabled = isCustom;
		this.UpdateSwatch();
	}

	private void UpdateSwatch()
	{
		string patName = (this._cboKind.SelectedIndex == 1) ? "_USER" : (this._cboPattern.SelectedItem as string ?? "ANSI31");
		double angle = double.TryParse(this._cboAngle.SelectedItem?.ToString(), out double a) ? a : 0;
		double scale = double.TryParse(this._cboScale.SelectedItem?.ToString(), out double s) ? s : 1.0;
		this._swatchControl.SetPattern(patName, angle, scale);
	}

	private async Task OpenPatternPaletteAsync()
	{
		var dlg = new HatchPatternPaletteDialog(this._cboPattern.SelectedItem as string);
		var res = await dlg.ShowDialog<string?>(this);
		if (!string.IsNullOrEmpty(res))
		{
			this._cboPattern.SelectedItem = res;
			this.UpdateSwatch();
		}
	}

	private void SelectGradientShape(GradientShape shape)
	{
		this._selectedGradientShape = shape;
		GradientShape[] shapes =
		{
			GradientShape.Linear, GradientShape.Cylinder, GradientShape.InvCylinder,
			GradientShape.Spherical, GradientShape.Hemispherical, GradientShape.Curved,
			GradientShape.InvSpherical, GradientShape.InvHemispherical, GradientShape.InvCurved,
		};
		for (int i = 0; i < 9; i++)
		{
			bool isSelected = shapes[i] == shape;
			this._gradShapeBorders[i].BorderBrush = isSelected ? Brushes.Black : Brushes.Gray;
			this._gradShapeBorders[i].BorderThickness = new Thickness(isSelected ? 2 : 1);
		}
	}

	private void RefreshGradientSwatches()
	{
		var avC1 = AvColor.FromRgb(this._gradColor1.R, this._gradColor1.G, this._gradColor1.B);
		var avC2 = AvColor.FromRgb(this._gradColor2.R, this._gradColor2.G, this._gradColor2.B);
		foreach (var b in this._gradShapeBorders)
		{
			if (b?.Child is GradientSwatchCanvas canvas)
			{
				canvas.Color1 = avC1;
				canvas.Color2 = avC2;
				canvas.InvalidateVisual();
			}
		}
	}

	private void UpdateBoundaryState()
	{
		int count = this._state.Regions.Count + this._state.Objects.Count;
		this._lblBoundaryCount.Text = $"경계: {count}개";
		this._btnPreview.IsEnabled = count > 0;
		this._btnOk.IsEnabled = count > 0;
	}

	private void LoadSettingsToUi()
	{
		var s = this._state.Settings;
		this._cboKind.SelectedIndex = s.Kind == HatchKind.UserDefined ? 1 : 0;
		this._cboPattern.SelectedItem = s.PatternName;
		this._cboAngle.SelectedItem = $"{s.AngleDegrees:0}";
		this._cboScale.SelectedItem = $"{s.Scale:0.##}";
		this._chkDouble.IsChecked = s.IsDouble;
		this._txtSpacing.Text = $"{s.Spacing:0.##}";

		this._rbCurrentOrigin.IsChecked = s.Origin == null;
		this._rbSpecifiedOrigin.IsChecked = s.Origin != null;
		this._txtOriginX.Text = $"{s.Origin?.X ?? 0:0.##}";
		this._txtOriginY.Text = $"{s.Origin?.Y ?? 0:0.##}";

		this._chkAssociative.IsChecked = s.Associative;
		this._chkSeparate.IsChecked = s.SeparateHatches;
		this._cboLayer.SelectedItem = s.Layer ?? "현재 사용";
		if (s.Transparency.HasValue)
		{
			this._cboTransparency.SelectedIndex = 3;
			this._txtTransparency.Text = s.Transparency.Value.ToString();
			this._sliderTransparency.Value = s.Transparency.Value;
		}

		this._chkDetectIslands.IsChecked = s.DetectIslands;
		this._rbIslandNormal.IsChecked = s.IslandStyle == HatchStyleType.Normal;
		this._rbIslandOuter.IsChecked = s.IslandStyle == HatchStyleType.Outer;
		this._rbIslandIgnore.IsChecked = s.IslandStyle == HatchStyleType.Ignore;
		this._chkRetainBoundaries.IsChecked = this._state.RetainBoundaries;

		this._gradColor1 = s.GradientColor1;
		this._gradColor2 = s.GradientColor2;
		this._selectedGradientShape = s.Gradient;
		this._rbGradOneColor.IsChecked = s.GradientOneColor;
		this._rbGradTwoColors.IsChecked = !s.GradientOneColor;
		this._chkGradCentered.IsChecked = s.GradientCentered;
		this._cboGradAngle.SelectedItem = $"{s.GradientAngleDegrees:0}";

		this.UpdateSwatch();
	}

	private void SaveUiToSettings()
	{
		var s = this._state.Settings;
		this._state.ActiveTab = this._mainTabs.SelectedIndex;
		bool isGradient = this._mainTabs.SelectedIndex == 1;
		s.IsGradient = isGradient;

		if (isGradient)
		{
			s.Gradient = this._selectedGradientShape;
			s.GradientOneColor = this._rbGradOneColor.IsChecked == true;
			s.GradientColor1 = this._gradColor1;
			s.GradientColor2 = this._gradColor2;
			s.GradientCentered = this._chkGradCentered.IsChecked == true;
			if (double.TryParse(this._cboGradAngle.SelectedItem?.ToString(), out double ga))
			{
				s.GradientAngleDegrees = ga;
			}
		}
		else
		{
			s.Kind = this._cboKind.SelectedIndex == 1 ? HatchKind.UserDefined : HatchKind.Predefined;
			s.PatternName = this._cboPattern.SelectedItem as string ?? "ANSI31";
			if (double.TryParse(this._cboAngle.SelectedItem?.ToString(), out double a))
			{
				s.AngleDegrees = a;
			}
			if (double.TryParse(this._cboScale.SelectedItem?.ToString(), out double sc) && sc > 0)
			{
				s.Scale = sc;
			}
			s.IsDouble = this._chkDouble.IsChecked == true;
			if (double.TryParse(this._txtSpacing.Text, out double sp) && sp > 0)
			{
				s.Spacing = sp;
			}

			// 색상 파싱
			int colIdx = this._cboColor.SelectedIndex;
			if (colIdx == 0)
			{
				s.Color = null; // BYLAYER
			}
			else if (colIdx == 1)
			{
				s.Color = AcadColor.ByBlock;
			}
			else if (colIdx >= 2 && colIdx <= 8)
			{
				s.Color = new AcadColor((short)(colIdx - 1));
			}
		}

		// 원점
		if (this._rbSpecifiedOrigin.IsChecked == true)
		{
			if (this._chkBoundsDefault.IsChecked == true)
			{
				// 경계 범위 기준 모서리 계산
				var bounds = this.CalculateBoundariesBounds();
				if (bounds.HasValue)
				{
					var b = bounds.Value;
					s.Origin = this._cboBoundsCorner.SelectedIndex switch
					{
						1 => new XY(b.Max.X, b.Min.Y), // 오른쪽 아래
						2 => new XY(b.Min.X, b.Max.Y), // 왼쪽 위
						3 => new XY(b.Max.X, b.Max.Y), // 오른쪽 위
						4 => new XY((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2), // 중앙
						_ => new XY(b.Min.X, b.Min.Y), // 왼쪽 아래
					};
				}
				else
				{
					s.Origin = XY.Zero;
				}
			}
			else
			{
				double x = double.TryParse(this._txtOriginX.Text, out var ox) ? ox : 0;
				double y = double.TryParse(this._txtOriginY.Text, out var oy) ? oy : 0;
				s.Origin = new XY(x, y);
			}
		}
		else
		{
			s.Origin = null;
		}

		// 옵션
		s.Associative = this._chkAssociative.IsChecked == true;
		s.SeparateHatches = this._chkSeparate.IsChecked == true;

		string? lyr = this._cboLayer.SelectedItem as string;
		s.Layer = (string.IsNullOrEmpty(lyr) || lyr == "현재 사용") ? null : lyr;

		if (this._cboTransparency.SelectedIndex == 3 && short.TryParse(this._txtTransparency.Text, out short tr))
		{
			s.Transparency = (short)Math.Clamp((int)tr, 0, 90);
		}
		else
		{
			s.Transparency = null;
		}

		// 고립영역
		s.DetectIslands = this._chkDetectIslands.IsChecked == true;
		if (this._rbIslandNormal.IsChecked == true)
		{
			s.IslandStyle = HatchStyleType.Normal;
		}
		else if (this._rbIslandIgnore.IsChecked == true)
		{
			s.IslandStyle = HatchStyleType.Ignore;
		}
		else
		{
			s.IslandStyle = HatchStyleType.Outer;
		}

		this._state.RetainBoundaries = this._chkRetainBoundaries.IsChecked == true;
	}

	private (XY Min, XY Max)? CalculateBoundariesBounds()
	{
		double minX = double.MaxValue, minY = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue;
		bool found = false;

		foreach (var r in this._state.Regions)
		{
			foreach (var loop in r)
			{
				for (int i = 0; i + 1 < loop.Length; i += 2)
				{
					double x = loop[i], y = loop[i + 1];
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
					found = true;
				}
			}
		}

		foreach (var obj in this._state.Objects)
		{
			var box = obj.GetBoundingBox();
			if (box.Min.X < minX) minX = box.Min.X;
			if (box.Max.X > maxX) maxX = box.Max.X;
			if (box.Min.Y < minY) minY = box.Min.Y;
			if (box.Max.Y > maxY) maxY = box.Max.Y;
			found = true;
		}

		if (!found)
		{
			return null;
		}
		return (new XY(minX, minY), new XY(maxX, maxY));
	}

	private static Border CreateGroupBox(string header, Control content)
	{
		var border = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Background = Brushes.White,
			Padding = new Thickness(8, 6),
			Margin = new Thickness(0, 0, 0, 6),
		};
		var dock = new DockPanel();
		var lbl = new TextBlock
		{
			Text = header,
			FontWeight = FontWeight.SemiBold,
			FontSize = 11,
			Foreground = new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0)),
			Margin = new Thickness(0, 0, 0, 4),
		};
		DockPanel.SetDock(lbl, Dock.Top);
		dock.Children.Add(lbl);
		dock.Children.Add(content);
		border.Child = dock;
		return border;
	}

	private void StyleCombo(ComboBox combo)
	{
		combo.Height = 24;
		combo.MinHeight = 0;
		combo.FontSize = 11;
		combo.Background = Brushes.White;
		combo.BorderBrush = CadTheme.DropdownBorder;
		combo.VerticalContentAlignment = VerticalAlignment.Center;
	}

	private void StyleTextBox(TextBox box)
	{
		box.Height = 24;
		box.MinHeight = 0;
		box.FontSize = 11;
		box.Padding = new Thickness(4, 0);
		box.VerticalAlignment = VerticalAlignment.Center;
		box.VerticalContentAlignment = VerticalAlignment.Center;
	}
}
