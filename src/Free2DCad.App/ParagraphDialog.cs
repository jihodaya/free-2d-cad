using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// 단락 창 (기존 CAD 문자 편집기 "단락"과 같은 구성): 탭(왼쪽/가운데/오른쪽/소수점, 추가·수정·제거, 소수점 기호),
/// 왼쪽 들여쓰기(첫 번째·행간), 오른쪽 들여쓰기, 단락 정렬, 단락 간격(이전·이후), 단락 줄 간격(정확히/최소/배수).
/// 창의 값은 도면 단위이고, 저장할 때는 기존 CAD처럼 문자 높이로 나눈 값(\px...;)이 된다. current는 높이 배수 값(Relative).
/// </summary>
public sealed class ParagraphDialog : Window
{
	private static readonly string[] TabKindLabels = { "왼쪽", "중앙", "오른쪽", "소수" };
	private static readonly char[] TabKindChars = { 'l', 'c', 'r', 'd' };
	private static readonly (string Label, char Ch)[] DecimalStyles = { ("'.'(기간)", '.'), ("','(쉼표)", ','), ("' '(공백)", ' ') };
	private static readonly (string Label, char Ch)[] SpacingKinds = { ("정확히", 'e'), ("최소", 'a'), ("배수", 'm') };
	private static readonly char[] AlignChars = { 'l', 'c', 'r', 'j', 'd' };

	private readonly double _h;
	private readonly RadioButton[] _tabKind = new RadioButton[4];
	private readonly TextBox _tabPos = Box("0");
	private readonly ListBox _tabList = new() { Height = 92, FontSize = 12 };
	private readonly List<(double Pos, char Kind)> _tabs = new();
	private readonly ComboBox _decimal = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
	private readonly TextBox _first = Box("0");
	private readonly TextBox _left = Box("0");
	private readonly TextBox _right = Box("0");
	private readonly CheckBox _chkAlign = new() { Content = "단락 정렬", FontSize = 12 };
	private readonly RadioButton[] _align = new RadioButton[5];
	private readonly CheckBox _chkSpace = new() { Content = "단락 간격", FontSize = 12 };
	private readonly TextBox _before = Box("0");
	private readonly TextBox _after = Box("0");
	private readonly CheckBox _chkLine = new() { Content = "단락 줄 간격", FontSize = 12 };
	private readonly ComboBox _lineKind = new() { FontSize = 12, Width = 90 };
	private readonly TextBox _lineValue = Box("0");

	public MTextParagraph? Result { get; private set; }

	/// <param name="current">커서가 있는 문단의 지금 서식.</param>
	/// <param name="textHeight">문자 높이 (\p 값 = 도면 단위 / 높이).</param>
	public ParagraphDialog(MTextParagraph current, double textHeight)
	{
		this._h = textHeight > 0 ? textHeight : 1;
		this.Title = "단락";
		this.Width = 500;
		this.SizeToContent = SizeToContent.Height;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		// ===== 탭 =====
		var kinds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
		string[] radioLabels = { "왼쪽", "가운데", "오른쪽", "소수점" };
		for (int i = 0; i < 4; i++)
		{
			this._tabKind[i] = new RadioButton { Content = radioLabels[i], GroupName = "ParaTab", FontSize = 12, IsChecked = i == 0, MinWidth = 0, Padding = new Thickness(2, 0, 0, 0) };
			kinds.Children.Add(this._tabKind[i]);
		}
		var add = SmallButton("추가", this.AddTab);
		var modify = SmallButton("수정", this.ModifyTab);
		var remove = SmallButton("제거", this.RemoveTab);
		var tabGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4 };
		tabGrid.Children.Add(this._tabPos);
		var btns = new StackPanel { Spacing = 4, Children = { modify, remove } };
		Grid.SetColumn(add, 1);
		tabGrid.Children.Add(add);
		Grid.SetRow(this._tabList, 1);
		tabGrid.Children.Add(this._tabList);
		Grid.SetRow(btns, 1);
		Grid.SetColumn(btns, 1);
		tabGrid.Children.Add(btns);
		this._tabList.SelectionChanged += (_, _) =>
		{
			int i = this._tabList.SelectedIndex;
			if (i < 0 || i >= this._tabs.Count) return;
			this._tabPos.Text = Fmt(this._tabs[i].Pos);
			this._tabKind[Array.IndexOf(TabKindChars, this._tabs[i].Kind)].IsChecked = true;
		};
		this._decimal.ItemsSource = DecimalStyles.Select(d => d.Label).ToArray();
		int dec = Array.FindIndex(DecimalStyles, d => d.Ch == current.DecimalChar);
		this._decimal.SelectedIndex = dec < 0 ? 0 : dec;
		this._tabKind[3].IsCheckedChanged += (_, _) => this._decimal.IsEnabled = this._tabKind[3].IsChecked == true;
		this._decimal.IsEnabled = false;
		var tabPanel = new StackPanel
		{
			Spacing = 6,
			Children = { kinds, tabGrid, new TextBlock { Text = "소수점 STYLE 지정:", FontSize = 12 }, this._decimal },
		};

		// ===== 들여쓰기 =====
		var leftIndent = new StackPanel { Spacing = 4, Children = { Row("첫 번째:", this._first), Row("행간:", this._left) } };
		var rightIndent = Row("오른쪽:", this._right);

		// ===== 단락 정렬 =====
		string[] alignLabels = { "왼쪽", "가운데", "오른쪽", "양쪽 정렬", "분산" };
		var alignGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(12, 2, 0, 0) };
		int[] row = { 0, 1, 2, 0, 1 }, col = { 0, 0, 0, 1, 1 };
		for (int i = 0; i < 5; i++)
		{
			this._align[i] = new RadioButton { Content = alignLabels[i], GroupName = "ParaAlign", FontSize = 12, IsChecked = i == 0, MinWidth = 0, Padding = new Thickness(2, 0, 0, 0) };
			Grid.SetRow(this._align[i], row[i]);
			Grid.SetColumn(this._align[i], col[i]);
			alignGrid.Children.Add(this._align[i]);
		}
		this._chkAlign.IsCheckedChanged += (_, _) => alignGrid.IsEnabled = this._chkAlign.IsChecked == true;

		// ===== 단락 간격·줄 간격 =====
		var spaceGrid = Pair("이전:", this._before, "이후:", this._after);
		this._chkSpace.IsCheckedChanged += (_, _) => spaceGrid.IsEnabled = this._chkSpace.IsChecked == true;
		this._lineKind.ItemsSource = SpacingKinds.Select(k => k.Label).ToArray();
		this._lineKind.SelectedIndex = 0;
		var lineGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(12, 2, 0, 0) };
		lineGrid.Children.Add(new TextBlock { Text = "줄 간격:", FontSize = 12 });
		var at = new TextBlock { Text = "에", FontSize = 12 };
		Grid.SetColumn(at, 1);
		lineGrid.Children.Add(at);
		Grid.SetRow(this._lineKind, 1);
		lineGrid.Children.Add(this._lineKind);
		Grid.SetRow(this._lineValue, 1);
		Grid.SetColumn(this._lineValue, 1);
		lineGrid.Children.Add(this._lineValue);
		this._chkLine.IsCheckedChanged += (_, _) => lineGrid.IsEnabled = this._chkLine.IsChecked == true;

		// ===== 배치 =====
		var leftCol = new StackPanel { Spacing = 8, Children = { Group("탭", tabPanel), Group(null, new StackPanel { Spacing = 4, Children = { this._chkSpace, spaceGrid } }) } };
		var rightCol = new StackPanel
		{
			Spacing = 8,
			Children =
			{
				Group("왼쪽 들여쓰기", leftIndent),
				Group("오른쪽 들여쓰기", rightIndent),
				Group(null, new StackPanel { Spacing = 4, Children = { this._chkAlign, alignGrid } }),
				Group(null, new StackPanel { Spacing = 4, Children = { this._chkLine, lineGrid } }),
			},
		};
		var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("1.15*,*"), ColumnSpacing = 8 };
		cols.Children.Add(leftCol);
		Grid.SetColumn(rightCol, 1);
		cols.Children.Add(rightCol);

		var ok = new Button { Content = "확인", Width = 80, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		var cancel = new Button { Content = "취소", Width = 80, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		ok.Click += (_, _) => this.Accept();
		cancel.Click += (_, _) => this.Close();
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel } };
		this.Content = new StackPanel { Margin = new Thickness(10), Children = { cols, buttons } };

		// ===== 지금 값 =====
		this._first.Text = Fmt((current.FirstIndent + current.LeftIndent) * this._h);
		this._left.Text = Fmt(current.LeftIndent * this._h);
		this._right.Text = Fmt(current.RightIndent * this._h);
		for (int i = 0; i < current.Tabs.Length; i++)
		{
			this._tabs.Add((current.Tabs[i] * this._h, current.TabKind(i)));
		}
		this.RefreshTabs();
		int ai = Array.IndexOf(AlignChars, current.Align);
		this._chkAlign.IsChecked = ai >= 0;
		if (ai >= 0) this._align[ai].IsChecked = true;
		alignGrid.IsEnabled = ai >= 0;
		bool spaced = current.Before != 0 || current.After != 0;
		this._chkSpace.IsChecked = spaced;
		spaceGrid.IsEnabled = spaced;
		this._before.Text = Fmt(current.Before * this._h);
		this._after.Text = Fmt(current.After * this._h);
		int si = Array.FindIndex(SpacingKinds, k => k.Ch == current.SpacingKind);
		this._chkLine.IsChecked = si >= 0;
		lineGrid.IsEnabled = si >= 0;
		if (si >= 0)
		{
			this._lineKind.SelectedIndex = si;
			this._lineValue.Text = Fmt(current.Spacing);
		}
	}

	private static string Fmt(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

	private static TextBox Box(string text) => new() { Text = text, FontSize = 12, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2) };

	private static Button SmallButton(string text, Action click)
	{
		var b = new Button { Content = text, Width = 64, Height = 24, FontSize = 12, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(0) };
		b.Click += (_, _) => click();
		return b;
	}

	private static Control Row(string label, Control box)
	{
		var g = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*") };
		g.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		Grid.SetColumn(box, 1);
		g.Children.Add(box);
		return g;
	}

	private static Grid Pair(string l1, Control b1, string l2, Control b2)
	{
		var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(12, 2, 0, 0) };
		g.Children.Add(new TextBlock { Text = l1, FontSize = 12 });
		var t2 = new TextBlock { Text = l2, FontSize = 12 };
		Grid.SetColumn(t2, 1);
		g.Children.Add(t2);
		Grid.SetRow(b1, 1);
		g.Children.Add(b1);
		Grid.SetRow(b2, 1);
		Grid.SetColumn(b2, 1);
		g.Children.Add(b2);
		return g;
	}

	private static Border Group(string? header, Control content)
	{
		var panel = new StackPanel { Spacing = 6 };
		if (header != null)
		{
			panel.Children.Add(new TextBlock { Text = header, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)) });
		}
		panel.Children.Add(content);
		return new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Background = Brushes.White,
			Padding = new Thickness(8, 6),
			Child = panel,
		};
	}

	private char SelectedTabKind() => TabKindChars[Math.Max(0, Array.FindIndex(this._tabKind, r => r.IsChecked == true))];

	/// <summary>탭 위치 값 (기존 CAD: 0~2000 밖이면 알림).</summary>
	private bool TryTabPos(out double v)
	{
		if (double.TryParse(this._tabPos.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0 && v <= 2000)
		{
			return true;
		}
		_ = this.ShowMessageAsync("범위 0.0000에서 2000.0000 사이의 값을 입력해 주세요.");
		return false;
	}

	private async Task ShowMessageAsync(string message)
	{
		var win = new Window
		{
			Title = "Free 2D CAD",
			Width = 340,
			Height = 130,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var sp = new StackPanel { Margin = new Thickness(14), Spacing = 12 };
		sp.Children.Add(new TextBlock { Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap });
		var btnOk = new Button { Content = "확인", Width = 64, Height = 24, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
		btnOk.Click += (_, _) => win.Close();
		sp.Children.Add(btnOk);
		win.Content = sp;
		await win.ShowDialog(this);
	}

	private void AddTab()
	{
		if (!this.TryTabPos(out double v)) return;
		this._tabs.RemoveAll(t => Math.Abs(t.Pos - v) < 1e-9);
		this._tabs.Add((v, this.SelectedTabKind()));
		this.RefreshTabs(v);
	}

	private void ModifyTab()
	{
		int i = this._tabList.SelectedIndex;
		if (i < 0 || i >= this._tabs.Count || !this.TryTabPos(out double v)) return;
		this._tabs[i] = (v, this.SelectedTabKind());
		this.RefreshTabs(v);
	}

	private void RemoveTab()
	{
		int i = this._tabList.SelectedIndex;
		if (i < 0 || i >= this._tabs.Count) return;
		this._tabs.RemoveAt(i);
		this.RefreshTabs();
	}

	private void RefreshTabs(double? select = null)
	{
		this._tabs.Sort((a, b) => a.Pos.CompareTo(b.Pos));
		this._tabList.ItemsSource = this._tabs.Select(t => $"{t.Pos.ToString("0.0", CultureInfo.InvariantCulture)} {TabKindLabels[Array.IndexOf(TabKindChars, t.Kind)]}").ToList();
		if (select is { } s)
		{
			this._tabList.SelectedIndex = this._tabs.FindIndex(t => Math.Abs(t.Pos - s) < 1e-9);
		}
	}

	private static double Num(TextBox tb) =>
		double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

	private void Accept()
	{
		double h = this._h;
		double left = Num(this._left), first = Num(this._first), right = Num(this._right);
		bool spaced = this._chkSpace.IsChecked == true, lined = this._chkLine.IsChecked == true;
		char align = this._chkAlign.IsChecked == true ? AlignChars[Math.Max(0, Array.FindIndex(this._align, r => r.IsChecked == true))] : '\0';
		this.Result = new MTextParagraph((first - left) / h, left / h, right / h, this._tabs.Select(t => t.Pos / h).ToArray(), align)
		{
			TabKinds = this._tabs.Select(t => t.Kind).ToArray(),
			DecimalChar = DecimalStyles[Math.Max(0, this._decimal.SelectedIndex)].Ch,
			Before = spaced ? Num(this._before) / h : 0,
			After = spaced ? Num(this._after) / h : 0,
			SpacingKind = lined ? SpacingKinds[Math.Max(0, this._lineKind.SelectedIndex)].Ch : '\0',
			Spacing = lined ? Num(this._lineValue) : 0,
			Relative = true,
		};
		this.Close();
	}
}
