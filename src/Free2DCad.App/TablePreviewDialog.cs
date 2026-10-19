using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Free2DCad.Core.Tools;

namespace Free2DCad.App;

/// <summary>
/// 도면에서 추출된 표 데이터를 미리보고 CSV 저장 및 클립보드 복사를 제공하는 대화상자.
/// </summary>
public sealed class TablePreviewDialog : Window
{
	private readonly TableResult _table;

	public TablePreviewDialog(TableResult table)
	{
		this._table = table ?? throw new ArgumentNullException(nameof(table));

		this.Title = "표 추출 미리보기";
		this.Width = 800;
		this.Height = 550;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var mainDock = new DockPanel { Margin = new Avalonia.Thickness(12) };

		// 상단 요약
		int rowCount = table.Rows.Length;
		int colCount = table.Rows.Length > 0 ? table.Rows[0].Length : 0;
		var titleText = new TextBlock
		{
			Text = $"추출된 표: {rowCount}행 {colCount}열",
			FontWeight = FontWeight.Bold,
			FontSize = 14,
			Foreground = CadTheme.DialogFg,
			Margin = new Avalonia.Thickness(0, 0, 0, 10),
		};
		DockPanel.SetDock(titleText, Dock.Top);
		mainDock.Children.Add(titleText);

		// 하단 버튼 모음
		var btnSaveCsv = new Button
		{
			Content = "CSV 저장(_S)...",
			Padding = new Avalonia.Thickness(12, 6),
			Margin = new Avalonia.Thickness(4),
		};
		btnSaveCsv.Click += async (_, _) => await this.SaveCsvAsync();

		var btnCopyClipboard = new Button
		{
			Content = "클립보드 복사(탭 구분)(_C)",
			Padding = new Avalonia.Thickness(12, 6),
			Margin = new Avalonia.Thickness(4),
		};
		btnCopyClipboard.Click += async (_, _) => await this.CopyClipboardAsync();

		var btnClose = new Button
		{
			Content = "닫기(_X)",
			Padding = new Avalonia.Thickness(12, 6),
			Margin = new Avalonia.Thickness(4),
		};
		btnClose.Click += (_, _) => this.Close();

		var buttonPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Avalonia.Thickness(0, 10, 0, 0),
			Children = { btnSaveCsv, btnCopyClipboard, btnClose },
		};
		DockPanel.SetDock(buttonPanel, Dock.Bottom);
		mainDock.Children.Add(buttonPanel);

		// 중앙 표 테이블
		var tableGrid = new Grid();
		for (int c = 0; c < colCount; c++)
		{
			tableGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		}
		for (int r = 0; r < rowCount; r++)
		{
			tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
		}

		for (int r = 0; r < rowCount; r++)
		{
			var row = table.Rows[r];
			bool isHeader = (r == 0);

			for (int c = 0; c < colCount && c < row.Length; c++)
			{
				var cellBorder = new Border
				{
					BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
					BorderThickness = new Avalonia.Thickness(1),
					Background = isHeader
						? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE4, 0xE8, 0xEE))
						: (r % 2 == 1
							? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF8, 0xF9, 0xFA))
							: Brushes.White),
					Padding = new Avalonia.Thickness(8, 6),
					MinHeight = 28,
				};

				var cellText = new TextBlock
				{
					Text = row[c],
					FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
					Foreground = isHeader
						? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x15, 0x65, 0xC0))
						: Brushes.Black,
					VerticalAlignment = VerticalAlignment.Center,
				};
				cellBorder.Child = cellText;

				Grid.SetRow(cellBorder, r);
				Grid.SetColumn(cellBorder, c);
				tableGrid.Children.Add(cellBorder);
			}
		}

		var scroll = new ScrollViewer
		{
			Content = tableGrid,
			Background = CadTheme.DialogTableBg,
			HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
		};
		mainDock.Children.Add(scroll);

		this.Content = mainDock;
	}

	private async Task SaveCsvAsync()
	{
		var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "CSV 파일 저장",
			DefaultExtension = "csv",
			SuggestedFileName = "table_export.csv",
			FileTypeChoices = new[]
			{
				new FilePickerFileType("CSV 파일 (*.csv)") { Patterns = new[] { "*.csv" } },
			},
		});

		string? path = file?.TryGetLocalPath();
		if (path != null)
		{
			TableExtractor.SaveCsv(this._table, path);
		}
	}

	private async Task CopyClipboardAsync()
	{
		var sb = new StringBuilder();
		for (int r = 0; r < this._table.Rows.Length; r++)
		{
			var row = this._table.Rows[r];
			sb.AppendLine(string.Join("\t", row));
		}

		if (this.Clipboard != null)
		{
			await this.Clipboard.SetTextAsync(sb.ToString());
		}
	}
}
