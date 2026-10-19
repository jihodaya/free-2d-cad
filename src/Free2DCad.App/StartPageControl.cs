using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 시작 페이지 컨트롤.
/// 새로 만들기, 열기 바로가기 및 최근 도면 문서 목록을 제공한다.
/// </summary>
public sealed class StartPageControl : Border
{
	public event Action? NewRequested;
	public event Action? OpenRequested;
	public event Action<string>? FileOpenRequested;

	private readonly StackPanel _recentList = new() { Spacing = 4 };

	public StartPageControl()
	{
		this.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x24, 0x28, 0x2C));
		this.Padding = new Avalonia.Thickness(40, 30);

		var mainGrid = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,*"),
			ColumnDefinitions = new ColumnDefinitions("360,*,480"),
		};

		// 1. 상단 타이틀
		var headerStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12,
			Margin = new Avalonia.Thickness(0, 0, 0, 30),
		};

		var logo = IconHelper.AppLogo(36);
		headerStack.Children.Add(logo);

		var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
		titleStack.Children.Add(new TextBlock
		{
			Text = "Free 2D CAD",
			FontSize = 20,
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
		});
		titleStack.Children.Add(new TextBlock
		{
			Text = $"무료 2D CAD (DWG·DXF) · 버전 {MainWindow.AppVersion}",
			FontSize = 12,
			Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
		});
		headerStack.Children.Add(titleStack);

		Grid.SetRow(headerStack, 0);
		Grid.SetColumn(headerStack, 0);
		Grid.SetColumnSpan(headerStack, 3);
		mainGrid.Children.Add(headerStack);

		// 2. 왼쪽: 시작 카드
		var startSection = new StackPanel { Spacing = 12 };
		startSection.Children.Add(new TextBlock
		{
			Text = "시작",
			FontSize = 14,
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
			Margin = new Avalonia.Thickness(0, 0, 0, 6),
		});

		var btnNew = this.CreateActionCard("새 도면 만들기", "기본 A3 템플릿으로 새 도면을 시작합니다.", IconHelper.New, () => this.NewRequested?.Invoke());
		var btnOpen = this.CreateActionCard("도면 열기...", "기존 DWG 또는 DXF 파일을 엽니다.", IconHelper.Open, () => this.OpenRequested?.Invoke());

		startSection.Children.Add(btnNew);
		startSection.Children.Add(btnOpen);

		Grid.SetRow(startSection, 1);
		Grid.SetColumn(startSection, 0);
		mainGrid.Children.Add(startSection);

		// 3. 오른쪽: 최근 문서
		var recentSection = new StackPanel { Spacing = 12 };
		recentSection.Children.Add(new TextBlock
		{
			Text = "최근 문서",
			FontSize = 14,
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
			Margin = new Avalonia.Thickness(0, 0, 0, 6),
		});

		var scroll = new ScrollViewer
		{
			Content = this._recentList,
			MaxHeight = 500,
		};
		recentSection.Children.Add(scroll);

		Grid.SetRow(recentSection, 1);
		Grid.SetColumn(recentSection, 2);
		mainGrid.Children.Add(recentSection);

		this.Child = mainGrid;

		this.PopulateDefaultRecentFiles();
	}

	private Border CreateActionCard(string title, string description, string iconKey, Action action)
	{
		var card = new Border
		{
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2F, 0x34, 0x3A)),
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x40, 0x47, 0x50)),
			BorderThickness = new Avalonia.Thickness(1),
			CornerRadius = new Avalonia.CornerRadius(6),
			Padding = new Avalonia.Thickness(16, 12),
			Cursor = new Cursor(StandardCursorType.Hand),
		};

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*"),
		};

		var icon = IconHelper.GetIcon(iconKey, 32);
		Grid.SetColumn(icon, 0);
		grid.Children.Add(icon);

		var textStack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Avalonia.Thickness(12, 0, 0, 0),
			Spacing = 2,
		};
		textStack.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 13,
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
		});
		textStack.Children.Add(new TextBlock
		{
			Text = description,
			FontSize = 11,
			Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xAA, 0xAA, 0xAA)),
			TextWrapping = TextWrapping.Wrap,
		});

		Grid.SetColumn(textStack, 1);
		grid.Children.Add(textStack);

		card.Child = grid;

		card.PointerEntered += (_, _) =>
		{
			card.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3B, 0x42, 0x4A));
			card.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x8E, 0xE6));
		};
		card.PointerExited += (_, _) =>
		{
			card.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2F, 0x34, 0x3A));
			card.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x40, 0x47, 0x50));
		};
		card.PointerPressed += (_, _) => action();

		return card;
	}

	public void AddRecentFile(string filePath, bool remember = true)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
		if (remember)
		{
			RecentFiles.Add(filePath);
		}

		// 중복 제거
		for (int i = this._recentList.Children.Count - 1; i >= 0; i--)
		{
			if (this._recentList.Children[i] is Border b && b.Tag is string p && string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase))
			{
				this._recentList.Children.RemoveAt(i);
			}
		}

		var row = this.CreateRecentFileRow(filePath);
		this._recentList.Children.Insert(0, row);
	}

	private Border CreateRecentFileRow(string filePath)
	{
		var row = new Border
		{
			Tag = filePath,
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2C, 0x30, 0x35)),
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3C, 0x41, 0x48)),
			BorderThickness = new Avalonia.Thickness(1),
			CornerRadius = new Avalonia.CornerRadius(4),
			Padding = new Avalonia.Thickness(12, 8),
			Cursor = new Cursor(StandardCursorType.Hand),
		};

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*"),
		};

		var icon = IconHelper.GetIcon(IconHelper.Save, 20);
		Grid.SetColumn(icon, 0);
		grid.Children.Add(icon);

		var stack = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Avalonia.Thickness(10, 0, 0, 0),
			Spacing = 2,
		};
		stack.Children.Add(new TextBlock
		{
			Text = Path.GetFileName(filePath),
			FontSize = 12,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brushes.White,
		});
		stack.Children.Add(new TextBlock
		{
			Text = filePath,
			FontSize = 10,
			Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88)),
			TextTrimming = TextTrimming.CharacterEllipsis,
		});

		Grid.SetColumn(stack, 1);
		grid.Children.Add(stack);

		row.Child = grid;

		row.PointerEntered += (_, _) =>
		{
			row.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x38, 0x3E, 0x46));
			row.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x8E, 0xE6));
		};
		row.PointerExited += (_, _) =>
		{
			row.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2C, 0x30, 0x35));
			row.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3C, 0x41, 0x48));
		};
		row.PointerPressed += (_, _) => this.FileOpenRequested?.Invoke(filePath);

		return row;
	}

	private void PopulateDefaultRecentFiles()
	{
		// 저장된 최근 파일 (오래된 것부터 넣어 최신이 맨 위)
		foreach (var path in RecentFiles.Items.Reverse())
		{
			this.AddRecentFile(path, remember: false);
		}
	}

}
