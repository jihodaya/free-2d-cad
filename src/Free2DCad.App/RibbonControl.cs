using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Tables;
using Color = ACadSharp.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 리본 컨트롤.
/// 90px 높이, 32px 큰 아이콘, 16px 작은 아이콘, 11px 글자, 세로 구분선 및 ▾ 패널 제목.
/// 그리기(큰 버튼 4 + 3x2 격자), 수정(큰 버튼 2 + 3x3 격자 + 3개 세로열),
/// 도면층(도면층 특성 + 전구/눈꽃/자물쇠/색상 드롭다운 + 2줄 10개 도구 아이콘),
/// 주석, 속성(색바퀴/선종류/선가중치 + 미리보기 콤보 3개 + ↘ 팔레트 버튼),
/// 유틸리티, 클립보드 패널을 완벽하게 제공한다.
/// </summary>
public sealed partial class RibbonControl : Border
{
	public event Action<string>? CommandRequested;
	public event Action? LayerManagerRequested;
	public event Action? PropertyPaletteToggleRequested;
	public event Action? CommandLineToggleRequested;
	public event Action? ExportPngRequested;
	public event Action? PlotRequested;
	public event Action<Layer>? CurrentLayerChanged;
	public event Action<Color>? ColorSelected;
	public event Action? LinetypeManagerRequested;
	public event Action<string>? LineTypeSelected;

	// 도면층 패널 액션 이벤트
	public event Action? SetCurrentLayerRequested;
	public event Action? PreviousLayerRequested;
	public event Action? LayerOffRequested;
	public event Action? LayerOnRequested;
	public event Action? LayerFreezeRequested;
	public event Action? LayerThawRequested;
	public event Action? LayerLockRequested;
	public event Action? LayerUnlockRequested;
	public event Action? MatchLayerRequested;
	public event Action? LayerDeleteRequested;

	private TabControl _tabs = null!;
	private readonly ComboBox _layerCombo = new();
	private readonly ComboBox _colorCombo = new();
	private readonly ComboBox _lineTypeCombo = new();
	private readonly ComboBox _lineWeightCombo = new();
	private readonly List<RibbonPanelRow> _panelRows = new();
	private bool _updatingCombos;

	public RibbonControl()
	{
		this.BorderBrush = CadTheme.RibbonPanelBorder;
		this.BorderThickness = new Thickness(0, 0, 0, 1);
		this.Background = CadTheme.RibbonBodyBg;

		var tabs = this._tabs = new TabControl
		{
			Margin = new Thickness(0),
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
		};

		tabs.Styles.Add(new Style(x => x.OfType<TabItem>())
		{
			Setters =
			{
				new Setter(TabItem.BackgroundProperty, CadTheme.RibbonTabStripBg),
				new Setter(TabItem.ForegroundProperty, CadTheme.RibbonTabInactiveFg),
				new Setter(TabItem.FontSizeProperty, 13.0),
				new Setter(TabItem.HeightProperty, 24.0),
				new Setter(TabItem.PaddingProperty, new Thickness(10, 2)),
			}
		});
		tabs.Styles.Add(new Style(x => x.OfType<TabItem>().Class(":selected"))
		{
			Setters =
			{
				new Setter(TabItem.BackgroundProperty, CadTheme.RibbonTabActiveBg),
				new Setter(TabItem.ForegroundProperty, CadTheme.RibbonTabActiveFg),
			}
		});

		tabs.Items.Add(CreateTabItem("홈", this.BuildHomeTab()));
		tabs.Items.Add(CreateTabItem("편집", this.BuildEditTab()));
		tabs.Items.Add(CreateTabItem("그리기", this.BuildDrawTab()));
		tabs.Items.Add(CreateTabItem("삽입", this.BuildInsertTab()));
		tabs.Items.Add(CreateTabItem("주석", this.BuildAnnotateTab()));
		tabs.Items.Add(CreateTabItem("보기", this.BuildViewTab()));
		tabs.Items.Add(CreateTabItem("출력", this.BuildOutputTab()));
		tabs.Items.Add(CreateTabItem("도구", this.BuildToolsTab()));
		tabs.Items.Add(CreateTabItem("도움말", this.BuildHelpTab()));

		var rootGrid = new Grid
		{
			RowDefinitions = new RowDefinitions("24,*"),
		};
		var headerBg = new Border { Background = CadTheme.RibbonTabStripBg };
		Grid.SetRow(headerBg, 0);
		rootGrid.Children.Add(headerBg);
		Grid.SetRow(tabs, 0);
		Grid.SetRowSpan(tabs, 2);
		rootGrid.Children.Add(tabs);

		this.Child = rootGrid;
	}

	private static TabItem CreateTabItem(string header, Control content) => new()
	{
		Header = header,
		Content = content,
	};

	// 1. 홈 탭 (기존 CAD 구성: 그리기·수정·도면층·주석·블록·속성·그룹·유틸리티·클립보드, 패널 제목 ▾ = 펼침 영역)
	private RibbonPanelRow BuildHomeTab()
	{
		var panel = this.CreatePanelRow();

		// 1) 그리기
		var draw = Row();
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Line, "선", "LINE"),
			(IconHelper.Ray, "광선", "RAY"),
			(IconHelper.XLine, "구성선", "XLINE"),
		}));
		draw.Children.Add(this.CreateBigButton(IconHelper.Polyline, "폴리선", "PLINE"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Circle, "중심, 반지름", "CIRCLE"),
			(IconHelper.CircleDiameter, "중심, 지름", "CIRCLE_D"),
			(IconHelper.Circle2P, "2점", "CIRCLE_2P"),
			(IconHelper.Circle3P, "3점", "CIRCLE_3P"),
			(IconHelper.CircleTtr, "접선, 접선, 반지름", "CIRCLE_TTR"),
			(IconHelper.CircleTtt, "접선, 접선, 접선", "CIRCLE_TTT"),
		}, "원"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Arc3P, "3점", "ARC"),
			(IconHelper.ArcSce, "시작점, 중심점, 끝점", "ARC_SCE"),
			(IconHelper.ArcSca, "시작점, 중심점, 각도", "ARC_SCA"),
			(IconHelper.ArcScl, "시작점, 중심점, 길이", "ARC_SCL"),
			(IconHelper.ArcSea, "시작점, 끝점, 각도", "ARC_SEA"),
			(IconHelper.ArcSed, "시작점, 끝점, 방향", "ARC_SED"),
			(IconHelper.ArcSer, "시작점, 끝점, 반지름", "ARC_SER"),
			(IconHelper.ArcSec, "시작점, 끝점, 중심점", "ARC_SEC"),
			(IconHelper.ArcCse, "중심점, 시작점, 끝점", "ARC_CSE"),
			(IconHelper.ArcCsa, "중심점, 시작점, 각도", "ARC_CSA"),
			(IconHelper.ArcCsl, "중심점, 시작점, 길이", "ARC_CSL"),
		}, "호"));
		draw.Children.Add(Column(
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.Rectang, "직사각형", "RECTANG"),
				(IconHelper.Polygon, "폴리곤 (중심-꼭짓점)", "POLYGON_I"),
				(IconHelper.PolygonCircum, "폴리곤 (중심-변)", "POLYGON_C"),
				(IconHelper.PolygonEdge, "폴리곤 (모서리)", "POLYGON_E"),
			}, showText: false),
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.EllipseArc, "타원형 호 (축, 끝점)", "ELLIPSE_A"),
				(IconHelper.EllipseArc, "타원형 호 (축, 회전)", "ELLIPSE_AR"),
				(IconHelper.EllipseArc, "타원형 호 (중심)", "ELLIPSE_AC"),
			}, showText: false),
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.Hatch, "해치", "HATCH"),
				(IconHelper.Gradient, "그라데이션", "GRADIENT"),
				(IconHelper.Boundary, "경계", "BOUNDARY"),
			}, showText: false)));
		var drawMore = Column(
			RowOf(
				this.CreateIconOnlyButton(IconHelper.Spline, "스플라인 맞춤 (SPLINE)", "SPLINE"),
				this.CreateIconOnlyButton(IconHelper.SplineCv, "스플라인 조정점 (SPLINE)", "SPLINE_CV"),
				this.CreateSmallSplitButton(new[]
				{
					(IconHelper.Ellipse, "타원 (축, 끝점)", "ELLIPSE"),
					(IconHelper.Ellipse, "타원 (축, 회전)", "ELLIPSE_R"),
					(IconHelper.EllipseCenter, "타원 (중심)", "ELLIPSE_C"),
					(IconHelper.EllipseCenter, "타원 (중심, 회전)", "ELLIPSE_CR"),
				}, showText: false)),
			RowOf(
				this.CreateIconOnlyButton(IconHelper.PointIcon, "다중 점 (POINT)", "POINT_M"),
				this.CreateIconOnlyButton(IconHelper.Donut, "도넛 (DONUT)", "DONUT"),
				this.CreateIconOnlyButton(IconHelper.RevCloud, "구름형 수정 기호 (REVCLOUD)", "REVCLOUD")),
			RowOf(
				this.CreateIconOnlyButton(IconHelper.BlockInsert, "블록 삽입 (INSERT)", "INSERT"),
				this.CreateIconOnlyButton(IconHelper.Plane, "2D 솔리드 (SOLID)", "SOLID"),
				this.CreateIconOnlyButton(IconHelper.Divide, "등분할 (DIVIDE)", "DIVIDE"),
				this.CreateIconOnlyButton(IconHelper.Measure, "길이 분할 (MEASURE)", "MEASURE")));
		panel.AddPanel(this.WrapInPanel("그리기", draw, drawMore));

		// 2) 수정
		var mod = Row();
		mod.Children.Add(this.CreateBigButton(IconHelper.Move, "이동", "MOVE"));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Copy, "복사", "COPY"),
			this.CreateSmallButton(IconHelper.Clone, "복제", "ADDSELECTED"),
			this.CreateSmallButton(IconHelper.Stretch, "신축", "STRETCH")));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Rotate, "회전", "ROTATE"),
			this.CreateSmallButton(IconHelper.Mirror, "대칭", "MIRROR"),
			this.CreateSmallButton(IconHelper.Scale, "축척", "SCALE")));
		mod.Children.Add(Column(
			this.CreateSmallSplitButton(new[] { (IconHelper.Trim, "자르기", "TRIM"), (IconHelper.Extend, "연장", "EXTEND") }),
			this.CreateSmallSplitButton(new[] { (IconHelper.Fillet, "모깎기", "FILLET"), (IconHelper.Chamfer, "모따기", "CHAMFER") }),
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.Array, "직사각형 배열", "ARRAYRECT"),
				(IconHelper.ArrayPolar, "원형 배열", "ARRAYPOLAR"),
				(IconHelper.ArrayPath, "경로 배열", "ARRAYPATH"),
			})));
		mod.Children.Add(Column(
			this.CreateIconOnlyButton(IconHelper.Erase, "지우기 (ERASE)", "ERASE"),
			this.CreateIconOnlyButton(IconHelper.Explode, "분해 (EXPLODE)", "EXPLODE"),
			this.CreateIconOnlyButton(IconHelper.Offset, "간격띄우기 (OFFSET)", "OFFSET")));
		var modMore = Column(
			RowOf(
				this.CreateIconOnlyButton(IconHelper.SetByLayer, "도면층별로 설정 (SETBYLAYER)", "SETBYLAYER"),
				this.CreateIconOnlyButton(IconHelper.Lengthen, "길이 조정 (LENGTHEN)", "LENGTHEN"),
				this.CreateIconOnlyButton(IconHelper.EditPline, "폴리선 편집 (PEDIT)", "PEDIT"),
				this.CreateIconOnlyButton(IconHelper.MatchProp, "특성 일치 (MATCHPROP)", "MATCHPROP")),
			RowOf(
				this.CreateIconOnlyButton(IconHelper.Align, "정렬 (ALIGN)", "ALIGN"),
				this.CreateIconOnlyButton(IconHelper.Break, "끊기 (BREAK)", "BREAK"),
				this.CreateIconOnlyButton(IconHelper.BreakAtPoint, "점에서 끊기 (BREAKATPOINT)", "BREAKATPOINT"),
				this.CreateIconOnlyButton(IconHelper.Join, "결합 (JOIN)", "JOIN"),
				this.CreateIconOnlyButton(IconHelper.Reverse, "반전 (REVERSE)", "REVERSE"),
				this.CreateSmallSplitButton(new[]
				{
					(IconHelper.DrawFront, "맨 앞으로 가져오기", "DRAWORDER_F"),
					(IconHelper.DrawBack, "맨 뒤로 보내기", "DRAWORDER_B"),
					(IconHelper.DrawAbove, "객체 앞으로 가져오기", "DRAWORDER_A"),
					(IconHelper.DrawUnder, "객체 뒤로 보내기", "DRAWORDER_U"),
					(IconHelper.HatchBack, "해치를 뒤로 보내기", "HATCHTOBACK"),
					(IconHelper.TextFront, "문자를 앞으로 가져오기", "TEXTTOFRONT_T"),
					(IconHelper.TextFront, "지시선을 앞으로 가져오기", "TEXTTOFRONT_L"),
					(IconHelper.TextFront, "치수를 앞으로 가져오기", "TEXTTOFRONT_D"),
					(IconHelper.TextFront, "모든 주석을 앞으로 가져오기", "TEXTTOFRONT_A"),
				}, showText: false)));
		panel.AddPanel(this.WrapInPanel("수정", mod, modMore));

		// 3) 도면층
		var layerContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
		layerContent.Children.Add(this.CreateBigButton(IconHelper.Layer, "도면층 특성", "LAYER_MANAGER"));
		var layerSubStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
		this.InitLayerCombo();
		layerSubStack.Children.Add(this._layerCombo);
		var layerTools = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
		layerTools.Children.Add(RowOf(
			this.CreateIconOnlyButton(IconHelper.LayerMakeCurrent, "객체의 도면층을 현재로 설정 (LAYMCUR)", "LAYMCUR"),
			this.CreateIconOnlyButton(IconHelper.LayerIsolate, "도면층 격리 (LAYISO)", "LAYISO"),
			this.CreateIconOnlyButton(IconHelper.LayerLock, "도면층 잠금 (LAYLCK)", "LAYLCK"),
			this.CreateIconOnlyButton(IconHelper.LayerOn, "모든 도면층 켜기 (LAYON)", "LAYON"),
			this.CreateIconOnlyButton(IconHelper.LayerFreeze, "도면층 동결 (LAYFRZ)", "LAYFRZ"),
			this.CreateSmallButton(IconHelper.MatchLayer, "도면층 일치", "LAYMCH")));
		layerTools.Children.Add(RowOf(
			this.CreateIconOnlyButton(IconHelper.LayerChangeCurrent, "현재 도면층으로 변경 (LAYCUR)", "LAYCUR"),
			this.CreateIconOnlyButton(IconHelper.LayerState, "도면층 격리 해제 (LAYUNISO)", "LAYUNISO"),
			this.CreateIconOnlyButton(IconHelper.LayerUnlock, "도면층 잠금 해제 (LAYULK)", "LAYULK"),
			this.CreateIconOnlyButton(IconHelper.LayerOff, "도면층 끄기 (LAYOFF)", "LAYOFF"),
			this.CreateIconOnlyButton(IconHelper.LayerThaw, "모든 도면층 동결 해제 (LAYTHW)", "LAYTHW"),
			this.CreateSmallButton(IconHelper.LayerDelete, "도면층 삭제", "LAYDEL")));
		layerSubStack.Children.Add(layerTools);
		layerContent.Children.Add(layerSubStack);
		var layerMore = Column(
			this.CreateSmallButton(IconHelper.LayerPrevious, "이전 도면층 상태", "LAYERP"),
			this.CreateSmallButton(IconHelper.SetByLayer, "도면층별로 설정", "SETBYLAYER"));
		panel.AddPanel(this.WrapInPanel("도면층", layerContent, layerMore));

		// 4) 주석: 문자▾ + 선형(큰 아이콘)▾ + 중심선▾ / 지시선▾ / 테이블
		var anno = Row();
		anno.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.MText, "여러 줄 문자", "MTEXT"),
			(IconHelper.Text, "단일 행 문자", "TEXT"),
			(IconHelper.Txt2Mtxt, "문자를 여러 줄 문자로", "TXT2MTXT"),
			(IconHelper.TextEdit, "문자 편집", "TEXTEDIT"),
			(IconHelper.TextStyle, "문자 스타일 관리자", "STYLE"),
		}, "문자"));
		anno.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.DimLinear, "선형", "DIMLINEAR"),
			(IconHelper.DimAligned, "정렬", "DIMALIGNED"),
			(IconHelper.DimAngular, "각도", "DIMANGULAR"),
			(IconHelper.DimArc, "호 길이", "DIMARC"),
			(IconHelper.DimOrdinate, "세로좌표", "DIMORDINATE"),
			(IconHelper.DimBaseline, "기준선", "DIMBASELINE"),
			(IconHelper.DimContinue, "연속", "DIMCONTINUE"),
		}, "선형"));
		anno.Children.Add(Column(
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.DimCenter, "중심 표시", "DIMCENTER"),
				(IconHelper.DimDiameter, "지름", "DIMDIAMETER"),
				(IconHelper.DimRadius, "반지름", "DIMRADIUS"),
			}),
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.Leader, "지시선", "MLEADER"),
				(IconHelper.Leader, "빠른 지시선", "QLEADER"),
			}),
			this.CreateSmallButton(IconHelper.Table, "테이블", "TABLE")));
		anno.Children.Add(Column(
			this.CreateIconOnlyButton(IconHelper.RevCloud, "구름형 수정 기호 (REVCLOUD)", "REVCLOUD"),
			this.CreateIconOnlyButton(IconHelper.TableOut, "표 추출 (TABLEOUT)", "TABLEOUT"),
			this.CreateIconOnlyButton(IconHelper.DimStyle, "치수 스타일 (DIMSTYLE)", "DIMSTYLE")));
		var annoMore = Column(
			this.CreateSmallButton(IconHelper.TextStyle, "문자 스타일", "STYLE"),
			this.CreateSmallButton(IconHelper.DimStyle, "치수 스타일", "DIMSTYLE"),
			this.CreateSmallButton(IconHelper.Table, "표를 CSV로 내보내기", "TABLEEXPORT"));
		panel.AddPanel(this.WrapInPanel("주석", anno, annoMore));

		// 5) 블록
		var block = Row();
		block.Children.Add(this.CreateBigButton(IconHelper.BlockInsert, "삽입", "INSERT"));
		block.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.EditAttribute, "단일", "EATTEDIT"),
			(IconHelper.EditAttribute, "다중", "-ATTEDIT"),
		}, "속성 편집"));
		block.Children.Add(Column(
			this.CreateSmallButton(IconHelper.BlockCreate, "작성", "BLOCK"),
			this.CreateSmallButton(IconHelper.BlockEditor, "블록 편집기", "BEDIT")));
		var blockMore = Column(
			RowOf(this.CreateSmallButton(IconHelper.AttDef, "속성 정의", "ATTDEF"), this.CreateSmallButton(IconHelper.AttSync, "속성 동기화", "ATTSYNC")),
			RowOf(this.CreateSmallSplitButton(new[]
			{
				(IconHelper.AttDisp, "속성 표시: 일반", "ATTDISP_N"),
				(IconHelper.AttDisp, "속성 표시: 모두 켜기", "ATTDISP_ON"),
				(IconHelper.AttDisp, "속성 표시: 모두 끄기", "ATTDISP_OFF"),
			}), this.CreateSmallButton(IconHelper.BasePoint, "기준점", "BASE")));
		panel.AddPanel(this.WrapInPanel("블록", block, blockMore));

		// 6) 속성
		var propPanelRoot = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(2, 0),
		};
		var propLeftIcons = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
		propLeftIcons.Children.Add(IconHelper.GetIcon(IconHelper.ColorWheel, 16));
		propLeftIcons.Children.Add(IconHelper.GetIcon(IconHelper.Linetype, 16));
		propLeftIcons.Children.Add(IconHelper.GetIcon(IconHelper.Lineweight, 16));
		Grid.SetColumn(propLeftIcons, 0);
		propPanelRoot.Children.Add(propLeftIcons);
		var propStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
		this.InitPropertyCombos();
		propStack.Children.Add(this._colorCombo);
		propStack.Children.Add(this._lineTypeCombo);
		propStack.Children.Add(this._lineWeightCombo);
		Grid.SetColumn(propStack, 1);
		propPanelRoot.Children.Add(propStack);
		var btnOpenPalette = new Button
		{
			Content = IconHelper.GetIcon(IconHelper.CornerArrow, 11),
			Width = 14,
			Height = 14,
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Bottom,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(2, 0, 0, 1),
			[ToolTip.TipProperty] = "속성 팔레트 (Ctrl+1)",
		};
		btnOpenPalette.Click += (_, _) =>
		{
			this.CloseAllPopups();
			this.PropertyPaletteToggleRequested?.Invoke();
		};
		Grid.SetColumn(btnOpenPalette, 2);
		propPanelRoot.Children.Add(btnOpenPalette);
		var propMore = Column(
			this.CreateSmallButton(IconHelper.List, "객체 정보 목록", "LIST"),
			this.CreateSmallButton(IconHelper.Properties, "속성 팔레트", "TOGGLE_PROPERTY"));
		panel.AddPanel(this.WrapInPanel("속성", propPanelRoot, propMore));

		// 7) 그룹
		var grp = Row();
		grp.Children.Add(this.CreateBigButton(IconHelper.Group, "그룹", "GROUP"));
		grp.Children.Add(Column(
			this.CreateSmallButton(IconHelper.QGroup, "빠른 그룹", "QGROUP"),
			this.CreateSmallButton(IconHelper.Ungroup, "그룹 해제", "UNGROUP")));
		var grpMore = Column(this.CreateSmallButton(IconHelper.GroupSelect, "그룹 선택 켜기/끄기", "GROUP_SELECT_TOGGLE"));
		panel.AddPanel(this.WrapInPanel("그룹", grp, grpMore));

		// 8) 유틸리티
		var util = Row();
		util.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Dist, "거리", "DIST"),
			(IconHelper.Area, "면적", "AREA"),
			(IconHelper.Measure, "길이 분할", "MEASURE"),
			(IconHelper.Divide, "등분할", "DIVIDE"),
		}, "측정"));
		util.Children.Add(Column(
			this.CreateSmallButton(IconHelper.QuickSelect, "빠른 선택", "QSELECT"),
			this.CreateSmallButton(IconHelper.SelectAll, "모두 선택", "SELECT_ALL"),
			this.CreateSmallButton(IconHelper.Settings, "제도 설정", "DSETTINGS")));
		var utilMore = Column(
			this.CreateSmallButton(IconHelper.Id, "ID 좌표", "ID"),
			this.CreateSmallButton(IconHelper.PointStyle, "점 스타일", "PTYPE"));
		panel.AddPanel(this.WrapInPanel("유틸리티", util, utilMore));

		// 9) 클립보드
		var clip = Row();
		clip.Children.Add(this.CreateBigButton(IconHelper.Paste, "붙여넣기", "PASTECLIP"));
		clip.Children.Add(Column(
			this.CreateIconOnlyButton(IconHelper.Cut, "잘라내기 (CUTCLIP)", "CUTCLIP"),
			this.CreateIconOnlyButton(IconHelper.Copy, "클립보드로 복사 (COPYCLIP)", "COPYCLIP"),
			this.CreateIconOnlyButton(IconHelper.MatchProp, "특성 일치 (MATCHPROP)", "MATCHPROP")));
		panel.AddPanel(this.WrapInPanel("클립보드", clip));

		return panel;
	}

	private static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 2 };

	private static StackPanel RowOf(params Control[] items)
	{
		var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
		foreach (var c in items) sp.Children.Add(c);
		return sp;
	}

	private static StackPanel Column(params Control[] items)
	{
		var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
		foreach (var c in items) sp.Children.Add(c);
		return sp;
	}

	private void InitLayerCombo()
	{
		this._layerCombo.Width = 230;
		this._layerCombo.Height = 24;
		this._layerCombo.FontSize = 12;
		this._layerCombo.Padding = new Thickness(4, 0);
		this.StyleDropdown(this._layerCombo);
		this._layerCombo.ItemTemplate = new FuncDataTemplate<LayerItem>((item, _) =>
		{
			// 콤보가 아직 선택 항목이 없을 때 null로 호출됨
			if (item == null)
			{
				return new TextBlock();
			}
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			sp.Children.Add(IconHelper.GetIcon(item.IsOn ? IconHelper.BulbOn : IconHelper.BulbOff, 12));
			sp.Children.Add(IconHelper.GetIcon(item.IsFrozen ? IconHelper.Snowflake : IconHelper.Sun, 12));
			sp.Children.Add(IconHelper.GetIcon(item.IsLocked ? IconHelper.Lock : IconHelper.Unlock, 12));
			sp.Children.Add(new Border
			{
				Width = 10,
				Height = 10,
				Background = item.ColorBrush,
				BorderBrush = Brushes.Gray,
				BorderThickness = new Thickness(1),
				VerticalAlignment = VerticalAlignment.Center,
			});
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, Foreground = CadTheme.DropdownFg, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});
		this._layerCombo.SelectionChanged += (_, _) =>
		{
			if (this._updatingCombos) return;
			if (this._layerCombo.SelectedItem is LayerItem item)
			{
				this.CurrentLayerChanged?.Invoke(item.Layer);
			}
		};
	}

	/// <summary>
	/// 큰 분할 버튼 (기존 CAD): 위쪽 아이콘을 누르면 마지막으로 고른 항목 실행, 아래 글자▾를 누르면 목록.
	/// 목록에서 고른 항목이 버튼의 기본 항목이 된다. title을 주면 글자는 고정 (예: "원"), 없으면 항목 이름.
	/// </summary>
	private Button CreateBigSplitButton((string Icon, string Label, string Cmd)[] items, string? title = null)
	{
		int current = 0;
		var btn = new Button
		{
			Padding = new Thickness(4, 2),
			Margin = new Thickness(4, 0),
			MinWidth = 54,
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		var iconHost = new ContentControl { Content = IconHelper.GetIcon(items[0].Icon, 32), HorizontalAlignment = HorizontalAlignment.Center };
		var label = new TextBlock { Text = title ?? items[0].Label, FontSize = 12, Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
		var textRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
		textRow.Children.Add(label);
		textRow.Children.Add(new TextBlock { Text = "▾", FontSize = 9, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center });
		var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
		stack.Children.Add(iconHost);
		stack.Children.Add(textRow);
		btn.Content = stack;

		void SetCurrent(int i)
		{
			current = i;
			iconHost.Content = IconHelper.GetIcon(items[i].Icon, 32);
			if (title == null) label.Text = items[i].Label;
			ToolTip.SetTip(btn, $"{(title != null ? title + " - " : "")}{items[i].Label} ({items[i].Cmd})");
		}
		SetCurrent(0);

		var menu = this.CreateItemsMenu(items, i =>
		{
			SetCurrent(i);
			this.HandleCommand(items[i].Cmd);
		});
		btn.ContextMenu = menu;
		double pressY = 0;
		btn.AddHandler(InputElement.PointerPressedEvent, (_, e) => pressY = e.GetPosition(btn).Y, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		btn.Click += (_, _) =>
		{
			// 아래쪽(글자▾)은 목록, 위쪽(아이콘)은 현재 항목 실행
			if (pressY > btn.Bounds.Height * 0.58)
			{
				menu.Open(btn);
			}
			else
			{
				this.HandleCommand(items[current].Cmd);
			}
		};
		btn.Tag = new RibbonBigButtonInfo(btn, textRow);
		return btn;
	}

	/// <summary>작은 분할 버튼: 아이콘(+글자)을 누르면 현재 항목, 오른쪽 ▾를 누르면 목록.</summary>
	private Button CreateSmallSplitButton((string Icon, string Label, string Cmd)[] items, bool showText = true)
	{
		int current = 0;
		var btn = new Button
		{
			Padding = new Thickness(showText ? 4 : 2, 1),
			Margin = new Thickness(showText ? 2 : 1, 0),
			Background = Brushes.Transparent,
			Height = 22,
		};
		var iconHost = new ContentControl { Content = IconHelper.GetIcon(items[0].Icon, 16), VerticalAlignment = VerticalAlignment.Center };
		var label = new TextBlock { Text = items[0].Label, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, IsVisible = showText };
		var arrow = new TextBlock { Text = "▾", FontSize = 9, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0) };
		var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = showText ? 6 : 1, VerticalAlignment = VerticalAlignment.Center };
		stack.Children.Add(iconHost);
		stack.Children.Add(label);
		stack.Children.Add(arrow);
		btn.Content = stack;

		void SetCurrent(int i)
		{
			current = i;
			iconHost.Content = IconHelper.GetIcon(items[i].Icon, 16);
			label.Text = items[i].Label;
			ToolTip.SetTip(btn, $"{items[i].Label} ({items[i].Cmd})");
		}
		SetCurrent(0);

		var menu = this.CreateItemsMenu(items, i =>
		{
			SetCurrent(i);
			this.HandleCommand(items[i].Cmd);
		});
		btn.ContextMenu = menu;
		double pressX = 0;
		btn.AddHandler(InputElement.PointerPressedEvent, (_, e) => pressX = e.GetPosition(btn).X, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		btn.Click += (_, _) =>
		{
			if (pressX > btn.Bounds.Width - 13)
			{
				menu.Open(btn);
			}
			else
			{
				this.HandleCommand(items[current].Cmd);
			}
		};
		if (showText)
		{
			btn.Tag = new RibbonSmallButtonInfo(btn, label, null);
		}
		return btn;
	}

	private ContextMenu CreateItemsMenu((string Icon, string Label, string Cmd)[] items, Action<int> chosen)
	{
		var menu = new ContextMenu();
		for (int i = 0; i < items.Length; i++)
		{
			int idx = i;
			var mi = new MenuItem { Header = items[i].Label, Icon = IconHelper.GetIcon(items[i].Icon, 16) };
			mi.Click += (_, _) => chosen(idx);
			menu.Items.Add(mi);
		}
		return menu;
	}

	// 2. 편집 탭 (기존 CAD: 수정 · 선택 · 그리기 순서)
	private RibbonPanelRow BuildEditTab()
	{
		var panel = this.CreatePanelRow();

		var mod = Row();
		mod.Children.Add(this.CreateBigButton(IconHelper.MatchProp, "특성 일치", "MATCHPROP"));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Move, "이동", "MOVE"),
			this.CreateSmallButton(IconHelper.Copy, "복사", "COPY"),
			this.CreateSmallButton(IconHelper.Clone, "복제", "ADDSELECTED")));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Cut, "잘라내기", "CUTCLIP"),
			this.CreateSmallButton(IconHelper.Paste, "붙여넣기", "PASTECLIP"),
			this.CreateSmallButton(IconHelper.Offset, "간격띄우기", "OFFSET")));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Scale, "축척", "SCALE"),
			this.CreateSmallButton(IconHelper.Undo, "명령 취소", "UNDO"),
			this.CreateSmallButton(IconHelper.Redo, "명령 복구", "REDO")));
		mod.Children.Add(this.CreateBigButton(IconHelper.Erase, "지우기", "ERASE"));
		mod.Children.Add(this.CreateBigButton(IconHelper.Rotate, "회전", "ROTATE"));
		mod.Children.Add(this.CreateBigButton(IconHelper.Mirror, "대칭", "MIRROR"));
		mod.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Array, "직사각형 배열", "ARRAYRECT"),
			(IconHelper.ArrayPolar, "원형 배열", "ARRAYPOLAR"),
			(IconHelper.ArrayPath, "경로 배열", "ARRAYPATH"),
			(IconHelper.Array, "배열 편집", "ARRAYEDIT"),
		}, "배열"));
		mod.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Break, "끊기", "BREAK"),
			(IconHelper.BreakAtPoint, "점에서 끊기", "BREAKATPOINT"),
			(IconHelper.Join, "결합", "JOIN"),
			(IconHelper.Trim, "자르기", "TRIM"),
			(IconHelper.Reverse, "반전", "REVERSE"),
		}, "끊기"));
		mod.Children.Add(Column(
			this.CreateSmallSplitButton(new[] { (IconHelper.Extend, "연장", "EXTEND"), (IconHelper.Lengthen, "길이 조정", "LENGTHEN"), (IconHelper.Stretch, "신축", "STRETCH") }),
			this.CreateSmallSplitButton(new[] { (IconHelper.Measure, "길이 분할", "MEASURE"), (IconHelper.Divide, "등분할", "DIVIDE") }),
			this.CreateSmallSplitButton(new[] { (IconHelper.Fillet, "모깎기", "FILLET"), (IconHelper.Chamfer, "모따기", "CHAMFER") })));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.EditPline, "폴리선 편집", "PEDIT"),
			this.CreateSmallButton(IconHelper.Spline, "스플라인 편집", "SPLINEDIT"),
			this.CreateSmallButton(IconHelper.Align, "정렬", "ALIGN")));
		mod.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Explode, "분해", "EXPLODE"),
			this.CreateSmallButton(IconHelper.SetByLayer, "도면층별로", "SETBYLAYER"),
			this.CreateSmallButton(IconHelper.Txt2Mtxt, "문자 합치기", "TXT2MTXT")));
		panel.AddPanel(this.WrapInPanel("수정", mod));

		var sel = Row();
		sel.Children.Add(this.CreateBigButton(IconHelper.QuickSelect, "빠른 선택", "QSELECT"));
		sel.Children.Add(Column(
			this.CreateSmallButton(IconHelper.SelectAll, "모두 선택", "SELECT_ALL"),
			this.CreateSmallButton(IconHelper.PickObject, "유사 선택", "SELECTSIMILAR"),
			this.CreateSmallButton(IconHelper.GroupSelect, "그룹 선택", "GROUP_SELECT_TOGGLE")));
		panel.AddPanel(this.WrapInPanel("선택", sel));

		var order = Row();
		order.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.DrawFront, "맨 앞으로", "DRAWORDER_F"),
			(IconHelper.DrawBack, "맨 뒤로", "DRAWORDER_B"),
			(IconHelper.DrawAbove, "객체 앞으로", "DRAWORDER_A"),
			(IconHelper.DrawUnder, "객체 뒤로", "DRAWORDER_U"),
		}));
		order.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.HatchBack, "해치를 뒤로", "HATCHTOBACK"),
			(IconHelper.TextFront, "문자를 앞으로", "TEXTTOFRONT_T"),
			(IconHelper.TextFront, "지시선을 앞으로", "TEXTTOFRONT_L"),
			(IconHelper.TextFront, "치수를 앞으로", "TEXTTOFRONT_D"),
			(IconHelper.TextFront, "모든 주석을 앞으로", "TEXTTOFRONT_A"),
		}));
		panel.AddPanel(this.WrapInPanel("그리기 순서", order));

		var clip = Row();
		clip.Children.Add(this.CreateBigButton(IconHelper.Paste, "붙여넣기", "PASTECLIP"));
		clip.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Cut, "잘라내기", "CUTCLIP"),
			this.CreateSmallButton(IconHelper.Copy, "복사", "COPYCLIP")));
		panel.AddPanel(this.WrapInPanel("클립보드", clip));
		return panel;
	}

	// 3. 그리기 탭 (기존 CAD: 그리기 · 객체 스냅 · 설정)
	private RibbonPanelRow BuildDrawTab()
	{
		var panel = this.CreatePanelRow();
		var draw = Row();
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Line, "선", "LINE"),
			(IconHelper.Ray, "광선", "RAY"),
			(IconHelper.XLine, "구성선", "XLINE"),
		}));
		draw.Children.Add(this.CreateBigButton(IconHelper.Polyline, "폴리선", "PLINE"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.XLine, "수평", "XLINE_H"),
			(IconHelper.XLine, "수직", "XLINE_V"),
			(IconHelper.XLine, "각도", "XLINE_A"),
			(IconHelper.XLine, "이등분", "XLINE_B"),
		}, "구성선"));
		draw.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Polyline, "다중선", "MLINE"),
			this.CreateSmallSplitButton(new[] { (IconHelper.Spline, "스플라인 맞춤", "SPLINE"), (IconHelper.SplineCv, "스플라인 조정점", "SPLINE_CV") }),
			this.CreateSmallButton(IconHelper.PointIcon, "다중 점", "POINT_M")));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Circle, "중심, 반지름", "CIRCLE"),
			(IconHelper.CircleDiameter, "중심, 지름", "CIRCLE_D"),
			(IconHelper.Circle2P, "2점", "CIRCLE_2P"),
			(IconHelper.Circle3P, "3점", "CIRCLE_3P"),
			(IconHelper.CircleTtr, "접선, 접선, 반지름", "CIRCLE_TTR"),
			(IconHelper.CircleTtt, "접선, 접선, 접선", "CIRCLE_TTT"),
		}, "원"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Arc3P, "3점", "ARC"),
			(IconHelper.ArcSce, "시작점, 중심점, 끝점", "ARC_SCE"),
			(IconHelper.ArcSca, "시작점, 중심점, 각도", "ARC_SCA"),
			(IconHelper.ArcScl, "시작점, 중심점, 길이", "ARC_SCL"),
			(IconHelper.ArcSea, "시작점, 끝점, 각도", "ARC_SEA"),
			(IconHelper.ArcSed, "시작점, 끝점, 방향", "ARC_SED"),
			(IconHelper.ArcSer, "시작점, 끝점, 반지름", "ARC_SER"),
			(IconHelper.ArcSec, "시작점, 끝점, 중심점", "ARC_SEC"),
			(IconHelper.ArcCse, "중심점, 시작점, 끝점", "ARC_CSE"),
			(IconHelper.ArcCsa, "중심점, 시작점, 각도", "ARC_CSA"),
			(IconHelper.ArcCsl, "중심점, 시작점, 길이", "ARC_CSL"),
		}, "호"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Ellipse, "축, 끝점", "ELLIPSE"),
			(IconHelper.Ellipse, "축, 회전", "ELLIPSE_R"),
			(IconHelper.EllipseCenter, "중심", "ELLIPSE_C"),
			(IconHelper.EllipseCenter, "중심, 회전", "ELLIPSE_CR"),
		}, "타원"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.EllipseArc, "축, 끝점", "ELLIPSE_A"),
			(IconHelper.EllipseArc, "축, 회전", "ELLIPSE_AR"),
			(IconHelper.EllipseArc, "중심", "ELLIPSE_AC"),
		}, "타원형 호"));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Rectang, "직사각형", "RECTANG"),
			(IconHelper.Polygon, "폴리곤 (중심-꼭짓점)", "POLYGON_I"),
			(IconHelper.PolygonCircum, "폴리곤 (중심-변)", "POLYGON_C"),
			(IconHelper.PolygonEdge, "폴리곤 (모서리)", "POLYGON_E"),
		}));
		draw.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Hatch, "해치", "HATCH"),
			(IconHelper.Gradient, "그라데이션", "GRADIENT"),
			(IconHelper.Boundary, "경계", "BOUNDARY"),
		}));
		draw.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Donut, "도넛", "DONUT"),
			this.CreateSmallButton(IconHelper.Plane, "2D 솔리드", "SOLID"),
			this.CreateSmallButton(IconHelper.Boundary, "와이프아웃", "WIPEOUT")));
		draw.Children.Add(Column(
			this.CreateSmallButton(IconHelper.RevCloud, "구름형 기호", "REVCLOUD"),
			this.CreateSmallButton(IconHelper.BlockInsert, "블록 삽입", "INSERT"),
			this.CreateSmallButton(IconHelper.Table, "테이블", "TABLE")));
		panel.AddPanel(this.WrapInPanel("그리기", draw));

		var snaps = Row();
		snaps.Children.Add(this.CreateBigButton(IconHelper.Settings, "설정", "OSNAP"));
		panel.AddPanel(this.WrapInPanel("객체 스냅", snaps));

		var settings = Row();
		settings.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Units, "도면 단위", "UNITS"),
			this.CreateSmallButton(IconHelper.PointStyle, "점 스타일", "PTYPE"),
			this.CreateSmallButton(IconHelper.Linetype, "선종류", "LINETYPE")));
		settings.Children.Add(Column(
			this.CreateSmallButton(IconHelper.BasePoint, "기준점", "BASE"),
			this.CreateSmallButton(IconHelper.Settings, "제도 설정", "DSETTINGS"),
			this.CreateSmallButton(IconHelper.TextStyle, "문자 스타일", "STYLE")));
		panel.AddPanel(this.WrapInPanel("설정", settings));
		return panel;
	}

	// 4. 삽입 탭 (기존 CAD: 블록 · 블록 정의)
	private RibbonPanelRow BuildInsertTab()
	{
		var panel = this.CreatePanelRow();
		var block = Row();
		block.Children.Add(this.CreateBigButton(IconHelper.BlockInsert, "블록 삽입", "INSERT"));
		block.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.EditAttribute, "단일", "EATTEDIT"),
			(IconHelper.EditAttribute, "다중", "-ATTEDIT"),
		}, "속성 편집"));
		block.Children.Add(this.CreateBigButton(IconHelper.BlockInsert, "블록 라이브러리", "BLOCKLIB"));
		panel.AddPanel(this.WrapInPanel("블록", block));

		var def = Row();
		def.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.BlockCreate, "블록 작성", "BLOCK"),
			(IconHelper.Save, "블록을 파일로 저장", "WBLOCK"),
		}));
		def.Children.Add(this.CreateBigButton(IconHelper.AttDef, "속성 정의", "ATTDEF"));
		def.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.AttDisp, "일반", "ATTDISP_N"),
			(IconHelper.AttDisp, "모두 켜기", "ATTDISP_ON"),
			(IconHelper.AttDisp, "모두 끄기", "ATTDISP_OFF"),
		}, "속성 표시"));
		def.Children.Add(this.CreateBigButton(IconHelper.TableOut, "속성 추출", "ATTEXT"));
		def.Children.Add(this.CreateBigButton(IconHelper.AttSync, "속성 동기화", "ATTSYNC"));
		def.Children.Add(this.CreateBigButton(IconHelper.BlockEditor, "블록 편집기", "BEDIT"));
		var defMore = Column(
			this.CreateSmallButton(IconHelper.BasePoint, "기준점", "BASE"),
			this.CreateSmallButton(IconHelper.BlockEditor, "블록 편집기 닫기", "BCLOSE"));
		panel.AddPanel(this.WrapInPanel("블록 정의", def, defMore));

		// 참조 (외부 참조: 기존 CAD XATTACH·XREF)
		var xref = Row();
		xref.Children.Add(this.CreateBigButton(IconHelper.Open, "부착", "XATTACH"));
		xref.Children.Add(this.CreateBigButton(IconHelper.BlockInsert, "외부 참조", "XREF"));
		panel.AddPanel(this.WrapInPanel("참조", xref));

		var layout = Row();
		layout.Children.Add(this.CreateBigButton(IconHelper.New, "새 배치", "LAYOUT_NEW"));
		panel.AddPanel(this.WrapInPanel("배치", layout));
		return panel;
	}

	// 5. 주석 탭 (기존 CAD: 문자 · 치수 · 중심선 · 지시선 · 테이블 · 마크업)
	private RibbonPanelRow BuildAnnotateTab()
	{
		var panel = this.CreatePanelRow();
		var text = Row();
		text.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.Text, "단일 행 문자", "TEXT"),
			(IconHelper.MText, "여러 줄 문자", "MTEXT"),
			(IconHelper.Txt2Mtxt, "문자를 여러 줄 문자로", "TXT2MTXT"),
			(IconHelper.TextEdit, "문자 편집", "TEXTEDIT"),
		}, "문자"));
		text.Children.Add(this.CreateBigButton(IconHelper.TextStyle, "문자 스타일", "STYLE"));
		text.Children.Add(Column(this.CreateSmallButton(IconHelper.Scale, "문자 축척", "SCALETEXT")));
		panel.AddPanel(this.WrapInPanel("문자", text));

		var dim = Row();
		dim.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.DimLinear, "선형", "DIMLINEAR"),
			(IconHelper.DimAligned, "정렬", "DIMALIGNED"),
			(IconHelper.DimAngular, "각도", "DIMANGULAR"),
		}, "선형"));
		dim.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.DimCenter, "중심 표시", "DIMCENTER"),
			(IconHelper.DimDiameter, "지름", "DIMDIAMETER"),
			(IconHelper.DimRadius, "반지름", "DIMRADIUS"),
			(IconHelper.DimRadius, "꺾기", "DIMJOGGED"),
		}, "지름"));
		dim.Children.Add(Column(
			this.CreateSmallButton(IconHelper.DimStyle, "치수 스타일", "DIMSTYLE"),
			this.CreateSmallSplitButton(new[]
			{
				(IconHelper.TextEdit, "치수 문자 편집", "DIMEDIT_N"),
				(IconHelper.TextEdit, "치수 문자 회전", "DIMEDIT_R"),
				(IconHelper.TextEdit, "치수 문자 위치", "DIMTEDIT"),
				(IconHelper.TextEdit, "문자 위치 복원", "DIMEDIT_H"),
			}),
			this.CreateSmallSplitButton(new[] { (IconHelper.DimBaseline, "기준선", "DIMBASELINE"), (IconHelper.DimContinue, "연속", "DIMCONTINUE") })));
		dim.Children.Add(Column(
			RowOf(
				this.CreateIconOnlyButton(IconHelper.DimArc, "호 길이 (DIMARC)", "DIMARC"),
				this.CreateIconOnlyButton(IconHelper.Leader, "지시선 (QLEADER)", "QLEADER"),
				this.CreateIconOnlyButton(IconHelper.DimOrdinate, "세로좌표 (DIMORDINATE)", "DIMORDINATE")),
			RowOf(
				this.CreateIconOnlyButton(IconHelper.DimAligned, "기울기 (DIMEDIT O)", "DIMEDIT_O"),
				this.CreateIconOnlyButton(IconHelper.DimBaseline, "간격 조정 (DIMSPACE)", "DIMSPACE"),
				this.CreateIconOnlyButton(IconHelper.DimRadius, "꺾기 반지름 (DIMJOGGED)", "DIMJOGGED"))));
		panel.AddPanel(this.WrapInPanel("치수", dim));

		var center = Row();
		center.Children.Add(this.CreateBigButton(IconHelper.DimCenter, "중심 표시", "CENTERMARK"));
		center.Children.Add(this.CreateBigButton(IconHelper.XLine, "중심선", "CENTERLINE"));
		panel.AddPanel(this.WrapInPanel("중심선", center));

		var leaders = Row();
		leaders.Children.Add(this.CreateBigButton(IconHelper.Leader, "지시선", "MLEADER"));
		leaders.Children.Add(Column(this.CreateSmallButton(IconHelper.Leader, "빠른 지시선", "QLEADER")));
		panel.AddPanel(this.WrapInPanel("지시선", leaders));

		var tables = Row();
		tables.Children.Add(this.CreateBigButton(IconHelper.Table, "테이블", "TABLE"));
		tables.Children.Add(Column(
			this.CreateSmallButton(IconHelper.TableOut, "CSV로 내보내기", "TABLEEXPORT"),
			this.CreateSmallButton(IconHelper.TableOut, "표 추출", "TABLEOUT")));
		panel.AddPanel(this.WrapInPanel("테이블", tables));

		var markup = Row();
		markup.Children.Add(this.CreateBigButton(IconHelper.Boundary, "와이프아웃", "WIPEOUT"));
		markup.Children.Add(this.CreateBigButton(IconHelper.RevCloud, "구름형 기호", "REVCLOUD"));
		panel.AddPanel(this.WrapInPanel("마크업", markup));
		return panel;
	}

	// 6. 보기 탭 (기존 CAD: 탐색 · 배치 · 창)
	private RibbonPanelRow BuildViewTab()
	{
		var panel = this.CreatePanelRow();
		var nav = Row();
		nav.Children.Add(this.CreateBigButton(IconHelper.Regen, "재생성", "REGEN"));
		nav.Children.Add(this.CreateBigSplitButton(new[]
		{
			(IconHelper.ZoomExtents, "범위", "ZOOM_EXTENTS"),
			(IconHelper.ZoomIn, "확대", "ZOOM_IN"),
			(IconHelper.ZoomOut, "축소", "ZOOM_OUT"),
			(IconHelper.ZoomExtents, "윈도우", "ZOOM_W"),
			(IconHelper.ZoomExtents, "이전", "ZOOM_P"),
			(IconHelper.ZoomExtents, "전체", "ZOOM_A"),
			(IconHelper.ZoomExtents, "중심", "ZOOM_C"),
			(IconHelper.ZoomExtents, "객체", "ZOOM_O"),
		}, "줌"));
		nav.Children.Add(this.CreateBigButton(IconHelper.Pan, "초점 이동", "PAN"));
		panel.AddPanel(this.WrapInPanel("탐색", nav));

		var layouts = Row();
		layouts.Children.Add(this.CreateBigButton(IconHelper.New, "새 배치", "LAYOUT_NEW"));
		panel.AddPanel(this.WrapInPanel("배치", layouts));

		var panes = Row();
		panes.Children.Add(this.CreateBigButton(IconHelper.Properties, "속성 팔레트", "TOGGLE_PROPERTY"));
		panes.Children.Add(this.CreateBigButton(IconHelper.Layer, "도면층 관리자", "LAYER_MANAGER"));
		panes.Children.Add(this.CreateBigButton(IconHelper.CommandLine, "명령줄", "TOGGLE_CMDLINE"));
		panes.Children.Add(this.CreateBigButton(IconHelper.TextWindow, "문자 윈도우", "TEXT_WINDOW"));
		panel.AddPanel(this.WrapInPanel("창", panes));
		return panel;
	}

	// 7. 출력 탭 (기존 CAD: 내보내기 · 인쇄)
	private RibbonPanelRow BuildOutputTab()
	{
		var panel = this.CreatePanelRow();
		var export = Row();
		export.Children.Add(this.CreateBigButton(IconHelper.Save, "저장", "QSAVE"));
		export.Children.Add(this.CreateBigButton(IconHelper.SaveAs, "다른 이름으로", "SAVEAS"));
		export.Children.Add(this.CreateBigButton(IconHelper.Save, "블록 저장", "WBLOCK"));
		export.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Print, "PDF 출력", "PDFEXPORT"),
			this.CreateSmallButton(IconHelper.Print, "PNG 출력", "EXPORT_PNG"),
			this.CreateSmallButton(IconHelper.TableOut, "CSV 출력", "TABLEEXPORT")));
		export.Children.Add(Column(this.CreateSmallButton(IconHelper.SaveAs, "DXF 출력", "DXFOUT")));
		panel.AddPanel(this.WrapInPanel("내보내기", export));

		var print = Row();
		print.Children.Add(this.CreateBigButton(IconHelper.Print, "인쇄", "PLOT"));
		panel.AddPanel(this.WrapInPanel("인쇄", print));
		return panel;
	}

	// 8. 도구 탭 (기존 CAD: 조회 · 관리 · 탐색기)
	private RibbonPanelRow BuildToolsTab()
	{
		var panel = this.CreatePanelRow();
		var inq = Row();
		inq.Children.Add(this.CreateBigButton(IconHelper.Dist, "거리", "DIST"));
		inq.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Area, "면적", "AREA"),
			this.CreateSmallButton(IconHelper.Measure, "총 길이", "TOTALLENGTH"),
			this.CreateSmallButton(IconHelper.Id, "ID 좌표", "ID")));
		inq.Children.Add(Column(
			this.CreateSmallButton(IconHelper.List, "객체 정보", "LIST"),
			this.CreateSmallButton(IconHelper.List, "도면 상태", "STATUS"),
			this.CreateSmallButton(IconHelper.TableOut, "표 추출", "TABLEOUT")));
		panel.AddPanel(this.WrapInPanel("조회", inq));

		var manage = Row();
		manage.Children.Add(this.CreateBigButton(IconHelper.Settings, "옵션", "OPTIONS"));
		manage.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Settings, "제도 설정", "DSETTINGS"),
			this.CreateSmallButton(IconHelper.ResetSettings, "설정 초기화", "RESETSETTINGS")));
		panel.AddPanel(this.WrapInPanel("관리", manage));

		var explorer = Row();
		explorer.Children.Add(this.CreateBigButton(IconHelper.Layer, "도면층", "LAYER_MANAGER"));
		explorer.Children.Add(Column(
			this.CreateSmallButton(IconHelper.LayerState, "도면층 상태", "LAYERSTATE"),
			this.CreateSmallButton(IconHelper.TextStyle, "문자 스타일", "STYLE"),
			this.CreateSmallButton(IconHelper.DimStyle, "치수 스타일", "DIMSTYLE")));
		explorer.Children.Add(Column(
			this.CreateSmallButton(IconHelper.Linetype, "선종류", "LINETYPE"),
			this.CreateSmallButton(IconHelper.BlockInsert, "블록 라이브러리", "BLOCKLIB"),
			this.CreateSmallButton(IconHelper.Group, "그룹", "GROUP")));
		panel.AddPanel(this.WrapInPanel("탐색기", explorer));
		return panel;
	}

	// 9. 도움말 탭
	private RibbonPanelRow BuildHelpTab()
	{
		var panel = this.CreatePanelRow();
		var helpContent = Row();
		helpContent.Children.Add(this.CreateBigButton(IconHelper.Help, "도움말 (F1)", "HELP"));
		helpContent.Children.Add(this.CreateBigButton(IconHelper.TextWindow, "문자 윈도우 (F2)", "TEXT_WINDOW"));
		panel.AddPanel(this.WrapInPanel("도움말", helpContent));
		return panel;
	}

	private RibbonPanelRow CreatePanelRow()
	{
		var row = new RibbonPanelRow();
		this._panelRows.Add(row);
		return row;
	}

	public void CloseAllPopups()
	{
		foreach (var row in this._panelRows)
		{
			row.CloseAllPopups();
		}
		foreach (var p in this._morePopups)
		{
			p.IsOpen = false;
		}
	}

	private readonly List<Popup> _morePopups = new();

	/// <summary>패널 상자. more를 주면 제목 줄(제목 ▾)을 눌렀을 때 아래로 펼쳐지는 추가 도구 영역 (기존 CAD 패널 확장).</summary>
	private RibbonPanelGroup WrapInPanel(string title, Control content, Control? more = null)
	{
		// 기존 CAD처럼 패널마다 밝은 테두리의 둥근 상자 + 패널 사이 틈 (어두운 1px 선만으로는 경계가 안 보였음)
		var normalBorder = new Border
		{
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x72, 0x72, 0x72)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Margin = new Thickness(2, 2, 1, 1),
			ClipToBounds = true,
		};

		var dock = new DockPanel();

		var footer = new Border
		{
			Height = 16,
			Background = CadTheme.RibbonPanelTitleBg,
			Margin = new Thickness(0, 1, 0, 0),
		};

		var labelStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 3,
		};
		labelStack.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 12,
			Foreground = CadTheme.RibbonPanelTitleFg,
			VerticalAlignment = VerticalAlignment.Center,
		});
		footer.Child = labelStack;
		Popup? morePopup = null;
		if (more != null)
		{
			labelStack.Children.Add(new TextBlock
			{
				Text = "▾",
				FontSize = 10,
				Foreground = CadTheme.RibbonPanelTitleFg,
				VerticalAlignment = VerticalAlignment.Center,
			});
			more.Margin = new Thickness(6, 4);
			morePopup = new Popup
			{
				PlacementTarget = normalBorder,
				Placement = PlacementMode.BottomEdgeAlignedLeft,
				IsLightDismissEnabled = true,
				Child = new Border
				{
					Background = CadTheme.RibbonBodyBg,
					BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x72, 0x72, 0x72)),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(0, 0, 3, 3),
					MinWidth = 120,
					Child = more,
				},
			};
			this._morePopups.Add(morePopup);
			footer.Cursor = new Cursor(StandardCursorType.Hand);
			ToolTip.SetTip(footer, $"{title} 패널 펼치기");
			var hoverBg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x4A, 0x5A, 0x70));
			footer.PointerEntered += (_, _) => footer.Background = hoverBg;
			footer.PointerExited += (_, _) => footer.Background = CadTheme.RibbonPanelTitleBg;
			var popup = morePopup;
			footer.PointerPressed += (_, e) =>
			{
				bool open = !popup.IsOpen;
				this.CloseAllPopups();
				popup.IsOpen = open;
				e.Handled = true;
			};
		}

		DockPanel.SetDock(footer, Dock.Bottom);
		dock.Children.Add(footer);
		if (content is Layoutable layoutable)
		{
			layoutable.Height = 74;
			// 제목 줄은 상자 폭 전체, 버튼 영역만 좌우 여백
			layoutable.Margin = new Thickness(6, 0);
		}
		dock.Children.Add(content);

		normalBorder.Child = dock;
		if (morePopup != null)
		{
			dock.Children.Insert(0, morePopup); // 채움(마지막 자식)은 내용 그대로
		}

		var bigButtons = new List<RibbonBigButtonInfo>();
		var smallButtons = new List<RibbonSmallButtonInfo>();
		CollectButtons(content, bigButtons, smallButtons);

		string repIconKey = GetRepresentativeIcon(title);
		return new RibbonPanelGroup(title, normalBorder, repIconKey, smallButtons, bigButtons);
	}

	private Button CreateBigButton(string iconKey, string text, string cmd)
	{
		var btn = new Button
		{
			Padding = new Thickness(4, 2),
			Margin = new Thickness(4, 0),
			MinWidth = 54,
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Stretch,
			[ToolTip.TipProperty] = $"{text} ({cmd})",
		};

		var stack = new StackPanel
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 2,
		};

		stack.Children.Add(IconHelper.GetIcon(iconKey, 32));
		var tb = new TextBlock
		{
			Text = text,
			FontSize = 12,
			Foreground = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Center,
			TextAlignment = TextAlignment.Center,
		};
		stack.Children.Add(tb);

		btn.Content = stack;
		btn.Click += (_, _) => this.HandleCommand(cmd);
		btn.Tag = new RibbonBigButtonInfo(btn, tb);
		return btn;
	}

	private Button CreateBigDropdownButton(string iconKey, string text, string defaultCmd, (string Label, string Cmd)[] items)
	{
		var btn = new Button
		{
			Padding = new Thickness(4, 2),
			Margin = new Thickness(4, 0),
			MinWidth = 54,
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Stretch,
			[ToolTip.TipProperty] = $"{text} ({defaultCmd})",
		};

		var stack = new StackPanel
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 2,
		};

		stack.Children.Add(IconHelper.GetIcon(iconKey, 32));

		var textRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
		textRow.Children.Add(new TextBlock
		{
			Text = text,
			FontSize = 12,
			Foreground = Brushes.White,
			TextAlignment = TextAlignment.Center,
		});
		textRow.Children.Add(new TextBlock
		{
			Text = "▾",
			FontSize = 8,
			Foreground = Brushes.LightGray,
			VerticalAlignment = VerticalAlignment.Center,
		});
		stack.Children.Add(textRow);

		btn.Content = stack;

		var contextMenu = new ContextMenu();
		foreach (var item in items)
		{
			var mi = new MenuItem { Header = item.Label };
			mi.Click += (_, _) => this.HandleCommand(item.Cmd);
			contextMenu.Items.Add(mi);
		}
		btn.ContextMenu = contextMenu;

		btn.Click += (_, _) =>
		{
			if (contextMenu.Items.Count > 0)
			{
				contextMenu.Open(btn);
			}
			else
			{
				this.HandleCommand(defaultCmd);
			}
		};
		btn.Tag = new RibbonBigButtonInfo(btn, textRow);
		return btn;
	}

	private Button CreateSmallButton(string iconKey, string text, string cmd)
	{
		var btn = new Button
		{
			Padding = new Thickness(4, 1),
			Margin = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Height = 22,
			[ToolTip.TipProperty] = $"{text} ({cmd})",
		};

		var stack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center,
		};

		stack.Children.Add(IconHelper.GetIcon(iconKey, 16));
		var tb = new TextBlock
		{
			Text = text,
			FontSize = 12,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
		};
		stack.Children.Add(tb);

		btn.Content = stack;
		btn.Click += (_, _) => this.HandleCommand(cmd);
		btn.Tag = new RibbonSmallButtonInfo(btn, tb, null);
		return btn;
	}

	private Button CreateSmallDropdownButton(string iconKey, string text, string defaultCmd, (string Label, string Cmd)[] items)
	{
		var btn = new Button
		{
			Padding = new Thickness(4, 1),
			Margin = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Height = 22,
			[ToolTip.TipProperty] = $"{text} ({defaultCmd})",
		};

		var stack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center,
		};

		stack.Children.Add(IconHelper.GetIcon(iconKey, 16));
		var tb = new TextBlock
		{
			Text = text,
			FontSize = 12,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
		};
		stack.Children.Add(tb);

		var arrow = new TextBlock
		{
			Text = "▾",
			FontSize = 8,
			Foreground = Brushes.LightGray,
			VerticalAlignment = VerticalAlignment.Center,
		};
		stack.Children.Add(arrow);

		btn.Content = stack;

		var contextMenu = new ContextMenu();
		foreach (var item in items)
		{
			var mi = new MenuItem { Header = item.Label };
			mi.Click += (_, _) => this.HandleCommand(item.Cmd);
			contextMenu.Items.Add(mi);
		}
		btn.ContextMenu = contextMenu;

		btn.Click += (_, _) =>
		{
			if (contextMenu.Items.Count > 0)
			{
				contextMenu.Open(btn);
			}
			else
			{
				this.HandleCommand(defaultCmd);
			}
		};
		btn.Tag = new RibbonSmallButtonInfo(btn, tb, arrow);
		return btn;
	}

	private Button CreateIconOnlyButton(string iconKey, string toolTip, string cmd)
	{
		var btn = new Button
		{
			Width = 22,
			Height = 22,
			Padding = new Thickness(1),
			Margin = new Thickness(1),
			Background = Brushes.Transparent,
			[ToolTip.TipProperty] = toolTip,
			Content = IconHelper.GetIcon(iconKey, 16),
		};
		btn.Click += (_, _) => this.HandleCommand(cmd);
		return btn;
	}

	private static void CollectButtons(Control control, List<RibbonBigButtonInfo> bigButtons, List<RibbonSmallButtonInfo> smallButtons)
	{
		if (control.Tag is RibbonBigButtonInfo bigInfo)
		{
			bigButtons.Add(bigInfo);
			return;
		}
		if (control.Tag is RibbonSmallButtonInfo smallInfo)
		{
			smallButtons.Add(smallInfo);
			return;
		}

		if (control is Panel panel)
		{
			foreach (var child in panel.Children)
			{
				CollectButtons(child, bigButtons, smallButtons);
			}
		}
		else if (control is ContentControl cc && cc.Content is Control childControl)
		{
			CollectButtons(childControl, bigButtons, smallButtons);
		}
		else if (control is Decorator decorator && decorator.Child != null)
		{
			CollectButtons(decorator.Child, bigButtons, smallButtons);
		}
	}

	private static string GetRepresentativeIcon(string title) => title switch
	{
		"그리기" => IconHelper.Line,
		"수정" => IconHelper.Move,
		"도면층" => IconHelper.Layer,
		"주석" => IconHelper.Text,
		"블록" => IconHelper.BlockInsert,
		"속성" => IconHelper.ColorWheel,
		"유틸리티" => IconHelper.Dist,
		"클립보드" => IconHelper.Paste,
		"선택" => IconHelper.QuickSelect,
		"그리기 순서" => IconHelper.DrawFront,
		"객체 스냅" => IconHelper.Settings,
		"설정" => IconHelper.Settings,
		"블록 정의" => IconHelper.BlockCreate,
		"배치" => IconHelper.New,
		"문자" => IconHelper.Text,
		"중심선" => IconHelper.DimCenter,
		"지시선" => IconHelper.Leader,
		"테이블" => IconHelper.Table,
		"마크업" => IconHelper.RevCloud,
		"창" => IconHelper.Properties,
		"내보내기" => IconHelper.Save,
		"인쇄" => IconHelper.Print,
		"조회" => IconHelper.Dist,
		"관리" => IconHelper.Settings,
		"탐색기" => IconHelper.Layer,
		"그룹" => IconHelper.Group,
		"실행 취소" => IconHelper.Undo,
		"선택 및 삭제" => IconHelper.Erase,
		"그리기 도구" => IconHelper.Line,
		"블록 및 배치" => IconHelper.Properties,
		"치수" => IconHelper.DimLinear,
		"문자 및 표" => IconHelper.Text,
		"탐색" => IconHelper.ZoomExtents,
		"인터페이스" => IconHelper.Properties,
		"출력" => IconHelper.Print,
		"도구" => IconHelper.TableOut,
		"도움말" => IconHelper.AppIcon,
		_ => IconHelper.AppIcon,
	};

	private void HandleCommand(string cmd)
	{
		this.CloseAllPopups();
		switch (cmd)
		{
			case "LAYER_MANAGER":
				this.LayerManagerRequested?.Invoke();
				break;
			case "TOGGLE_PROPERTY":
				this.PropertyPaletteToggleRequested?.Invoke();
				break;
			case "TOGGLE_CMDLINE":
				this.CommandLineToggleRequested?.Invoke();
				break;
			case "EXPORT_PNG":
				this.ExportPngRequested?.Invoke();
				break;
			case "PLOT":
				this.PlotRequested?.Invoke();
				break;
			case "LAYER_SET_CURRENT":
				this.SetCurrentLayerRequested?.Invoke();
				break;
			case "LAYER_PREV":
				this.PreviousLayerRequested?.Invoke();
				break;
			case "LAYER_OFF":
				this.LayerOffRequested?.Invoke();
				break;
			case "LAYER_ON":
				this.LayerOnRequested?.Invoke();
				break;
			case "LAYER_FREEZE":
				this.LayerFreezeRequested?.Invoke();
				break;
			case "LAYER_THAW":
				this.LayerThawRequested?.Invoke();
				break;
			case "LAYER_LOCK":
				this.LayerLockRequested?.Invoke();
				break;
			case "LAYER_UNLOCK":
				this.LayerUnlockRequested?.Invoke();
				break;
			case "LAYER_DELETE":
				this.LayerDeleteRequested?.Invoke();
				break;
			default:
				this.CommandRequested?.Invoke(cmd);
				break;
		}
	}

	private void StyleDropdown(ComboBox combo)
	{
		combo.Background = CadTheme.DropdownBg;
		combo.Foreground = CadTheme.DropdownFg;
		combo.BorderBrush = CadTheme.DropdownBorder;
	}

	private void InitPropertyCombos()
	{
		this.StyleDropdown(this._colorCombo);
		this.StyleDropdown(this._lineTypeCombo);
		this.StyleDropdown(this._lineWeightCombo);

		// 1. 색상
		this._colorCombo.Width = 170;
		this._colorCombo.Height = 22;
		this._colorCombo.MinHeight = 22;
		this._colorCombo.FontSize = 12;
		this._colorCombo.Padding = new Thickness(4, 0);

		var colorItems = new ColorOptionItem[]
		{
			new("BYLAYER", Color.ByLayer, Brushes.White),
			new("BYBLOCK", Color.ByBlock, Brushes.White),
			new("1 빨강", new Color(1), Brushes.Red),
			new("2 노랑", new Color(2), Brushes.Yellow),
			new("3 초록", new Color(3), Brushes.LimeGreen),
			new("4 하늘", new Color(4), Brushes.Cyan),
			new("5 파랑", new Color(5), Brushes.DodgerBlue),
			new("6 자홍", new Color(6), Brushes.Magenta),
			new("7 흰색", new Color(7), Brushes.White),
		};

		this._colorCombo.ItemTemplate = new FuncDataTemplate<ColorOptionItem>((item, _) =>
		{
			if (item == null) return new TextBlock();
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
			sp.Children.Add(new Border
			{
				Width = 12,
				Height = 10,
				Background = item.Brush,
				BorderBrush = Brushes.Gray,
				BorderThickness = new Thickness(1),
				VerticalAlignment = VerticalAlignment.Center,
			});
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, Foreground = CadTheme.DropdownFg, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});

		this._colorCombo.ItemsSource = colorItems;
		this._colorCombo.SelectedIndex = 0;
		this._colorCombo.SelectionChanged += (_, _) =>
		{
			if (this._updatingCombos) return;
			if (this._colorCombo.SelectedItem is ColorOptionItem item)
			{
				this.ColorSelected?.Invoke(item.Color);
			}
		};

		// 2. 선종류
		this._lineTypeCombo.Width = 170;
		this._lineTypeCombo.Height = 22;
		this._lineTypeCombo.MinHeight = 22;
		this._lineTypeCombo.FontSize = 12;
		this._lineTypeCombo.Padding = new Thickness(4, 0);

		var linetypeItems = new LinetypeOptionItem[]
		{
			new("BYLAYER", null),
			new("ByBlock", null),
			new("Continuous", null),
		};

		this._lineTypeCombo.ItemTemplate = new FuncDataTemplate<LinetypeOptionItem>((item, _) =>
		{
			if (item == null) return new TextBlock();
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
			if (!item.IsOther)
			{
				sp.Children.Add(new LinetypeSwatch(item.Pattern, item.Marks, CadTheme.DropdownFg, 40, 12));
			}
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, Foreground = CadTheme.DropdownFg, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});

		this._lineTypeCombo.ItemsSource = linetypeItems;
		this._lineTypeCombo.SelectedIndex = 0;
		this._lineTypeCombo.SelectionChanged += (_, _) =>
		{
			if (this._updatingCombos) return;
			if (this._lineTypeCombo.SelectedItem is LinetypeOptionItem item)
			{
				if (item.IsOther)
				{
					this.LinetypeManagerRequested?.Invoke();
				}
				else
				{
					this.LineTypeSelected?.Invoke(item.Name);
				}
			}
		};

		// 3. 선가중치
		this._lineWeightCombo.Width = 170;
		this._lineWeightCombo.Height = 22;
		this._lineWeightCombo.MinHeight = 22;
		this._lineWeightCombo.FontSize = 12;
		this._lineWeightCombo.Padding = new Thickness(4, 0);

		var lineweightItems = new LineweightOptionItem[]
		{
			new("BYLAYER", 1.0),
			new("ByBlock", 1.0),
			new("기본값", 1.0),
			new("0.05 mm", 0.6),
			new("0.09 mm", 0.9),
			new("0.15 mm", 1.4),
			new("0.25 mm", 2.2),
			new("0.30 mm", 2.8),
			new("0.50 mm", 4.0),
		};

		this._lineWeightCombo.ItemTemplate = new FuncDataTemplate<LineweightOptionItem>((item, _) =>
		{
			if (item == null) return new TextBlock();
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
			var line = new Avalonia.Controls.Shapes.Line
			{
				StartPoint = new Point(0, 5),
				EndPoint = new Point(24, 5),
				Stroke = Brushes.Black,
				StrokeThickness = item.StrokeWeight,
				VerticalAlignment = VerticalAlignment.Center,
			};
			sp.Children.Add(line);
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, Foreground = CadTheme.DropdownFg, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});

		this._lineWeightCombo.ItemsSource = lineweightItems;
		this._lineWeightCombo.SelectedIndex = 0;
	}

	public void UpdateLayers(IEnumerable<Layer> layers, Layer current)
	{
		this._updatingCombos = true;
		try
		{
			var items = layers.Select(l => new LayerItem(l)).ToList();
			this._layerCombo.ItemsSource = items;
			this._layerCombo.SelectedItem = items.FirstOrDefault(i => i.Layer.Name == current.Name);
		}
		finally
		{
			this._updatingCombos = false;
		}
	}

	public void UpdateLineTypes(IEnumerable<LineType> lineTypes, string? currentName)
	{
		this._updatingCombos = true;
		try
		{
			var items = new List<LinetypeOptionItem>
			{
				new("BYLAYER", null),
				new("ByBlock", null),
			};
			foreach (var lt in lineTypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
			{
				if (string.Equals(lt.Name, "ByLayer", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(lt.Name, "ByBlock", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				items.Add(new LinetypeOptionItem(lt.Name, LinetypeSwatch.PatternOf(lt), lt.Segments.Select(sg => sg.IsShape || sg.IsText).ToArray()));
			}
			items.Add(new LinetypeOptionItem("기타...", null, IsOther: true));

			this._lineTypeCombo.ItemsSource = items;
			int idx = items.FindIndex(x => string.Equals(x.Name, currentName, StringComparison.OrdinalIgnoreCase));
			this._lineTypeCombo.SelectedIndex = idx >= 0 ? idx : 0;
		}
		finally
		{
			this._updatingCombos = false;
		}
	}

	public sealed class LayerItem
	{
		public Layer Layer { get; }
		public string Name => this.Layer.Name;
		public bool IsOn => this.Layer.IsOn;
		public bool IsFrozen => this.Layer.Flags.HasFlag(LayerFlags.Frozen);
		public bool IsLocked => this.Layer.Flags.HasFlag(LayerFlags.Locked);
		public IBrush ColorBrush { get; }

		public LayerItem(Layer layer)
		{
			this.Layer = layer;
			this.ColorBrush = CadColorToBrush(layer.Color);
		}

		public override string ToString() => this.Layer.Name;

		private static IBrush CadColorToBrush(Color color)
		{
			if (color.Index == 0 || color.Index == 256) return Brushes.White;
			return color.Index switch
			{
				1 => Brushes.Red,
				2 => Brushes.Yellow,
				3 => Brushes.LimeGreen,
				4 => Brushes.Cyan,
				5 => Brushes.DodgerBlue,
				6 => Brushes.Magenta,
				7 => Brushes.White,
				_ => new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)),
			};
		}
	}

	private sealed record ColorOptionItem(string Name, Color Color, IBrush Brush);
	private sealed record LinetypeOptionItem(string Name, double[]? Pattern, bool[]? Marks = null, bool IsOther = false);
	private sealed record LineweightOptionItem(string Name, double StrokeWeight);
}

/// <summary>
/// 리본 패널 줄을 담는 컨테이너.
/// 화면 폭을 측정하여 필요한 폭이 실제 폭을 넘치면 오른쪽 패널부터 한 단계씩 줄인다:
/// 1단계: 작은 버튼 글자 숨김 (아이콘만 표시, 툴팁 유지)
/// 2단계: 큰 버튼 글자 숨김 (버튼 폭 36으로 축소)
/// 3단계: 패널 전체를 "패널 이름 ▾" 버튼 하나로 접고, 누르면 원래 패널 내용을 Popup으로 표시
/// 창이 넓어지면 반대 순서로 복원한다.
/// </summary>
internal sealed class RibbonPanelRow : Panel
{
	private readonly List<RibbonPanelGroup> _panels = new();
	private bool _levelsMeasured;
	private int _currentStep;

	public IReadOnlyList<RibbonPanelGroup> Panels => this._panels;

	public RibbonPanelRow()
	{
		this.Height = 94;
	}

	public void AddPanel(RibbonPanelGroup group)
	{
		this._panels.Add(group);
		this.Children.Add(group);
	}

	public void CloseAllPopups()
	{
		foreach (var p in this._panels)
		{
			p.ClosePopup();
		}
	}

	private double CalculateRequiredWidth(int step)
	{
		double sum = 0;
		int n = this._panels.Count;
		for (int i = 0; i < n; i++)
		{
			int rightIndex = n - 1 - i;
			int level = 0;
			if (step > 2 * n + rightIndex)
			{
				level = 3;
			}
			else if (step > n + rightIndex)
			{
				level = 2;
			}
			else if (step > rightIndex)
			{
				level = 1;
			}
			sum += this._panels[i].GetWidthForLevel(level);
		}
		return sum;
	}

	private int FindOptimalStep(double availableWidth)
	{
		int n = this._panels.Count;
		int maxStep = 3 * n;
		for (int s = 0; s <= maxStep; s++)
		{
			if (this.CalculateRequiredWidth(s) <= availableWidth)
			{
				return s;
			}
		}
		return maxStep;
	}

	private void ApplyStep(int step)
	{
		int n = this._panels.Count;
		for (int i = 0; i < n; i++)
		{
			int rightIndex = n - 1 - i;
			int level = 0;
			if (step > 2 * n + rightIndex)
			{
				level = 3;
			}
			else if (step > n + rightIndex)
			{
				level = 2;
			}
			else if (step > rightIndex)
			{
				level = 1;
			}
			this._panels[i].ApplyCollapseLevel(level);
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		if (this._panels.Count == 0)
		{
			return new Size(0, 94);
		}

		if (!this._levelsMeasured)
		{
			foreach (var p in this._panels)
			{
				p.MeasureLevels();
			}
			this._levelsMeasured = true;
		}

		// 오른쪽 끝에 여유 20px (패널이 창 끝에 붙지 않게)
		double targetWidth = availableSize.Width - 20;
		if (double.IsInfinity(targetWidth) || double.IsNaN(targetWidth) || targetWidth <= 0)
		{
			if (this._currentStep != 0)
			{
				this._currentStep = 0;
				this.ApplyStep(0);
			}
		}
		else
		{
			int optimalStep = this.FindOptimalStep(targetWidth);
			if (optimalStep != this._currentStep)
			{
				this._currentStep = optimalStep;
				this.ApplyStep(optimalStep);
			}
		}

		double totalWidth = 0;
		foreach (var child in this.Children)
		{
			if (!child.IsVisible) continue;
			child.Measure(new Size(double.PositiveInfinity, 94));
			totalWidth += child.DesiredSize.Width;
		}

		return new Size(totalWidth, 94);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double x = 0;
		foreach (var child in this.Children)
		{
			if (!child.IsVisible) continue;
			double w = child.DesiredSize.Width;
			child.Arrange(new Rect(x, 0, w, finalSize.Height));
			x += w;
		}
		return new Size(Math.Max(x, finalSize.Width), finalSize.Height);
	}
}

/// <summary>
/// 개별 리본 패널 그룹.
/// 0단계(기본), 1단계(작은 버튼 텍스트 숨김), 2단계(큰 버튼 텍스트 숨김 및 폭 36),
/// 3단계("패널 이름 ▾" 버튼으로 축소 및 클릭 시 Popup 표시)를 지원한다.
/// </summary>
internal sealed class RibbonPanelGroup : Panel
{
	public string Title { get; }
	private readonly Border _normalBorder;
	private readonly Border _collapsedBorder;
	private readonly Border _popupBorder;
	private readonly Popup _popup;
	private readonly List<RibbonSmallButtonInfo> _smallButtons;
	private readonly List<RibbonBigButtonInfo> _bigButtons;
	private int _currentLevel;

	public double WidthLevel0 { get; private set; }
	public double WidthLevel1 { get; private set; }
	public double WidthLevel2 { get; private set; }
	public double WidthLevel3 { get; private set; }

	public RibbonPanelGroup(
		string title,
		Border normalBorder,
		string repIconKey,
		List<RibbonSmallButtonInfo> smallButtons,
		List<RibbonBigButtonInfo> bigButtons)
	{
		this.Title = title;
		this._normalBorder = normalBorder;
		this._smallButtons = smallButtons;
		this._bigButtons = bigButtons;

		var collapsedBtn = new Button
		{
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(4, 2),
			VerticalAlignment = VerticalAlignment.Stretch,
			HorizontalAlignment = HorizontalAlignment.Center,
			MinWidth = 36,
			[ToolTip.TipProperty] = $"{title} 패널 열기",
		};

		var colStack = new StackPanel
		{
			Orientation = Orientation.Vertical,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 3,
		};
		colStack.Children.Add(IconHelper.GetIcon(repIconKey, 24));
		colStack.Children.Add(new TextBlock
		{
			Text = $"{title} ▾",
			FontSize = 11,
			Foreground = CadTheme.RibbonPanelTitleFg,
			HorizontalAlignment = HorizontalAlignment.Center,
			TextAlignment = TextAlignment.Center,
		});
		collapsedBtn.Content = colStack;

		this._collapsedBorder = new Border
		{
			BorderBrush = CadTheme.RibbonPanelBorder,
			BorderThickness = new Thickness(0, 0, 1, 0),
			Padding = new Thickness(1, 0),
			Height = 94,
			Child = collapsedBtn,
			IsVisible = false,
		};

		this._popupBorder = new Border
		{
			Background = CadTheme.RibbonBodyBg,
			BorderBrush = CadTheme.RibbonPanelBorder,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Padding = new Thickness(4),
		};

		this._popup = new Popup
		{
			PlacementTarget = collapsedBtn,
			Placement = PlacementMode.BottomEdgeAlignedLeft,
			IsLightDismissEnabled = true,
			Child = this._popupBorder,
		};

		collapsedBtn.Click += (_, _) =>
		{
			this._popup.IsOpen = !this._popup.IsOpen;
		};

		this.Children.Add(this._normalBorder);
		this.Children.Add(this._collapsedBorder);
		this.Children.Add(this._popup);
	}

	public void ClosePopup()
	{
		if (this._popup.IsOpen)
		{
			this._popup.IsOpen = false;
		}
	}

	public double GetWidthForLevel(int level) => level switch
	{
		0 => this.WidthLevel0,
		1 => this.WidthLevel1,
		2 => this.WidthLevel2,
		_ => this.WidthLevel3,
	};

	/// <summary>
	/// 하위 전체의 측정 캐시를 무효화한다. 이게 없으면 Measure가 이전 결과를 그대로 돌려줘서
	/// 1·2단계 폭이 0단계와 같게 잡히고, 결국 모든 패널이 과하게 접혔다.
	/// </summary>
	private static void InvalidateTree(Avalonia.Visual visual)
	{
		if (visual is Avalonia.Layout.Layoutable layoutable)
		{
			layoutable.InvalidateMeasure();
		}
		foreach (Avalonia.Visual child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(visual))
		{
			InvalidateTree(child);
		}
	}

	private double MeasureNormalWidth(int level)
	{
		this.ApplyLevelInternal(level, forMeasurement: true);
		InvalidateTree(this._normalBorder);
		this._normalBorder.Measure(new Size(double.PositiveInfinity, 94));
		return Math.Ceiling(this._normalBorder.DesiredSize.Width);
	}

	public void MeasureLevels()
	{
		this.WidthLevel0 = this.MeasureNormalWidth(0);
		this.WidthLevel1 = this.MeasureNormalWidth(1);
		this.WidthLevel2 = this.MeasureNormalWidth(2);

		this._collapsedBorder.IsVisible = true;
		this._collapsedBorder.Measure(new Size(double.PositiveInfinity, 94));
		this.WidthLevel3 = Math.Ceiling(this._collapsedBorder.DesiredSize.Width);
		this._collapsedBorder.IsVisible = false;

		this.WidthLevel1 = Math.Min(this.WidthLevel1, this.WidthLevel0);
		this.WidthLevel2 = Math.Min(this.WidthLevel2, this.WidthLevel1);
		this.WidthLevel3 = Math.Min(this.WidthLevel3, this.WidthLevel2);

		this.ApplyLevelInternal(this._currentLevel, forMeasurement: false);
	}

	public void ApplyCollapseLevel(int level)
	{
		if (this._currentLevel == level) return;
		this._currentLevel = level;
		this.ApplyLevelInternal(level, forMeasurement: false);
	}

	private void ApplyLevelInternal(int level, bool forMeasurement)
	{
		if (level == 3)
		{
			if (!forMeasurement)
			{
				if (this.Children.Contains(this._normalBorder))
				{
					this.Children.Remove(this._normalBorder);
				}
				this._popupBorder.Child = this._normalBorder;
				this._normalBorder.IsVisible = true;
				this._collapsedBorder.IsVisible = true;
			}

			foreach (var b in this._smallButtons) b.SetCollapsed(false);
			foreach (var b in this._bigButtons) b.SetCollapsed(false);
		}
		else
		{
			if (!forMeasurement)
			{
				if (this._popup.IsOpen) this._popup.IsOpen = false;
				this._popupBorder.Child = null;

				if (!this.Children.Contains(this._normalBorder))
				{
					this.Children.Insert(0, this._normalBorder);
				}
				this._normalBorder.IsVisible = true;
				this._collapsedBorder.IsVisible = false;
			}

			bool smallCollapsed = level >= 1;
			bool bigCollapsed = level >= 2;

			foreach (var b in this._smallButtons) b.SetCollapsed(smallCollapsed);
			foreach (var b in this._bigButtons) b.SetCollapsed(bigCollapsed);
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		Control? active = this._currentLevel == 3 ? this._collapsedBorder : this._normalBorder;
		if (active != null && this.Children.Contains(active))
		{
			active.Measure(new Size(double.PositiveInfinity, 94));
			return active.DesiredSize;
		}
		return new Size(0, 94);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		Control? active = this._currentLevel == 3 ? this._collapsedBorder : this._normalBorder;
		if (active != null && this.Children.Contains(active))
		{
			active.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
		}
		if (this.Children.Contains(this._popup))
		{
			this._popup.Arrange(new Rect(0, 0, 0, 0));
		}
		return finalSize;
	}
}

internal sealed class RibbonSmallButtonInfo
{
	public Button Button { get; }
	public TextBlock TextLabel { get; }
	public TextBlock? ArrowLabel { get; }

	public RibbonSmallButtonInfo(Button button, TextBlock textLabel, TextBlock? arrowLabel)
	{
		this.Button = button;
		this.TextLabel = textLabel;
		this.ArrowLabel = arrowLabel;
	}

	public void SetCollapsed(bool collapsed)
	{
		this.TextLabel.IsVisible = !collapsed;
		if (this.ArrowLabel != null)
		{
			this.ArrowLabel.IsVisible = !collapsed;
		}
		this.Button.Padding = collapsed ? new Thickness(2, 1) : new Thickness(4, 1);
		this.Button.Margin = collapsed ? new Thickness(1, 0) : new Thickness(2, 0);
	}
}

internal sealed class RibbonBigButtonInfo
{
	public Button Button { get; }
	public Control TextControl { get; }

	public RibbonBigButtonInfo(Button button, Control textControl)
	{
		this.Button = button;
		this.TextControl = textControl;
	}

	public void SetCollapsed(bool collapsed)
	{
		this.TextControl.IsVisible = !collapsed;
		if (collapsed)
		{
			this.Button.Width = 36;
			this.Button.MinWidth = 36;
			this.Button.Padding = new Thickness(2, 2);
			this.Button.Margin = new Thickness(1, 0);
		}
		else
		{
			this.Button.Width = double.NaN;
			this.Button.MinWidth = 54;
			this.Button.Padding = new Thickness(4, 2);
			this.Button.Margin = new Thickness(4, 0);
		}
	}
}
