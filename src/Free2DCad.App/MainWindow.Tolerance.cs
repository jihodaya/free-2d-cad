using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// TOLERANCE: 기존 CAD "기하학적 공차" 대화상자와 같은 구성 — 기호 / 공차 1·2(지름·값·재료 조건) / 데이터 1~3(값·재료 조건) 두 줄,
/// 높이·투영 공차 영역·데이터 식별자. 확인하면 "공차 위치 선택:".
/// </summary>
public sealed partial class MainWindow
{
	/// <summary>gdt 글자 → 화면 표시 글자.</summary>
	private static string GdtGlyph(char c) => c switch
	{
		'j' => "⌖", 'k' => "⌒", 'e' => "○", 'a' => "∠", 'i' => "⌯", 't' => "⌰", 'd' => "⌓", 'c' => "▱", 'b' => "⊥",
		'r' => "◎", 'h' => "↗", 'u' => "—", 'g' => "⌭", 'f' => "∥", 'n' => "⌀", 'm' => "Ⓜ", 'l' => "Ⓛ", 's' => "Ⓢ", 'p' => "Ⓟ",
		_ => string.Empty,
	};

	private async Task ShowToleranceDialogAsync()
	{
		if (await this.AskToleranceAsync() is { } result)
		{
			this.StartCommand(new ToleranceCommand(result));
		}
	}

	/// <summary>기하학적 공차 대화상자 → 공차 문자열 (취소 = null). TOLERANCE와 LEADER 공차(T) 옵션이 함께 쓴다.</summary>
	internal async Task<string?> AskToleranceAsync()
	{
		var black = Brushes.Black;
		var font = new FontFamily("Segoe UI Symbol, Malgun Gothic");

		// 검은 사각 버튼 (기존 CAD처럼): 누르면 고르기 목록
		Button Square(Func<char> get, Action<char> set, char[] choices, int columns)
		{
			var tb = new TextBlock { FontFamily = font, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			var b = new Button { Width = 24, Height = 24, Padding = new Thickness(0), Background = black, BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(0), Content = tb };
			void Refresh() => tb.Text = GdtGlyph(get());
			Refresh();
			var grid = new UniformGrid { Columns = columns, Background = Brushes.White };
			var flyout = new Flyout { Placement = PlacementMode.Bottom, Content = grid };
			foreach (char c in choices)
			{
				var cb = new Button
				{
					Width = 28,
					Height = 28,
					Margin = new Thickness(1),
					Padding = new Thickness(0),
					Background = black,
					CornerRadius = new CornerRadius(0),
					Content = new TextBlock { Text = GdtGlyph(c), FontFamily = font, FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
					[ToolTip.TipProperty] = GdtText.Symbols.FirstOrDefault(s => s.Code == c).Name ?? (c == '\0' ? "없음" : c.ToString()),
				};
				cb.Click += (_, _) =>
				{
					set(c);
					Refresh();
					flyout.Hide();
				};
				grid.Children.Add(cb);
			}
			b.Flyout = flyout;
			return b;
		}

		// 지름 칸: 누를 때마다 켜고 끔
		Button Dia(Func<bool> get, Action<bool> set)
		{
			var tb = new TextBlock { FontFamily = font, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			var b = new Button { Width = 24, Height = 24, Padding = new Thickness(0), Background = black, BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(0), Content = tb };
			b.Click += (_, _) =>
			{
				set(!get());
				tb.Text = get() ? "⌀" : string.Empty;
			};
			return b;
		}

		TextBox Box(double w) => new() { Width = w, Height = 24, MinHeight = 24, Padding = new Thickness(3, 1), FontSize = 12 };

		char[] symbols = { 'j', 'k', 'e', 'a', 'i', 't', 'd', 'c', 'b', 'r', 'h', 'u', 'g', 'f', '\0' };
		char[] mcs = { 'm', 'l', 's', '\0' };

		var sym = new char[2];
		var dia1 = new bool[2];
		var dia2 = new bool[2];
		var mc1 = new char[2];
		var mc2 = new char[2];
		var dmc = new char[2, 3];
		var tol1 = new TextBox[2];
		var tol2 = new TextBox[2];
		var dat = new TextBox[2, 3];
		bool projected = false;

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto,Auto,Auto,12,Auto,Auto,Auto,12,Auto,Auto,8,Auto,Auto,8,Auto,Auto"),
			RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
		};
		void Put(Control c, int row, int col, int span = 1)
		{
			Grid.SetRow(c, row);
			Grid.SetColumn(c, col);
			Grid.SetColumnSpan(c, span);
			c.Margin = new Thickness(1, 2);
			grid.Children.Add(c);
		}
		TextBlock Label(string t) => new() { Text = t, FontSize = 12 };
		Put(Label("기호"), 0, 0);
		Put(Label("공차 1"), 0, 2, 3);
		Put(Label("공차 2"), 0, 6, 3);
		Put(Label("데이터 1"), 0, 10, 2);
		Put(Label("데이터 2"), 0, 13, 2);
		Put(Label("데이터 3"), 0, 16, 2);
		Put(Label("지"), 1, 2);
		Put(Label("M.C."), 1, 4);
		Put(Label("지"), 1, 6);
		Put(Label("M.C."), 1, 8);
		Put(Label("M.C."), 1, 11);
		Put(Label("M.C."), 1, 14);
		Put(Label("M.C."), 1, 17);
		for (int r = 0; r < 2; r++)
		{
			int row = r;
			int gr = 2 + r;
			Put(Square(() => sym[row], c => sym[row] = c, symbols, 3), gr, 0);
			Put(Dia(() => dia1[row], v => dia1[row] = v), gr, 2);
			Put(tol1[row] = Box(90), gr, 3);
			Put(Square(() => mc1[row], c => mc1[row] = c, mcs, 1), gr, 4);
			Put(Dia(() => dia2[row], v => dia2[row] = v), gr, 6);
			Put(tol2[row] = Box(90), gr, 7);
			Put(Square(() => mc2[row], c => mc2[row] = c, mcs, 1), gr, 8);
			for (int d = 0; d < 3; d++)
			{
				int dd = d;
				Put(dat[row, d] = Box(46), gr, 10 + d * 3);
				Put(Square(() => dmc[row, dd], c => dmc[row, dd] = c, mcs, 1), gr, 11 + d * 3);
			}
		}

		var height = Box(90);
		var ident = Box(90);
		var projBtn = Dia(() => projected, v => projected = v);
		((TextBlock)projBtn.Content!).Text = string.Empty;
		projBtn.Click += (_, _) => ((TextBlock)projBtn.Content!).Text = projected ? "Ⓟ" : string.Empty;

		string? result = null;
		var ok = new Button { Content = "확인", Width = 84, IsDefault = true };
		var cancel = new Button { Content = "취소", Width = 84, IsCancel = true };
		var dlg = new Window
		{
			Title = "기하학적 공차",
			SizeToContent = SizeToContent.WidthAndHeight,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		ok.Click += (_, _) =>
		{
			var lines = new List<string>();
			for (int r = 0; r < 2; r++)
			{
				string row = GdtText.Row(sym[r], (dia1[r], tol1[r].Text ?? string.Empty, mc1[r]), (dia2[r], tol2[r].Text ?? string.Empty, mc2[r]),
					(dat[r, 0].Text ?? string.Empty, dmc[r, 0]), (dat[r, 1].Text ?? string.Empty, dmc[r, 1]), (dat[r, 2].Text ?? string.Empty, dmc[r, 2]));
				if (!GdtText.IsEmptyRow(row)) lines.Add(row);
			}
			// 투영 공차 영역 높이 (값 + Ⓟ), 데이터 식별자: 각각 따로 줄 (기존 CAD 형식)
			if (!string.IsNullOrWhiteSpace(height.Text) || projected)
			{
				lines.Add((height.Text ?? string.Empty).Trim() + (projected ? "{\\Fgdt;p}" : string.Empty));
			}
			if (!string.IsNullOrWhiteSpace(ident.Text))
			{
				lines.Add(ident.Text!.Trim());
			}
			result = lines.Count > 0 ? string.Join("^J", lines) : null;
			dlg.Close();
		};
		cancel.Click += (_, _) => dlg.Close();

		var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,Auto,*,Auto,8,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
		void B(Control c, int row, int col)
		{
			Grid.SetRow(c, row);
			Grid.SetColumn(c, col);
			c.Margin = new Thickness(0, 3);
			c.VerticalAlignment = VerticalAlignment.Center;
			bottom.Children.Add(c);
		}
		B(Label("높이(H):"), 0, 0);
		B(height, 0, 2);
		B(Label("투영 공차 영역:"), 0, 4);
		B(projBtn, 0, 6);
		B(Label("데이터 식별자"), 1, 0);
		B(ident, 1, 2);

		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				grid,
				bottom,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
			},
		};
		await dlg.ShowDialog(this);
		return result;
	}
}
