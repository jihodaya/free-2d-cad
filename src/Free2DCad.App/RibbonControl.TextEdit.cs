using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ACadSharp.Entities;
using AcColor = ACadSharp.Color;

namespace Free2DCad.App;

/// <summary>문자 편집기 탭이 부르는 동작 (MainWindow가 채운다). null이면 그 버튼은 꺼짐.</summary>
internal sealed class TextEditActions
{
	public IReadOnlyList<string> StyleNames { get; init; } = Array.Empty<string>();

	public Action<string>? SetStyle { get; init; }
	public Action<double>? SetHeight { get; init; }
	public Action? Bold { get; init; }
	public Action? Italic { get; init; }
	public Action? Strike { get; init; }
	public Action? Underline { get; init; }
	public Action? Overline { get; init; }
	public Action? Superscript { get; init; }
	public Action? Subscript { get; init; }
	public Action? UpperSelection { get; init; }
	public Action? LowerSelection { get; init; }
	public Action<string>? SetFont { get; init; }
	public Action<AcColor>? SetColor { get; init; }
	public Action<double>? SetOblique { get; init; }
	public Action<double>? SetTracking { get; init; }
	public Action<double>? SetWidth { get; init; }
	public Action? ClearCharFormat { get; init; }
	public Action? ClearParagraphFormat { get; init; }
	public Action? ClearAllFormat { get; init; }
	public Action? CombineParagraphs { get; init; }
	public Action<AttachmentPointType>? SetAttachment { get; init; }
	public Action<char>? SetParagraphAlign { get; init; }
	public Action<double>? SetLineSpacing { get; init; }
	public Action<string>? SetList { get; init; }
	public Action? Paragraph { get; init; }
	public Action? BackgroundMask { get; init; }
	public Action<bool>? SpellCheck { get; init; }
	/// <summary>문자 세트 (\f글꼴|c번호;) — 기존 CAD "더 보기 ▸ 문자 세트".</summary>
	public Action<int>? SetCharset { get; init; }
	public Action? HighlightColor { get; init; }
	public Action? Help { get; init; }
	/// <summary>열: "none", "dynamic-auto", "dynamic-manual", "static:N", "settings", "settings-static".</summary>
	public Action<string>? SetColumns { get; init; }
	/// <summary>지금 열 종류 (0 없음, 1 정적, 2 동적) — 메뉴 체크 표시.</summary>
	public Func<int>? ColumnType { get; init; }
	public Action<string>? Insert { get; init; }
	public Action<string>? InsertField { get; init; }
	public Action? Undo { get; init; }
	public Action? Redo { get; init; }
	public Action? FindReplace { get; init; }
	public Action? ImportText { get; init; }
	public Action<bool>? AutoCaps { get; init; }
	public Action<bool>? Ruler { get; init; }
	public Action? Stack { get; init; }
	public Action? Done { get; init; }
}

/// <summary>선택한 글자의 지금 서식 (버튼 눌림·목록 표시).</summary>
internal sealed record TextEditState(
	bool Bold, bool Italic, bool Strike, bool Underline, bool Overline,
	string? Font, AcColor? Color, double Oblique, double Tracking, double Width,
	string? StyleName, double Height, AttachmentPointType? Attachment, bool AutoCaps, bool Ruler);

/// <summary>
/// 문자 편집기 탭 (기존 CAD "텍스트 편집"): 문자를 고치는 동안만 리본에 나타난다.
/// 스타일 / 서식 / 단락 / 삽입 / 도구 / 옵션 / 닫기 — 기존 CAD와 같은 배치·동작.
/// </summary>
public sealed partial class RibbonControl
{
	private TabItem? _textEditTab;
	private object? _tabBeforeTextEdit;
	private bool _teUpdating;
	private ComboBox? _teStyle, _teHeight, _teFont, _teColor;
	private TextBox? _teOblique, _teTracking, _teWidth;
	private ToggleButton? _teBold, _teItalic, _teStrike, _teUnderline, _teOverline, _teAutoCaps, _teRuler;
	private TextBlock? _teJustifyLabel;

	private static readonly IBrush TextEditTabBg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF4, 0xC7, 0xD0));
	private static readonly IBrush TextEditTabFg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x30, 0x10, 0x18));
	// 기존 CAD 서식 버튼: 연한 청회색 사각 버튼 + 남색 글자, 눌린 상태는 진한 파랑
	private static readonly IBrush FmtBtnBg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC9, 0xD5, 0xE4));
	private static readonly IBrush FmtBtnOn = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x5E, 0x92, 0xD6));
	private static readonly IBrush FmtBtnFg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x1D, 0x3D, 0x7A));
	private static readonly IBrush DarkFieldBg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x3A, 0x3A));
	private static readonly IBrush DarkFieldFg = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE6, 0xE6, 0xE6));
	private static readonly IBrush DarkFieldBorder = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x5A, 0x5A, 0x5A));

	private static readonly (string Name, AcColor Color, IBrush Brush)[] TeColors =
	{
		("ByLayer", AcColor.ByLayer, Brushes.White), ("ByBlock", AcColor.ByBlock, Brushes.White),
		("빨강", new AcColor(1), Brushes.Red), ("노랑", new AcColor(2), Brushes.Yellow), ("초록", new AcColor(3), Brushes.Lime),
		("하늘", new AcColor(4), Brushes.Cyan), ("파랑", new AcColor(5), Brushes.Blue), ("자홍", new AcColor(6), Brushes.Magenta),
		("흰색", new AcColor(7), Brushes.White), ("회색", new AcColor(8), Brushes.Gray), ("밝은 회색", new AcColor(9), Brushes.LightGray),
	};

	/// <summary>기호 목록 (리본 "기호" 메뉴와 문자 편집 오른쪽 클릭 메뉴).</summary>
	internal static readonly (string Label, string Text)[] TextSymbols =
	{
		("도 %%d", "°"), ("플러스/마이너스 %%p", "±"), ("지름 %%c", "Ø"), ("거의 같음 \\U+2248", "≈"), ("각도 \\U+2220", "∠"),
		("중심선 \\U+2104", "℄"), ("델타 \\U+0394", "Δ"), ("전기 위상 \\U+0278", "ɸ"), ("항등 \\U+2261", "≡"),
		("같지 않음 \\U+2260", "≠"), ("오옴 \\U+2126", "Ω"), ("오메가 \\U+03A9", "Ω"), ("지계 선 \\U+214A", "⅊"),
		("아래 첨자 2 \\U+2082", "₂"), ("제곱 \\U+00B2", "²"), ("세제곱 \\U+00B3", "³"), ("줄 바꿈하지 않는 공백", " "),
	};

	private static readonly (string Label, AttachmentPointType Ap)[] TeJustify =
	{
		("상단 왼쪽", AttachmentPointType.TopLeft), ("상단 중심", AttachmentPointType.TopCenter), ("상단 오른쪽", AttachmentPointType.TopRight),
		("중간 왼쪽", AttachmentPointType.MiddleLeft), ("중간 중심", AttachmentPointType.MiddleCenter), ("중간 오른쪽", AttachmentPointType.MiddleRight),
		("하단 왼쪽", AttachmentPointType.BottomLeft), ("하단 중심", AttachmentPointType.BottomCenter), ("하단 오른쪽", AttachmentPointType.BottomRight),
	};

	/// <summary>컨트롤이 문자 편집기 탭(또는 그 펼침 목록) 안에 있는지 — 이때는 문자 편집을 끝내지 않는다.</summary>
	internal bool IsTextEditUi(object? element)
	{
		if (this._textEditTab == null || element is not Visual v)
		{
			return false;
		}
		for (Visual? cur = v; cur != null; cur = cur.GetVisualParent())
		{
			if (ReferenceEquals(cur, this) || cur is PopupRoot or OverlayPopupHost)
			{
				return true;
			}
		}
		return false;
	}

	internal void ShowTextEditTab(TextEditActions a)
	{
		if (this._textEditTab == null)
		{
			this._tabBeforeTextEdit = this._tabs.SelectedItem;
		}
		else
		{
			this._tabs.Items.Remove(this._textEditTab);
		}
		var tab = new TabItem { Header = "텍스트 편집", Content = this.BuildTextEditTab(a), Background = TextEditTabBg, Foreground = TextEditTabFg };
		this._tabs.Items.Insert(Math.Max(0, this._tabs.Items.Count - 1), tab); // 도구 탭 뒤 (기존 CAD와 같은 자리)
		this._textEditTab = tab;
		this._tabs.SelectedItem = tab;
	}

	internal void HideTextEditTab()
	{
		if (this._textEditTab == null)
		{
			return;
		}
		var tab = this._textEditTab;
		this._textEditTab = null;
		this._tabs.Items.Remove(tab);
		this._tabs.SelectedItem = this._tabBeforeTextEdit != null && this._tabs.Items.Contains(this._tabBeforeTextEdit) ? this._tabBeforeTextEdit : this._tabs.Items[0];
	}

	/// <summary>선택한 글자의 서식으로 버튼 눌림·목록 값 갱신 (기존 CAD처럼).</summary>
	internal void UpdateTextEditState(TextEditState s)
	{
		if (this._textEditTab == null)
		{
			return;
		}
		this._teUpdating = true;
		try
		{
			static void T(ToggleButton? b, bool on)
			{
				if (b == null) return;
				b.IsChecked = on;
				b.Background = on ? FmtBtnOn : FmtBtnBg;
			}
			T(this._teBold, s.Bold);
			T(this._teItalic, s.Italic);
			T(this._teStrike, s.Strike);
			T(this._teUnderline, s.Underline);
			T(this._teOverline, s.Overline);
			if (this._teAutoCaps != null) this._teAutoCaps.IsChecked = s.AutoCaps;
			if (this._teRuler != null) this._teRuler.IsChecked = s.Ruler;
			if (this._teFont != null)
			{
				// 목록에 없는 글꼴(SHX 등)도 기존 CAD처럼 이름을 보인다
				if (s.Font != null && this._teFont.ItemsSource is System.Collections.ObjectModel.ObservableCollection<string> fonts && !fonts.Contains(s.Font))
				{
					fonts.Insert(0, s.Font);
				}
				this._teFont.SelectedItem = s.Font;
			}
			if (this._teColor != null)
			{
				this._teColor.SelectedIndex = s.Color is { } c ? Array.FindIndex(TeColors, x => x.Color.Equals(c)) : 0;
			}
			if (this._teOblique != null) this._teOblique.Text = s.Oblique.ToString("0.####");
			if (this._teTracking != null) this._teTracking.Text = s.Tracking.ToString("0.####");
			if (this._teWidth != null) this._teWidth.Text = s.Width.ToString("0.####");
			if (this._teStyle != null) this._teStyle.SelectedItem = s.StyleName;
			if (this._teHeight != null)
			{
				this._teHeight.SelectedItem = null;
				this._teHeight.PlaceholderText = s.Height.ToString("0.####");
			}
			if (this._teJustifyLabel != null)
			{
				string label = TeJustify.FirstOrDefault(j => j.Ap == s.Attachment).Label ?? "상단 왼쪽";
				this._teJustifyLabel.Text = label.Replace(' ', '\n');
			}
		}
		finally
		{
			this._teUpdating = false;
		}
	}

	private Control BuildTextEditTab(TextEditActions a)
	{
		var panel = this.CreatePanelRow();

		// 1) 스타일: 문자 스타일 / 높이 / 주석
		this._teStyle = DarkCombo(130, a.StyleNames.ToList(), a.SetStyle != null, "문자 스타일");
		this._teStyle.SelectionChanged += (_, _) =>
		{
			if (!this._teUpdating && this._teStyle.SelectedItem is string n) a.SetStyle?.Invoke(n);
		};
		this._teHeight = DarkCombo(130, new[] { "1", "1.5", "2", "2.5", "3", "3.5", "4", "5", "7", "10" }.ToList(), a.SetHeight != null, "문자 높이 (선택한 글자, 없으면 전체)");
		this._teHeight.SelectionChanged += (_, _) =>
		{
			if (!this._teUpdating && this._teHeight.SelectedItem is string h && double.TryParse(h, out double v)) a.SetHeight?.Invoke(v);
		};
		var annot = new TextBlock { Text = "주석", FontSize = 12, Foreground = Brushes.Gray, Margin = new Thickness(4, 3), [ToolTip.TipProperty] = "주석 축척 (상태 막대의 주석 축척을 따름)" };
		panel.AddPanel(this.WrapInPanel("스타일", Column(this._teStyle, this._teHeight, annot)));

		// 2) 서식: 3×3 서식 버튼 / 글꼴·색 / 기울기·자간·폭 / 서식 지우기·단락 결합
		this._teBold = FmtToggle("B", "굵게", a.Bold, FontWeight.Bold);
		this._teItalic = FmtToggle("I", "기울임꼴", a.Italic, FontWeight.Bold, FontStyle.Italic);
		this._teStrike = FmtToggle("A", "취소선", a.Strike, FontWeight.Bold, deco: TextDecorations.Strikethrough);
		this._teUnderline = FmtToggle("U", "밑줄", a.Underline, FontWeight.Bold, deco: TextDecorations.Underline);
		this._teOverline = FmtToggle("Ō", "윗줄", a.Overline, FontWeight.Bold);
		var sup = FmtToggle("x²", "위 첨자", a.Superscript, FontWeight.Bold);
		var upper = FmtToggle("aA", "대문자로", a.UpperSelection, FontWeight.Bold, size: 11);
		var lower = FmtToggle("Aa", "소문자로", a.LowerSelection, FontWeight.Bold, size: 11);
		var sub = FmtToggle("x₂", "아래 첨자", a.Subscript, FontWeight.Bold);
		var grid = Column(RowOf(this._teBold, this._teItalic, this._teStrike), RowOf(this._teUnderline, this._teOverline, sup), RowOf(upper, lower, sub));

		this._teFont = LightCombo(170, FontNames(), a.SetFont != null, "글꼴 (선택한 글자)");
		this._teFont.PlaceholderText = "글꼴";
		this._teFont.ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock { Text = name, FontSize = 12, Foreground = Brushes.Black, FontFamily = SafeFont(name) });
		this._teFont.SelectionChanged += (_, _) =>
		{
			if (!this._teUpdating && this._teFont.SelectedItem is string f) a.SetFont?.Invoke(f);
		};
		this._teColor = LightCombo(170, TeColors.Select(c => c.Name).ToList(), a.SetColor != null, "색상 (선택한 글자)");
		this._teColor.ItemTemplate = new FuncDataTemplate<string>((name, _) =>
		{
			var c = TeColors.FirstOrDefault(x => x.Name == name);
			return new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6,
				Children =
				{
					new Border { Width = 12, Height = 10, Background = c.Brush ?? Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center },
					new TextBlock { Text = name, FontSize = 12, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center },
				},
			};
		});
		this._teColor.SelectionChanged += (_, _) =>
		{
			if (!this._teUpdating && this._teColor.SelectedIndex >= 0) a.SetColor?.Invoke(TeColors[this._teColor.SelectedIndex].Color);
		};
		var mask = IconButton(IconHelper.TeMask, "배경 마스크", a.BackgroundMask);
		var fontCol = Column(this._teFont, this._teColor, RowOf(mask));

		this._teOblique = DarkNumber(0, a.SetOblique, "기울기 각도 (선택한 글자)");
		this._teTracking = DarkNumber(1, a.SetTracking, "자간 (선택한 글자)");
		this._teWidth = DarkNumber(1, a.SetWidth, "폭 비율 (선택한 글자)");
		var nums = Column(
			RowOf(IconHelper.GetIcon(IconHelper.TeOblique, 20), this._teOblique),
			RowOf(IconHelper.GetIcon(IconHelper.TeTracking, 20), this._teTracking),
			RowOf(IconHelper.GetIcon(IconHelper.TeWidth, 20), this._teWidth));

		var clear = SmallMenuButton(IconHelper.TeClearFormat, "서식 지우기", new (string, Action?)[]
		{
			("문자 서식 지우기", a.ClearCharFormat), ("단락 서식 지우기", a.ClearParagraphFormat), ("모두 지우기", a.ClearAllFormat),
		});
		var combine = SmallButton(IconHelper.TeCombine, "단락 결합", a.CombineParagraphs);
		var fmt = Row();
		fmt.Children.Add(grid);
		fmt.Children.Add(fontCol);
		fmt.Children.Add(nums);
		fmt.Children.Add(Column(clear, combine));
		panel.AddPanel(this.WrapInPanel("서식", fmt));

		// 3) 단락: 자리맞춤 / 목록·줄 간격·단락 / 정렬
		this._teJustifyLabel = new TextBlock { Text = "상단\n왼쪽", FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
		var justify = BigMenuButton(IconHelper.TeJustify, this._teJustifyLabel, "자리맞춤", a.SetAttachment == null ? null : TeJustify.Select(j => (j.Label, (Action?)(() => a.SetAttachment(j.Ap)))).ToArray());
		var list = SmallMenuButton(IconHelper.TeList, "목록", a.SetList == null ? null : new (string, Action?)[]
		{
			("끄기", () => a.SetList("off")), ("문자 (a. b. c.)", () => a.SetList("a")), ("숫자 (1. 2. 3.)", () => a.SetList("1")), ("글머리 기호 (•)", () => a.SetList("•")),
		});
		var spacing = SmallMenuButton(IconHelper.TeLineSpacing, "줄 간격", a.SetLineSpacing == null ? null : new[] { 1.0, 1.5, 2.0, 2.5 }.Select(f => ($"{f:0.0}x", (Action?)(() => a.SetLineSpacing(f)))).ToArray());
		var paragraph = SmallButton(IconHelper.TeParagraph, "단락", a.Paragraph);
		var aligns = Column(
			RowOf(IconButton(IconHelper.TeAlignLeft, "왼쪽 정렬", a.SetParagraphAlign == null ? null : () => a.SetParagraphAlign('l')),
				IconButton(IconHelper.TeAlignCenter, "가운데 정렬", a.SetParagraphAlign == null ? null : () => a.SetParagraphAlign('c')),
				IconButton(IconHelper.TeAlignRight, "오른쪽 정렬", a.SetParagraphAlign == null ? null : () => a.SetParagraphAlign('r'))),
			RowOf(IconButton(IconHelper.TeAlignJustify, "양쪽 맞춤", a.SetParagraphAlign == null ? null : () => a.SetParagraphAlign('j')),
				IconButton(IconHelper.TeAlignDistribute, "균등 분할", a.SetParagraphAlign == null ? null : () => a.SetParagraphAlign('d'))));
		var par = Row();
		par.Children.Add(justify);
		par.Children.Add(Column(list, spacing, paragraph));
		par.Children.Add(aligns);
		panel.AddPanel(this.WrapInPanel("단락", par));

		// 4) 삽입: 취소·명령 복구 / 기호·필드 삽입·열
		var symbols = TextSymbols;
		var ins = Row();
		ins.Children.Add(BigButton(IconHelper.Undo, "취소", a.Undo));
		ins.Children.Add(BigButton(IconHelper.Redo, "명령\n복구", a.Redo));
		ins.Children.Add(Column(
			SmallMenuButton(IconHelper.TeSymbol, "기호", a.Insert == null ? null : symbols.Select(sy => (sy.Label, (Action?)(() => a.Insert(sy.Text)))).ToArray()),
			SmallMenuButton(IconHelper.TeField, "필드 삽입", a.InsertField == null ? null : new (string, Action?)[]
			{
				("날짜", () => a.InsertField("date")), ("날짜와 시간", () => a.InsertField("datetime")), ("파일 이름", () => a.InsertField("filename")), ("작성자", () => a.InsertField("author")),
			}),
			this.ColumnsButton(a)));
		panel.AddPanel(this.WrapInPanel("삽입", ins));

		// 5) 도구: 찾기 및 바꾸기 / 텍스트 가져오기·자동 대문자
		var tools = Row();
		tools.Children.Add(BigButton(IconHelper.TeFind, "Find and\nReplace", a.FindReplace));
		this._teAutoCaps = SmallToggle(IconHelper.TeAutoCaps, "자동 대문자", a.AutoCaps);
		tools.Children.Add(Column(SmallButton(IconHelper.TeImport, "텍스트 가져오기", a.ImportText), this._teAutoCaps));
		panel.AddPanel(this.WrapInPanel("도구", tools));

		// 6) 옵션: 맞춤법 검사 / 자·스택 텍스트·더 보기
		var opts = Row();
		// 맞춤법 검사: 켜고 끔 (기존 CAD처럼 기본 켜짐, 틀린 영어 단어에 빨간 물결선)
		var spell = new ToggleButton { Padding = new Thickness(4, 2), Margin = new Thickness(2, 0), MinWidth = 48, Background = Brushes.Transparent, IsEnabled = a.SpellCheck != null && TextSpellChecker.IsAvailable, VerticalAlignment = VerticalAlignment.Stretch, IsChecked = TextSpellChecker.Enabled && TextSpellChecker.IsAvailable };
		spell.Content = new StackPanel
		{
			Spacing = 2,
			HorizontalAlignment = HorizontalAlignment.Center,
			Children =
			{
				IconHelper.GetIcon(IconHelper.TeSpell, 30),
				new TextBlock { Text = "맞춤법\n검사", FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
			},
		};
		spell.IsCheckedChanged += (_, _) => a.SpellCheck?.Invoke(spell.IsChecked == true);
		opts.Children.Add(spell);
		this._teRuler = SmallToggle(IconHelper.TeRuler, "자", a.Ruler);
		opts.Children.Add(Column(this._teRuler, SmallButton(IconHelper.TeStack, "스택 텍스트", a.Stack), SmallMenuButton(IconHelper.TeMore, "더 보기", MoreMenu(a))));
		panel.AddPanel(this.WrapInPanel("옵션", opts));

		// 7) 닫기: 완료
		panel.AddPanel(this.WrapInPanel("닫기", BigButton(IconHelper.Checkmark, "완료", a.Done)));
		return panel;
	}

	private static FontFamily SafeFont(string name)
	{
		try
		{
			return new FontFamily(name);
		}
		catch (Exception)
		{
			return FontFamily.Default;
		}
	}

	private static ComboBox DarkCombo(double width, List<string> items, bool enabled, string tip) => new()
	{
		Width = width,
		Height = 22,
		MinHeight = 22,
		FontSize = 12,
		Padding = new Thickness(4, 0),
		Background = DarkFieldBg,
		Foreground = DarkFieldFg,
		BorderBrush = DarkFieldBorder,
		ItemsSource = items,
		IsEnabled = enabled,
		[ToolTip.TipProperty] = tip,
	};

	private static ComboBox LightCombo(double width, IList<string> items, bool enabled, string tip) => new()
	{
		Width = width,
		Height = 22,
		MinHeight = 22,
		FontSize = 12,
		Padding = new Thickness(4, 0),
		Background = CadTheme.DropdownBg,
		Foreground = CadTheme.DropdownFg,
		BorderBrush = CadTheme.DropdownBorder,
		ItemsSource = items,
		IsEnabled = enabled,
		[ToolTip.TipProperty] = tip,
	};

	/// <summary>어두운 숫자 칸: Enter 또는 칸을 벗어날 때 적용.</summary>
	private static TextBox DarkNumber(double value, Action<double>? apply, string tip)
	{
		var box = new TextBox
		{
			Text = value.ToString("0.####"),
			Width = 74,
			Height = 22,
			MinHeight = 22,
			FontSize = 12,
			Padding = new Thickness(4, 1),
			Background = DarkFieldBg,
			Foreground = DarkFieldFg,
			BorderBrush = DarkFieldBorder,
			IsEnabled = apply != null,
			[ToolTip.TipProperty] = tip,
		};
		string last = box.Text;
		void Apply()
		{
			if (box.Text != last && double.TryParse(box.Text, out double v))
			{
				last = box.Text;
				apply?.Invoke(v);
			}
		}
		box.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter)
			{
				Apply();
				e.Handled = true;
			}
		};
		box.LostFocus += (_, _) => Apply();
		box.PropertyChanged += (_, e) =>
		{
			if (e.Property == TextBox.TextProperty && !box.IsFocused) last = box.Text ?? string.Empty; // 선택 서식으로 바뀐 값
		};
		return box;
	}

	/// <summary>기존 CAD 서식 사각 버튼 (눌림 상태 표시).</summary>
	private static ToggleButton FmtToggle(string text, string tip, Action? act, FontWeight weight, FontStyle style = FontStyle.Normal, TextDecorationCollection? deco = null, double size = 14)
	{
		var b = new ToggleButton
		{
			Width = 24,
			Height = 22,
			Padding = new Thickness(0),
			Margin = new Thickness(0, 0, 1, 0),
			Background = FmtBtnBg,
			BorderBrush = DarkFieldBorder,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(0),
			IsEnabled = act != null,
			[ToolTip.TipProperty] = tip,
			Content = new TextBlock
			{
				Text = text,
				FontSize = size,
				FontWeight = weight,
				FontStyle = style,
				TextDecorations = deco,
				Foreground = FmtBtnFg,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			},
		};
		b.Click += (_, _) => act?.Invoke(); // 눌림 상태는 선택 글자 서식으로 다시 맞춘다
		return b;
	}

	private static Button IconButton(string icon, string tip, Action? act)
	{
		var b = new Button { Width = 28, Height = 22, Padding = new Thickness(1), Background = Brushes.Transparent, IsEnabled = act != null, [ToolTip.TipProperty] = tip, Content = IconHelper.GetIcon(icon, 18) };
		b.Click += (_, _) => act?.Invoke();
		return b;
	}

	private static StackPanel IconText(string icon, string text) => new()
	{
		Orientation = Orientation.Horizontal,
		Spacing = 5,
		Children = { IconHelper.GetIcon(icon, 16), new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } },
	};

	private static Button SmallButton(string icon, string text, Action? act)
	{
		var b = new Button { Height = 22, Padding = new Thickness(3, 1), Background = Brushes.Transparent, IsEnabled = act != null, Content = IconText(icon, text) };
		b.Click += (_, _) => act?.Invoke();
		return b;
	}

	private static ToggleButton SmallToggle(string icon, string text, Action<bool>? act)
	{
		var b = new ToggleButton { Height = 22, Padding = new Thickness(3, 1), Background = Brushes.Transparent, IsEnabled = act != null, Content = IconText(icon, text) };
		b.IsCheckedChanged += (_, _) => act?.Invoke(b.IsChecked == true);
		return b;
	}

	private static Button SmallMenuButton(string icon, string text, (string Label, Action? Act)[]? items)
	{
		var content = IconText(icon, text);
		content.Children.Add(new TextBlock { Text = "▾", FontSize = 10, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
		var b = new Button { Height = 22, Padding = new Thickness(3, 1), Background = Brushes.Transparent, IsEnabled = items != null, Content = content };
		AttachMenu(b, items);
		return b;
	}

	private static Button BigButton(string icon, string text, Action? act)
	{
		var b = new Button { Padding = new Thickness(4, 2), Margin = new Thickness(2, 0), MinWidth = 48, Background = Brushes.Transparent, IsEnabled = act != null, VerticalAlignment = VerticalAlignment.Stretch };
		b.Content = new StackPanel
		{
			Spacing = 2,
			HorizontalAlignment = HorizontalAlignment.Center,
			Children =
			{
				IconHelper.GetIcon(icon, 30),
				new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
			},
		};
		b.Click += (_, _) => act?.Invoke();
		return b;
	}

	private static Button BigMenuButton(string icon, TextBlock label, string tip, (string Label, Action? Act)[]? items)
	{
		var b = new Button { Padding = new Thickness(4, 2), Margin = new Thickness(2, 0), MinWidth = 52, Background = Brushes.Transparent, IsEnabled = items != null, [ToolTip.TipProperty] = tip };
		var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { label, new TextBlock { Text = "▾", FontSize = 10, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom } } };
		b.Content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, Children = { IconHelper.GetIcon(icon, 30), row } };
		AttachMenu(b, items);
		return b;
	}

	/// <summary>
	/// 메뉴 항목: "-" = 구분선, "상위>항목" = 하위 메뉴, "항목|Alt+Enter" = 단축키 표시.
	/// </summary>
	private static void AttachMenu(Button b, (string Label, Action? Act)[]? items)
	{
		if (items == null)
		{
			return;
		}
		var menu = BuildMenu(items);
		b.ContextMenu = menu;
		b.Click += (_, _) => menu.Open(b);
	}

	/// <summary>문자 세트 (기존 CAD "더 보기 ▸ 문자 세트" 순서, Windows 문자 집합 번호).</summary>
	internal static readonly (string Name, int Charset)[] Charsets =
	{
		("중앙 유럽", 238), ("키릴", 204), ("히브리어", 177), ("아랍어", 178), ("발트해", 186), ("그리스어", 161), ("터키어", 162),
		("베트남어", 163), ("일본어", 128), ("중국어_GB2312", 134), ("중국어_BIG5", 136), ("한국어", 129), ("서양어", 0), ("태국어", 222),
	};

	/// <summary>더 보기 메뉴 (기존 CAD와 같음): 문자 세트 ▸ / 편집기 설정 ▸ / 도움말.</summary>
	internal static (string Label, Action? Act)[] MoreMenu(TextEditActions a)
	{
		var items = new List<(string, Action?)>();
		foreach (var (name, cs) in Charsets)
		{
			items.Add(($"문자 세트>{name}", a.SetCharset == null ? null : () => a.SetCharset(cs)));
		}
		items.Add(("편집기 설정>항상 WYSIWYG으로 표시", null));
		items.Add(("편집기 설정>텍스트 강조 색상...", a.HighlightColor));
		items.Add(("-", null));
		items.Add(("도움말|F1", a.Help));
		return items.ToArray();
	}

	/// <summary>메뉴 만들기 (AttachMenu와 같은 규칙: "-" 구분선, "상위>항목" 하위 메뉴, "항목|단축키").</summary>
	internal static ContextMenu BuildMenu(IEnumerable<(string Label, Action? Act)> items)
	{
		var menu = new ContextMenu();
		var parents = new Dictionary<string, MenuItem>();
		foreach (var (label0, act) in items)
		{
			if (label0 == "-")
			{
				menu.Items.Add(new Separator());
				continue;
			}
			string label = label0;
			string? gesture = null;
			int bar = label.IndexOf('|');
			if (bar > 0)
			{
				gesture = label[(bar + 1)..];
				label = label[..bar];
			}
			var target = menu.Items;
			int gt = label.IndexOf('>');
			if (gt > 0)
			{
				string parent = label[..gt];
				if (!parents.TryGetValue(parent, out var pm))
				{
					pm = new MenuItem { Header = parent };
					parents[parent] = pm;
					menu.Items.Add(pm);
				}
				target = pm.Items;
				label = label[(gt + 1)..];
			}
			var mi = new MenuItem { Header = label, IsEnabled = act != null };
			if (gesture != null)
			{
				mi.InputGesture = Avalonia.Input.KeyGesture.Parse(gesture);
			}
			mi.Click += (_, _) => act?.Invoke();
			target.Add(mi);
		}
		return menu;
	}

	/// <summary>열 버튼 (기존 CAD와 같은 메뉴): 열 없음 / 동적 열 ▸ 자동·수동 높이 / 정적 열 ▸ 2~6·더 보기 / 열 분할 삽입 / 열 설정.</summary>
	private Button ColumnsButton(TextEditActions a)
	{
		var set = a.SetColumns;
		Action? Do(string kind) => set == null ? null : () => set(kind);
		var items = new List<(string, Action?)>
		{
			("열 없음", Do("none")),
			("동적 열>자동 높이", Do("dynamic-auto")),
			("동적 열>수동 높이", Do("dynamic-manual")),
		};
		for (int n = 2; n <= 6; n++)
		{
			items.Add(($"정적 열>{n}", Do("static:" + n)));
		}
		items.Add(("정적 열>더 보기...", Do("settings-static")));
		items.Add(("-", null));
		items.Add(("열 분할 삽입|Alt+Enter", null));
		items.Add(("-", null));
		items.Add(("열 설정...", Do("settings")));
		var b = SmallMenuButton(IconHelper.TeColumns, "열", items.ToArray());
		if (b.ContextMenu is { } menu && a.ColumnType != null)
		{
			// 지금 종류에 체크 (열 없음 / 동적 / 정적)
			menu.Opened += (_, _) =>
			{
				int t = a.ColumnType();
				foreach (var it in menu.Items.OfType<MenuItem>())
				{
					string h = it.Header as string ?? string.Empty;
					bool on = (h == "열 없음" && t == 0) || (h == "동적 열" && t == 2) || (h == "정적 열" && t == 1);
					it.Icon = on ? new TextBlock { Text = "✓", FontSize = 12 } : null;
				}
			};
		}
		return b;
	}

	private static System.Collections.ObjectModel.ObservableCollection<string> FontNames()
	{
		try
		{
			return new(FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n));
		}
		catch (Exception)
		{
			return new() { "굴림", "돋움", "맑은 고딕", "Arial" };
		}
	}
}
