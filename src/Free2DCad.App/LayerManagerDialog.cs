using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp.Tables;
using Free2DCad.Core;
using Color = ACadSharp.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 도면층 특성 관리자 대화상자.
/// 도면층 목록 확인, 새 도면층 추가, 삭제, 현재 지정, 이름·색상·켜기·동결·잠금 편집 및 Undo/Redo를 지원한다.
/// </summary>
public sealed class LayerManagerDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly StackPanel _rowsPanel = new();
	private Layer? _selectedLayer;
	private readonly TextBlock _statusText = new() { Foreground = Brushes.Gray, FontSize = 11, Margin = new Avalonia.Thickness(8, 4) };

	public LayerManagerDialog(EditorDocument doc)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));

		this.Title = "도면층 특성 관리자";
		this.Width = 760;
		this.Height = 480;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel();

		// 1. 상단 도구 모음
		var toolBar = this.BuildToolBar();
		DockPanel.SetDock(toolBar, Dock.Top);
		root.Children.Add(toolBar);

		// 2. 하단 상태 표시줄
		DockPanel.SetDock(this._statusText, Dock.Bottom);
		root.Children.Add(this._statusText);

		// 3. 중앙 목록 (헤더 + 스크롤)
		var centerDock = new DockPanel();
		var header = this.BuildHeaderRow();
		DockPanel.SetDock(header, Dock.Top);
		centerDock.Children.Add(header);

		var scroll = new ScrollViewer
		{
			Content = this._rowsPanel,
			Background = CadTheme.DialogTableBg,
			Margin = new Avalonia.Thickness(4, 0, 4, 4),
		};
		centerDock.Children.Add(scroll);

		root.Children.Add(centerDock);
		this.Content = root;

		this._doc.Changed += this.OnDocChanged;
		this.Closing += (_, _) => this._doc.Changed -= this.OnDocChanged;

		this.Refresh();
	}

	private Control BuildToolBar()
	{
		var bar = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Background = CadTheme.DialogBg,
			Height = 36,
			Spacing = 4,
			Margin = new Avalonia.Thickness(0, 0, 0, 1),
		};

		var btnNew = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Children =
				{
					IconHelper.Create(IconHelper.New, 14, new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2E, 0x7D, 0x32))),
					new TextBlock { Text = "새 도면층 (Alt+N)", FontSize = 11, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }
				}
			},
			Padding = new Avalonia.Thickness(8, 4),
			Background = Brushes.Transparent,
		};
		btnNew.Click += (_, _) => this.CreateNewLayer();
		bar.Children.Add(btnNew);

		var btnDelete = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Children =
				{
					IconHelper.Create(IconHelper.Erase, 14, Brushes.IndianRed),
					new TextBlock { Text = "도면층 삭제 (Del)", FontSize = 11, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }
				}
			},
			Padding = new Avalonia.Thickness(8, 4),
			Background = Brushes.Transparent,
		};
		btnDelete.Click += (_, _) => this.DeleteSelectedLayer();
		bar.Children.Add(btnDelete);

		var btnSetCurrent = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4,
				Children =
				{
					IconHelper.Create(IconHelper.Checkmark, 14, new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x19, 0x76, 0xD2))),
					new TextBlock { Text = "현재로 설정 (Alt+C)", FontSize = 11, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center }
				}
			},
			Padding = new Avalonia.Thickness(8, 4),
			Background = Brushes.Transparent,
		};
		btnSetCurrent.Click += (_, _) => this.SetSelectedAsCurrent();
		bar.Children.Add(btnSetCurrent);

		return bar;
	}

	private Control BuildHeaderRow()
	{
		var header = new Grid
		{
			Height = 26,
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE4, 0xE4, 0xE4)),
			ColumnDefinitions = new ColumnDefinitions("36, 180, 44, 44, 44, 100, 100, *"),
			Margin = new Avalonia.Thickness(4, 2, 4, 2),
		};

		header.Children.Add(MakeHeaderLabel("상태", 0));
		header.Children.Add(MakeHeaderLabel("이름", 1));
		header.Children.Add(MakeHeaderLabel("켜기", 2));
		header.Children.Add(MakeHeaderLabel("동결", 3));
		header.Children.Add(MakeHeaderLabel("잠금", 4));
		header.Children.Add(MakeHeaderLabel("색상", 5));
		header.Children.Add(MakeHeaderLabel("선종류", 6));
		header.Children.Add(MakeHeaderLabel("선가중치", 7));

		return header;

		static TextBlock MakeHeaderLabel(string text, int col)
		{
			var tb = new TextBlock
			{
				Text = text,
				FontSize = 11,
				FontWeight = FontWeight.SemiBold,
				Foreground = Brushes.Black,
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = col >= 2 && col <= 4 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
				Margin = new Avalonia.Thickness(4, 0),
			};
			Grid.SetColumn(tb, col);
			return tb;
		}
	}

	private void OnDocChanged(object? sender, EventArgs e)
	{
		this.Refresh();
	}

	public void Refresh()
	{
		this._rowsPanel.Children.Clear();
		var layers = this._doc.Document.Layers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

		this._statusText.Text = $"전체 도면층: {layers.Count}개 · 현재 도면층: {this._doc.CurrentLayer.Name}";

		foreach (var layer in layers)
		{
			bool isSelected = (this._selectedLayer == layer);
			bool isCurrent = (this._doc.CurrentLayer == layer);

			var rowBorder = new Border
			{
				Background = isSelected
					? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xCE, 0xE4, 0xFE))
					: isCurrent
						? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF0, 0xF6, 0xFF))
						: Brushes.White,
				BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xEA, 0xEA, 0xEA)),
				BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
				Height = 28,
				Padding = new Avalonia.Thickness(2, 0),
			};

			var grid = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("36, 180, 44, 44, 44, 100, 100, *"),
			};

			// 0. 상태 (현재 도면층 체크 표시)
			var statusBlock = new TextBlock
			{
				Text = isCurrent ? "✓" : "",
				Foreground = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x2E, 0x7D, 0x32)),
				FontSize = 12,
				FontWeight = FontWeight.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
			};
			Grid.SetColumn(statusBlock, 0);
			grid.Children.Add(statusBlock);

			// 1. 도면층 이름 (더블 클릭 또는 선택 후 Enter 시 이름 변경)
			var nameBox = new TextBox
			{
				Text = layer.Name,
				FontSize = 11,
				Foreground = Brushes.Black,
				Background = Brushes.Transparent,
				BorderThickness = new Avalonia.Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
				Padding = new Avalonia.Thickness(4, 2),
				IsReadOnly = (layer.Name == "0" || layer.Name == Layer.DefaultName),
			};
			nameBox.KeyDown += (_, ke) =>
			{
				if (ke.Key == Key.Enter)
				{
					string newName = (nameBox.Text ?? "").Trim();
					if (!string.IsNullOrEmpty(newName) && newName != layer.Name)
					{
						if (this._doc.Document.Layers.Contains(newName))
						{
							this._statusText.Text = $"오류: 도면층 \"{newName}\"이(가) 이미 존재합니다.";
						}
						else
						{
							this._doc.RenameLayer(layer, newName);
						}
					}
					ke.Handled = true;
				}
			};
			Grid.SetColumn(nameBox, 1);
			grid.Children.Add(nameBox);

			// 2. 켜기/끄기 (전구 💡)
			var btnOn = new Button
			{
				Content = IconHelper.Create(layer.IsOn ? IconHelper.BulbOn : IconHelper.BulbOff, 14, layer.IsOn ? Brushes.Gold : Brushes.Gray),
				Background = Brushes.Transparent,
				Padding = new Avalonia.Thickness(2),
				HorizontalAlignment = HorizontalAlignment.Center,
				[ToolTip.TipProperty] = layer.IsOn ? "켜기 (클릭하여 끄기)" : "끄기 (클릭하여 켜기)",
			};
			btnOn.Click += (_, _) => this._doc.SetLayerOn(layer, !layer.IsOn);
			Grid.SetColumn(btnOn, 2);
			grid.Children.Add(btnOn);

			// 3. 동결/해제 (❄️)
			bool isFrozen = layer.Flags.HasFlag(LayerFlags.Frozen);
			var btnFreeze = new Button
			{
				Content = IconHelper.Create(IconHelper.Snowflake, 14, isFrozen ? Brushes.LightSkyBlue : Brushes.Gray),
				Background = Brushes.Transparent,
				Padding = new Avalonia.Thickness(2),
				HorizontalAlignment = HorizontalAlignment.Center,
				[ToolTip.TipProperty] = isFrozen ? "동결됨 (클릭하여 동결 해제)" : "동결 해제됨 (클릭하여 동결)",
			};
			btnFreeze.Click += (_, _) => this._doc.SetLayerFrozen(layer, !isFrozen);
			Grid.SetColumn(btnFreeze, 3);
			grid.Children.Add(btnFreeze);

			// 4. 잠금/해제 (🔒)
			bool isLocked = layer.Flags.HasFlag(LayerFlags.Locked);
			var btnLock = new Button
			{
				Content = IconHelper.Create(isLocked ? IconHelper.Lock : IconHelper.Unlock, 14, isLocked ? Brushes.Orange : Brushes.Gray),
				Background = Brushes.Transparent,
				Padding = new Avalonia.Thickness(2),
				HorizontalAlignment = HorizontalAlignment.Center,
				[ToolTip.TipProperty] = isLocked ? "잠김 (클릭하여 잠금 해제)" : "잠금 해제됨 (클릭하여 잠금)",
			};
			btnLock.Click += (_, _) => this._doc.SetLayerLocked(layer, !isLocked);
			Grid.SetColumn(btnLock, 4);
			grid.Children.Add(btnLock);

			// 5. 색상 (견본 상자 + 이름/색인, 클릭 시 메뉴)
			var colorStack = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6,
				VerticalAlignment = VerticalAlignment.Center,
				Cursor = new Cursor(StandardCursorType.Hand),
			};
			var swatch = new Border
			{
				Width = 14,
				Height = 14,
				Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(layer.Color.R, layer.Color.G, layer.Color.B)),
				BorderBrush = Brushes.Gray,
				BorderThickness = new Avalonia.Thickness(1),
				CornerRadius = new Avalonia.CornerRadius(2),
			};
			var colorLabel = new TextBlock
			{
				Text = GetColorName(layer.Color),
				FontSize = 11,
				Foreground = Brushes.Black,
				VerticalAlignment = VerticalAlignment.Center,
			};
			colorStack.Children.Add(swatch);
			colorStack.Children.Add(colorLabel);

			colorStack.PointerPressed += (_, _) =>
			{
				var menu = new ContextMenu();
				short[] indices = { 1, 2, 3, 4, 5, 6, 7 };
				string[] names = { "1 빨강", "2 노랑", "3 초록", "4 하늘", "5 파랑", "6 자홍", "7 흰색" };
				for (int i = 0; i < indices.Length; i++)
				{
					short idx = indices[i];
					var mi = new MenuItem { Header = names[i] };
					mi.Click += (_, _) => this._doc.SetLayerColor(layer, new Color(idx));
					menu.Items.Add(mi);
				}
				var miMore = new MenuItem { Header = "색상 선택..." };
				miMore.Click += async (_, _) =>
				{
					var dlg = new ColorPickerDialog(layer.Color);
					var res = await dlg.ShowDialog<Color?>(this);
					if (res != null)
					{
						this._doc.SetLayerColor(layer, res.Value);
					}
				};
				menu.Items.Add(new Separator());
				menu.Items.Add(miMore);
				menu.Open(colorStack);
			};
			Grid.SetColumn(colorStack, 5);
			grid.Children.Add(colorStack);

			// 6. 선종류
			var lineTypeLabel = new TextBlock
			{
				Text = layer.LineType?.Name ?? "Continuous",
				FontSize = 11,
				Foreground = Brushes.Black,
				VerticalAlignment = VerticalAlignment.Center,
			};
			Grid.SetColumn(lineTypeLabel, 6);
			grid.Children.Add(lineTypeLabel);

			// 7. 선가중치
			var lineWeightLabel = new TextBlock
			{
				Text = "기본값",
				FontSize = 11,
				Foreground = Brushes.Black,
				VerticalAlignment = VerticalAlignment.Center,
			};
			Grid.SetColumn(lineWeightLabel, 7);
			grid.Children.Add(lineWeightLabel);

			rowBorder.Child = grid;

			// 마우스 선택 및 더블 클릭 (현재 도면층 설정)
			rowBorder.PointerPressed += (_, pe) =>
			{
				this._selectedLayer = layer;
				if (pe.ClickCount == 2)
				{
					this._doc.CurrentLayer = layer;
				}
				this.Refresh();
			};

			this._rowsPanel.Children.Add(rowBorder);
		}
	}

	private void CreateNewLayer()
	{
		int idx = 1;
		string name;
		do
		{
			name = $"도면층{idx++}";
		} while (this._doc.Document.Layers.Contains(name));

		var newLayer = this._doc.AddLayer(name, new Color(7));
		this._selectedLayer = newLayer;
		this.Refresh();
	}

	private void DeleteSelectedLayer()
	{
		if (this._selectedLayer == null)
		{
			this._statusText.Text = "삭제할 도면층을 먼저 선택하십시오.";
			return;
		}

		if (!this._doc.CanRemoveLayer(this._selectedLayer))
		{
			this._statusText.Text = $"도면층 \"{this._selectedLayer.Name}\"은(는) 삭제할 수 없습니다. (0 도면층, 현재 도면층, 객체가 포함된 도면층)";
			return;
		}

		string delName = this._selectedLayer.Name;
		this._doc.RemoveLayer(this._selectedLayer);
		this._selectedLayer = null;
		this._statusText.Text = $"도면층 \"{delName}\"이(가) 삭제되었습니다.";
		this.Refresh();
	}

	private void SetSelectedAsCurrent()
	{
		if (this._selectedLayer != null)
		{
			this._doc.CurrentLayer = this._selectedLayer;
			this._statusText.Text = $"현재 도면층 설정: {this._selectedLayer.Name}";
			this.Refresh();
		}
	}

	private static string GetColorName(Color c)
	{
		return c.Index switch
		{
			1 => "빨강 (1)",
			2 => "노랑 (2)",
			3 => "초록 (3)",
			4 => "하늘 (4)",
			5 => "파랑 (5)",
			6 => "자홍 (6)",
			7 => "흰색 (7)",
			_ => $"색상 {c.Index}"
		};
	}
}
