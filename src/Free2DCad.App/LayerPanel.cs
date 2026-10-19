using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp.Tables;
using Free2DCad.Core;

namespace Free2DCad.App;

/// <summary>
/// 도면의 레이어 목록을 표시하고 켜기/끄기 및 현재 레이어 지정을 지원하는 패널.
/// </summary>
public sealed class LayerPanel : Border
{
	private readonly StackPanel _layerList = new() { Spacing = 2 };
	private EditorDocument? _doc;

	public event Action? CloseRequested;

	public LayerPanel()
	{
		this.Width = 220;
		this.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x25, 0x25, 0x26));
		this.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3E, 0x3E, 0x42));
		this.BorderThickness = new Avalonia.Thickness(0, 0, 1, 0);

		var header = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto"),
			Margin = new Avalonia.Thickness(8, 8, 8, 8),
		};

		var title = new TextBlock
		{
			Text = "도면층 특성",
			FontWeight = FontWeight.Bold,
			Foreground = Brushes.White,
			VerticalAlignment = VerticalAlignment.Center,
		};
		header.Children.Add(title);

		var btnAdd = new Button
		{
			Content = "＋",
			Padding = new Avalonia.Thickness(6, 2),
			Background = Brushes.Transparent,
			Foreground = Brushes.LightGreen,
			[ToolTip.TipProperty] = "새 도면층 만들기",
		};
		btnAdd.Click += (_, _) =>
		{
			if (this._doc == null) return;
			int idx = 1;
			string name;
			do
			{
				name = $"Layer{idx++}";
			} while (this._doc.Document.Layers.Contains(name));

			this._doc.AddLayer(name, new ACadSharp.Color(7));
			this.Refresh();
		};
		Grid.SetColumn(btnAdd, 1);

		var closeBtn = new Button
		{
			Content = "◀",
			Padding = new Avalonia.Thickness(6, 2),
			Background = Brushes.Transparent,
			Foreground = Brushes.LightGray,
		};
		closeBtn.Click += (_, _) => this.CloseRequested?.Invoke();
		Grid.SetColumn(closeBtn, 2);

		header.ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
		header.Children.Add(btnAdd);
		header.Children.Add(closeBtn);

		var scroll = new ScrollViewer
		{
			Content = this._layerList,
			Margin = new Avalonia.Thickness(4, 0, 4, 4),
		};

		var dock = new DockPanel();
		DockPanel.SetDock(header, Dock.Top);
		dock.Children.Add(header);
		dock.Children.Add(scroll);

		this.Child = dock;
	}

	public void AttachDocument(EditorDocument? doc)
	{
		this._doc = doc;
		if (doc != null)
		{
			doc.Changed += (_, _) => this.Refresh();
		}
		this.Refresh();
	}

	public void Refresh()
	{
		this._layerList.Children.Clear();
		if (this._doc == null)
		{
			return;
		}

		var layers = this._doc.Document.Layers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
		foreach (var layer in layers)
		{
			var row = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
				Margin = new Avalonia.Thickness(4, 2),
			};

			// 1. 색 견본 (클릭 시 1~7번 색상 메뉴 표시)
			byte r = layer.Color.R, g = layer.Color.G, b = layer.Color.B;
			var swatch = new Border
			{
				Width = 14,
				Height = 14,
				CornerRadius = new Avalonia.CornerRadius(2),
				Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(r, g, b)),
				BorderBrush = Brushes.Gray,
				BorderThickness = new Avalonia.Thickness(1),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Avalonia.Thickness(2, 0, 6, 0),
				Cursor = new Cursor(StandardCursorType.Hand),
			};

			swatch.PointerPressed += (_, _) =>
			{
				if (this._doc == null) return;
				var menu = new ContextMenu();
				short[] colorIndices = { 1, 2, 3, 4, 5, 6, 7 };
				string[] colorNames = { "1 빨강", "2 노랑", "3 녹색", "4 하늘", "5 파랑", "6 선홍", "7 흰색" };
				for (int i = 0; i < colorIndices.Length; i++)
				{
					short idx = colorIndices[i];
					var mi = new MenuItem { Header = colorNames[i] };
					mi.Click += (_, _) =>
					{
						this._doc.SetLayerColor(layer, new ACadSharp.Color(idx));
					};
					menu.Items.Add(mi);
				}
				menu.Open(swatch);
			};
			row.Children.Add(swatch);

			// 2. 켜기/끄기 체크박스
			var chk = new CheckBox
			{
				IsChecked = layer.IsOn,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Avalonia.Thickness(0, 0, 4, 0),
			};
			bool handlingCheck = false;
			chk.IsCheckedChanged += (_, _) =>
			{
				if (handlingCheck || this._doc == null) return;
				handlingCheck = true;
				this._doc.SetLayerOn(layer, chk.IsChecked == true);
				handlingCheck = false;
			};
			Grid.SetColumn(chk, 1);
			row.Children.Add(chk);

			// 3. 레이어 이름
			bool isCurrent = ReferenceEquals(layer, this._doc.CurrentLayer);
			var nameText = new TextBlock
			{
				Text = layer.Name,
				FontWeight = isCurrent ? FontWeight.Bold : FontWeight.Normal,
				Foreground = isCurrent ? Brushes.Yellow : Brushes.White,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
			Grid.SetColumn(nameText, 2);
			row.Children.Add(nameText);

			// 행 ContextMenu: 현재 레이어로 지정, 이름 바꾸기
			var rowMenu = new ContextMenu();
			var miSetCurrent = new MenuItem { Header = "현재 도면층으로 설정" };
			miSetCurrent.Click += (_, _) =>
			{
				if (this._doc != null)
				{
					this._doc.CurrentLayer = layer;
					this.Refresh();
				}
			};
			rowMenu.Items.Add(miSetCurrent);

			if (layer.Name != Layer.DefaultName)
			{
				var miRename = new MenuItem { Header = "이름 바꾸기" };
				miRename.Click += (_, _) =>
				{
					var editBox = new TextBox
					{
						Text = layer.Name,
						FontSize = 11,
						Padding = new Avalonia.Thickness(2),
						VerticalAlignment = VerticalAlignment.Center,
					};
					editBox.KeyDown += (_, ke) =>
					{
						if (ke.Key == Key.Enter)
						{
							string newName = editBox.Text?.Trim() ?? string.Empty;
							if (!string.IsNullOrEmpty(newName) && newName != layer.Name && this._doc != null)
							{
								this._doc.RenameLayer(layer, newName);
							}
							this.Refresh();
						}
						else if (ke.Key == Key.Escape)
						{
							this.Refresh();
						}
					};
					Grid.SetColumn(editBox, 2);
					row.Children.Remove(nameText);
					row.Children.Add(editBox);
					editBox.Focus();
				};
				rowMenu.Items.Add(miRename);
			}
			row.ContextMenu = rowMenu;

			// 더블클릭 시 현재 레이어로 지정
			row.DoubleTapped += (_, _) =>
			{
				if (this._doc != null)
				{
					this._doc.CurrentLayer = layer;
					this.Refresh();
				}
			};

			this._layerList.Children.Add(row);
		}
	}
}
