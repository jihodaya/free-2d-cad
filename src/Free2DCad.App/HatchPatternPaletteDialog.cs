using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Free2DCad.Core.Hatching;
using Point = Avalonia.Point;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 견본 그림 렌더링 캔버스 (76x44).
/// </summary>
public sealed class HatchSwatchCanvas : Control
{
	private readonly string _patternName;
	private readonly IReadOnlyList<(double X1, double Y1, double X2, double Y2)> _segments;

	public HatchSwatchCanvas(string patternName, double width = 76, double height = 44)
	{
		this._patternName = patternName;
		this.Width = width;
		this.Height = height;
		this._segments = HatchPatternLibrary.PreviewSegments(patternName, width, height, 0);
	}

	public override void Render(DrawingContext dc)
	{
		var rect = new Rect(0, 0, this.Width, this.Height);
		dc.DrawRectangle(Brushes.Black, null, rect);

		if (string.Equals(this._patternName, "SOLID", StringComparison.OrdinalIgnoreCase))
		{
			// 기존 CAD 견본처럼 가운데 흰 직사각형 채움
			dc.DrawRectangle(Brushes.White, null, new Rect(10, 8, this.Width - 20, this.Height - 16));
			return;
		}

		var pen = new Pen(Brushes.White, 1.0);
		foreach (var seg in this._segments)
		{
			// 선분의 y는 위쪽이 +이므로 뒤집어서 그림
			double y1 = this.Height - seg.Y1;
			double y2 = this.Height - seg.Y2;
			dc.DrawLine(pen, new Point(seg.X1, y1), new Point(seg.X2, y2));
		}
	}
}

/// <summary>
/// 기존 CAD 스타일 해치 패턴 선택 대화상자.
/// </summary>
public sealed class HatchPatternPaletteDialog : Window
{
	private string? _selectedPattern;
	private Border? _selectedBorder;
	private readonly Dictionary<string, Border> _patternBorders = new(StringComparer.OrdinalIgnoreCase);

	public string? SelectedPattern => this._selectedPattern;

	public HatchPatternPaletteDialog(string? initialPattern = null)
	{
		this._selectedPattern = initialPattern ?? "ANSI31";

		this.Title = "해치 패턴";
		this.Width = 430;
		this.Height = 500;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(10) };

		// 하단 버튼 바
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 중앙 탭
		var tabControl = new TabControl
		{
			Background = Brushes.Transparent,
		};

		tabControl.Items.Add(this.CreateTab("ANSI", HatchPatternLibrary.ByCategory("ANSI")));
		tabControl.Items.Add(this.CreateTab("ISO", HatchPatternLibrary.ByCategory("ISO")));
		tabControl.Items.Add(this.CreateTab("기타 미리 정의", HatchPatternLibrary.ByCategory("기타")));
		// 사용자: 패턴 폴더의 .pat 파일 + 열어 둔 도면에서 가져온 패턴
		var custom = HatchPatternLibrary.ByCategory("사용자");
		if (custom.Count > 0)
		{
			tabControl.Items.Add(this.CreateTab("사용자", custom));
		}
		else
		{
			tabControl.Items.Add(new TabItem
			{
				Header = "사용자",
				Content = new TextBlock
				{
					Text = "사용자 패턴이 없습니다.\n.pat 파일을 아래 폴더에 넣으면 여기에 나옵니다.\n"
						+ HatchPatternLibrary.UserPatternFolder
						+ "\n해치가 있는 도면을 열면 그 도면의 패턴도 여기에 추가됩니다.",
					Margin = new Thickness(20),
					Foreground = Brushes.Gray,
					FontSize = 12,
					TextWrapping = TextWrapping.Wrap,
				},
			});
		}

		// 초기 선택 탭 활성화
		var initInfo = HatchPatternLibrary.All.FirstOrDefault(p => string.Equals(p.Name, this._selectedPattern, StringComparison.OrdinalIgnoreCase));
		if (initInfo != null)
		{
			if (initInfo.Category == "ISO")
			{
				tabControl.SelectedIndex = 1;
			}
			else if (initInfo.Category == "기타")
			{
				tabControl.SelectedIndex = 2;
			}
			else if (initInfo.Category == "사용자")
			{
				tabControl.SelectedIndex = 3;
			}
			else
			{
				tabControl.SelectedIndex = 0;
			}
		}

		root.Children.Add(tabControl);
		this.Content = root;

		if (!string.IsNullOrEmpty(this._selectedPattern))
		{
			this.HighlightPattern(this._selectedPattern);
		}
	}

	private TabItem CreateTab(string header, IReadOnlyList<HatchPatternInfo> patterns)
	{
		var grid = new UniformGrid
		{
			Columns = 4,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(2),
		};

		foreach (var pat in patterns)
		{
			var border = new Border
			{
				Width = 84,
				Height = 72,
				BorderThickness = new Thickness(1.5),
				BorderBrush = Brushes.Transparent,
				CornerRadius = new CornerRadius(2),
				Padding = new Thickness(3),
				Margin = new Thickness(2),
				Background = Brushes.Transparent,
				Cursor = new Cursor(StandardCursorType.Hand),
				[ToolTip.TipProperty] = string.IsNullOrEmpty(pat.Description) ? pat.Name : $"{pat.Name}: {pat.Description}",
			};

			var stack = new StackPanel
			{
				HorizontalAlignment = HorizontalAlignment.Center,
				Children =
				{
					new HatchSwatchCanvas(pat.Name, 76, 44),
					new TextBlock
					{
						Text = pat.Name,
						FontSize = 11,
						TextAlignment = TextAlignment.Center,
						Foreground = Brushes.Black,
						Margin = new Thickness(0, 3, 0, 0),
						TextTrimming = TextTrimming.CharacterEllipsis,
					},
				},
			};
			border.Child = stack;

			string name = pat.Name;
			border.PointerPressed += (s, e) =>
			{
				if (e.ClickCount >= 2)
				{
					this.SelectPattern(name);
					this.Close(this._selectedPattern);
				}
				else
				{
					this.SelectPattern(name);
				}
			};

			this._patternBorders[pat.Name] = border;
			grid.Children.Add(border);
		}

		var scroll = new ScrollViewer
		{
			Content = grid,
			HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			Margin = new Thickness(0, 4, 0, 0),
		};

		return new TabItem
		{
			Header = header,
			Content = scroll,
		};
	}

	private void SelectPattern(string name)
	{
		this._selectedPattern = name;
		this.HighlightPattern(name);
	}

	private void HighlightPattern(string name)
	{
		if (this._selectedBorder != null)
		{
			this._selectedBorder.BorderBrush = Brushes.Transparent;
			this._selectedBorder.Background = Brushes.Transparent;
		}

		if (this._patternBorders.TryGetValue(name, out var b))
		{
			this._selectedBorder = b;
			b.BorderBrush = new SolidColorBrush(AvColor.FromRgb(0x00, 0x78, 0xD7));
			b.Background = new SolidColorBrush(AvColor.FromArgb(30, 0, 120, 215));
		}
	}

	private Control BuildBottomBar()
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Spacing = 8,
			Margin = new Thickness(0, 10, 0, 0),
		};

		var btnOk = new Button
		{
			Content = "확인",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsDefault = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnOk.Click += (_, _) => this.Close(this._selectedPattern);
		panel.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsCancel = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close(null);
		panel.Children.Add(btnCancel);

		var btnApply = new Button
		{
			Content = "적용(A)",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsEnabled = false,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		panel.Children.Add(btnApply);

		return panel;
	}
}
