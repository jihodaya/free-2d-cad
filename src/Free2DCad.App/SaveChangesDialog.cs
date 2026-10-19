using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Free2DCad.App;

/// <summary>저장 확인 결과.</summary>
public enum SaveChoice
{
	Cancel,
	Save,
	DontSave,
}

/// <summary>
/// 변경 내용 저장 확인 창 (Windows 11 메시지 상자 형식): 위쪽 흰 영역에 아이콘 + 굵은 질문 + 파일 이름,
/// 아래쪽 회색 막대에 같은 크기의 버튼(저장 = 파란 기본 버튼)을 오른쪽 정렬.
/// </summary>
public sealed class SaveChangesDialog : Window
{
	private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));
	private static readonly IBrush AccentHover = new SolidColorBrush(Color.FromRgb(0x19, 0x6E, 0xBF));
	private static readonly IBrush BarBg = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
	private static readonly IBrush BarLine = new SolidColorBrush(Color.FromRgb(0xE5, 0xE5, 0xE5));
	private static readonly IBrush SubText = new SolidColorBrush(Color.FromRgb(0x5F, 0x5F, 0x5F));

	public SaveChoice Choice { get; private set; } = SaveChoice.Cancel;

	public SaveChangesDialog(string docName)
	{
		this.Title = "Free 2D CAD";
		this.Width = 440;
		this.SizeToContent = SizeToContent.Height;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = Brushes.White;
		this.ShowInTaskbar = false;

		// 아이콘: 파란 원 안의 흰 "?" (Windows 질문 아이콘과 같은 느낌)
		var icon = new Border
		{
			Width = 36,
			Height = 36,
			CornerRadius = new CornerRadius(18),
			Background = Accent,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0, 2, 16, 0),
			Child = new TextBlock
			{
				Text = "?",
				FontSize = 22,
				FontWeight = FontWeight.SemiBold,
				Foreground = Brushes.White,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, -2, 0, 0),
			},
		};

		var texts = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
		texts.Children.Add(new TextBlock
		{
			Text = "변경 내용을 저장하시겠습니까?",
			FontSize = 16,
			FontWeight = FontWeight.SemiBold,
			Foreground = Brushes.Black,
			TextWrapping = TextWrapping.Wrap,
		});
		texts.Children.Add(new TextBlock
		{
			Text = docName,
			FontSize = 13,
			Foreground = Brushes.Black,
			TextTrimming = TextTrimming.CharacterEllipsis,
		});
		texts.Children.Add(new TextBlock
		{
			Text = "저장하지 않으면 변경 내용이 사라집니다.",
			FontSize = 12,
			Foreground = SubText,
			TextWrapping = TextWrapping.Wrap,
		});

		var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(24, 22, 24, 22) };
		content.Children.Add(icon);
		Grid.SetColumn(texts, 1);
		content.Children.Add(texts);

		var btnSave = MakeButton("저장(S)", primary: true);
		var btnDontSave = MakeButton("저장 안 함(N)", primary: false);
		var btnCancel = MakeButton("취소", primary: false);
		btnSave.IsDefault = true;
		btnCancel.IsCancel = true;
		btnSave.Click += (_, _) => this.Finish(SaveChoice.Save);
		btnDontSave.Click += (_, _) => this.Finish(SaveChoice.DontSave);
		btnCancel.Click += (_, _) => this.Finish(SaveChoice.Cancel);

		var buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center,
			Children = { btnSave, btnDontSave, btnCancel },
		};
		var bar = new Border
		{
			Background = BarBg,
			BorderBrush = BarLine,
			BorderThickness = new Thickness(0, 1, 0, 0),
			Padding = new Thickness(24, 14),
			Child = buttons,
		};

		var root = new DockPanel();
		DockPanel.SetDock(bar, Dock.Bottom);
		root.Children.Add(bar);
		root.Children.Add(content);
		this.Content = root;

		// S / N 단축키 (Alt 없이도)
		this.KeyDown += (_, e) =>
		{
			if (e.Key == Key.S) this.Finish(SaveChoice.Save);
			else if (e.Key == Key.N) this.Finish(SaveChoice.DontSave);
		};
		this.Opened += (_, _) => btnSave.Focus();
	}

	private void Finish(SaveChoice choice)
	{
		this.Choice = choice;
		this.Close();
	}

	private static Button MakeButton(string text, bool primary)
	{
		var b = new Button
		{
			Content = text,
			Width = 104,
			Height = 32,
			MinHeight = 0,
			FontSize = 13,
			Padding = new Thickness(0),
			CornerRadius = new CornerRadius(4),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		if (primary)
		{
			b.Background = Accent;
			b.Foreground = Brushes.White;
			b.BorderThickness = new Thickness(0);
			// 플루언트 테마의 호버 배경을 덮어 기본 버튼 색 유지
			b.Styles.Add(new Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
			{
				Setters =
				{
					new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, AccentHover),
					new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
				},
			});
			b.Styles.Add(new Style(x => x.OfType<Button>().Class(":pressed").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
			{
				Setters =
				{
					new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, Accent),
					new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
				},
			});
		}
		else
		{
			b.Background = Brushes.White;
			b.BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD1, 0xD1));
			b.BorderThickness = new Thickness(1);
		}
		return b;
	}
}
