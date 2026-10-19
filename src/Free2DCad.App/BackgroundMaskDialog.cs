using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AcadColor = ACadSharp.Color;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 배경 설정 창 (기존 CAD 문자 편집기 "배경 마스크"와 같은 구성): 배경 마스크 사용, 테두리 간격띄우기 계수,
/// 배경 채우기 색상 (그리기 배경색 사용 / 빨간색·노란색·…·색상 선택).
/// </summary>
public sealed class BackgroundMaskDialog : Window
{
	private static readonly (string Name, short Index)[] Colors =
	{
		("빨간색", 1), ("노란색", 2), ("초록색", 3), ("청록색", 4), ("파란색", 5), ("선홍색", 6), ("흰색", 7), ("검정색", 250),
	};

	private readonly CheckBox _use = new() { Content = "배경 마스크 사용", FontSize = 12 };
	private readonly TextBox _scale = new() { FontSize = 12, Height = 24, MinHeight = 24, Padding = new Thickness(4, 2), Width = 110 };
	private readonly CheckBox _drawingBg = new() { Content = "그리기 배경색 사용", FontSize = 12 };
	private readonly ComboBox _color = new() { FontSize = 12, Width = 130 };
	private AcadColor _custom;
	private bool _updating;

	public bool Accepted { get; private set; }

	public bool UseMask { get; private set; }

	public bool UseDrawingBackground { get; private set; }

	public double Scale { get; private set; }

	public AcadColor Color { get; private set; }

	public BackgroundMaskDialog(bool use, bool drawingBg, double scale, AcadColor color)
	{
		this.Title = "배경 설정";
		this.Width = 340;
		this.SizeToContent = SizeToContent.Height;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;
		this._custom = color;

		var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "테두리 간격띄우기 계수:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, this._scale } };
		var colorRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		colorRow.Children.Add(this._drawingBg);
		Grid.SetColumn(this._color, 1);
		colorRow.Children.Add(this._color);
		var group = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Background = Brushes.White,
			Padding = new Thickness(8, 6),
			Child = new StackPanel
			{
				Spacing = 6,
				Children =
				{
					new TextBlock { Text = "배경 채우기 색상", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0)) },
					colorRow,
				},
			},
		};

		var ok = new Button { Content = "확인", Width = 80, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		var cancel = new Button { Content = "취소", Width = 80, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		ok.Click += (_, _) => this.Accept();
		cancel.Click += (_, _) => this.Close();
		this.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				this._use, scaleRow, group,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
			},
		};

		this.FillColors();
		this._use.IsChecked = use;
		this._drawingBg.IsChecked = drawingBg;
		this._scale.Text = (scale > 0 ? scale : 1.5).ToString("0.####", CultureInfo.InvariantCulture);
		this._use.IsCheckedChanged += (_, _) => this.UpdateEnabled();
		this._drawingBg.IsCheckedChanged += (_, _) => this.UpdateEnabled();
		this._color.SelectionChanged += async (_, _) =>
		{
			if (this._updating || this._color.SelectedIndex != this._color.ItemCount - 1) return;
			// 색상 선택...
			var dlg = new ColorPickerDialog(this._custom);
			var res = await dlg.ShowDialog<AcadColor?>(this);
			if (res != null) this._custom = res.Value;
			this.FillColors();
		};
		this.UpdateEnabled();
	}

	private static Control Swatch(AvColor c, string name) => new StackPanel
	{
		Orientation = Orientation.Horizontal,
		Spacing = 6,
		Children =
		{
			new Border { Width = 12, Height = 12, Background = new SolidColorBrush(c), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center },
			new TextBlock { Text = name, FontSize = 12 },
		},
	};

	/// <summary>목록의 색 (검정색 = 트루컬러 0,0,0).</summary>
	private static AcadColor ColorOf(int i) => Colors[i].Index == 250 ? new AcadColor((byte)0, (byte)0, (byte)0) : new AcadColor(Colors[i].Index);

	private int IndexOfStandard(AcadColor c) =>
		c.IsTrueColor ? (c.R == 0 && c.G == 0 && c.B == 0 ? Colors.Length - 1 : -1) : Array.FindIndex(Colors, k => k.Index == c.Index && k.Index != 250);

	private void FillColors()
	{
		this._updating = true;
		var items = new List<Control>();
		for (int k = 0; k < Colors.Length; k++)
		{
			var ac = ColorOf(k);
			items.Add(Swatch(AvColor.FromRgb(ac.R, ac.G, ac.B), Colors[k].Name));
		}
		int std = this.IndexOfStandard(this._custom);
		if (std < 0)
		{
			string label = this._custom.IsTrueColor ? $"{this._custom.R},{this._custom.G},{this._custom.B}" : $"색상 {this._custom.Index}";
			items.Add(Swatch(AvColor.FromRgb(this._custom.R, this._custom.G, this._custom.B), label));
		}
		items.Add(new TextBlock { Text = "색상 선택...", FontSize = 12 });
		this._color.ItemsSource = items;
		this._color.SelectedIndex = std >= 0 ? std : Colors.Length;
		this._updating = false;
	}

	private void UpdateEnabled()
	{
		bool on = this._use.IsChecked == true;
		this._scale.IsEnabled = on;
		this._drawingBg.IsEnabled = on;
		this._color.IsEnabled = on && this._drawingBg.IsChecked != true;
	}

	private void Accept()
	{
		if (!double.TryParse(this._scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) || s < 1 || s > 5)
		{
			// 기존 CAD: 계수는 1~5
			this._scale.Text = "1.5";
			return;
		}
		this.Accepted = true;
		this.UseMask = this._use.IsChecked == true;
		this.UseDrawingBackground = this._drawingBg.IsChecked == true;
		this.Scale = s;
		int i = this._color.SelectedIndex;
		this.Color = i >= 0 && i < Colors.Length ? ColorOf(i) : this._custom;
		this.Close();
	}
}
