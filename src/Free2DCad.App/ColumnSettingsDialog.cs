using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// 칼럼 설정 창 (기존 CAD 문자 편집기 "열 설정"과 같은 구성): 칼럼 유형(다이나믹 칼럼 자동/수동 높이, 정적 칼럼, 칼럼 없음),
/// 칼럼 번호(열 수), 높이, 너비(칼럼·갭·총계). 확인하면 Result (null = 칼럼 없음).
/// </summary>
public sealed class ColumnSettingsDialog : Window
{
	private readonly RadioButton _dynamic = new() { Content = "다이나믹 칼럼", GroupName = "ColType", FontSize = 12 };
	private readonly RadioButton _auto = new() { Content = "자동 높이", GroupName = "ColHeight", FontSize = 12, IsChecked = true, Margin = new Thickness(18, 0, 0, 0) };
	private readonly RadioButton _manual = new() { Content = "수동 높이", GroupName = "ColHeight", FontSize = 12, Margin = new Thickness(18, 0, 0, 0) };
	private readonly RadioButton _static = new() { Content = "정적 칼럼", GroupName = "ColType", FontSize = 12 };
	private readonly RadioButton _none = new() { Content = "칼럼 없음", GroupName = "ColType", FontSize = 12 };
	private readonly TextBox _count = Box();
	private readonly TextBox _height = Box();
	private readonly TextBox _width = Box();
	private readonly TextBox _gutter = Box();
	private readonly TextBox _total = Box();
	private readonly double[] _heights;
	private bool _updating;

	public bool Accepted { get; private set; }

	public MTextColumnInfo? Result { get; private set; }

	/// <param name="current">지금 열 (없으면 null).</param>
	/// <param name="width">열이 없을 때 쓸 열 폭 (지금 글상자 폭).</param>
	/// <param name="height">열이 없을 때 쓸 높이 (지금 글 높이).</param>
	/// <param name="gutter">기본 간격.</param>
	/// <param name="preferStatic">"정적 열 ▸ 더 보기"로 열면 정적 칼럼을 골라 둔다.</param>
	public ColumnSettingsDialog(MTextColumnInfo? current, double width, double height, double gutter, bool preferStatic)
	{
		this.Title = "칼럼 설정";
		this.Width = 380;
		this.SizeToContent = SizeToContent.Height;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;
		this._heights = current?.Heights ?? Array.Empty<double>();

		foreach (var r in new[] { this._dynamic, this._auto, this._manual, this._static, this._none })
		{
			r.MinWidth = 0;
			r.IsCheckedChanged += (_, _) => this.UpdateEnabled();
		}
		var type = new StackPanel { Spacing = 2, Children = { this._dynamic, this._auto, this._manual, this._static, this._none } };
		var left = new StackPanel { Spacing = 8, Children = { Group("칼럼 유형", type), Group("칼럼 번호", this._count) } };
		var widthGrid = new StackPanel { Spacing = 4, Children = { Row("칼럼:", this._width), Row("갭:", this._gutter), Row("총계:", this._total) } };
		var right = new StackPanel { Spacing = 8, Children = { Group("높이", this._height), Group("너비", widthGrid) } };
		var cols = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
		cols.Children.Add(left);
		Grid.SetColumn(right, 1);
		cols.Children.Add(right);

		var ok = new Button { Content = "확인", Width = 80, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		var cancel = new Button { Content = "취소", Width = 80, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		ok.Click += (_, _) => this.Accept();
		cancel.Click += (_, _) => this.Close();
		this.Content = new StackPanel
		{
			Margin = new Thickness(10),
			Children = { cols, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel } } },
		};

		// 지금 값
		this._updating = true;
		int count = current?.Columns ?? 2;
		this._count.Text = count.ToString(CultureInfo.InvariantCulture);
		this._height.Text = Fmt(current?.DefinedHeight ?? height);
		this._width.Text = Fmt(current?.Width ?? width);
		this._gutter.Text = Fmt(current?.Gutter ?? gutter);
		this._updating = false;
		if (current == null)
		{
			(preferStatic ? this._static : this._none).IsChecked = true;
		}
		else if (current.Type == 1)
		{
			this._static.IsChecked = true;
		}
		else
		{
			this._dynamic.IsChecked = true;
			(current.AutoHeight ? this._auto : this._manual).IsChecked = true;
		}
		foreach (var tb in new[] { this._count, this._width, this._gutter })
		{
			tb.TextChanged += (_, _) => this.UpdateTotal();
		}
		this._total.IsReadOnly = true;
		this.UpdateTotal();
		this.UpdateEnabled();
	}

	private static string Fmt(double v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);

	private static TextBox Box() => new() { FontSize = 12, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2) };

	private static double Num(TextBox tb) =>
		double.TryParse(tb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

	private static Control Row(string label, Control box)
	{
		var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*") };
		g.Children.Add(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		Grid.SetColumn(box, 1);
		g.Children.Add(box);
		return g;
	}

	private static Border Group(string header, Control content) => new()
	{
		BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
		BorderThickness = new Thickness(1),
		CornerRadius = new CornerRadius(3),
		Background = Brushes.White,
		Padding = new Thickness(8, 6),
		Child = new StackPanel
		{
			Spacing = 6,
			Children =
			{
				new TextBlock { Text = header, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)) },
				content,
			},
		},
	};

	private int Count => Math.Max(1, (int)Math.Round(Num(this._count)));

	private void UpdateTotal()
	{
		if (this._updating) return;
		int n = this.Count;
		this._total.Text = Fmt(Num(this._width) * n + Num(this._gutter) * (n - 1));
	}

	private void UpdateEnabled()
	{
		bool dyn = this._dynamic.IsChecked == true, stat = this._static.IsChecked == true, none = this._none.IsChecked == true;
		this._auto.IsEnabled = this._manual.IsEnabled = dyn;
		this._count.IsEnabled = stat; // 동적 열은 글 길이·열 높이로 정해짐 (기존 CAD와 같음)
		this._height.IsEnabled = stat || (dyn && this._auto.IsChecked == true);
		this._width.IsEnabled = this._gutter.IsEnabled = !none;
	}

	private void Accept()
	{
		this.Accepted = true;
		if (this._none.IsChecked == true)
		{
			this.Result = null;
			this.Close();
			return;
		}
		double w = Num(this._width), g = Math.Max(0, Num(this._gutter)), h = Num(this._height);
		if (w <= 0)
		{
			this.Accepted = false;
			return;
		}
		if (this._static.IsChecked == true)
		{
			int n = this.Count;
			this.Result = new MTextColumnInfo(1, false, n, false, w, g, new double[n], h);
		}
		else if (this._auto.IsChecked == true)
		{
			this.Result = new MTextColumnInfo(2, true, 1, false, w, g, Array.Empty<double>(), h);
		}
		else
		{
			// 수동 높이: 열마다 높이 (지금 값이 없으면 높이 하나로)
			double[] hs = this._heights.Length > 0 && this._heights.Any(x => x > 0) ? this._heights : new[] { h };
			this.Result = new MTextColumnInfo(2, false, hs.Length, false, w, g, hs, h);
		}
		this.Close();
	}
}
