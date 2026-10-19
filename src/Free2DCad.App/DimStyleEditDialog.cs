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
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using AcadColor = ACadSharp.Color;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 치수 스타일 수정 대화상자 (DimStyleEditDialog).
/// 선, 화살촉, 문자, 형식, 단위 탭 제공 및 실시간 DimStylePreview 갱신.
/// </summary>
public sealed class DimStyleEditDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly DimensionStyle _targetStyle;
	private readonly DimensionStyle _workStyle;
	// 탭마다 미리보기가 하나씩 필요 (컨트롤 하나를 여러 부모에 넣을 수 없음)
	private readonly List<DimStylePreview> _previews = new();
	private bool _isUpdating;

	// === 선 탭 ===
	private readonly ComboBox _cboDimLineColor = new();
	private readonly ComboBox _cboDimLineType = new();
	private readonly ComboBox _cboDimLineWeight = new();
	private readonly TextBox _txtDimLineExt = new();
	private readonly TextBox _txtDimLineInc = new();
	private readonly CheckBox _chkDimLineSupp1 = new();
	private readonly CheckBox _chkDimLineSupp2 = new();

	private readonly ComboBox _cboExtLineColor = new();
	private readonly ComboBox _cboExtLineType1 = new();
	private readonly ComboBox _cboExtLineType2 = new();
	private readonly ComboBox _cboExtLineWeight = new();
	private readonly CheckBox _chkExtLineSupp1 = new();
	private readonly CheckBox _chkExtLineSupp2 = new();
	private readonly TextBox _txtExtLineExt = new();
	private readonly TextBox _txtExtLineOff = new();
	private readonly CheckBox _chkExtLineFixed = new();
	private readonly TextBox _txtExtLineFixedLen = new();

	// === 화살촉 탭 ===
	private readonly CheckBox _chkSeparateArrows = new();
	private readonly ComboBox _cboArrow1 = new();
	private readonly ComboBox _cboArrow2 = new();
	private readonly ComboBox _cboArrowLeader = new();
	private readonly TextBox _txtArrowSize = new();
	private readonly CheckBox _chkEnableTick = new();
	private readonly TextBox _txtTickSize = new();
	private readonly ComboBox _cboCenterMark = new();
	private readonly TextBox _txtCenterMarkSize = new();
	private readonly TextBox _txtJogAngle = new();

	// === 문자 탭 ===
	private readonly ComboBox _cboTextStyle = new();
	private readonly ComboBox _cboTextColor = new();
	private readonly ComboBox _cboTextBgMode = new();
	private readonly ComboBox _cboTextBgColor = new();
	private readonly TextBox _txtTextHeight = new();
	private readonly ComboBox _cboTextVertAlign = new();
	private readonly TextBox _txtTextGap = new();
	private readonly ComboBox _cboTextHorizAlign = new();
	private readonly ComboBox _cboTextDirection = new();
	private readonly ComboBox _cboTextOutsideDir = new();
	private readonly ComboBox _cboTextInsideDir = new();
	private readonly CheckBox _chkDrawTextBorder = new();

	// === 형식 탭 ===
	private readonly ComboBox _cboFitType = new();
	private readonly ComboBox _cboTextMovement = new();
	private readonly TextBox _txtOverallScale = new();
	private readonly CheckBox _chkTextInsideExt = new();
	private readonly CheckBox _chkSuppressOutsideExt = new();

	// === 단위 탭 ===
	private readonly ComboBox _cboLinearUnitFormat = new();
	private readonly ComboBox _cboLinearPrecision = new();
	private readonly ComboBox _cboDecimalSeparator = new();
	private readonly TextBox _txtRounding = new();
	private readonly TextBox _txtPrefix = new();
	private readonly TextBox _txtSuffix = new();
	private readonly TextBox _txtLinearScaleFactor = new();
	private readonly CheckBox _chkZeroLeading = new();
	private readonly CheckBox _chkZeroTrailing = new();
	private readonly ComboBox _cboAngularUnit = new();
	private readonly ComboBox _cboAngularPrecision = new();
	private readonly CheckBox _chkAngZeroLeading = new();
	private readonly CheckBox _chkAngZeroTrailing = new();

	public DimStyleEditDialog(EditorDocument doc, DimensionStyle targetStyle)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._targetStyle = targetStyle ?? throw new ArgumentNullException(nameof(targetStyle));

		// 작업용 복제본 생성
		this._workStyle = (DimensionStyle)targetStyle.Clone();

		this.Title = $"현재 치수 스타일 덮어쓰기: {targetStyle.Name}";
		this.Width = 780;
		this.Height = 560;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(10) };

		// 하단 버튼 모음
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 중앙 영역: 상단 탭 컨트롤 (선, 화살촉, 문자, 형식, 단위, Alternate Units, Tolerances)
		// 각 탭 내부: 좌측 설정 패널 + 우측 DimStylePreview
		var tabControl = new TabControl { Background = Brushes.Transparent };
		tabControl.Items.Add(new TabItem { Header = "선", Content = this.WrapWithPreview(this.BuildLinesTab()) });
		tabControl.Items.Add(new TabItem { Header = "화살촉", Content = this.WrapWithPreview(this.BuildArrowsTab()) });
		tabControl.Items.Add(new TabItem { Header = "문자", Content = this.WrapWithPreview(this.BuildTextTab()) });
		tabControl.Items.Add(new TabItem { Header = "형식", Content = this.WrapWithPreview(this.BuildFitTab()) });
		tabControl.Items.Add(new TabItem { Header = "단위", Content = this.WrapWithPreview(this.BuildUnitsTab()) });
		tabControl.Items.Add(new TabItem { Header = "Alternate Units", Content = this.WrapWithPreview(this.BuildAltUnitsTab()) });
		tabControl.Items.Add(new TabItem { Header = "Tolerances", Content = this.WrapWithPreview(this.BuildTolerancesTab()) });

		root.Children.Add(tabControl);
		this.Content = root;

		this.PopulateFromWorkingStyle();
		this.AttachEventHandlers();
		this.UpdatePreviews();
	}

	private DimStylePreview NewPreview()
	{
		var p = new DimStylePreview();
		this._previews.Add(p);
		return p;
	}

	private void UpdatePreviews()
	{
		foreach (var p in this._previews)
		{
			p.UpdateStyle(this._workStyle);
		}
	}

	private Control WrapWithPreview(Control leftPanel)
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("380,10,360"),
			Margin = new Thickness(0, 8, 0, 4),
		};

		var leftScroll = new ScrollViewer
		{
			Content = leftPanel,
			VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
		};
		Grid.SetColumn(leftScroll, 0);
		grid.Children.Add(leftScroll);

		var previewBox = new Border
		{
			Background = Brushes.Black,
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0x80, 0x80, 0x80)),
			BorderThickness = new Thickness(1),
			Height = 380,
			Child = this.NewPreview(),
		};
		Grid.SetColumn(previewBox, 2);
		grid.Children.Add(previewBox);

		return grid;
	}

	// 1. 선 탭
	private Control BuildLinesTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };

		// 치수선 그룹
		var grpDimLine = new HeaderedContentControl { Header = "치수선", Margin = new Thickness(0, 0, 0, 4) };
		var pnlDimLine = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };

		pnlDimLine.Children.Add(CreateRow("색상:", this._cboDimLineColor));
		pnlDimLine.Children.Add(CreateRow("선종류:", this._cboDimLineType));
		pnlDimLine.Children.Add(CreateRow("선굵기:", this._cboDimLineWeight));
		pnlDimLine.Children.Add(CreateRow("틱 마크 넘어 확장:", this._txtDimLineExt));
		pnlDimLine.Children.Add(CreateRow("기본선 간격:", this._txtDimLineInc));

		var rowSuppDim = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
		rowSuppDim.Children.Add(new TextBlock { Text = "억제:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
		this._chkDimLineSupp1.Content = "치수선 1";
		this._chkDimLineSupp2.Content = "치수선 2";
		rowSuppDim.Children.Add(this._chkDimLineSupp1);
		rowSuppDim.Children.Add(this._chkDimLineSupp2);
		pnlDimLine.Children.Add(rowSuppDim);
		grpDimLine.Content = pnlDimLine;
		panel.Children.Add(grpDimLine);

		// 확장선 그룹
		var grpExtLine = new HeaderedContentControl { Header = "확장선", Margin = new Thickness(0, 4, 0, 0) };
		var pnlExtLine = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };

		pnlExtLine.Children.Add(CreateRow("색상:", this._cboExtLineColor));
		pnlExtLine.Children.Add(CreateRow("선종류 1:", this._cboExtLineType1));
		pnlExtLine.Children.Add(CreateRow("선종류 2:", this._cboExtLineType2));
		pnlExtLine.Children.Add(CreateRow("선두께:", this._cboExtLineWeight));

		var rowSuppExt = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
		rowSuppExt.Children.Add(new TextBlock { Text = "억제:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
		this._chkExtLineSupp1.Content = "확장선 1";
		this._chkExtLineSupp2.Content = "확장선 2";
		rowSuppExt.Children.Add(this._chkExtLineSupp1);
		rowSuppExt.Children.Add(this._chkExtLineSupp2);
		pnlExtLine.Children.Add(rowSuppExt);

		pnlExtLine.Children.Add(CreateRow("치수선 너머 연장:", this._txtExtLineExt));
		pnlExtLine.Children.Add(CreateRow("원점 간격띄우기:", this._txtExtLineOff));

		var rowFixed = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		this._chkExtLineFixed.Content = "고정 길이 확장선";
		rowFixed.Children.Add(this._chkExtLineFixed);
		rowFixed.Children.Add(new TextBlock { Text = "길이:", VerticalAlignment = VerticalAlignment.Center });
		this._txtExtLineFixedLen.Width = 60;
		rowFixed.Children.Add(this._txtExtLineFixedLen);
		pnlExtLine.Children.Add(rowFixed);

		grpExtLine.Content = pnlExtLine;
		panel.Children.Add(grpExtLine);

		return panel;
	}

	// 2. 화살촉 탭
	private Control BuildArrowsTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };

		var grpArrows = new HeaderedContentControl { Header = "화살촉 머리" };
		var pnlArrows = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };

		this._chkSeparateArrows.Content = "시작/끝에 별도의 화살촉 허용";
		pnlArrows.Children.Add(this._chkSeparateArrows);

		pnlArrows.Children.Add(CreateRow("시작 화살촉:", this._cboArrow1));
		pnlArrows.Children.Add(CreateRow("끝 화살촉:", this._cboArrow2));
		pnlArrows.Children.Add(CreateRow("지시선 화살촉:", this._cboArrowLeader));
		pnlArrows.Children.Add(CreateRow("화살촉 크기:", this._txtArrowSize));

		var rowTick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
		this._chkEnableTick.Content = "틱 마크 활성화";
		rowTick.Children.Add(this._chkEnableTick);
		rowTick.Children.Add(new TextBlock { Text = "크기:", VerticalAlignment = VerticalAlignment.Center });
		this._txtTickSize.Width = 60;
		rowTick.Children.Add(this._txtTickSize);
		pnlArrows.Children.Add(rowTick);

		grpArrows.Content = pnlArrows;
		panel.Children.Add(grpArrows);

		// 센터 마크 그룹
		var grpCenter = new HeaderedContentControl { Header = "센터 마크" };
		var pnlCenter = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlCenter.Children.Add(CreateRow("심볼:", this._cboCenterMark));
		pnlCenter.Children.Add(CreateRow("마크 크기:", this._txtCenterMarkSize));
		grpCenter.Content = pnlCenter;
		panel.Children.Add(grpCenter);

		// 절곡 차원 그룹
		var grpJog = new HeaderedContentControl { Header = "절곡 차원" };
		var pnlJog = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlJog.Children.Add(CreateRow("절곡 각도:", this._txtJogAngle));
		grpJog.Content = pnlJog;
		panel.Children.Add(grpJog);

		return panel;
	}

	// 3. 문자 탭
	private Control BuildTextTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };

		var grpApp = new HeaderedContentControl { Header = "텍스트 외관" };
		var pnlApp = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlApp.Children.Add(CreateRow("텍스트 스타일:", this._cboTextStyle));
		pnlApp.Children.Add(CreateRow("텍스트 색상:", this._cboTextColor));
		pnlApp.Children.Add(CreateRow("텍스트 배경:", this._cboTextBgMode));
		pnlApp.Children.Add(CreateRow("배경 색상:", this._cboTextBgColor));
		pnlApp.Children.Add(CreateRow("텍스트 높이:", this._txtTextHeight));
		grpApp.Content = pnlApp;
		panel.Children.Add(grpApp);

		var grpPlace = new HeaderedContentControl { Header = "텍스트 배치" };
		var pnlPlace = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlPlace.Children.Add(CreateRow("수직:", this._cboTextVertAlign));
		pnlPlace.Children.Add(CreateRow("수직 간격띄우기:", this._txtTextGap));
		pnlPlace.Children.Add(CreateRow("수평:", this._cboTextHorizAlign));
		pnlPlace.Children.Add(CreateRow("보기 방향:", this._cboTextDirection));
		grpPlace.Content = pnlPlace;
		panel.Children.Add(grpPlace);

		var grpDir = new HeaderedContentControl { Header = "텍스트 방향" };
		var pnlDir = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlDir.Children.Add(CreateRow("외부 텍스트:", this._cboTextOutsideDir));
		pnlDir.Children.Add(CreateRow("내부 텍스트:", this._cboTextInsideDir));
		this._chkDrawTextBorder.Content = "텍스트 주변에 테두리 그리기";
		pnlDir.Children.Add(this._chkDrawTextBorder);
		grpDir.Content = pnlDir;
		panel.Children.Add(grpDir);

		return panel;
	}

	// 4. 형식 탭
	private Control BuildFitTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };

		var grpFit = new HeaderedContentControl { Header = "맞춤 옵션" };
		var pnlFit = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlFit.Children.Add(CreateRow("공간 부족 시:", this._cboFitType));
		pnlFit.Children.Add(CreateRow("문자 이동 시:", this._cboTextMovement));
		grpFit.Content = pnlFit;
		panel.Children.Add(grpFit);

		var grpScale = new HeaderedContentControl { Header = "치수 피쳐 축척" };
		var pnlScale = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlScale.Children.Add(CreateRow("전체 축척 (DIMSCALE):", this._txtOverallScale));
		grpScale.Content = pnlScale;
		panel.Children.Add(grpScale);

		var grpFine = new HeaderedContentControl { Header = "미세 조정" };
		var pnlFine = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		this._chkTextInsideExt.Content = "항상 연장선 사이에 텍스트 그리기";
		this._chkSuppressOutsideExt.Content = "화살촉 억제 (공간 부족 시)";
		pnlFine.Children.Add(this._chkTextInsideExt);
		pnlFine.Children.Add(this._chkSuppressOutsideExt);
		grpFine.Content = pnlFine;
		panel.Children.Add(grpFine);

		return panel;
	}

	// 5. 단위 탭
	private Control BuildUnitsTab()
	{
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };

		var grpLinear = new HeaderedContentControl { Header = "선형 차원" };
		var pnlLinear = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlLinear.Children.Add(CreateRow("단위 형식:", this._cboLinearUnitFormat));
		pnlLinear.Children.Add(CreateRow("정밀도:", this._cboLinearPrecision));
		pnlLinear.Children.Add(CreateRow("소수점 구분자:", this._cboDecimalSeparator));
		pnlLinear.Children.Add(CreateRow("반올림:", this._txtRounding));
		pnlLinear.Children.Add(CreateRow("접두사:", this._txtPrefix));
		pnlLinear.Children.Add(CreateRow("접미사:", this._txtSuffix));
		pnlLinear.Children.Add(CreateRow("축척 계수 (DIMLFAC):", this._txtLinearScaleFactor));

		var rowZero = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
		rowZero.Children.Add(new TextBlock { Text = "0 억제:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
		this._chkZeroLeading.Content = "선행";
		this._chkZeroTrailing.Content = "후행";
		rowZero.Children.Add(this._chkZeroLeading);
		rowZero.Children.Add(this._chkZeroTrailing);
		pnlLinear.Children.Add(rowZero);

		grpLinear.Content = pnlLinear;
		panel.Children.Add(grpLinear);

		var grpAng = new HeaderedContentControl { Header = "각도 차원" };
		var pnlAng = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		pnlAng.Children.Add(CreateRow("단위 형식:", this._cboAngularUnit));
		pnlAng.Children.Add(CreateRow("정밀도:", this._cboAngularPrecision));

		var rowAngZero = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
		rowAngZero.Children.Add(new TextBlock { Text = "0 억제:", Width = 70, VerticalAlignment = VerticalAlignment.Center });
		this._chkAngZeroLeading.Content = "선행";
		this._chkAngZeroTrailing.Content = "후행";
		rowAngZero.Children.Add(this._chkAngZeroLeading);
		rowAngZero.Children.Add(this._chkAngZeroTrailing);
		pnlAng.Children.Add(rowAngZero);

		grpAng.Content = pnlAng;
		panel.Children.Add(grpAng);

		return panel;
	}

	private Control BuildBottomBar()
	{
		var panel = new Grid
		{
			Height = 36,
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,8,Auto"),
			Margin = new Thickness(0, 8, 0, 0),
		};

		var btnHelp = new Button
		{
			Content = "?",
			Width = 26,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(btnHelp, 0);
		panel.Children.Add(btnHelp);

		var btnOk = new Button
		{
			Content = "확인(O)",
			Width = 84,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnOk.Click += (_, _) => this.ApplyAndClose();
		Grid.SetColumn(btnOk, 2);
		panel.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소(C)",
			Width = 84,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close();
		Grid.SetColumn(btnCancel, 4);
		panel.Children.Add(btnCancel);

		return panel;
	}

	private static Grid CreateRow(string label, Control control)
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("120,*"),
			Height = 26,
		};
		var tb = new TextBlock
		{
			Text = label,
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(tb, 0);
		grid.Children.Add(tb);

		StyleControl(control);
		Grid.SetColumn(control, 1);
		grid.Children.Add(control);
		return grid;
	}

	private static void StyleControl(Control c)
	{
		c.Height = 24;
		c.MinHeight = 0;
		if (c is TemplatedControl tc)
		{
			tc.FontSize = 12;
		}
		if (c is TextBox tb)
		{
			tb.VerticalContentAlignment = VerticalAlignment.Center;
			tb.Padding = new Thickness(4, 0);
		}
		else if (c is ComboBox cb)
		{
			cb.VerticalContentAlignment = VerticalAlignment.Center;
			cb.Padding = new Thickness(4, 0);
		}
	}

	private void PopulateFromWorkingStyle()
	{
		this._isUpdating = true;

		// 1. 색상 콤보 채우기
		var colorItems = new (string Name, AcadColor Color)[]
		{
			("BYBLOCK", AcadColor.ByBlock),
			("BYLAYER", AcadColor.ByLayer),
			("빨간색", new AcadColor(1)),
			("노란색", new AcadColor(2)),
			("초록색", new AcadColor(3)),
			("하늘색", new AcadColor(4)),
			("파란색", new AcadColor(5)),
			("자홍색", new AcadColor(6)),
			("흰색", new AcadColor(7)),
		};

		void InitColorCombo(ComboBox cb, AcadColor selected)
		{
			cb.ItemsSource = colorItems.Select(x => x.Name).ToList();
			int idx = Array.FindIndex(colorItems, x => x.Color.Index == selected.Index);
			cb.SelectedIndex = idx >= 0 ? idx : 0;
		}

		InitColorCombo(this._cboDimLineColor, this._workStyle.DimensionLineColor);
		InitColorCombo(this._cboExtLineColor, this._workStyle.ExtensionLineColor);
		InitColorCombo(this._cboTextColor, this._workStyle.TextColor);
		InitColorCombo(this._cboTextBgColor, this._workStyle.TextBackgroundColor);

		// 2. 선종류 콤보 채우기
		var ltNames = new List<string> { "BYBLOCK", "BYLAYER", "Continuous" };
		foreach (var lt in this._doc.Document.LineTypes)
		{
			if (!ltNames.Contains(lt.Name, StringComparer.OrdinalIgnoreCase))
			{
				ltNames.Add(lt.Name);
			}
		}

		void InitLtCombo(ComboBox cb, LineType? selected)
		{
			cb.ItemsSource = ltNames;
			string name = selected?.Name ?? "BYBLOCK";
			int idx = ltNames.FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
			cb.SelectedIndex = idx >= 0 ? idx : 0;
		}

		InitLtCombo(this._cboDimLineType, this._workStyle.LineType);
		InitLtCombo(this._cboExtLineType1, this._workStyle.LineTypeExt1);
		InitLtCombo(this._cboExtLineType2, this._workStyle.LineTypeExt2);

		// 3. 선가중치 콤보
		var lwNames = new List<string> { "BYBLOCK", "BYLAYER", "0.00mm", "0.05mm", "0.09mm", "0.13mm", "0.15mm", "0.18mm", "0.20mm", "0.25mm", "0.30mm", "0.35mm", "0.40mm", "0.50mm" };
		this._cboDimLineWeight.ItemsSource = lwNames;
		this._cboDimLineWeight.SelectedIndex = 0;
		this._cboExtLineWeight.ItemsSource = lwNames;
		this._cboExtLineWeight.SelectedIndex = 0;

		// 수치 필드들
		this._txtDimLineExt.Text = this._workStyle.DimensionLineExtension.ToString("F4", CultureInfo.InvariantCulture);
		this._txtDimLineInc.Text = this._workStyle.DimensionLineIncrement.ToString("F4", CultureInfo.InvariantCulture);
		this._chkDimLineSupp1.IsChecked = this._workStyle.SuppressFirstDimensionLine;
		this._chkDimLineSupp2.IsChecked = this._workStyle.SuppressSecondDimensionLine;

		this._chkExtLineSupp1.IsChecked = this._workStyle.SuppressFirstExtensionLine;
		this._chkExtLineSupp2.IsChecked = this._workStyle.SuppressSecondExtensionLine;
		this._txtExtLineExt.Text = this._workStyle.ExtensionLineExtension.ToString("F4", CultureInfo.InvariantCulture);
		this._txtExtLineOff.Text = this._workStyle.ExtensionLineOffset.ToString("F4", CultureInfo.InvariantCulture);
		this._chkExtLineFixed.IsChecked = this._workStyle.IsExtensionLineLengthFixed;
		this._txtExtLineFixedLen.Text = this._workStyle.FixedExtensionLineLength.ToString("F4", CultureInfo.InvariantCulture);

		// 화살촉
		var arrowOptions = new List<string> { "화살촉, 채움", "닫힘", "점", "건축 틱", "사선", "열림", "원점 표시", "직각", "없음" };
		this._cboArrow1.ItemsSource = arrowOptions;
		this._cboArrow1.SelectedIndex = 0;
		this._cboArrow2.ItemsSource = arrowOptions;
		this._cboArrow2.SelectedIndex = 0;
		this._cboArrowLeader.ItemsSource = arrowOptions;
		this._cboArrowLeader.SelectedIndex = 0;
		this._txtArrowSize.Text = this._workStyle.ArrowSize.ToString("F4", CultureInfo.InvariantCulture);
		this._chkEnableTick.IsChecked = this._workStyle.TickSize > 0;
		this._txtTickSize.Text = this._workStyle.TickSize.ToString("F4", CultureInfo.InvariantCulture);

		this._cboCenterMark.ItemsSource = new[] { "없음", "마크", "선" };
		this._cboCenterMark.SelectedIndex = this._workStyle.CenterMarkSize == 0 ? 0 : (this._workStyle.CenterMarkSize > 0 ? 1 : 2);
		this._txtCenterMarkSize.Text = Math.Abs(this._workStyle.CenterMarkSize).ToString("F4", CultureInfo.InvariantCulture);
		this._txtJogAngle.Text = (this._workStyle.JoggedRadiusDimensionTransverseSegmentAngle * 180.0 / Math.PI).ToString("F1", CultureInfo.InvariantCulture);

		// 문자
		var styleNames = this._doc.Document.TextStyles.Select(x => x.Name).ToList();
		this._cboTextStyle.ItemsSource = styleNames;
		this._cboTextStyle.SelectedItem = this._workStyle.Style?.Name ?? styleNames.FirstOrDefault();

		this._cboTextBgMode.ItemsSource = new[] { "배경 없음", "도면 배경", "지정 색상" };
		this._cboTextBgMode.SelectedIndex = (int)this._workStyle.TextBackgroundFillMode;
		this._txtTextHeight.Text = this._workStyle.TextHeight.ToString("F4", CultureInfo.InvariantCulture);

		this._cboTextVertAlign.ItemsSource = new[] { "중심", "위", "외부", "JIS" };
		this._cboTextVertAlign.SelectedIndex = Math.Clamp((int)this._workStyle.TextVerticalAlignment, 0, 3);
		this._txtTextGap.Text = Math.Abs(this._workStyle.DimensionLineGap).ToString("F4", CultureInfo.InvariantCulture);

		this._cboTextHorizAlign.ItemsSource = new[] { "연장 선 사이의 중심", "확장선 1", "확장선 2", "확장선 1 위", "확장선 2 위" };
		this._cboTextHorizAlign.SelectedIndex = Math.Clamp((int)this._workStyle.TextHorizontalAlignment, 0, 4);

		this._cboTextDirection.ItemsSource = new[] { "왼쪽에서 오른쪽으로", "오른쪽에서 왼쪽으로" };
		this._cboTextDirection.SelectedIndex = this._workStyle.TextDirection == TextDirection.RightToLeft ? 1 : 0;

		this._cboTextOutsideDir.ItemsSource = new[] { "수평", "치수선에 정렬" };
		this._cboTextOutsideDir.SelectedIndex = this._workStyle.TextOutsideHorizontal ? 0 : 1;

		this._cboTextInsideDir.ItemsSource = new[] { "수평", "치수선에 정렬" };
		this._cboTextInsideDir.SelectedIndex = this._workStyle.TextInsideHorizontal ? 0 : 1;

		this._chkDrawTextBorder.IsChecked = this._workStyle.DimensionLineGap < 0;

		// 형식
		this._cboFitType.ItemsSource = new[] { "문자와 화살표 모두", "화살표", "문자", "문자 또는 화살표 (최적 맞춤)" }; // DIMATFIT 0~3
		this._cboFitType.SelectedIndex = Math.Clamp((int)this._workStyle.DimensionTextArrowFit, 0, 3);

		this._cboTextMovement.ItemsSource = new[] { "치수선과 함께 이동", "지시선 추가", "지시선 없음" };
		this._cboTextMovement.SelectedIndex = Math.Clamp((int)this._workStyle.TextMovement, 0, 2);

		this._txtOverallScale.Text = this._workStyle.ScaleFactor.ToString("F4", CultureInfo.InvariantCulture);
		this._chkTextInsideExt.IsChecked = this._workStyle.TextInsideExtensions;
		this._chkSuppressOutsideExt.IsChecked = this._workStyle.SuppressOutsideExtensions;

		// 단위
		this._cboLinearUnitFormat.ItemsSource = new[] { "과학", "십진", "공학", "건축", "분수" };
		this._cboLinearUnitFormat.SelectedIndex = Math.Clamp((int)this._workStyle.LinearUnitFormat - 1, 0, 4);

		var precisions = new[] { "0", "0.0", "0.00", "0.000", "0.0000", "0.00000", "0.000000", "0.0000000", "0.00000000" };
		this._cboLinearPrecision.ItemsSource = precisions;
		this._cboLinearPrecision.SelectedIndex = Math.Clamp((int)this._workStyle.DecimalPlaces, 0, 8);

		this._cboDecimalSeparator.ItemsSource = new[] { "'.' (마침표)", "',' (쉼표)", "' ' (공백)" };
		this._cboDecimalSeparator.SelectedIndex = this._workStyle.DecimalSeparator == ',' ? 1 : (this._workStyle.DecimalSeparator == ' ' ? 2 : 0);

		this._txtRounding.Text = this._workStyle.Rounding.ToString("F4", CultureInfo.InvariantCulture);
		this._txtPrefix.Text = this._workStyle.Prefix;
		this._txtSuffix.Text = this._workStyle.Suffix;
		this._txtLinearScaleFactor.Text = this._workStyle.LinearScaleFactor.ToString("F4", CultureInfo.InvariantCulture);

		this._chkZeroLeading.IsChecked = this._workStyle.ZeroHandling.HasFlag(ZeroHandling.SuppressDecimalLeadingZeroes);
		this._chkZeroTrailing.IsChecked = this._workStyle.ZeroHandling.HasFlag(ZeroHandling.SuppressDecimalTrailingZeroes);

		this._cboAngularUnit.ItemsSource = new[] { "십진 각도", "도/분/초", "그레드", "라디안", "측량사" };
		this._cboAngularUnit.SelectedIndex = Math.Clamp((int)this._workStyle.AngularUnit, 0, 4);

		this._cboAngularPrecision.ItemsSource = new[] { "0", "0.0", "0.00", "0.000", "0.0000" };
		this._cboAngularPrecision.SelectedIndex = Math.Clamp((int)this._workStyle.AngularDecimalPlaces, 0, 4);

		this._chkAngZeroLeading.IsChecked = this._workStyle.AngularZeroHandling.HasFlag(AngularZeroHandling.SuppressLeadingZeroes);
		this._chkAngZeroTrailing.IsChecked = this._workStyle.AngularZeroHandling.HasFlag(AngularZeroHandling.SupressTrailingZeroes);

		// F2D: 화살촉(기존 CAD 이름)·선가중치는 스타일 값으로 (전에는 늘 첫 항목)
		var st = this._workStyle;
		var arrowNames = Free2DCad.Core.Styles.DimensionOverrides.Arrows.Select(a => a.Name).ToList();
		int ArrowIndex(ACadSharp.Tables.BlockRecord? b)
		{
			int i = arrowNames.IndexOf(Free2DCad.Core.Styles.DimensionOverrides.ArrowName(b));
			return i >= 0 ? i : 0;
		}
		this._chkSeparateArrows.IsChecked = st.SeparateArrowBlocks;
		this._cboArrow1.ItemsSource = arrowNames;
		this._cboArrow1.SelectedIndex = ArrowIndex(ACadSharp.Entities.Dimension.FirstArrowBlock(st));
		this._cboArrow2.ItemsSource = arrowNames;
		this._cboArrow2.SelectedIndex = ArrowIndex(ACadSharp.Entities.Dimension.SecondArrowBlock(st));
		this._cboArrowLeader.ItemsSource = arrowNames;
		this._cboArrowLeader.SelectedIndex = ArrowIndex(st.LeaderArrow);
		this._cboDimLineWeight.ItemsSource = LineWeightNames;
		this._cboDimLineWeight.SelectedIndex = Math.Max(0, Array.IndexOf(LineWeights, st.DimensionLineWeight));
		this._cboExtLineWeight.ItemsSource = LineWeightNames;
		this._cboExtLineWeight.SelectedIndex = Math.Max(0, Array.IndexOf(LineWeights, st.ExtensionLineWeight));

		this._isUpdating = false;
	}

	private void AttachEventHandlers()
	{
		Action onAnyChange = () =>
		{
			if (this._isUpdating) return;
			this.SyncToWorkingStyle();
			this.UpdatePreviews();
		};

		// 모든 입력 칸에 이벤트 연결
		TextBox[] textboxes =
		{
			this._txtDimLineExt, this._txtDimLineInc, this._txtExtLineExt, this._txtExtLineOff, this._txtExtLineFixedLen,
			this._txtArrowSize, this._txtTickSize, this._txtCenterMarkSize, this._txtJogAngle,
			this._txtTextHeight, this._txtTextGap, this._txtOverallScale,
			this._txtRounding, this._txtPrefix, this._txtSuffix, this._txtLinearScaleFactor
		};
		foreach (var tb in textboxes)
		{
			tb.TextChanged += (_, _) => onAnyChange();
		}

		CheckBox[] checkboxes =
		{
			this._chkDimLineSupp1, this._chkDimLineSupp2, this._chkExtLineSupp1, this._chkExtLineSupp2, this._chkExtLineFixed,
			this._chkSeparateArrows, this._chkEnableTick, this._chkDrawTextBorder,
			this._chkTextInsideExt, this._chkSuppressOutsideExt,
			this._chkZeroLeading, this._chkZeroTrailing, this._chkAngZeroLeading, this._chkAngZeroTrailing
		};
		foreach (var chk in checkboxes)
		{
			chk.IsCheckedChanged += (_, _) => onAnyChange();
		}

		ComboBox[] combos =
		{
			this._cboDimLineColor, this._cboDimLineType, this._cboDimLineWeight,
			this._cboExtLineColor, this._cboExtLineType1, this._cboExtLineType2, this._cboExtLineWeight,
			this._cboArrow1, this._cboArrow2, this._cboArrowLeader, this._cboCenterMark,
			this._cboTextStyle, this._cboTextColor, this._cboTextBgMode, this._cboTextBgColor,
			this._cboTextVertAlign, this._cboTextHorizAlign, this._cboTextDirection, this._cboTextOutsideDir, this._cboTextInsideDir,
			this._cboFitType, this._cboTextMovement,
			this._cboLinearUnitFormat, this._cboLinearPrecision, this._cboDecimalSeparator,
			this._cboAngularUnit, this._cboAngularPrecision
		};
		foreach (var cb in combos)
		{
			cb.SelectionChanged += (_, _) => onAnyChange();
		}
	}

	private void SyncToWorkingStyle()
	{
		// 색상 반영
		var colors = new[]
		{
			AcadColor.ByBlock, AcadColor.ByLayer, new AcadColor(1), new AcadColor(2),
			new AcadColor(3), new AcadColor(4), new AcadColor(5), new AcadColor(6), new AcadColor(7)
		};
		if (this._cboDimLineColor.SelectedIndex >= 0 && this._cboDimLineColor.SelectedIndex < colors.Length)
		{
			this._workStyle.DimensionLineColor = colors[this._cboDimLineColor.SelectedIndex];
		}
		if (this._cboExtLineColor.SelectedIndex >= 0 && this._cboExtLineColor.SelectedIndex < colors.Length)
		{
			this._workStyle.ExtensionLineColor = colors[this._cboExtLineColor.SelectedIndex];
		}
		if (this._cboTextColor.SelectedIndex >= 0 && this._cboTextColor.SelectedIndex < colors.Length)
		{
			this._workStyle.TextColor = colors[this._cboTextColor.SelectedIndex];
		}

		// 수치 반영
		if (double.TryParse(this._txtArrowSize.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double asz) && asz > 0)
		{
			this._workStyle.ArrowSize = asz;
		}
		if (double.TryParse(this._txtTextHeight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double th) && th > 0)
		{
			this._workStyle.TextHeight = th;
		}
		if (double.TryParse(this._txtOverallScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double sc) && sc > 0)
		{
			this._workStyle.ScaleFactor = sc;
		}
		if (double.TryParse(this._txtLinearScaleFactor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double lfac) && lfac != 0)
		{
			this._workStyle.LinearScaleFactor = lfac;
		}
		if (double.TryParse(this._txtRounding.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double rnd) && rnd >= 0)
		{
			this._workStyle.Rounding = rnd;
		}

		this._workStyle.DecimalPlaces = (short)Math.Clamp(this._cboLinearPrecision.SelectedIndex, 0, 8);
		this._workStyle.DecimalSeparator = this._cboDecimalSeparator.SelectedIndex == 1 ? ',' : (this._cboDecimalSeparator.SelectedIndex == 2 ? ' ' : '.');
		this._workStyle.Prefix = this._txtPrefix.Text ?? string.Empty;
		this._workStyle.Suffix = this._txtSuffix.Text ?? string.Empty;

		ZeroHandling zh = ZeroHandling.SuppressZeroFeetAndInches; // 0 = 억제 없음(십진 단위 기준)
		if (this._chkZeroLeading.IsChecked == true) zh |= ZeroHandling.SuppressDecimalLeadingZeroes;
		if (this._chkZeroTrailing.IsChecked == true) zh |= ZeroHandling.SuppressDecimalTrailingZeroes;
		this._workStyle.ZeroHandling = zh;

		this._workStyle.SuppressFirstDimensionLine = this._chkDimLineSupp1.IsChecked == true;
		this._workStyle.SuppressSecondDimensionLine = this._chkDimLineSupp2.IsChecked == true;
		this._workStyle.SuppressFirstExtensionLine = this._chkExtLineSupp1.IsChecked == true;
		this._workStyle.SuppressSecondExtensionLine = this._chkExtLineSupp2.IsChecked == true;

		this._workStyle.TextInsideHorizontal = this._cboTextInsideDir.SelectedIndex == 0;
		this._workStyle.TextOutsideHorizontal = this._cboTextOutsideDir.SelectedIndex == 0;

		this.SyncRemaining();
	}

	private static readonly ACadSharp.LineWeightType[] LineWeights =
	{
		ACadSharp.LineWeightType.ByBlock, ACadSharp.LineWeightType.ByLayer, ACadSharp.LineWeightType.W0, ACadSharp.LineWeightType.W5, ACadSharp.LineWeightType.W9,
		ACadSharp.LineWeightType.W13, ACadSharp.LineWeightType.W15, ACadSharp.LineWeightType.W18, ACadSharp.LineWeightType.W20, ACadSharp.LineWeightType.W25,
		ACadSharp.LineWeightType.W30, ACadSharp.LineWeightType.W35, ACadSharp.LineWeightType.W40, ACadSharp.LineWeightType.W50,
	};

	private static readonly string[] LineWeightNames = { "BYBLOCK", "BYLAYER", "0.00mm", "0.05mm", "0.09mm", "0.13mm", "0.15mm", "0.18mm", "0.20mm", "0.25mm", "0.30mm", "0.35mm", "0.40mm", "0.50mm" };

	/// <summary>
	/// F2D: 다른 탭의 칸을 작업 스타일로 (전에는 선·화살촉·문자·형식·단위 탭의 많은 칸이 옮겨지지 않아 "확인"해도 바뀌지 않았다).
	/// </summary>
	private void SyncRemaining()
	{
		var st = this._workStyle;
		var cad = this._doc.Document;
		static bool Num(TextBox tb, out double v) => double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

		if (Num(this._txtDimLineExt, out double dle) && dle >= 0) st.DimensionLineExtension = dle;
		if (Num(this._txtDimLineInc, out double dli) && dli >= 0) st.DimensionLineIncrement = dli;
		if (Num(this._txtExtLineExt, out double exe) && exe >= 0) st.ExtensionLineExtension = exe;
		if (Num(this._txtExtLineOff, out double exo) && exo >= 0) st.ExtensionLineOffset = exo;
		st.IsExtensionLineLengthFixed = this._chkExtLineFixed.IsChecked == true;
		if (Num(this._txtExtLineFixedLen, out double fxl) && fxl >= 0) st.FixedExtensionLineLength = fxl;

		ACadSharp.Tables.LineType? Lt(ComboBox cb)
		{
			if (cb.SelectedItem is not string name) return null;
			if (string.Equals(name, "BYBLOCK", StringComparison.OrdinalIgnoreCase)) name = "ByBlock";
			else if (string.Equals(name, "BYLAYER", StringComparison.OrdinalIgnoreCase)) name = "ByLayer";
			return cad.LineTypes.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
		}
		if (Lt(this._cboDimLineType) is { } lt0) st.LineType = lt0;
		if (Lt(this._cboExtLineType1) is { } lt1) st.LineTypeExt1 = lt1;
		if (Lt(this._cboExtLineType2) is { } lt2) st.LineTypeExt2 = lt2;
		if (this._cboDimLineWeight.SelectedIndex >= 0 && this._cboDimLineWeight.SelectedIndex < LineWeights.Length) st.DimensionLineWeight = LineWeights[this._cboDimLineWeight.SelectedIndex];
		if (this._cboExtLineWeight.SelectedIndex >= 0 && this._cboExtLineWeight.SelectedIndex < LineWeights.Length) st.ExtensionLineWeight = LineWeights[this._cboExtLineWeight.SelectedIndex];

		// 화살촉: 별도 허용이면 DIMBLK1/2, 아니면 DIMBLK (시작 화살촉 값)
		var arrows = Free2DCad.Core.Styles.DimensionOverrides.Arrows;
		ACadSharp.Tables.BlockRecord? Arrow(ComboBox cb) =>
			cb.SelectedIndex >= 0 && cb.SelectedIndex < arrows.Length ? Free2DCad.Core.Styles.DimensionOverrides.GetOrCreateArrow(cad, arrows[cb.SelectedIndex].Block) : null;
		bool separate = this._chkSeparateArrows.IsChecked == true;
		var a1 = Arrow(this._cboArrow1);
		var a2 = Arrow(this._cboArrow2);
		st.SeparateArrowBlocks = separate;
		st.ArrowBlock = a1;
		st.DimArrow1 = separate ? a1 : null;
		st.DimArrow2 = separate ? a2 : null;
		st.LeaderArrow = Arrow(this._cboArrowLeader);
		if (this._chkEnableTick.IsChecked == true)
		{
			if (Num(this._txtTickSize, out double tk) && tk >= 0) st.TickSize = tk;
		}
		else
		{
			st.TickSize = 0;
		}
		if (Num(this._txtCenterMarkSize, out double cm))
		{
			cm = Math.Abs(cm);
			st.CenterMarkSize = this._cboCenterMark.SelectedIndex switch { 0 => 0, 2 => -cm, _ => cm };
		}
		if (Num(this._txtJogAngle, out double jog) && jog > 0 && jog < 180) st.JoggedRadiusDimensionTransverseSegmentAngle = jog * Math.PI / 180;

		// 문자
		if (this._cboTextStyle.SelectedItem is string tsn && cad.TextStyles.TryGetValue(tsn, out var ts)) st.Style = ts;
		if (this._cboTextBgMode.SelectedIndex >= 0) st.TextBackgroundFillMode = (DimensionTextBackgroundFillMode)this._cboTextBgMode.SelectedIndex;
		var bgColors = new[] { AcadColor.ByBlock, AcadColor.ByLayer, new AcadColor(1), new AcadColor(2), new AcadColor(3), new AcadColor(4), new AcadColor(5), new AcadColor(6), new AcadColor(7) };
		if (this._cboTextBgColor.SelectedIndex >= 0 && this._cboTextBgColor.SelectedIndex < bgColors.Length) st.TextBackgroundColor = bgColors[this._cboTextBgColor.SelectedIndex];
		if (this._cboTextVertAlign.SelectedIndex >= 0) st.TextVerticalAlignment = (DimensionTextVerticalAlignment)this._cboTextVertAlign.SelectedIndex;
		if (this._cboTextHorizAlign.SelectedIndex >= 0) st.TextHorizontalAlignment = (DimensionTextHorizontalAlignment)this._cboTextHorizAlign.SelectedIndex;
		if (Num(this._txtTextGap, out double gap)) st.DimensionLineGap = Math.Abs(gap) * (this._chkDrawTextBorder.IsChecked == true ? -1 : 1);
		st.TextDirection = this._cboTextDirection.SelectedIndex == 1 ? TextDirection.RightToLeft : TextDirection.LeftToRight;

		// 형식(맞춤)
		if (this._cboFitType.SelectedIndex >= 0) st.DimensionTextArrowFit = (TextArrowFitType)this._cboFitType.SelectedIndex;
		if (this._cboTextMovement.SelectedIndex >= 0) st.TextMovement = (TextMovement)this._cboTextMovement.SelectedIndex;
		st.TextInsideExtensions = this._chkTextInsideExt.IsChecked == true;
		st.SuppressOutsideExtensions = this._chkSuppressOutsideExt.IsChecked == true;

		// 단위
		if (this._cboLinearUnitFormat.SelectedIndex >= 0) st.LinearUnitFormat = (LinearUnitFormat)(this._cboLinearUnitFormat.SelectedIndex + 1);
		if (this._cboAngularUnit.SelectedIndex >= 0) st.AngularUnit = (AngularUnitFormat)this._cboAngularUnit.SelectedIndex;
		if (this._cboAngularPrecision.SelectedIndex >= 0) st.AngularDecimalPlaces = (short)this._cboAngularPrecision.SelectedIndex;
		var az = AngularZeroHandling.DisplayAll;
		if (this._chkAngZeroLeading.IsChecked == true) az |= AngularZeroHandling.SuppressLeadingZeroes;
		if (this._chkAngZeroTrailing.IsChecked == true) az |= AngularZeroHandling.SupressTrailingZeroes;
		st.AngularZeroHandling = az;
	}

	/// <summary>스타일의 모든 DXF 값 (이름·플래그 제외) — 확인할 때 작업 스타일 → 실제 스타일로 전부 옮긴다.</summary>
	private static readonly System.Reflection.PropertyInfo[] StyleProperties = typeof(DimensionStyle)
		.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
		.Where(pi => pi.CanRead && pi.CanWrite && pi.GetIndexParameters().Length == 0
			&& System.Reflection.CustomAttributeExtensions.GetCustomAttributes(pi, typeof(ACadSharp.Attributes.DxfCodeValueAttribute), true).Any()
			&& pi.Name is not ("Name" or "Flags"))
		.ToArray();

	private static object?[] Snapshot(DimensionStyle st) => StyleProperties.Select(pi => pi.GetValue(st)).ToArray();

	private static void Restore(DimensionStyle st, object?[] values)
	{
		for (int i = 0; i < StyleProperties.Length; i++)
		{
			try
			{
				if (values[i] == null && !StyleProperties[i].PropertyType.IsValueType)
				{
					// 참조(문자 스타일 등)는 없음으로 못 바꾸는 것이 있어 건너뛴다 — 화살촉 블록 null(닫힌 채움)은 바꿀 수 있음
					if (StyleProperties[i].PropertyType != typeof(ACadSharp.Tables.BlockRecord)) continue;
				}
				StyleProperties[i].SetValue(st, values[i]);
			}
			catch (Exception)
			{
				// 바꿀 수 없는 값은 그대로
			}
		}
	}

	private void ApplyAndClose()
	{
		this.SyncToWorkingStyle();

		var orig = this._targetStyle;
		var oldValues = Snapshot(orig);
		var newValues = Snapshot(this._workStyle);
		var doc = this._doc;

		// 이 스타일을 쓰는 치수는 다시 그린다 (스타일만 바꾸면 화면의 치수 모양이 그대로였음)
		void Regenerate()
		{
			foreach (var dim in doc.Document.BlockRecords.SelectMany(br => br.Entities.OfType<ACadSharp.Entities.Dimension>()).Where(d => ReferenceEquals(d.Style, orig)).ToList())
			{
				try
				{
					dim.UpdateBlock();
				}
				catch (Exception)
				{
					// 다시 그리기 실패한 치수는 그대로
				}
			}
		}

		doc.Execute(new DelegateAction(
			$"치수 스타일 수정: {orig.Name}",
			() => { Restore(orig, newValues); Regenerate(); },
			() => { Restore(orig, oldValues); Regenerate(); }
		));

		this.Close(true);
	}

	// ===== Alternate Units·Tolerances 탭 (기존 CAD와 같은 항목, 바꾸면 바로 작업 스타일·미리보기에) =====

	private static readonly string[] UnitFormatNames = { "과학", "십진", "공학", "건축", "분수", "Windows 바탕화면" };
	private static readonly string[] PrecisionNames = { "0", "0.0", "0.00", "0.000", "0.0000", "0.00000", "0.000000", "0.0000000", "0.00000000" };

	private static ComboBox Combo(IReadOnlyList<string> items, int index)
	{
		var c = new ComboBox { ItemsSource = items, SelectedIndex = Math.Clamp(index, 0, items.Count - 1), HorizontalAlignment = HorizontalAlignment.Stretch };
		return c;
	}

	private static TextBox Num(double v) => new() { Text = v.ToString("0.0000", CultureInfo.InvariantCulture) };

	private void OnNum(TextBox tb, Action<double> set, Func<double, bool>? valid = null)
	{
		tb.TextChanged += (_, _) =>
		{
			if (this._isUpdating) return;
			if (double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && (valid == null || valid(v)))
			{
				set(v);
				this.UpdatePreviews();
			}
		};
	}

	private void OnPick(ComboBox cb, Action<int> set)
	{
		cb.SelectionChanged += (_, _) =>
		{
			if (this._isUpdating || cb.SelectedIndex < 0) return;
			set(cb.SelectedIndex);
			this.UpdatePreviews();
		};
	}

	private void OnCheck(CheckBox chk, Action<bool> set)
	{
		chk.IsCheckedChanged += (_, _) =>
		{
			if (this._isUpdating) return;
			set(chk.IsChecked == true);
			this.UpdatePreviews();
		};
	}

	/// <summary>0 억제 체크 네 개 (선행 4, 후행 8, 0 피트·0 인치 = 하위 2비트: 0 둘 다, 1 없음, 2 인치만, 3 피트만).</summary>
	private Control ZeroBox(string title, Func<ZeroHandling> get, Action<ZeroHandling> set, bool withLeadTrail = true)
	{
		var panel = new StackPanel { Spacing = 4 };
		int bits = (int)get();
		int low = bits & 3;
		var lead = new CheckBox { Content = "선행 제로", IsChecked = (bits & 4) != 0 };
		var trail = new CheckBox { Content = "후행 제로", IsChecked = (bits & 8) != 0 };
		var feet = new CheckBox { Content = "0 피트", IsChecked = low == 0 || low == 3 };
		var inch = new CheckBox { Content = "0 인치", IsChecked = low == 0 || low == 2 };
		void Update()
		{
			if (this._isUpdating) return;
			int b = 0;
			if (lead.IsChecked == true) b |= 4;
			if (trail.IsChecked == true) b |= 8;
			bool f = feet.IsChecked == true, i = inch.IsChecked == true;
			b |= f && i ? 0 : !f && !i ? 1 : i ? 2 : 3;
			set((ZeroHandling)b);
			this.UpdatePreviews();
		}
		foreach (var c in new[] { lead, trail, feet, inch }) c.IsCheckedChanged += (_, _) => Update();
		if (withLeadTrail)
		{
			var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
			row.Children.Add(lead);
			row.Children.Add(trail);
			panel.Children.Add(row);
		}
		var ft = new HeaderedContentControl { Header = "피트-인치" };
		var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(8, 2, 0, 2) };
		row2.Children.Add(feet);
		row2.Children.Add(inch);
		ft.Content = row2;
		panel.Children.Add(ft);
		return new HeaderedContentControl { Header = title, Content = panel };
	}

	private Control BuildAltUnitsTab()
	{
		var st = this._workStyle;
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };
		var chkAlt = new CheckBox { Content = "대체 단위 표시", IsChecked = st.AlternateUnitDimensioning };
		this.OnCheck(chkAlt, v => st.AlternateUnitDimensioning = v);
		panel.Children.Add(chkAlt);

		var grp = new HeaderedContentControl { Header = "대체 단위 설정" };
		var pnl = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		var cboFmt = Combo(UnitFormatNames, (int)st.AlternateUnitFormat - 1);
		this.OnPick(cboFmt, i => st.AlternateUnitFormat = (LinearUnitFormat)(i + 1));
		pnl.Children.Add(CreateRow("단위 형식:", cboFmt));
		var cboPrec = Combo(PrecisionNames, st.AlternateUnitDecimalPlaces);
		this.OnPick(cboPrec, i => st.AlternateUnitDecimalPlaces = (short)i);
		pnl.Children.Add(CreateRow("정밀도:", cboPrec));
		var txtMul = Num(st.AlternateUnitScaleFactor);
		this.OnNum(txtMul, v => st.AlternateUnitScaleFactor = v, v => Math.Abs(v) > 1e-12);
		pnl.Children.Add(CreateRow("대체 단위의 배수:", txtMul));
		var txtRnd = Num(st.AlternateUnitRounding);
		this.OnNum(txtRnd, v => st.AlternateUnitRounding = v, v => v >= 0);
		pnl.Children.Add(CreateRow("거리 반올림:", txtRnd));
		// 접두사·접미사는 DIMAPOST "앞[]뒤"
		string post = st.AlternateDimensioningSuffix ?? "";
		int at = post.IndexOf("[]", StringComparison.Ordinal);
		var txtPre = new TextBox { Text = at >= 0 ? post[..at] : "" };
		var txtSuf = new TextBox { Text = at >= 0 ? post[(at + 2)..] : post };
		void Post()
		{
			if (this._isUpdating) return;
			string p0 = txtPre.Text ?? "", s0 = txtSuf.Text ?? "";
			st.AlternateDimensioningSuffix = p0.Length == 0 ? s0 : p0 + "[]" + s0;
			this.UpdatePreviews();
		}
		txtPre.TextChanged += (_, _) => Post();
		txtSuf.TextChanged += (_, _) => Post();
		pnl.Children.Add(CreateRow("접두사:", txtPre));
		pnl.Children.Add(CreateRow("접미사:", txtSuf));
		var cboPlace = Combo(new[] { "기본 값 뒤", "기본 값 아래" }, 0);
		cboPlace.IsEnabled = false;
		pnl.Children.Add(CreateRow("배치:", cboPlace));
		grp.Content = pnl;
		panel.Children.Add(grp);
		panel.Children.Add(this.ZeroBox("제로 억제", () => st.AlternateUnitZeroHandling, v => st.AlternateUnitZeroHandling = v));

		var grpTol = new HeaderedContentControl { Header = "대체 허용오차" };
		var pnlTol = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		var cboTolPrec = Combo(PrecisionNames, st.AlternateUnitToleranceDecimalPlaces);
		this.OnPick(cboTolPrec, i => st.AlternateUnitToleranceDecimalPlaces = (short)i);
		pnlTol.Children.Add(CreateRow("정밀도:", cboTolPrec));
		pnlTol.Children.Add(this.ZeroBox("제로 억제", () => st.AlternateUnitToleranceZeroHandling, v => st.AlternateUnitToleranceZeroHandling = v));
		grpTol.Content = pnlTol;
		panel.Children.Add(grpTol);
		return new ScrollViewer { Content = panel };
	}

	private Control BuildTolerancesTab()
	{
		var st = this._workStyle;
		var panel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 6, 0) };
		var grp = new HeaderedContentControl { Header = "허용오차 형식" };
		var pnl = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 4) };
		// 방법: 없음 / 대칭 / 편차 / 한계 / 기본 (DIMTOL·DIMLIM, 대칭 = 하한이 상한과 같음, 기본 = DIMGAP 음수)
		var methods = new[] { "없음", "대칭", "편차", "한계", "기본" };
		int cur = st.LimitsGeneration ? 3 : st.GenerateTolerances ? (Math.Abs(st.MinusTolerance - st.PlusTolerance) < 1e-12 ? 1 : 2) : st.DimensionLineGap < 0 ? 4 : 0;
		var cboMethod = Combo(methods, cur);
		var txtUpper = Num(st.PlusTolerance);
		var txtLower = Num(st.MinusTolerance);
		this.OnPick(cboMethod, i =>
		{
			st.GenerateTolerances = i is 1 or 2;
			st.LimitsGeneration = i == 3;
			double gap = Math.Abs(st.DimensionLineGap);
			st.DimensionLineGap = i == 4 ? -gap : gap;
			if (i == 1)
			{
				st.MinusTolerance = st.PlusTolerance;
				this._isUpdating = true;
				txtLower.Text = st.MinusTolerance.ToString("0.0000", CultureInfo.InvariantCulture);
				this._isUpdating = false;
			}
		});
		pnl.Children.Add(CreateRow("방법:", cboMethod));
		var cboPrec = Combo(PrecisionNames, st.ToleranceDecimalPlaces);
		this.OnPick(cboPrec, i => st.ToleranceDecimalPlaces = (short)i);
		pnl.Children.Add(CreateRow("정밀도:", cboPrec));
		this.OnNum(txtUpper, v =>
		{
			st.PlusTolerance = v;
			if (cboMethod.SelectedIndex == 1) st.MinusTolerance = v; // 대칭
		});
		pnl.Children.Add(CreateRow("상한 허용 오차 한계:", txtUpper));
		this.OnNum(txtLower, v => st.MinusTolerance = v);
		pnl.Children.Add(CreateRow("하한 허용 오차 한계:", txtLower));
		var txtScale = Num(st.ToleranceScaleFactor);
		this.OnNum(txtScale, v => st.ToleranceScaleFactor = v, v => v > 0);
		pnl.Children.Add(CreateRow("높이 축척링:", txtScale));
		var cboPos = Combo(new[] { "아래", "중간", "위" }, (int)st.ToleranceAlignment);
		this.OnPick(cboPos, i => st.ToleranceAlignment = (ToleranceAlignment)i);
		pnl.Children.Add(CreateRow("수직 위치:", cboPos));
		var cboAlign = Combo(new[] { "연산 기호", "소수 구분 기호" }, 0);
		cboAlign.IsEnabled = false;
		pnl.Children.Add(CreateRow("허용 오차 정렬:", cboAlign));
		grp.Content = pnl;
		panel.Children.Add(grp);
		panel.Children.Add(this.ZeroBox("허용 오차 0 억제", () => st.ToleranceZeroHandling, v => st.ToleranceZeroHandling = v));
		return new ScrollViewer { Content = panel };
	}
}
