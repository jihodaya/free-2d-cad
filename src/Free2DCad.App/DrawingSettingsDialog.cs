using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Types.Units;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Point = Avalonia.Point;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 각도 기준 및 방향 표시 캔버스 (70x70).
/// </summary>
public sealed class BaseAngleCanvas : Control
{
	private double _angleDegrees;
	private bool _clockWise;

	public BaseAngleCanvas()
	{
		this.Width = 70;
		this.Height = 70;
	}

	public void UpdateAngle(double degrees, bool clockWise)
	{
		this._angleDegrees = degrees;
		this._clockWise = clockWise;
		this.InvalidateVisual();
	}

	public override void Render(DrawingContext dc)
	{
		var borderRect = new Rect(2, 2, 66, 66);
		dc.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 1), borderRect);

		double cx = 35, cy = 35, r = 26;
		var circleGeom = new EllipseGeometry(new Rect(cx - r, cy - r, r * 2, r * 2));
		dc.DrawGeometry(null, new Pen(Brushes.Black, 1.2), circleGeom);

		// 4방향 틱 마크 (0, 90, 180, 270)
		var tickPen = new Pen(Brushes.Black, 1);
		dc.DrawLine(tickPen, new Point(cx + r - 4, cy), new Point(cx + r + 2, cy));
		dc.DrawLine(tickPen, new Point(cx - r - 2, cy), new Point(cx - r + 4, cy));
		dc.DrawLine(tickPen, new Point(cx, cy - r - 2), new Point(cx, cy - r + 4));
		dc.DrawLine(tickPen, new Point(cx, cy + r - 4), new Point(cx, cy + r + 2));

		// 기준 각도 선
		double rad = this._angleDegrees * Math.PI / 180.0;
		double dir = this._clockWise ? 1.0 : -1.0;
		double ex = cx + (r - 2) * Math.Cos(rad);
		double ey = cy + (r - 2) * Math.Sin(rad) * dir;

		var linePen = new Pen(new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0)), 1.5);
		dc.DrawLine(linePen, new Point(cx, cy), new Point(ex, ey));

		// 중심점
		dc.DrawEllipse(Brushes.Black, null, new Point(cx, cy), 2, 2);
	}
}

/// <summary>
/// 기존 CAD 스타일 도면 설정 대화상자 (DSETTINGS / UNITS / OSNAP).
/// </summary>
public sealed class DrawingSettingsDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly DrawingCanvas _canvas;

	private readonly TabControl _mainTabs = new();
	private readonly TabControl _unitsTabs = new();
	private readonly TabControl _coordTabs = new();

	// 도면 단위 - Angular
	private readonly RadioButton _rbAngDecimal = new();
	private readonly RadioButton _rbAngDms = new();
	private readonly RadioButton _rbAngGrad = new();
	private readonly RadioButton _rbAngRad = new();
	private readonly RadioButton _rbAngSurvey = new();
	private readonly TextBox _txtAngPrecision = new();
	private readonly RadioButton _rbAngCw = new();
	private readonly RadioButton _rbAngCcw = new();
	private readonly TextBox _txtAngleBase = new();
	private readonly BaseAngleCanvas _angleCanvas = new();

	// 도면 단위 - Linear
	private readonly RadioButton _rbLinScientific = new();
	private readonly RadioButton _rbLinDecimal = new();
	private readonly RadioButton _rbLinEngineering = new();
	private readonly RadioButton _rbLinArchitectural = new();
	private readonly RadioButton _rbLinFractional = new();
	private readonly TextBox _txtLinPrecision = new();

	// 좌표 입력 - 공통
	private readonly CheckBox _chkOrtho = new();

	// 좌표 입력 - 스냅 및 모눈
	private readonly CheckBox _chkSnapOn = new();
	private readonly TextBox _txtSnapX = new();
	private readonly TextBox _txtSnapY = new();
	private readonly CheckBox _chkGridOn = new();
	private readonly TextBox _txtGridX = new();
	private readonly TextBox _txtGridY = new();
	private readonly TextBox _txtGridMajor = new();
	private readonly CheckBox _chkAdaptiveGrid = new();

	// 좌표 입력 - 객체 스냅
	private readonly CheckBox _chkOsnapOn = new();
	private readonly CheckBox _chkSnapEndpoint = new();
	private readonly CheckBox _chkSnapMidpoint = new();
	private readonly CheckBox _chkSnapCenter = new();
	private readonly CheckBox _chkSnapQuadrant = new();
	private readonly CheckBox _chkSnapIntersection = new();
	private readonly CheckBox _chkSnapInsertion = new();
	private readonly CheckBox _chkSnapNode = new();
	private readonly CheckBox _chkSnapNearest = new();
	private readonly CheckBox _chkSnapPerpendicular = new();
	private readonly CheckBox _chkSnapTangent = new();
	private readonly CheckBox _chkSnapExtension = new();
	private readonly CheckBox _chkSnapParallel = new();

	// 표시 - 글꼴
	private readonly ComboBox _cboAltFont = new();
	private readonly ComboBox _cboAltBigFont = new();

	/// <summary>대체 글꼴을 바꿨는지 (닫은 뒤 화면을 다시 그려야 함).</summary>
	public bool FontsChanged { get; private set; }

	public DrawingSettingsDialog(EditorDocument doc, DrawingCanvas canvas, int initialTab = 0, int initialSubTab = 0)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

		this.Title = "도면 설정";
		this.Width = 630;
		this.Height = 520;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(10) };

		// 하단 버튼 모음
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 메인 탭 컨트롤 (도면 단위, 좌표 입력, 표시, 객체 생성, 객체 수정, 3D 설정, 동적 입력, 빠른 속성)
		this._mainTabs.Background = Brushes.Transparent;
		this._mainTabs.Items.Add(new TabItem { Header = "도면 단위", Content = this.BuildUnitsTab(initialSubTab) });
		this._mainTabs.Items.Add(new TabItem { Header = "좌표 입력", Content = this.BuildCoordTab(initialSubTab) });
		this._mainTabs.Items.Add(new TabItem { Header = "표시", Content = this.BuildDisplayTab() });
		this._mainTabs.Items.Add(this.CreatePlaceholderTab("객체 생성"));
		this._mainTabs.Items.Add(this.CreatePlaceholderTab("객체 수정"));
		this._mainTabs.Items.Add(this.CreatePlaceholderTab("3D 설정"));
		this._mainTabs.Items.Add(this.CreatePlaceholderTab("동적 입력"));
		this._mainTabs.Items.Add(this.CreatePlaceholderTab("빠른 속성"));

		this._mainTabs.SelectedIndex = Math.Clamp(initialTab, 0, 7);
		root.Children.Add(this._mainTabs);
		this.Content = root;

		this.LoadFromDocAndCanvas();
	}

	/// <summary>
	/// 표시 탭: 도면의 SHX 글꼴 파일이 없을 때 대신 쓸 글꼴. 기본은 SHX(romans / whgtxt)라 글자 폭이 원본과 같고,
	/// 원하면 TrueType도 고를 수 있다.
	/// </summary>
	private Control BuildDisplayTab()
	{
		var shx = Free2DCad.Rendering.UserFonts.ShxList();
		var all = shx.Concat(Free2DCad.Rendering.UserFonts.Catalog.Select(c => c.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

		void Setup(ComboBox cbo, IReadOnlyList<string> items, string current)
		{
			cbo.ItemsSource = items;
			cbo.SelectedItem = items.FirstOrDefault(i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase));
			cbo.Width = 260;
			cbo.Height = 24;
			cbo.MinHeight = 0;
			cbo.FontSize = 11;
			cbo.MaxDropDownHeight = 360;
			cbo.VerticalAlignment = VerticalAlignment.Center;
		}
		Setup(this._cboAltFont, all, Free2DCad.Rendering.UserFonts.AlternateFont);
		Setup(this._cboAltBigFont, shx, Free2DCad.Rendering.UserFonts.AlternateBigFont);

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("150,*"),
			RowDefinitions = new RowDefinitions("30,30,Auto"),
		};
		void Row(int r, string label, Control c)
		{
			var lbl = new TextBlock { Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
			Grid.SetRow(lbl, r);
			grid.Children.Add(lbl);
			Grid.SetRow(c, r);
			Grid.SetColumn(c, 1);
			grid.Children.Add(c);
		}
		Row(0, "대체 글꼴(영문·숫자):", this._cboAltFont);
		Row(1, "대체 큰 글꼴(한글):", this._cboAltBigFont);

		var btnDefault = new Button { Content = "기본값 (romans.shx / whgtxt.shx)", FontSize = 11, Height = 24, MinHeight = 0, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
		btnDefault.Click += (_, _) =>
		{
			this._cboAltFont.SelectedItem = all.FirstOrDefault(i => string.Equals(i, Free2DCad.Rendering.UserFonts.DefaultAlternateFont, StringComparison.OrdinalIgnoreCase));
			this._cboAltBigFont.SelectedItem = shx.FirstOrDefault(i => string.Equals(i, Free2DCad.Rendering.UserFonts.DefaultAlternateBigFont, StringComparison.OrdinalIgnoreCase));
		};
		Grid.SetRow(btnDefault, 2);
		Grid.SetColumn(btnDefault, 1);
		grid.Children.Add(btnDefault);

		var note = new TextBlock
		{
			Text = "도면이 쓰는 SHX 글꼴 파일이 Fonts 폴더에 없을 때 대신 그릴 글꼴입니다.\n"
				+ "SHX를 고르면 글자 폭과 배치가 원본 도면과 같게 나옵니다. TrueType(.ttf)을 고르면 모양은 부드럽지만 문자 길이가 달라질 수 있습니다.\n"
				+ "글꼴마다 다르게 바꾸려면 Fonts\\fontmap.txt에 \"원래이름.shx=대신쓸파일\"을 적습니다.",
			FontSize = 11,
			Foreground = Brushes.DimGray,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 8, 0, 0),
		};

		var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
		panel.Children.Add(CreateGroupBox("문자 글꼴 대체", new StackPanel { Children = { grid, note } }));
		return panel;
	}

	private TabItem CreatePlaceholderTab(string header)
	{
		return new TabItem
		{
			Header = header,
			Content = new Border
			{
				Padding = new Thickness(20),
				Child = new TextBlock
				{
					Text = "준비 중입니다.",
					Foreground = Brushes.Gray,
					FontSize = 12,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
				},
			},
		};
	}

	private Control BuildUnitsTab(int initialSubTab)
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };

		var chkStripSpaces = new CheckBox
		{
			Content = "거리 또는 각도를 문자로 변환할 때 공백 제거(V)",
			FontSize = 11,
			Margin = new Thickness(4, 0, 0, 4),
		};
		panel.Children.Add(chkStripSpaces);

		this._unitsTabs.Background = Brushes.Transparent;
		this._unitsTabs.Items.Add(new TabItem { Header = "Angular", Content = this.BuildAngularSubTab() });
		this._unitsTabs.Items.Add(new TabItem { Header = "Linear", Content = this.BuildLinearSubTab() });
		this._unitsTabs.SelectedIndex = Math.Clamp(initialSubTab, 0, 1);
		panel.Children.Add(this._unitsTabs);

		return panel;
	}

	private Control BuildAngularSubTab()
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("200, 20, *"),
			Margin = new Thickness(6),
		};

		// 좌측: 단위 유형
		var typeStack = new StackPanel { Spacing = 4 };
		this._rbAngDecimal.Content = "십진 도수(D)";
		this._rbAngDecimal.GroupName = "AngType";
		this._rbAngDecimal.FontSize = 12;
		typeStack.Children.Add(this._rbAngDecimal);

		this._rbAngDms.Content = "도/분/초(M)";
		this._rbAngDms.GroupName = "AngType";
		this._rbAngDms.FontSize = 12;
		typeStack.Children.Add(this._rbAngDms);

		this._rbAngGrad.Content = "그래드(G)";
		this._rbAngGrad.GroupName = "AngType";
		this._rbAngGrad.FontSize = 12;
		typeStack.Children.Add(this._rbAngGrad);

		this._rbAngRad.Content = "라디안(R)";
		this._rbAngRad.GroupName = "AngType";
		this._rbAngRad.FontSize = 12;
		typeStack.Children.Add(this._rbAngRad);

		this._rbAngSurvey.Content = "측량사 단위(S)";
		this._rbAngSurvey.GroupName = "AngType";
		this._rbAngSurvey.FontSize = 12;
		typeStack.Children.Add(this._rbAngSurvey);

		var precRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 40"), Margin = new Thickness(0, 10, 0, 0) };
		precRow.Children.Add(new TextBlock { Text = "정밀도 표시(P):", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		this.StyleSmallBox(this._txtAngPrecision);
		Grid.SetColumn(this._txtAngPrecision, 1);
		precRow.Children.Add(this._txtAngPrecision);
		typeStack.Children.Add(precRow);

		Grid.SetColumn(typeStack, 0);
		grid.Children.Add(CreateGroupBox("단위 유형", typeStack));

		// 우측: 각도 방향 + 각도 기준 + 원 그림
		var rightStack = new StackPanel { Spacing = 8 };

		// 각도 방향
		var dirStack = new StackPanel { Spacing = 4 };
		this._rbAngCw.Content = "시계 방향(K)";
		this._rbAngCw.GroupName = "AngDir";
		this._rbAngCw.FontSize = 12;
		this._rbAngCw.IsCheckedChanged += (_, _) => this.UpdateAngleCanvas();
		dirStack.Children.Add(this._rbAngCw);

		this._rbAngCcw.Content = "시계 반대 방향(C)";
		this._rbAngCcw.GroupName = "AngDir";
		this._rbAngCcw.FontSize = 12;
		this._rbAngCcw.IsCheckedChanged += (_, _) => this.UpdateAngleCanvas();
		dirStack.Children.Add(this._rbAngCcw);
		rightStack.Children.Add(CreateGroupBox("각도 방향", dirStack));

		// 각도 기준 + 원 캔버스
		var baseRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, Auto") };
		var baseInputStack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
		baseInputStack.Children.Add(new TextBlock { Text = "각도 기준(B):", FontSize = 12 });
		this.StyleSmallBox(this._txtAngleBase);
		this._txtAngleBase.TextChanged += (_, _) => this.UpdateAngleCanvas();
		baseInputStack.Children.Add(this._txtAngleBase);
		baseRow.Children.Add(baseInputStack);

		Grid.SetColumn(this._angleCanvas, 1);
		baseRow.Children.Add(this._angleCanvas);

		rightStack.Children.Add(baseRow);
		Grid.SetColumn(rightStack, 2);
		grid.Children.Add(rightStack);

		return grid;
	}

	private Control BuildLinearSubTab()
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("220, *"),
			Margin = new Thickness(6),
		};

		var typeStack = new StackPanel { Spacing = 6 };
		this._rbLinScientific.Content = "과학(Scientific)";
		this._rbLinScientific.GroupName = "LinType";
		this._rbLinScientific.FontSize = 12;
		typeStack.Children.Add(this._rbLinScientific);

		this._rbLinDecimal.Content = "십진(Decimal)";
		this._rbLinDecimal.GroupName = "LinType";
		this._rbLinDecimal.FontSize = 12;
		typeStack.Children.Add(this._rbLinDecimal);

		this._rbLinEngineering.Content = "공학(Engineering)";
		this._rbLinEngineering.GroupName = "LinType";
		this._rbLinEngineering.FontSize = 12;
		typeStack.Children.Add(this._rbLinEngineering);

		this._rbLinArchitectural.Content = "건축(Architectural)";
		this._rbLinArchitectural.GroupName = "LinType";
		this._rbLinArchitectural.FontSize = 12;
		typeStack.Children.Add(this._rbLinArchitectural);

		this._rbLinFractional.Content = "분수(Fractional)";
		this._rbLinFractional.GroupName = "LinType";
		this._rbLinFractional.FontSize = 12;
		typeStack.Children.Add(this._rbLinFractional);

		var precRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*, 40"), Margin = new Thickness(0, 10, 0, 0) };
		precRow.Children.Add(new TextBlock { Text = "정밀도(Precision):", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		this.StyleSmallBox(this._txtLinPrecision);
		Grid.SetColumn(this._txtLinPrecision, 1);
		precRow.Children.Add(this._txtLinPrecision);
		typeStack.Children.Add(precRow);

		grid.Children.Add(CreateGroupBox("단위 유형", typeStack));

		return grid;
	}

	private Control BuildCoordTab(int initialSubTab)
	{
		var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };

		var topRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(4, 0, 0, 4) };
		this._chkOrtho.Content = "직교 그리기(O)";
		this._chkOrtho.FontSize = 12;
		topRow.Children.Add(this._chkOrtho);

		var chkMagnet = new CheckBox { Content = "AutoSnap magnet", FontSize = 11, IsChecked = true, IsEnabled = false };
		topRow.Children.Add(chkMagnet);
		panel.Children.Add(topRow);

		this._coordTabs.Background = Brushes.Transparent;
		this._coordTabs.Items.Add(new TabItem { Header = "스냅 및 모눈", Content = this.BuildSnapGridSubTab() });
		this._coordTabs.Items.Add(new TabItem { Header = "객체 스냅", Content = this.BuildOsnapSubTab() });
		this._coordTabs.Items.Add(this.CreatePlaceholderTab("객체 선택"));
		this._coordTabs.Items.Add(this.CreatePlaceholderTab("한계"));
		this._coordTabs.Items.Add(this.CreatePlaceholderTab("극추적"));

		this._coordTabs.SelectedIndex = Math.Clamp(initialSubTab, 0, 1);
		panel.Children.Add(this._coordTabs);

		return panel;
	}

	private Control BuildSnapGridSubTab()
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, 16, *"),
			Margin = new Thickness(6),
		};

		// 1. 좌측: 스냅
		var snapStack = new StackPanel { Spacing = 6 };
		this._chkSnapOn.Content = "스냅 켜기 (F9)";
		this._chkSnapOn.FontSize = 12;
		this._chkSnapOn.FontWeight = FontWeight.SemiBold;
		snapStack.Children.Add(this._chkSnapOn);

		var snapSpacingGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, *"),
			Margin = new Thickness(0, 4),
		};
		snapSpacingGrid.Children.Add(new TextBlock { Text = "X:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
		this.StyleSmallBox(this._txtSnapX);
		Grid.SetColumn(this._txtSnapX, 1);
		snapSpacingGrid.Children.Add(this._txtSnapX);

		var lblSnapY = new TextBlock { Text = "Y:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
		Grid.SetColumn(lblSnapY, 2);
		snapSpacingGrid.Children.Add(lblSnapY);

		this.StyleSmallBox(this._txtSnapY);
		Grid.SetColumn(this._txtSnapY, 3);
		snapSpacingGrid.Children.Add(this._txtSnapY);

		snapStack.Children.Add(CreateGroupBox("스냅 간격", snapSpacingGrid));

		var snapTypeStack = new StackPanel { Spacing = 4 };
		var rbRect = new RadioButton { Content = "직사각형", GroupName = "SnapGridType", FontSize = 12, IsChecked = true };
		var rbIso = new RadioButton { Content = "등각", GroupName = "SnapGridType", FontSize = 12, IsEnabled = false };
		snapTypeStack.Children.Add(rbRect);
		snapTypeStack.Children.Add(rbIso);
		snapStack.Children.Add(CreateGroupBox("스냅 동작", snapTypeStack));

		Grid.SetColumn(snapStack, 0);
		grid.Children.Add(snapStack);

		// 2. 우측: 그리드(모눈)
		var gridStack = new StackPanel { Spacing = 6 };
		this._chkGridOn.Content = "그리드 켜기 (F7)";
		this._chkGridOn.FontSize = 12;
		this._chkGridOn.FontWeight = FontWeight.SemiBold;
		gridStack.Children.Add(this._chkGridOn);

		var gridSpacingGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, *"),
			RowDefinitions = new RowDefinitions("Auto, Auto"),
			Margin = new Thickness(0, 4),
		};
		gridSpacingGrid.Children.Add(new TextBlock { Text = "X:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
		this.StyleSmallBox(this._txtGridX);
		Grid.SetColumn(this._txtGridX, 1);
		gridSpacingGrid.Children.Add(this._txtGridX);

		var lblGridY = new TextBlock { Text = "Y:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) };
		Grid.SetColumn(lblGridY, 2);
		gridSpacingGrid.Children.Add(lblGridY);

		this.StyleSmallBox(this._txtGridY);
		Grid.SetColumn(this._txtGridY, 3);
		gridSpacingGrid.Children.Add(this._txtGridY);

		var lblMajor = new TextBlock { Text = "주 선 간격:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 4, 0) };
		Grid.SetRow(lblMajor, 1);
		Grid.SetColumnSpan(lblMajor, 2);
		gridSpacingGrid.Children.Add(lblMajor);

		this.StyleSmallBox(this._txtGridMajor);
		this._txtGridMajor.Margin = new Thickness(0, 6, 0, 0);
		Grid.SetRow(this._txtGridMajor, 1);
		Grid.SetColumn(this._txtGridMajor, 3);
		gridSpacingGrid.Children.Add(this._txtGridMajor);

		gridStack.Children.Add(CreateGroupBox("그리드 간격", gridSpacingGrid));

		var gridBehaviorStack = new StackPanel { Spacing = 4 };
		this._chkAdaptiveGrid.Content = "가변성 그리드";
		this._chkAdaptiveGrid.FontSize = 11;
		this._chkAdaptiveGrid.IsChecked = true;
		gridBehaviorStack.Children.Add(this._chkAdaptiveGrid);

		var chkBeyondLimits = new CheckBox { Content = "한계 너머 그리드 표시", FontSize = 11, IsChecked = true, IsEnabled = false };
		gridBehaviorStack.Children.Add(chkBeyondLimits);

		gridStack.Children.Add(CreateGroupBox("그리드 동작", gridBehaviorStack));

		Grid.SetColumn(gridStack, 2);
		grid.Children.Add(gridStack);

		return grid;
	}

	private Control BuildOsnapSubTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(6) };

		this._chkOsnapOn.Content = "객체 스냅 켜기 (F3)";
		this._chkOsnapOn.FontSize = 12;
		this._chkOsnapOn.FontWeight = FontWeight.SemiBold;
		panel.Children.Add(this._chkOsnapOn);

		var modesGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, *, *"),
			RowDefinitions = new RowDefinitions("26, 26, 26, 26, 26"),
			Margin = new Thickness(4),
		};

		// 1열
		this.StyleCheck(this._chkSnapEndpoint, "끝점(E)", true);
		modesGrid.Children.Add(this._chkSnapEndpoint);

		this.StyleCheck(this._chkSnapNearest, "근처점(N)", true);
		Grid.SetRow(this._chkSnapNearest, 1);
		modesGrid.Children.Add(this._chkSnapNearest);

		this.StyleCheck(this._chkSnapPerpendicular, "수직(L)", false);
		Grid.SetRow(this._chkSnapPerpendicular, 2);
		modesGrid.Children.Add(this._chkSnapPerpendicular);

		this.StyleCheck(this._chkSnapQuadrant, "사분점(Q)", true);
		Grid.SetRow(this._chkSnapQuadrant, 3);
		modesGrid.Children.Add(this._chkSnapQuadrant);

		this.StyleCheck(this._chkSnapParallel, "평행(Parallel)", false);
		Grid.SetRow(this._chkSnapParallel, 4);
		modesGrid.Children.Add(this._chkSnapParallel);

		// 2열
		this.StyleCheck(this._chkSnapCenter, "중심(R)", true);
		Grid.SetColumn(this._chkSnapCenter, 1);
		modesGrid.Children.Add(this._chkSnapCenter);

		this.StyleCheck(this._chkSnapMidpoint, "중간점(M)", true);
		Grid.SetRow(this._chkSnapMidpoint, 1);
		Grid.SetColumn(this._chkSnapMidpoint, 1);
		modesGrid.Children.Add(this._chkSnapMidpoint);

		var chkGeomCenter = new CheckBox { Content = "기하학적 중심", FontSize = 11, IsEnabled = false };
		Grid.SetRow(chkGeomCenter, 2);
		Grid.SetColumn(chkGeomCenter, 1);
		modesGrid.Children.Add(chkGeomCenter);

		this.StyleCheck(this._chkSnapNode, "점(노드)(P)", true);
		Grid.SetRow(this._chkSnapNode, 3);
		Grid.SetColumn(this._chkSnapNode, 1);
		modesGrid.Children.Add(this._chkSnapNode);

		this.StyleCheck(this._chkSnapTangent, "접점(T)", false);
		Grid.SetRow(this._chkSnapTangent, 4);
		Grid.SetColumn(this._chkSnapTangent, 1);
		modesGrid.Children.Add(this._chkSnapTangent);

		// 3열
		this.StyleCheck(this._chkSnapIntersection, "교차점(O)", true);
		Grid.SetColumn(this._chkSnapIntersection, 2);
		modesGrid.Children.Add(this._chkSnapIntersection);

		this.StyleCheck(this._chkSnapInsertion, "삽입(S)", true);
		Grid.SetRow(this._chkSnapInsertion, 1);
		Grid.SetColumn(this._chkSnapInsertion, 2);
		modesGrid.Children.Add(this._chkSnapInsertion);

		this.StyleCheck(this._chkSnapExtension, "연장선(Extension)", false);
		Grid.SetRow(this._chkSnapExtension, 2);
		Grid.SetColumn(this._chkSnapExtension, 2);
		modesGrid.Children.Add(this._chkSnapExtension);

		panel.Children.Add(CreateGroupBox("객체 스냅 모드", modesGrid));

		// Select All / Clear 버튼
		var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
		var btnSelectAll = new Button
		{
			Content = "모두 선택",
			Width = 80,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnSelectAll.Click += (_, _) => this.SetAllSnapModes(true);
		btnRow.Children.Add(btnSelectAll);

		var btnClearAll = new Button
		{
			Content = "지우기(C)",
			Width = 80,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnClearAll.Click += (_, _) => this.SetAllSnapModes(false);
		btnRow.Children.Add(btnClearAll);

		panel.Children.Add(btnRow);

		return panel;
	}

	private void StyleCheck(CheckBox box, string text, bool enabled)
	{
		box.Content = text;
		box.FontSize = 11;
		box.IsEnabled = enabled;
	}

	private void SetAllSnapModes(bool check)
	{
		this._chkSnapEndpoint.IsChecked = check;
		this._chkSnapMidpoint.IsChecked = check;
		this._chkSnapCenter.IsChecked = check;
		this._chkSnapQuadrant.IsChecked = check;
		this._chkSnapIntersection.IsChecked = check;
		this._chkSnapInsertion.IsChecked = check;
		this._chkSnapNode.IsChecked = check;
		this._chkSnapNearest.IsChecked = check;
	}

	private Control BuildBottomBar()
	{
		var bar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, Auto"),
			Height = 36,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 10, 0, 0),
		};

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
		Grid.SetColumn(btnHelp, 0);
		bar.Children.Add(btnHelp);

		var btnOk = new Button
		{
			Content = "확인(O)",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsDefault = true,
			Margin = new Thickness(0, 0, 8, 0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnOk.Click += (_, _) =>
		{
			this.SaveAndApply();
			this.Close();
		};
		Grid.SetColumn(btnOk, 2);
		bar.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소(C)",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsCancel = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close();
		Grid.SetColumn(btnCancel, 3);
		bar.Children.Add(btnCancel);

		return bar;
	}

	private void LoadFromDocAndCanvas()
	{
		var h = this._doc.Document.Header;

		// Angular units
		this._rbAngDecimal.IsChecked = h.AngularUnit == AngularUnitFormat.DecimalDegrees;
		this._rbAngDms.IsChecked = h.AngularUnit == AngularUnitFormat.DegreesMinutesSeconds;
		this._rbAngGrad.IsChecked = h.AngularUnit == AngularUnitFormat.Gradians;
		this._rbAngRad.IsChecked = h.AngularUnit == AngularUnitFormat.Radians;
		this._rbAngSurvey.IsChecked = h.AngularUnit == AngularUnitFormat.SurveyorsUnits;
		this._txtAngPrecision.Text = h.AngularUnitPrecision.ToString();

		this._rbAngCw.IsChecked = h.AngularDirection == AngularDirection.ClockWise;
		this._rbAngCcw.IsChecked = h.AngularDirection == AngularDirection.CounterClockWise;
		double deg = h.AngleBase * 180.0 / Math.PI;
		this._txtAngleBase.Text = $"{deg:0.##}";

		// Linear units
		this._rbLinScientific.IsChecked = h.LinearUnitFormat == LinearUnitFormat.Scientific;
		this._rbLinDecimal.IsChecked = h.LinearUnitFormat == LinearUnitFormat.Decimal;
		this._rbLinEngineering.IsChecked = h.LinearUnitFormat == LinearUnitFormat.Engineering;
		this._rbLinArchitectural.IsChecked = h.LinearUnitFormat == LinearUnitFormat.Architectural;
		this._rbLinFractional.IsChecked = h.LinearUnitFormat == LinearUnitFormat.Fractional;
		this._txtLinPrecision.Text = h.LinearUnitPrecision.ToString();

		// Canvas settings
		this._chkOrtho.IsChecked = this._canvas.OrthoEnabled;
		this._chkSnapOn.IsChecked = this._canvas.SnapEnabled;
		this._txtSnapX.Text = $"{this._canvas.SnapSpacingX:0.####}";
		this._txtSnapY.Text = $"{this._canvas.SnapSpacingY:0.####}";

		this._chkGridOn.IsChecked = this._canvas.GridEnabled;
		this._txtGridX.Text = $"{this._canvas.GridSpacingX:0.####}";
		this._txtGridY.Text = $"{this._canvas.GridSpacingY:0.####}";
		this._txtGridMajor.Text = this._canvas.GridMajorEvery.ToString();

		this._chkOsnapOn.IsChecked = this._canvas.ObjectSnapEnabled;
		var modes = this._canvas.SnapModes;
		this._chkSnapEndpoint.IsChecked = modes.HasFlag(SnapModes.Endpoint);
		this._chkSnapMidpoint.IsChecked = modes.HasFlag(SnapModes.Midpoint);
		this._chkSnapCenter.IsChecked = modes.HasFlag(SnapModes.Center);
		this._chkSnapQuadrant.IsChecked = modes.HasFlag(SnapModes.Quadrant);
		this._chkSnapIntersection.IsChecked = modes.HasFlag(SnapModes.Intersection);
		this._chkSnapInsertion.IsChecked = modes.HasFlag(SnapModes.Insertion);
		this._chkSnapNode.IsChecked = modes.HasFlag(SnapModes.Node);
		this._chkSnapNearest.IsChecked = modes.HasFlag(SnapModes.Nearest);

		this.UpdateAngleCanvas();
	}

	private void UpdateAngleCanvas()
	{
		double deg = double.TryParse(this._txtAngleBase.Text, out var v) ? v : 0;
		bool cw = this._rbAngCw.IsChecked == true;
		this._angleCanvas.UpdateAngle(deg, cw);
	}

	private void SaveAndApply()
	{
		// 1. 도면 단위 (Header) - DelegateAction
		var h = this._doc.Document.Header;
		var oldAngUnit = h.AngularUnit;
		var oldAngPrec = h.AngularUnitPrecision;
		var oldAngDir = h.AngularDirection;
		var oldAngBase = h.AngleBase;
		var oldLinUnit = h.LinearUnitFormat;
		var oldLinPrec = h.LinearUnitPrecision;

		var newAngUnit = this._rbAngDms.IsChecked == true ? AngularUnitFormat.DegreesMinutesSeconds :
			this._rbAngGrad.IsChecked == true ? AngularUnitFormat.Gradians :
			this._rbAngRad.IsChecked == true ? AngularUnitFormat.Radians :
			this._rbAngSurvey.IsChecked == true ? AngularUnitFormat.SurveyorsUnits :
			AngularUnitFormat.DecimalDegrees;

		short newAngPrec = short.TryParse(this._txtAngPrecision.Text, out var ap) ? (short)Math.Clamp((int)ap, 0, 8) : (short)0;
		var newAngDir = this._rbAngCw.IsChecked == true ? AngularDirection.ClockWise : AngularDirection.CounterClockWise;
		double newAngBaseDeg = double.TryParse(this._txtAngleBase.Text, out var ab) ? ab : 0;
		double newAngBaseRad = newAngBaseDeg * Math.PI / 180.0;

		var newLinUnit = this._rbLinScientific.IsChecked == true ? LinearUnitFormat.Scientific :
			this._rbLinEngineering.IsChecked == true ? LinearUnitFormat.Engineering :
			this._rbLinArchitectural.IsChecked == true ? LinearUnitFormat.Architectural :
			this._rbLinFractional.IsChecked == true ? LinearUnitFormat.Fractional :
			LinearUnitFormat.Decimal;

		short newLinPrec = short.TryParse(this._txtLinPrecision.Text, out var lp) ? (short)Math.Clamp((int)lp, 0, 8) : (short)4;

		this._doc.Execute(new DelegateAction(
			"도면 단위 변경",
			() =>
			{
				h.AngularUnit = newAngUnit;
				h.AngularUnitPrecision = newAngPrec;
				h.AngularDirection = newAngDir;
				h.AngleBase = newAngBaseRad;
				h.LinearUnitFormat = newLinUnit;
				h.LinearUnitPrecision = newLinPrec;
			},
			() =>
			{
				h.AngularUnit = oldAngUnit;
				h.AngularUnitPrecision = oldAngPrec;
				h.AngularDirection = oldAngDir;
				h.AngleBase = oldAngBase;
				h.LinearUnitFormat = oldLinUnit;
				h.LinearUnitPrecision = oldLinPrec;
			}));

		// 2. 캔버스 및 앱 설정 반영
		this._canvas.OrthoEnabled = this._chkOrtho.IsChecked == true;
		this._canvas.SnapEnabled = this._chkSnapOn.IsChecked == true;
		if (double.TryParse(this._txtSnapX.Text, out var sx) && sx > 0) this._canvas.SnapSpacingX = sx;
		if (double.TryParse(this._txtSnapY.Text, out var sy) && sy > 0) this._canvas.SnapSpacingY = sy;

		this._canvas.GridEnabled = this._chkGridOn.IsChecked == true;
		if (double.TryParse(this._txtGridX.Text, out var gx) && gx > 0) this._canvas.GridSpacingX = gx;
		if (double.TryParse(this._txtGridY.Text, out var gy) && gy > 0) this._canvas.GridSpacingY = gy;
		if (int.TryParse(this._txtGridMajor.Text, out var gm) && gm > 0) this._canvas.GridMajorEvery = gm;

		this._canvas.ObjectSnapEnabled = this._chkOsnapOn.IsChecked == true;

		SnapModes sm = SnapModes.None;
		if (this._chkSnapEndpoint.IsChecked == true) sm |= SnapModes.Endpoint;
		if (this._chkSnapMidpoint.IsChecked == true) sm |= SnapModes.Midpoint;
		if (this._chkSnapCenter.IsChecked == true) sm |= SnapModes.Center;
		if (this._chkSnapQuadrant.IsChecked == true) sm |= SnapModes.Quadrant;
		if (this._chkSnapIntersection.IsChecked == true) sm |= SnapModes.Intersection;
		if (this._chkSnapInsertion.IsChecked == true) sm |= SnapModes.Insertion;
		if (this._chkSnapNode.IsChecked == true) sm |= SnapModes.Node;
		if (this._chkSnapNearest.IsChecked == true) sm |= SnapModes.Nearest;
		this._canvas.SnapModes = sm;

		// 대체 글꼴
		string altFont = this._cboAltFont.SelectedItem as string ?? Free2DCad.Rendering.UserFonts.AlternateFont;
		string altBig = this._cboAltBigFont.SelectedItem as string ?? Free2DCad.Rendering.UserFonts.AlternateBigFont;
		if (!string.Equals(altFont, Free2DCad.Rendering.UserFonts.AlternateFont, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(altBig, Free2DCad.Rendering.UserFonts.AlternateBigFont, StringComparison.OrdinalIgnoreCase))
		{
			Free2DCad.Rendering.UserFonts.AlternateFont = altFont;
			Free2DCad.Rendering.UserFonts.AlternateBigFont = altBig;
			this.FontsChanged = true;
		}

		// 3. 앱 설정 json 저장
		var appSettings = CadAppSettings.FromCanvas(this._canvas);
		appSettings.Save();

		this._canvas.InvalidateVisual();
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
			Margin = new Thickness(0, 0, 0, 4),
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

	private void StyleSmallBox(TextBox box)
	{
		box.Width = 50;
		box.Height = 24;
		box.MinHeight = 0;
		box.FontSize = 11;
		box.Padding = new Thickness(4, 0);
		box.VerticalAlignment = VerticalAlignment.Center;
		box.VerticalContentAlignment = VerticalAlignment.Center;
		box.TextAlignment = TextAlignment.Right;
	}
}
