using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Core.Commands;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 명령줄: 위쪽은 스크롤되는 명령 기록, 아래쪽은 테두리로 구분된 "명령어:" 입력 줄.
/// 명령 자동완성은 두 가지로 뜬다 (기존 CAD와 같음).
/// - 도면(캔버스)에서 글자를 치면: 커서 옆에 입력 상자 + 밝은 목록 (동적 입력)
/// - 명령줄 입력 칸에 직접 치면: 명령줄 위에 어두운 목록
/// </summary>
public sealed class CommandLineControl : Border
{
	private const string CmdFont = "굴림체, GulimChe, Consolas, monospace";

	private readonly List<string> _history = new();
	private readonly TextBlock _historyText = new();
	private readonly ScrollViewer _historyScroll = new();
	private readonly TextBlock _promptLabel = new();
	private readonly TextBox _inputBox = new();

	// 명령줄 위 어두운 목록
	private readonly ListBox _lineList;
	private readonly Popup _linePopup = new();

	// 커서 옆 동적 입력: 입력 상자 + 밝은 목록
	private readonly ListBox _dynList;
	private readonly Popup _dynPopup = new();
	private readonly TextBlock _dynText = new();

	// 현재 쓰는 목록/팝업 (모드에 따라 위 둘 중 하나)
	private ListBox _autoCompleteList;
	private Popup _autoCompletePopup;

	/// <summary>true면 마지막 입력이 캔버스에서 온 것 (동적 입력 모드).</summary>
	private bool _dynamic;

	/// <summary>자동완성 목록에 함께 보여 줄 명령 설명 (기존 CAD 한국어판 이름).</summary>
	private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
	{
		["LINE"] = "선", ["PLINE"] = "폴리선", ["RECTANG"] = "직사각형", ["CIRCLE"] = "원", ["ARC"] = "호",
		["TEXT"] = "단일 행 문자", ["MTEXT"] = "여러 줄 문자", ["HATCH"] = "해치",
		["ERASE"] = "지우기", ["MOVE"] = "이동", ["COPY"] = "복사", ["ROTATE"] = "회전", ["SCALE"] = "축척",
		["MIRROR"] = "대칭", ["OFFSET"] = "간격띄우기", ["EXPLODE"] = "분해", ["TRIM"] = "자르기", ["EXTEND"] = "연장",
		["FILLET"] = "모깎기", ["CHAMFER"] = "모따기", ["BREAK"] = "끊기", ["JOIN"] = "결합", ["STRETCH"] = "신축",
		["ARRAY"] = "배열", ["MATCHPROP"] = "특성 일치",
		["DIMLINEAR"] = "선형 치수", ["DIMALIGNED"] = "정렬 치수", ["DIMRADIUS"] = "반지름 치수",
		["DIMDIAMETER"] = "지름 치수", ["DIMANGULAR"] = "각도 치수",
		["DIST"] = "거리", ["LIST"] = "리스트", ["ID"] = "점 좌표", ["AREA"] = "면적",
		["ZOOM"] = "줌", ["PAN"] = "초점 이동", ["U"] = "명령 취소", ["UNDO"] = "명령 취소", ["REDO"] = "명령 복구",
		["LAYER"] = "도면층 특성 관리자", ["LAYOUT"] = "배치", ["TABLEOUT"] = "표 추출(CSV)",
		["TABLE"] = "테이블", ["TABLEEXPORT"] = "표를 CSV로 내보내기",
		["COPYCLIP"] = "클립보드로 복사", ["PASTECLIP"] = "붙여넣기", ["PLOT"] = "플롯(인쇄)",
		["BLOCK"] = "블록 작성", ["INSERT"] = "블록 삽입",
		["EATTEDIT"] = "속성 편집", ["BEDIT"] = "블록 편집기",
		["PROPERTIES"] = "속성 팔레트",
		["POLYGON"] = "폴리곤", ["ELLIPSE"] = "타원", ["SPLINE"] = "스플라인", ["POINT"] = "점", ["DIVIDE"] = "등분할",
		["MEASURE"] = "길이 분할", ["XLINE"] = "구성선", ["RAY"] = "광선", ["DONUT"] = "도넛", ["REVCLOUD"] = "구름형 수정 기호",
		["SOLID"] = "2D 솔리드", ["BOUNDARY"] = "경계",
		["LENGTHEN"] = "길이 조정", ["BREAKATPOINT"] = "점에서 끊기", ["REVERSE"] = "반전", ["ALIGN"] = "정렬",
		["PEDIT"] = "폴리선 편집", ["DRAWORDER"] = "그리기 순서", ["HATCHTOBACK"] = "해치를 뒤로", ["TEXTTOFRONT"] = "문자를 앞으로",
		["SETBYLAYER"] = "도면층별로 설정", ["ARRAYRECT"] = "직사각형 배열", ["ARRAYPOLAR"] = "원형 배열", ["ARRAYPATH"] = "경로 배열",
		["ADDSELECTED"] = "복제(같은 종류 그리기)",
		["LAYMCUR"] = "객체의 도면층을 현재로", ["LAYOFF"] = "도면층 끄기", ["LAYFRZ"] = "도면층 동결", ["LAYLCK"] = "도면층 잠금",
		["LAYULK"] = "도면층 잠금 해제", ["LAYISO"] = "도면층 격리", ["LAYUNISO"] = "도면층 격리 해제", ["LAYCUR"] = "현재 도면층으로 변경",
		["LAYMCH"] = "도면층 일치", ["LAYDEL"] = "도면층 삭제", ["LAYON"] = "모든 도면층 켜기", ["LAYTHW"] = "모든 도면층 동결 해제",
		["LAYERP"] = "이전 도면층 상태",
		["DIMBASELINE"] = "기준선 치수", ["DIMCONTINUE"] = "연속 치수", ["DIMORDINATE"] = "세로좌표 치수", ["DIMARC"] = "호 길이 치수",
		["DIMCENTER"] = "중심 표시", ["LEADER"] = "지시선", ["QLEADER"] = "빠른 지시선", ["MLEADER"] = "지시선(다중)",
		["TXT2MTXT"] = "문자를 여러 줄 문자로", ["TEXTEDIT"] = "문자 편집", ["FIND"] = "찾기 및 바꾸기", ["QDIM"] = "빠른 치수", ["TOLERANCE"] = "기하학적 공차", ["XREF"] = "외부 참조 관리자", ["XATTACH"] = "외부 참조 부착",
		["ATTDEF"] = "속성 정의", ["ATTSYNC"] = "속성 동기화", ["ATTDISP"] = "속성 표시", ["BASE"] = "기준점", ["-ATTEDIT"] = "속성 편집(명령줄)",
		["GROUP"] = "그룹", ["QGROUP"] = "빠른 그룹", ["UNGROUP"] = "그룹 해제", ["QSELECT"] = "빠른 선택", ["PTYPE"] = "점 스타일",
	};

	/// <summary>Core 명령 등록부에 없는, 앱에서 처리하는 명령 (예: PLOT 대화상자).</summary>
	private static readonly List<(string Name, string[] Aliases)> UiCommands = new();

	/// <summary>앱 전용 명령을 자동완성 목록에 추가한다.</summary>
	public static void AddUiCommand(string name, string description, params string[] aliases)
	{
		if (UiCommands.All(c => !c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
		{
			UiCommands.Add((name.ToUpperInvariant(), aliases));
		}
		Descriptions[name] = description;
	}

	private readonly List<string> _suggestions = new();
	private bool _idle = true; // 명령 대기 중("명령어:")일 때만 자동완성

	public event Action<string>? CommandSubmitted;
	public event Action? CancelRequested;

	/// <summary>지금 명령이 문자를 받는 단계인지 (true면 Space = 공백 글자).</summary>
	public Func<bool>? SpaceIsText { get; set; }

	/// <summary>Space 키: 문자를 받는 단계면 공백을 넣고, 아니면 Enter와 같음.</summary>
	public void SpaceOrSubmit()
	{
		if (this.SpaceIsText?.Invoke() == true)
		{
			this.AppendText(" ");
		}
		else
		{
			this.AcceptOrSubmit();
		}
	}

	/// <summary>왼쪽 ▾ 버튼: 전체 기록 창(F2) 요청.</summary>
	public event Action? HistoryRequested;

	public TextBox InputBox => this._inputBox;

	public CommandLineControl()
	{
		this.Background = CadTheme.CmdLineBg;
		this.BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
		this.BorderThickness = new Avalonia.Thickness(1);
		this.Height = 92;
		this.Margin = new Avalonia.Thickness(0);
		this.Padding = new Avalonia.Thickness(0);

		var root = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*") };

		// 왼쪽 띠: × (기록 접기/펴기), ▾ (전체 기록 창)
		var strip = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)) };
		strip.Children.Add(StripButton("×", "명령 기록 접기/펴기", () => this._historyScroll.IsVisible = !this._historyScroll.IsVisible));
		strip.Children.Add(StripButton("▾", "명령 기록 전체 보기 (F2)", () => this.HistoryRequested?.Invoke()));
		root.Children.Add(strip);

		var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Avalonia.Thickness(1) };
		Grid.SetColumn(body, 1);
		root.Children.Add(body);

		// 위: 명령 기록 (스크롤, 흰 글자)
		this._historyText.FontFamily = CmdFont;
		this._historyText.FontSize = 12;
		this._historyText.LineHeight = 14;
		this._historyText.Foreground = Brushes.White;
		this._historyText.Margin = new Avalonia.Thickness(3, 1);
		this._historyScroll.Content = this._historyText;
		this._historyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
		this._historyScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
		body.Children.Add(new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)),
			BorderThickness = new Avalonia.Thickness(1),
			Background = CadTheme.CmdLineBg,
			Child = this._historyScroll,
		});

		// 아래: 테두리로 구분된 입력 줄 "명령어: ___"
		var inputGrid = new Grid { Height = 20, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
		this._promptLabel.Text = "명령어: ";
		this._promptLabel.Foreground = Brushes.White;
		this._promptLabel.VerticalAlignment = VerticalAlignment.Center;
		this._promptLabel.FontFamily = CmdFont;
		this._promptLabel.FontSize = 12;
		this._promptLabel.Margin = new Avalonia.Thickness(3, 0, 2, 0);
		inputGrid.Children.Add(this._promptLabel);

		this._inputBox.Background = Brushes.Transparent;
		this._inputBox.Foreground = Brushes.White;
		this._inputBox.BorderThickness = new Avalonia.Thickness(0);
		this._inputBox.FontFamily = CmdFont;
		this._inputBox.FontSize = 12;
		this._inputBox.VerticalAlignment = VerticalAlignment.Center;
		this._inputBox.Padding = new Avalonia.Thickness(0);
		this._inputBox.MinHeight = 18;
		this._inputBox.Height = 18;
		this._inputBox.KeyDown += this.OnInputKeyDown;
		this._inputBox.TextChanged += this.OnInputTextChanged;
		// 명령줄 칸을 직접 클릭해 입력하면 명령줄 위 목록 모드
		this._inputBox.GotFocus += (_, _) => this._dynamic = false;
		Grid.SetColumn(this._inputBox, 1);
		inputGrid.Children.Add(this._inputBox);

		var inputBorder = new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)),
			BorderThickness = new Avalonia.Thickness(1),
			Margin = new Avalonia.Thickness(0, 2, 0, 0),
			Background = CadTheme.CmdLineBg,
			Child = inputGrid,
		};
		Grid.SetRow(inputBorder, 1);
		body.Children.Add(inputBorder);

		// 명령줄 위 어두운 목록 (기존 CAD의 명령줄 자동완성)
		this._lineList = CreateSuggestList(light: false);
		this._lineList.Tapped += (_, _) => this.SubmitSelected(this._lineList);
		this._linePopup.Placement = PlacementMode.TopEdgeAlignedLeft;
		this._linePopup.PlacementTarget = this;
		this._linePopup.IsLightDismissEnabled = false;
		this._linePopup.Child = this._lineList;

		// 커서 옆 동적 입력: 작은 입력 상자 + 밝은 목록 (기존 CAD의 도면 위 자동완성)
		this._dynList = CreateSuggestList(light: true);
		this._dynList.Tapped += (_, _) => this.SubmitSelected(this._dynList);
		this._dynText.FontFamily = CmdFont;
		this._dynText.FontSize = 12;
		this._dynText.Foreground = Brushes.Black;
		var dynPanel = new StackPanel { Spacing = 2 };
		dynPanel.Children.Add(new Border
		{
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)),
			BorderThickness = new Avalonia.Thickness(1),
			Padding = new Avalonia.Thickness(4, 1),
			HorizontalAlignment = HorizontalAlignment.Left,
			MinWidth = 18,
			Child = this._dynText,
		});
		dynPanel.Children.Add(this._dynList);
		this._dynPopup.Placement = PlacementMode.Pointer;
		this._dynPopup.PlacementTarget = this;
		this._dynPopup.HorizontalOffset = 14;
		this._dynPopup.VerticalOffset = 14;
		this._dynPopup.IsLightDismissEnabled = false;
		this._dynPopup.Child = dynPanel;

		this._autoCompleteList = this._lineList;
		this._autoCompletePopup = this._linePopup;

		// Popup은 논리 트리에 있어야 열린다
		root.Children.Add(this._linePopup);
		root.Children.Add(this._dynPopup);
		this.Child = root;
	}

	private static Button StripButton(string glyph, string tip, Action onClick)
	{
		var b = new Button
		{
			Content = glyph,
			FontSize = 11,
			Foreground = Brushes.White,
			Background = Brushes.Transparent,
			BorderThickness = new Avalonia.Thickness(0),
			Padding = new Avalonia.Thickness(0),
			Width = 16,
			Height = 16,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		ToolTip.SetTip(b, tip);
		b.Click += (_, _) => onClick();
		return b;
	}

	/// <summary>자동완성 목록. light=true면 기존 CAD 동적 입력처럼 흰 바탕, 아니면 어두운 반투명.</summary>
	private static ListBox CreateSuggestList(bool light)
	{
		IBrush fg = light ? Brushes.Black : Brushes.White;
		var list = new ListBox
		{
			Background = light ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xEE, 0x3C, 0x3C, 0x3C)),
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A)),
			BorderThickness = new Avalonia.Thickness(1),
			FontSize = 12,
			Width = 210,
			MaxHeight = 190, // 약 10줄
			Focusable = false, // 입력 포커스는 명령줄(또는 캔버스)에 유지
			ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<SuggestItem>((item, _) => BuildSuggestRow(item)),
		};
		// 항목 높이를 촘촘하게, 글자색은 항목에서 상속 (선택 줄은 파란 바탕 + 흰 글자)
		list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
		{
			Setters =
			{
				new Setter(ListBoxItem.PaddingProperty, new Avalonia.Thickness(2, 0)),
				new Setter(ListBoxItem.MinHeightProperty, 0.0),
				new Setter(ListBoxItem.HeightProperty, 19.0),
				new Setter(ListBoxItem.ForegroundProperty, fg),
			},
		});
		list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>())
		{
			Setters = { new Setter(ContentPresenter.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7))) },
		});
		list.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
		{
			Setters = { new Setter(ListBoxItem.ForegroundProperty, Brushes.White) },
		});
		return list;
	}

	/// <summary>목록 한 줄: 표시 글자("L (_LINE)"), 실행할 명령 이름.</summary>
	private sealed record SuggestItem(string Label, string Command)
	{
		/// <summary>입력과 비교하는 이름 (별칭 또는 명령 이름).</summary>
		public string Key { get; init; } = string.Empty;
	}

	private static Control BuildSuggestRow(SuggestItem? item)
	{
		// 목록 항목이 바뀔 때 재사용되는 칸에 null이 들어온다. 처리하지 않으면 앱이 종료됐음 (명령 여러 번 입력 시 팅김)
		if (item == null)
		{
			return new TextBlock();
		}

		var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,18"), Height = 18 };

		var icon = CommandIcon(item.Command, 13);
		if (icon != null)
		{
			icon.VerticalAlignment = VerticalAlignment.Center;
			grid.Children.Add(icon);
		}

		// 글자색은 지정하지 않고 ListBoxItem에서 상속 (선택되면 흰색)
		var text = new TextBlock
		{
			Text = item.Label,
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
		Grid.SetColumn(text, 1);
		grid.Children.Add(text);

		// 오른쪽 파란 "?" (마우스를 올리면 명령 설명)
		var help = new Border
		{
			Width = 13,
			Height = 13,
			CornerRadius = new Avalonia.CornerRadius(7),
			Background = new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xD6)),
			VerticalAlignment = VerticalAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			Child = new TextBlock { Text = "?", FontSize = 10, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
		};
		if (Descriptions.TryGetValue(item.Command, out string? desc))
		{
			ToolTip.SetTip(help, $"{item.Command}: {desc}");
			ToolTip.SetTip(text, desc);
		}
		Grid.SetColumn(help, 2);
		grid.Children.Add(help);
		return grid;
	}

	/// <summary>명령 이름에 맞는 컬러 아이콘. 없으면 null.</summary>
	internal static Control? CommandIcon(string command, double size = 13)
	{
		string key = command.ToUpperInvariant() switch
		{
			"PLINE" => IconHelper.Polyline,
			"POLYGON" => IconHelper.Polygon,
			"ELLIPSE" => IconHelper.Ellipse,
			"SPLINE" => IconHelper.Spline,
			"POINT" => IconHelper.PointIcon,
			"DIVIDE" => IconHelper.Divide,
			"MEASURE" => IconHelper.Measure,
			"XLINE" => IconHelper.XLine,
			"RAY" => IconHelper.Ray,
			"DONUT" => IconHelper.Donut,
			"REVCLOUD" => IconHelper.RevCloud,
			"SOLID" => IconHelper.Plane,
			"BOUNDARY" => IconHelper.Boundary,
			"LENGTHEN" => IconHelper.Lengthen,
			"BREAKATPOINT" => IconHelper.BreakAtPoint,
			"REVERSE" => IconHelper.Reverse,
			"ALIGN" => IconHelper.Align,
			"PEDIT" => IconHelper.EditPline,
			"DRAWORDER" => IconHelper.DrawFront,
			"HATCHTOBACK" => IconHelper.HatchBack,
			"TEXTTOFRONT" => IconHelper.TextFront,
			"SETBYLAYER" => IconHelper.SetByLayer,
			"ARRAYRECT" => IconHelper.Array,
			"ARRAYPOLAR" => IconHelper.ArrayPolar,
			"ARRAYPATH" => IconHelper.ArrayPath,
			"ADDSELECTED" => IconHelper.Clone,
			"LAYMCUR" => IconHelper.LayerMakeCurrent,
			"LAYOFF" => IconHelper.LayerOff,
			"LAYFRZ" => IconHelper.LayerFreeze,
			"LAYLCK" => IconHelper.LayerLock,
			"LAYULK" => IconHelper.LayerUnlock,
			"LAYISO" => IconHelper.LayerIsolate,
			"LAYUNISO" => IconHelper.LayerState,
			"LAYCUR" => IconHelper.LayerChangeCurrent,
			"LAYMCH" => IconHelper.MatchLayer,
			"LAYDEL" => IconHelper.LayerDelete,
			"LAYON" => IconHelper.LayerOn,
			"LAYTHW" => IconHelper.LayerThaw,
			"LAYERP" => IconHelper.LayerPrevious,
			"DIMBASELINE" => IconHelper.DimBaseline,
			"DIMCONTINUE" => IconHelper.DimContinue,
			"DIMORDINATE" => IconHelper.DimOrdinate,
			"DIMARC" => IconHelper.DimArc,
			"DIMCENTER" => IconHelper.DimCenter,
			"LEADER" or "QLEADER" or "MLEADER" => IconHelper.Leader,
			"TXT2MTXT" => IconHelper.Txt2Mtxt,
			"TEXTEDIT" => IconHelper.TextEdit,
			"ATTDEF" => IconHelper.AttDef,
			"ATTSYNC" => IconHelper.AttSync,
			"ATTDISP" => IconHelper.AttDisp,
			"BASE" => IconHelper.BasePoint,
			"-ATTEDIT" => IconHelper.EditAttribute,
			"GROUP" => IconHelper.Group,
			"QGROUP" => IconHelper.QGroup,
			"UNGROUP" => IconHelper.Ungroup,
			"QSELECT" => IconHelper.QuickSelect,
			"PTYPE" => IconHelper.PointStyle,
			"TABLE" => IconHelper.Table,
			"TABLEEXPORT" => IconHelper.TableOut,
			"U" or "UNDO" => IconHelper.Undo,
			"LAYOUT" => IconHelper.Layer,
			"MTEXT" => IconHelper.MText,
			"COPYCLIP" => IconHelper.Copy,
			"LIST" or "ID" or "AREA" => IconHelper.Properties,
			"ZOOM" => IconHelper.ZoomIn,
			"RECTANG" => IconHelper.Rectang,
			"PLOT" => IconHelper.Print,
			"CUTCLIP" => IconHelper.Cut,
			"PASTECLIP" => IconHelper.Paste,
			"INSERT" => IconHelper.BlockInsert,
			"BLOCK" => IconHelper.BlockCreate,
			"LAYER" => IconHelper.Layer,
			"PAN" => IconHelper.Pan,
			"STYLE" => IconHelper.TextStyle,
			"DIMSTYLE" => IconHelper.DimStyle,
			"DSETTINGS" => IconHelper.Settings,
			"UNITS" => IconHelper.Units,
			"GRADIENT" => IconHelper.Gradient,
			"LINETYPE" => IconHelper.Linetype,
			"COLOR" => IconHelper.ColorWheel,
			"HELP" => IconHelper.Help,
			"REGEN" or "REGENALL" => IconHelper.Regen,
			"RESETSETTINGS" => IconHelper.ResetSettings,
			"OPTIONS" => IconHelper.Settings,
			"BLOCKLIB" => IconHelper.BlockInsert,
			_ => command,
		};
		return IconHelper.TryGetIcon(key, size);
	}

	private void SubmitSelected(ListBox list)
	{
		int i = list.SelectedIndex;
		if (i >= 0 && i < this._suggestions.Count)
		{
			this.Submit(this._suggestions[i]);
		}
	}

	/// <summary>자동완성 목록 위/아래 이동, Tab 완성. 목록이 열려 있어 처리했으면 true.</summary>
	public bool HandleListKey(Key key)
	{
		if (!this._autoCompletePopup.IsOpen || this._suggestions.Count == 0)
		{
			return false;
		}
		int i = this._autoCompleteList.SelectedIndex;
		switch (key)
		{
			case Key.Down:
				this._autoCompleteList.SelectedIndex = Math.Min(i + 1, this._suggestions.Count - 1);
				break;
			case Key.Up:
				this._autoCompleteList.SelectedIndex = Math.Max(i - 1, 0);
				break;
			case Key.Tab:
				string pick = this._suggestions[Math.Max(i, 0)];
				this._inputBox.Text = pick;
				this._inputBox.CaretIndex = pick.Length;
				break;
			default:
				return false;
		}
		this._autoCompleteList.ScrollIntoView(this._autoCompleteList.SelectedIndex);
		return true;
	}

	/// <summary>Enter/Space: 목록에서 고른 항목이 있으면 그 명령, 없으면 입력한 글자 그대로 실행.</summary>
	public void AcceptOrSubmit()
	{
		int i = this._autoCompleteList.SelectedIndex;
		if (this._autoCompletePopup.IsOpen && this._autoCompleteList.IsVisible && i >= 0 && i < this._suggestions.Count)
		{
			this.Submit(this._suggestions[i]);
		}
		else
		{
			this.Submit();
		}
	}

	/// <summary>목록(또는 동적 입력 상자)이 열려 있으면 닫고 true.</summary>
	public bool CloseSuggestions()
	{
		bool wasOpen = this._linePopup.IsOpen || this._dynPopup.IsOpen;
		this._linePopup.IsOpen = false;
		this._dynPopup.IsOpen = false;
		if (wasOpen)
		{
			// 기존 CAD처럼 Esc는 입력한 글자도 지운다
			this._inputBox.Text = string.Empty;
		}
		return wasOpen;
	}

	/// <summary>명령 기록에 한 줄 추가하고 맨 아래로 스크롤.</summary>
	public void Print(string message)
	{
		if (string.IsNullOrEmpty(message)) return;

		this._history.Add(message);
		if (this._history.Count > 500)
		{
			this._history.RemoveRange(0, this._history.Count - 500);
		}
		this._historyText.Text = string.Join("\n", this._history);
		Avalonia.Threading.Dispatcher.UIThread.Post(() => this._historyScroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
	}

	/// <summary>
	/// 명령 진행 기록 (기존 CAD처럼): 지금 질문(프롬프트)과 입력한 값을 기록에 한 줄로 남긴다. 명령어 대기 중이면 남기지 않음.
	/// </summary>
	public void EchoPrompt(string input)
	{
		if (this._idle)
		{
			return;
		}
		string prompt = this._promptLabel.Text?.TrimEnd() ?? string.Empty;
		this.Print(string.IsNullOrEmpty(input) ? prompt : $"{prompt} {input}");
	}

	public void SetPrompt(string prompt)
	{
		this._idle = string.IsNullOrEmpty(prompt) || prompt.TrimEnd(':', ' ') == "명령어";
		this._promptLabel.Text = string.IsNullOrEmpty(prompt) ? "명령어: " : prompt + " ";
		if (!this._idle)
		{
			this._linePopup.IsOpen = false;
			this._dynList.IsVisible = false;
		}
	}

	public void FocusInput()
	{
		this._inputBox.Focus();
	}

	/// <summary>캔버스에서 친 글자 (동적 입력 모드).</summary>
	public void AppendText(string text)
	{
		this._dynamic = true;
		this._inputBox.Text += text;
		this._inputBox.CaretIndex = this._inputBox.Text.Length;
	}

	public void Backspace()
	{
		if (!string.IsNullOrEmpty(this._inputBox.Text))
		{
			this._inputBox.Text = this._inputBox.Text[..^1];
			this._inputBox.CaretIndex = this._inputBox.Text.Length;
		}
	}

	public void Submit(string? overrideText = null)
	{
		// 입력값을 먼저 읽는다: CloseSuggestions는 (Esc 동작을 위해) 입력 글자를 지우므로,
		// 먼저 닫으면 도면 위에서 친 좌표·값(예: @100,0)이 빈 값으로 넘어가 명령이 끝나 버렸다
		string text = overrideText ?? this._inputBox.Text ?? string.Empty;
		this.CloseSuggestions();
		this._inputBox.Text = string.Empty;
		this.CommandSubmitted?.Invoke(text);
	}

	public IReadOnlyList<string> GetAllHistory() => this._history;

	/// <summary>
	/// 기존 CAD 방식: 명령 이름과 별칭을 각각 한 줄로 만들어, 입력한 글자로 시작하는 것을 알파벳순으로 나열한다.
	/// 별칭은 "L (_LINE)", 명령 이름은 "LINE"으로 표시. 입력과 정확히 같은 줄(없으면 첫 줄)을 선택해 둔다.
	/// </summary>
	private void OnInputTextChanged(object? sender, TextChangedEventArgs e)
	{
		string raw = this._inputBox.Text ?? string.Empty;
		string query = raw.Trim();

		// 동적 입력 모드면 쓰는 목록을 커서 옆 것으로, 아니면 명령줄 위 것으로
		this._autoCompleteList = this._dynamic ? this._dynList : this._lineList;
		this._autoCompletePopup = this._dynamic ? this._dynPopup : this._linePopup;
		(this._dynamic ? this._linePopup : this._dynPopup).IsOpen = false;

		// 동적 입력 상자는 글자가 있으면 항상 보인다 (명령 도중 좌표 입력 포함)
		if (this._dynamic)
		{
			this._dynText.Text = raw;
			if (raw.Length == 0)
			{
				this._dynPopup.IsOpen = false;
				return;
			}
		}

		// 명령 도중(좌표·숫자 입력)이나 좌표 형식 입력에는 목록을 띄우지 않는다
		bool listAllowed = this._idle && query.Length > 0 && char.IsLetter(query[0]) && query.IndexOfAny(new[] { ',', '@', '<', ' ' }) < 0;

		List<SuggestItem> items = !listAllowed ? new() : CommandRegistry.All.Concat(UiCommands)
			.SelectMany(c => c.Aliases.Select(a => new SuggestItem($"{a.ToUpperInvariant()} (_{c.Name})", c.Name) { Key = a })
				.Append(new SuggestItem(c.Name, c.Name) { Key = c.Name }))
			.Where(s => s.Key.StartsWith(query, StringComparison.OrdinalIgnoreCase))
			.GroupBy(s => s.Key.ToUpperInvariant())
			.Select(g => g.First())
			.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();

		this._suggestions.Clear();
		this._suggestions.AddRange(items.Select(s => s.Command));

		if (items.Count == 0)
		{
			if (this._dynamic)
			{
				// 입력 상자만 보여 준다
				this._dynList.IsVisible = false;
				this._dynPopup.IsOpen = true;
			}
			else
			{
				this._linePopup.IsOpen = false;
			}
			return;
		}

		this._autoCompleteList.IsVisible = true;
		this._autoCompleteList.ItemsSource = items;
		int exact = items.FindIndex(s => s.Key.Equals(query, StringComparison.OrdinalIgnoreCase));
		this._autoCompleteList.SelectedIndex = exact >= 0 ? exact : 0;
		this._autoCompletePopup.IsOpen = true;
	}

	private void OnInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (this.HandleListKey(e.Key))
		{
			e.Handled = true;
		}
		else if (e.Key == Key.Space && this.SpaceIsText?.Invoke() == true)
		{
			// 문자 입력 단계: 입력 칸이 공백을 그대로 받는다
		}
		else if (e.Key == Key.Enter || e.Key == Key.Space)
		{
			this.AcceptOrSubmit();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			if (!this.CloseSuggestions())
			{
				this._inputBox.Text = string.Empty;
				this.CancelRequested?.Invoke();
			}
			e.Handled = true;
		}
	}
}
