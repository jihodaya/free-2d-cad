using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AcadColor = ACadSharp.Color;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 색상 선택 대화상자 (기존 CAD 색인 색상 ACI 1~255, BYLAYER, BYBLOCK).
/// </summary>
public sealed class ColorPickerDialog : Window
{
	private AcadColor? _selectedColor;
	private readonly Border _previewSwatch = new();
	private readonly TextBlock _lblSelectedInfo = new();
	private readonly TextBox _txtColor = new();
	private readonly Border[] _swatchBorders = new Border[258]; // 0~257

	public AcadColor? SelectedColor => this._selectedColor;

	public ColorPickerDialog(AcadColor? initialColor = null)
	{
		this._selectedColor = initialColor ?? AcadColor.ByLayer;

		this.Title = "색상 선택";
		this.Width = 530;
		this.Height = 440;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(10) };

		// 하단 버튼 및 정보 줄
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 중앙 색상 팔레트 영역
		var mainPanel = new StackPanel { Spacing = 8 };

		// 1. ACI 10~249 팔레트 (24열 x 10행)
		var grpAci = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			Background = Brushes.White,
			Padding = new Thickness(6),
		};

		var aciGrid = new Grid();
		for (int c = 0; c < 24; c++)
		{
			aciGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		}
		for (int r = 0; r < 10; r++)
		{
			aciGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
		}

		// 행 순서: 짝수(10, 12, 14, 16, 18), 홀수(11, 13, 15, 17, 19)
		int[] rowOffsets = { 0, 2, 4, 6, 8, 1, 3, 5, 7, 9 };
		for (int col = 0; col < 24; col++)
		{
			int baseIndex = 10 + col * 10;
			for (int row = 0; row < 10; row++)
			{
				int colorIndex = baseIndex + rowOffsets[row];
				var swatch = this.CreateColorSwatch((short)colorIndex, 18, 14);
				Grid.SetColumn(swatch, col);
				Grid.SetRow(swatch, row);
				aciGrid.Children.Add(swatch);
			}
		}
		grpAci.Child = aciGrid;
		mainPanel.Children.Add(grpAci);

		// 2. 표준 9색 (1~9) 및 무채색/회색 (250~255)
		var grpStd = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			Background = Brushes.White,
			Padding = new Thickness(6, 4),
		};
		var stdDock = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
		};

		// 표준 9색
		var stdStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		for (short i = 1; i <= 9; i++)
		{
			stdStack.Children.Add(this.CreateColorSwatch(i, 20, 20));
		}
		Grid.SetColumn(stdStack, 0);
		stdDock.Children.Add(stdStack);

		// 회색 6색 (250~255)
		var grayStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
		for (short i = 250; i <= 255; i++)
		{
			grayStack.Children.Add(this.CreateColorSwatch(i, 20, 20));
		}
		Grid.SetColumn(grayStack, 2);
		stdDock.Children.Add(grayStack);

		grpStd.Child = stdDock;
		mainPanel.Children.Add(grpStd);

		// 3. BYLAYER / BYBLOCK 버튼
		var byLayerBlockStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Left,
		};
		var btnByLayer = new Button
		{
			Content = "BYLAYER",
			Width = 90,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			Padding = new Thickness(4, 2),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnByLayer.Click += (_, _) => this.SelectColor(AcadColor.ByLayer);
		byLayerBlockStack.Children.Add(btnByLayer);

		var btnByBlock = new Button
		{
			Content = "BYBLOCK",
			Width = 90,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			Padding = new Thickness(4, 2),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnByBlock.Click += (_, _) => this.SelectColor(AcadColor.ByBlock);
		byLayerBlockStack.Children.Add(btnByBlock);

		mainPanel.Children.Add(byLayerBlockStack);

		root.Children.Add(mainPanel);
		this.Content = root;

		this.SelectColor(this._selectedColor.Value);
	}

	private Border CreateColorSwatch(short index, double width, double height)
	{
		var acadColor = new AcadColor(index);
		var avColor = AvColor.FromRgb(acadColor.R, acadColor.G, acadColor.B);

		var border = new Border
		{
			Width = width,
			Height = height,
			Background = new SolidColorBrush(avColor),
			BorderBrush = new SolidColorBrush(AvColor.FromArgb(40, 0, 0, 0)),
			BorderThickness = new Thickness(1),
			Margin = new Thickness(1),
			Cursor = new Cursor(StandardCursorType.Hand),
			[ToolTip.TipProperty] = $"색상 {index} ({GetColorDescription(index)})",
		};

		border.PointerPressed += (_, _) => this.SelectColor(acadColor);
		if (index >= 0 && index < this._swatchBorders.Length)
		{
			this._swatchBorders[index] = border;
		}

		return border;
	}

	private void SelectColor(AcadColor color)
	{
		this._selectedColor = color;

		// 모든 견본 테두리 초기화
		for (int i = 0; i < this._swatchBorders.Length; i++)
		{
			if (this._swatchBorders[i] != null)
			{
				this._swatchBorders[i].BorderBrush = new SolidColorBrush(AvColor.FromArgb(40, 0, 0, 0));
				this._swatchBorders[i].BorderThickness = new Thickness(1);
			}
		}

		if (!color.IsByLayer && !color.IsByBlock && color.Index >= 0 && color.Index < this._swatchBorders.Length)
		{
			var b = this._swatchBorders[color.Index];
			if (b != null)
			{
				b.BorderBrush = Brushes.Black;
				b.BorderThickness = new Thickness(2);
			}
		}

		// 미리보기 업데이트
		if (color.IsByLayer)
		{
			this._previewSwatch.Background = Brushes.White;
			this._txtColor.Text = "BYLAYER";
			this._lblSelectedInfo.Text = "BYLAYER (도면층별)";
		}
		else if (color.IsByBlock)
		{
			this._previewSwatch.Background = Brushes.White;
			this._txtColor.Text = "BYBLOCK";
			this._lblSelectedInfo.Text = "BYBLOCK (블록별)";
		}
		else
		{
			this._previewSwatch.Background = new SolidColorBrush(AvColor.FromRgb(color.R, color.G, color.B));
			this._txtColor.Text = color.Index.ToString();
			this._lblSelectedInfo.Text = $"색인 {color.Index} - {GetColorDescription(color.Index)} (R:{color.R}, G:{color.G}, B:{color.B})";
		}
	}

	private Control BuildBottomBar()
	{
		var panel = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
			Height = 36,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 10, 0, 0),
		};

		// 1. 색상 견본
		this._previewSwatch.Width = 32;
		this._previewSwatch.Height = 24;
		this._previewSwatch.BorderBrush = Brushes.Gray;
		this._previewSwatch.BorderThickness = new Thickness(1);
		this._previewSwatch.VerticalAlignment = VerticalAlignment.Center;
		this._previewSwatch.Margin = new Thickness(0, 0, 8, 0);
		Grid.SetColumn(this._previewSwatch, 0);
		panel.Children.Add(this._previewSwatch);

		// 2. 색상 입력 칸
		var inputStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
		inputStack.Children.Add(new TextBlock { Text = "색상(C):", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		this._txtColor.Width = 80;
		this._txtColor.Height = 24;
		this._txtColor.MinHeight = 0;
		this._txtColor.FontSize = 12;
		this._txtColor.Padding = new Thickness(4, 0);
		this._txtColor.VerticalContentAlignment = VerticalAlignment.Center;
		this._txtColor.KeyDown += (s, e) =>
		{
			if (e.Key == Key.Enter)
			{
				this.ApplyTextColorInput();
				e.Handled = true;
			}
		};
		inputStack.Children.Add(this._txtColor);
		Grid.SetColumn(inputStack, 1);
		panel.Children.Add(inputStack);

		// 3. 안내 레이블
		this._lblSelectedInfo.FontSize = 11;
		this._lblSelectedInfo.Foreground = Brushes.DimGray;
		this._lblSelectedInfo.VerticalAlignment = VerticalAlignment.Center;
		this._lblSelectedInfo.Margin = new Thickness(10, 0);
		Grid.SetColumn(this._lblSelectedInfo, 2);
		panel.Children.Add(this._lblSelectedInfo);

		// 4. 확인 / 취소 버튼
		var btnOk = new Button
		{
			Content = "확인",
			Width = 68,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsDefault = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 6, 0),
		};
		btnOk.Click += (_, _) =>
		{
			this.ApplyTextColorInput();
			this.Close(this._selectedColor);
		};
		Grid.SetColumn(btnOk, 3);
		panel.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소",
			Width = 68,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsCancel = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close(null);
		Grid.SetColumn(btnCancel, 4);
		panel.Children.Add(btnCancel);

		return panel;
	}

	private void ApplyTextColorInput()
	{
		string txt = this._txtColor.Text?.Trim() ?? string.Empty;
		if (string.Equals(txt, "BYLAYER", StringComparison.OrdinalIgnoreCase))
		{
			this.SelectColor(AcadColor.ByLayer);
		}
		else if (string.Equals(txt, "BYBLOCK", StringComparison.OrdinalIgnoreCase))
		{
			this.SelectColor(AcadColor.ByBlock);
		}
		else if (short.TryParse(txt, out short idx) && idx >= 1 && idx <= 255)
		{
			this.SelectColor(new AcadColor(idx));
		}
	}

	public static string GetColorDescription(short index) => index switch
	{
		1 => "빨간색",
		2 => "노란색",
		3 => "초록색",
		4 => "하늘색",
		5 => "파란색",
		6 => "자홍색",
		7 => "흰색",
		8 => "어두운 회색",
		9 => "밝은 회색",
		_ => $"색인 {index}",
	};
}
