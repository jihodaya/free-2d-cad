using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Core.Tables;

namespace Free2DCad.App;

/// <summary>
/// 테이블 삽입 창 (기존 CAD와 같은 구성): 테이블 스타일, 삽입 옵션, 미리보기, 삽입 동작(위치/창), 열·행 설정, 셀 스타일.
/// 확인하면 Spec(표 설정)과 ByWindow(창 지정)를 돌려준다.
/// </summary>
public sealed class TableInsertDialog : Window
{
	private readonly NumericUpDown _cols = new();
	private readonly NumericUpDown _colWidth = new();
	private readonly NumericUpDown _dataRows = new();
	private readonly NumericUpDown _rowLines = new();
	private readonly NumericUpDown _textHeight = new();
	private readonly RadioButton _rbPoint = new() { Content = "삽입 위치 지정", GroupName = "TblIns", IsChecked = true };
	private readonly RadioButton _rbWindow = new() { Content = "창 지정", GroupName = "TblIns" };
	private readonly ComboBox _firstRow = new();
	private readonly ComboBox _secondRow = new();
	private readonly ComboBox _otherRows = new();
	private readonly CheckBox _chkPreview = new() { Content = "미리보기", IsChecked = true };
	private readonly TablePreview _preview = new();

	public TableEditor.TableSpec? Result { get; private set; }

	public bool ByWindow { get; private set; }

	/// <summary>지난번 설정 (다시 열 때 그대로).</summary>
	public static TableEditor.TableSpec Last { get; set; } = TableEditor.TableSpec.Default;

	public TableInsertDialog()
	{
		this.Title = "테이블 삽입";
		this.Width = 640;
		this.Height = 560;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var last = Last;
		Setup(this._cols, 1, 1000, last.Columns, 0, "0");
		Setup(this._colWidth, 0.01, 100000, last.ColumnWidth, 1, "0.0000");
		Setup(this._dataRows, 1, 10000, last.DataRows, 0, "0");
		Setup(this._rowLines, 1, 100, last.RowLines, 0, "0");
		Setup(this._textHeight, 0.01, 10000, last.TextHeight, 0.5, "0.###");

		string[] styles = { "제목", "헤더", "데이터" };
		foreach (var cb in new[] { this._firstRow, this._secondRow, this._otherRows })
		{
			cb.ItemsSource = styles;
			cb.Width = 110;
			cb.Height = 24;
			cb.FontSize = 12;
		}
		this._firstRow.SelectedIndex = last.TitleRow ? 0 : (last.HeaderRow ? 1 : 2);
		this._secondRow.SelectedIndex = last.HeaderRow ? 1 : 2;
		this._otherRows.SelectedIndex = 2;
		this._otherRows.IsEnabled = false;

		var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(12) };

		// 왼쪽
		var left = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 8, 0) };
		var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
		styleRow.Children.Add(new ComboBox { ItemsSource = new[] { "Standard" }, SelectedIndex = 0, Width = 200, Height = 24, FontSize = 12 });
		styleRow.Children.Add(new Button { Content = "…", Width = 28, Height = 24, IsEnabled = false });
		left.Children.Add(Group("테이블 STYLE", styleRow));

		var insOpt = new StackPanel { Spacing = 4 };
		insOpt.Children.Add(new RadioButton { Content = "빈 테이블에서 시작", GroupName = "TblSrc", IsChecked = true, FontSize = 12 });
		insOpt.Children.Add(new RadioButton { Content = "데이터 링크에서", GroupName = "TblSrc", IsEnabled = false, FontSize = 12 });
		insOpt.Children.Add(new RadioButton { Content = "데이터 추출에서", GroupName = "TblSrc", IsEnabled = false, FontSize = 12 });
		left.Children.Add(Group("삽입 옵션", insOpt));

		this._chkPreview.FontSize = 12;
		this._chkPreview.IsCheckedChanged += (_, _) => this._preview.IsVisible = this._chkPreview.IsChecked == true;
		this._preview.Height = 150;
		left.Children.Add(Group(null, new StackPanel { Spacing = 4, Children = { this._chkPreview, this._preview } }));

		// 오른쪽
		var right = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0, 0, 0) };
		this._rbPoint.FontSize = 12;
		this._rbWindow.FontSize = 12;
		right.Children.Add(Group("삽입 동작", new StackPanel { Spacing = 4, Children = { this._rbPoint, this._rbWindow } }));

		var rc = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 2, ColumnSpacing = 10 };
		void Cell(int r, int c, Control ctl) { Grid.SetRow(ctl, r); Grid.SetColumn(ctl, c); rc.Children.Add(ctl); }
		Cell(0, 0, Label("열:"));
		Cell(0, 1, Label("열 너비:"));
		Cell(1, 0, this._cols);
		Cell(1, 1, this._colWidth);
		Cell(2, 0, Label("데이터 행:"));
		Cell(2, 1, Label("행 높이(줄 수):"));
		Cell(3, 0, this._dataRows);
		Cell(3, 1, this._rowLines);
		Cell(4, 0, Label("문자 높이:"));
		Cell(5, 0, this._textHeight);
		right.Children.Add(Group("열 및 행 설정", rc));

		var cs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("30,30,30") };
		void CsRow(int r, string text, ComboBox cb) { var l = Label(text); Grid.SetRow(l, r); cs.Children.Add(l); Grid.SetRow(cb, r); Grid.SetColumn(cb, 1); cs.Children.Add(cb); }
		CsRow(0, "첫 번째 행 셀 STYLE:", this._firstRow);
		CsRow(1, "두 번째 행 셀 STYLE:", this._secondRow);
		CsRow(2, "그 외 모든 행의 셀 STYLE:", this._otherRows);
		right.Children.Add(Group("셀 STYLE 설정", cs));

		Grid.SetColumn(right, 1);
		root.Children.Add(left);
		root.Children.Add(right);

		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
		var ok = new Button { Content = "확인", Width = 84, Height = 28, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		var cancel = new Button { Content = "취소", Width = 84, Height = 28, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		ok.Click += (_, _) =>
		{
			this.Result = this.CurrentSpec();
			this.ByWindow = this._rbWindow.IsChecked == true;
			Last = this.Result;
			this.Close();
		};
		cancel.Click += (_, _) => this.Close();
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);
		Grid.SetRow(buttons, 1);
		Grid.SetColumnSpan(buttons, 2);
		root.Children.Add(buttons);
		this.Content = root;

		foreach (var n in new[] { this._cols, this._dataRows })
		{
			n.ValueChanged += (_, _) => this.UpdatePreview();
		}
		this._firstRow.SelectionChanged += (_, _) => this.UpdatePreview();
		this._secondRow.SelectionChanged += (_, _) => this.UpdatePreview();
		this.UpdatePreview();
	}

	private TableEditor.TableSpec CurrentSpec() => new(
		(int)(this._cols.Value ?? 5),
		(double)(this._colWidth.Value ?? 63.5m),
		(int)(this._dataRows.Value ?? 1),
		(int)(this._rowLines.Value ?? 1),
		(double)(this._textHeight.Value ?? 4.5m),
		this._firstRow.SelectedIndex == 0,
		this._firstRow.SelectedIndex == 1 || (this._firstRow.SelectedIndex == 0 && this._secondRow.SelectedIndex == 1));

	private void UpdatePreview()
	{
		var s = this.CurrentSpec();
		this._preview.Columns = Math.Clamp(s.Columns, 1, 12);
		this._preview.TitleRow = s.TitleRow;
		this._preview.HeaderRow = s.HeaderRow;
		this._preview.DataRows = Math.Clamp(s.DataRows, 1, 8);
		this._preview.InvalidateVisual();
	}

	private static void Setup(NumericUpDown n, double min, double max, double value, double inc, string fmt)
	{
		n.Minimum = (decimal)min;
		n.Maximum = (decimal)max;
		n.Value = (decimal)value;
		n.Increment = inc > 0 ? (decimal)inc : 1m;
		n.FormatString = fmt;
		n.Height = 26;
		n.FontSize = 12;
		n.HorizontalAlignment = HorizontalAlignment.Stretch;
	}

	private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = CadTheme.DialogFg, Margin = new Thickness(0, 4, 0, 0) };

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
			Padding = new Thickness(10, 8),
			Child = panel,
		};
	}

	/// <summary>미리보기: 검은 바탕에 제목/헤더/데이터 칸 (기존 CAD 미리보기와 같은 모양).</summary>
	private sealed class TablePreview : Control
	{
		public int Columns { get; set; } = 3;
		public bool TitleRow { get; set; } = true;
		public bool HeaderRow { get; set; } = true;
		public int DataRows { get; set; } = 1;

		public override void Render(DrawingContext dc)
		{
			var b = this.Bounds;
			dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, b.Width, b.Height));
			int rows = (this.TitleRow ? 1 : 0) + (this.HeaderRow ? 1 : 0) + this.DataRows;
			double w = b.Width - 40, h = Math.Min(b.Height - 16, rows * 16);
			double x0 = 20, y0 = (b.Height - h) / 2;
			double rh = h / rows, cw = w / this.Columns;
			var pen = new Pen(Brushes.White, 1);
			var typeface = new Typeface("Malgun Gothic");
			void Text(string s, double cx, double cy, double size)
			{
				var ft = new FormattedText(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White);
				dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
			}
			dc.DrawRectangle(null, pen, new Rect(x0, y0, w, h));
			for (int r = 1; r < rows; r++)
			{
				dc.DrawLine(pen, new Point(x0, y0 + r * rh), new Point(x0 + w, y0 + r * rh));
			}
			double colTop = y0 + (this.TitleRow ? rh : 0);
			for (int c = 1; c < this.Columns; c++)
			{
				dc.DrawLine(pen, new Point(x0 + c * cw, colTop), new Point(x0 + c * cw, y0 + h));
			}
			int row = 0;
			if (this.TitleRow)
			{
				Text("제목", x0 + w / 2, y0 + rh / 2, Math.Min(rh * 0.7, 13));
				row++;
			}
			if (this.HeaderRow)
			{
				for (int c = 0; c < this.Columns; c++) Text("헤더", x0 + (c + 0.5) * cw, y0 + (row + 0.5) * rh, Math.Min(rh * 0.6, 10));
				row++;
			}
			for (; row < rows; row++)
			{
				for (int c = 0; c < this.Columns; c++) Text("데이터", x0 + (c + 0.5) * cw, y0 + (row + 0.5) * rh, Math.Min(rh * 0.6, 10));
			}
		}
	}
}
