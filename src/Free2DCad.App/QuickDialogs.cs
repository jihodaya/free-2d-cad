using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Entities;
using Point = ACadSharp.Entities.Point;
using Line = ACadSharp.Entities.Line;
using Ellipse = ACadSharp.Entities.Ellipse;
using Arc = ACadSharp.Entities.Arc;

namespace Free2DCad.App;

/// <summary>점 스타일 (PTYPE): 모양 20가지 + 크기(화면 상대 % / 절대 단위). 결과는 PDMODE·PDSIZE.</summary>
public sealed class PointStyleDialog : Window
{
	private static readonly int[] Modes = { 0, 1, 2, 3, 4, 32, 33, 34, 35, 36, 64, 65, 66, 67, 68, 96, 97, 98, 99, 100 };
	private readonly List<Border> _cells = new();
	private readonly TextBox _size = new() { Width = 90, Height = 24, FontSize = 12 };
	private readonly RadioButton _relative = new() { Content = "화면에 상대적인 크기 설정(R)", GroupName = "PtSize", FontSize = 12 };
	private readonly RadioButton _absolute = new() { Content = "절대 단위로 크기 설정(A)", GroupName = "PtSize", FontSize = 12 };
	private int _mode;

	public (short Mode, double Size)? Result { get; private set; }

	public PointStyleDialog(short mode, double size)
	{
		this.Title = "점 스타일";
		this.Width = 330;
		this.Height = 420;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;
		this._mode = mode;

		var grid = new UniformGrid { Columns = 5, Rows = 4, Margin = new Thickness(0, 0, 0, 10) };
		foreach (int m in Modes)
		{
			int mm = m;
			var cell = new Border
			{
				Width = 52,
				Height = 44,
				Margin = new Thickness(2),
				Background = Brushes.Black,
				BorderThickness = new Thickness(2),
				Child = Glyph(m),
				Cursor = new Cursor(StandardCursorType.Hand),
			};
			cell.PointerPressed += (_, _) =>
			{
				this._mode = mm;
				this.Highlight();
			};
			this._cells.Add(cell);
			grid.Children.Add(cell);
		}
		this.Highlight();

		this._size.Text = Math.Abs(size) < 1e-12 ? "5" : Math.Abs(size).ToString("0.####");
		this._relative.IsChecked = size <= 0;
		this._absolute.IsChecked = size > 0;
		var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
		sizeRow.Children.Add(new TextBlock { Text = "점 크기(S):", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		sizeRow.Children.Add(this._size);
		var unit = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		void UpdateUnit() => unit.Text = this._relative.IsChecked == true ? "%" : "단위";
		this._relative.IsCheckedChanged += (_, _) => UpdateUnit();
		UpdateUnit();
		sizeRow.Children.Add(unit);

		var ok = new Button { Content = "확인", Width = 80, IsDefault = true };
		var cancel = new Button { Content = "취소", Width = 80, IsCancel = true };
		ok.Click += (_, _) =>
		{
			if (!double.TryParse(this._size.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v < 0)
			{
				v = 5;
			}
			this.Result = ((short)this._mode, this._relative.IsChecked == true ? -v : v);
			this.Close();
		};
		cancel.Click += (_, _) => this.Close();
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);

		var root = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
		root.Children.Add(grid);
		root.Children.Add(sizeRow);
		root.Children.Add(this._relative);
		root.Children.Add(this._absolute);
		root.Children.Add(buttons);
		this.Content = root;
	}

	private void Highlight()
	{
		for (int i = 0; i < Modes.Length; i++)
		{
			this._cells[i].BorderBrush = Modes[i] == this._mode ? new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xD6)) : Brushes.Gray;
		}
	}

	/// <summary>PDMODE 모양 미리보기 (렌더러와 같은 규칙).</summary>
	private static Control Glyph(int mode)
	{
		var c = new Canvas { Width = 48, Height = 40 };
		const double cx = 24, cy = 20, r = 9;
		var white = Brushes.White;
		void L(double x1, double y1, double x2, double y2) => c.Children.Add(new Shapes.Line { StartPoint = new Avalonia.Point(x1, y1), EndPoint = new Avalonia.Point(x2, y2), Stroke = white, StrokeThickness = 1.2 });
		switch (mode & 7)
		{
			case 0:
				c.Children.Add(new Shapes.Ellipse { Width = 3, Height = 3, Fill = white, [Canvas.LeftProperty] = cx - 1.5, [Canvas.TopProperty] = cy - 1.5 });
				break;
			case 2: L(cx - r, cy, cx + r, cy); L(cx, cy - r, cx, cy + r); break;
			case 3: L(cx - r, cy - r, cx + r, cy + r); L(cx - r, cy + r, cx + r, cy - r); break;
			case 4: L(cx, cy, cx, cy - r); break;
		}
		if ((mode & 32) != 0)
		{
			c.Children.Add(new Shapes.Ellipse { Width = 2 * r, Height = 2 * r, Stroke = white, StrokeThickness = 1.2, [Canvas.LeftProperty] = cx - r, [Canvas.TopProperty] = cy - r });
		}
		if ((mode & 64) != 0)
		{
			c.Children.Add(new Shapes.Rectangle { Width = 2 * r, Height = 2 * r, Stroke = white, StrokeThickness = 1.2, [Canvas.LeftProperty] = cx - r, [Canvas.TopProperty] = cy - r });
		}
		return c;
	}
}

/// <summary>
/// 빠른 선택 (QSELECT): 객체 유형 + 특성(도면층/색상/선종류/문자 내용) + 연산자(=, ≠, 와일드카드) + 값으로 객체를 고른다.
/// </summary>
public sealed class QuickSelectDialog : Window
{
	private readonly IReadOnlyList<Entity> _entities;
	private readonly ComboBox _type = new() { Width = 220, Height = 24, FontSize = 12 };
	private readonly ComboBox _prop = new() { Width = 220, Height = 24, FontSize = 12 };
	private readonly ComboBox _op = new() { Width = 220, Height = 24, FontSize = 12 };
	private readonly ComboBox _value = new() { Width = 220, Height = 24, FontSize = 12, IsTextSearchEnabled = true };
	private readonly RadioButton _include = new() { Content = "새 선택 세트에 포함(I)", GroupName = "QsMode", IsChecked = true, FontSize = 12 };
	private readonly RadioButton _exclude = new() { Content = "새 선택 세트에서 제외(E)", GroupName = "QsMode", FontSize = 12 };
	private readonly CheckBox _append = new() { Content = "현재 선택 세트에 추가(A)", FontSize = 12 };

	private static readonly string[] Props = { "도면층", "색상", "선종류", "문자 내용" };
	private static readonly string[] Ops = { "= 같음", "<> 같지 않음", "* 와일드카드 일치" };

	/// <summary>고른 객체 (확인을 누른 경우).</summary>
	public List<Entity>? Result { get; private set; }

	public bool Append => this._append.IsChecked == true;

	public QuickSelectDialog(IReadOnlyList<Entity> entities)
	{
		this._entities = entities;
		this.Title = "빠른 선택";
		this.Width = 380;
		this.Height = 360;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var types = new List<string> { "다중" };
		types.AddRange(entities.Select(TypeName).Distinct().OrderBy(n => n));
		this._type.ItemsSource = types;
		this._type.SelectedIndex = 0;
		this._prop.ItemsSource = Props;
		this._prop.SelectedIndex = 0;
		this._op.ItemsSource = Ops;
		this._op.SelectedIndex = 0;
		this._type.SelectionChanged += (_, _) => this.FillValues();
		this._prop.SelectionChanged += (_, _) => this.FillValues();
		this.FillValues();

		var form = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowDefinitions = new RowDefinitions("30,30,30,30"), Margin = new Thickness(0, 0, 0, 8) };
		void Add(int row, string label, Control c)
		{
			var tb = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
			Grid.SetRow(tb, row);
			form.Children.Add(tb);
			Grid.SetRow(c, row);
			Grid.SetColumn(c, 1);
			form.Children.Add(c);
		}
		Add(0, "객체 유형(B):", this._type);
		Add(1, "특성(P):", this._prop);
		Add(2, "연산자(O):", this._op);
		Add(3, "값(V):", this._value);

		var ok = new Button { Content = "확인", Width = 80, IsDefault = true };
		var cancel = new Button { Content = "취소", Width = 80, IsCancel = true };
		ok.Click += (_, _) =>
		{
			this.Result = this.Select();
			this.Close();
		};
		cancel.Click += (_, _) => this.Close();
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);

		var root = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
		root.Children.Add(form);
		root.Children.Add(new TextBlock { Text = "적용 방법:", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
		root.Children.Add(this._include);
		root.Children.Add(this._exclude);
		root.Children.Add(this._append);
		root.Children.Add(buttons);
		this.Content = root;
	}

	private static string TypeName(Entity e) => e switch
	{
		Arc => "호",
		Circle => "원",
		Line => "선",
		LwPolyline or Polyline2D or Polyline3D => "폴리선",
		MText => "여러 줄 문자",
		AttributeDefinition => "속성 정의",
		TextEntity => "문자",
		Dimension => "치수",
		Hatch => "해치",
		Insert => "블록 참조",
		Ellipse => "타원",
		Spline => "스플라인",
		Point => "점",
		Leader or MultiLeader => "지시선",
		_ => e.ObjectName,
	};

	private static string PropValue(Entity e, int prop) => prop switch
	{
		0 => e.Layer?.Name ?? "0",
		1 => e.Color.IsByLayer ? "ByLayer" : e.Color.IsByBlock ? "ByBlock" : e.Color.Index >= 0 ? e.Color.Index.ToString() : e.Color.ToString(),
		2 => e.LineType?.Name ?? "ByLayer",
		_ => e switch { MText m => m.Value, TextEntity t => t.Value, _ => string.Empty },
	};

	private IEnumerable<Entity> OfType()
	{
		string? type = this._type.SelectedItem as string;
		return type == null || type == "다중" ? this._entities : this._entities.Where(e => TypeName(e) == type);
	}

	private void FillValues()
	{
		int prop = Math.Max(0, this._prop.SelectedIndex);
		var values = this.OfType().Select(e => PropValue(e, prop)).Where(v => v.Length > 0).Distinct().OrderBy(v => v).Take(500).ToList();
		this._value.ItemsSource = values;
		this._value.SelectedIndex = values.Count > 0 ? 0 : -1;
	}

	private List<Entity> Select()
	{
		int prop = Math.Max(0, this._prop.SelectedIndex);
		int op = Math.Max(0, this._op.SelectedIndex);
		string value = this._value.SelectedItem as string ?? string.Empty;
		bool Match(Entity e)
		{
			string v = PropValue(e, prop);
			return op switch
			{
				0 => string.Equals(v, value, StringComparison.OrdinalIgnoreCase),
				1 => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase),
				_ => System.Text.RegularExpressions.Regex.IsMatch(v, "^" + System.Text.RegularExpressions.Regex.Escape(value).Replace("\\*", ".*").Replace("\\?", ".") + "$",
					System.Text.RegularExpressions.RegexOptions.IgnoreCase),
			};
		}
		var candidates = this.OfType().ToList();
		return this._include.IsChecked == true ? candidates.Where(Match).ToList() : candidates.Where(e => !Match(e)).ToList();
	}
}
