using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Styles;
using AvColor = Avalonia.Media.Color;
using Point = Avalonia.Point;

namespace Free2DCad.App;

/// <summary>
/// 선종류 외형을 Avalonia 그리기(Line/Dash)로 렌더링하는 컨트롤.
/// </summary>
public sealed class LinetypeSampleCanvas : Control
{
	private readonly IEnumerable<double>? _pattern;

	public LinetypeSampleCanvas(IEnumerable<double>? pattern)
	{
		this._pattern = pattern;
		this.Width = 120;
		this.Height = 16;
		this.VerticalAlignment = VerticalAlignment.Center;
	}

	public override void Render(DrawingContext dc)
	{
		base.Render(dc);

		double w = this.Bounds.Width;
		double h = this.Bounds.Height;
		double cy = h / 2.0;

		var pen = new Pen(Brushes.Black, 1.2);

		if (this._pattern == null || !this._pattern.Any())
		{
			// 실선 (Continuous / ByLayer / ByBlock)
			dc.DrawLine(pen, new Point(0, cy), new Point(w, cy));
			return;
		}

		var segments = this._pattern.ToList();
		double sum = segments.Sum(x => Math.Abs(x));
		if (sum <= 0) sum = 10;

		// 120px 너비에 약 2~3회 패턴이 반복되도록 축척 계산
		double scale = Math.Clamp(w / (sum * 2.5), 0.5, 3.0);

		double curX = 0;
		while (curX < w)
		{
			foreach (double seg in segments)
			{
				double len = Math.Abs(seg) * scale;
				if (len < 1.0) len = 1.0;

				if (seg > 0)
				{
					// 대시 선
					double nextX = Math.Min(curX + len, w);
					dc.DrawLine(pen, new Point(curX, cy), new Point(nextX, cy));
					curX = nextX;
				}
				else if (seg < 0)
				{
					// 공백
					curX += len;
				}
				else
				{
					// 점 (dot)
					dc.DrawEllipse(Brushes.Black, null, new Point(curX, cy), 1.0, 1.0);
					curX += Math.Max(2.0, 2.0 * scale);
				}

				if (curX >= w) break;
			}
		}
	}
}

/// <summary>
/// 선종류 관리자 목록 항목 모델.
/// </summary>
public sealed class LinetypeRowItem
{
	public string Name { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
	public Func<Control> Appearance { get; set; } = () => new Control();
	public LineType? LineType { get; set; }
}

/// <summary>
/// 기존 CAD 스타일 선종류 관리자 대화상자 (LINETYPE / LT / LTYPE).
/// </summary>
public sealed class LinetypeManagerDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly ListBox _lstItems = new();
	private readonly TextBox _txtGlobalScale = new();
	private readonly TextBox _txtCurrentScale = new();
	private LinetypeRowItem? _selectedItem;

	public LinetypeManagerDialog(EditorDocument doc)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));

		this.Title = "선종류 관리자";
		this.Width = 600;
		this.Height = 440;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(12) };

		// 하단 버튼 모음
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 하단 축척 입력 영역
		var scalePanel = this.BuildScalePanel();
		DockPanel.SetDock(scalePanel, Dock.Bottom);
		root.Children.Add(scalePanel);

		// 중앙 선종류 목록 테이블 (3열: 선종류 이름, 외형, 선종류 설명)
		var tableGrid = new Grid
		{
			RowDefinitions = new RowDefinitions("24,*"),
		};

		// 헤더 줄
		var headerBorder = new Border
		{
			Background = new SolidColorBrush(AvColor.FromRgb(0xEE, 0xEE, 0xEE)),
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1, 1, 1, 0),
			Height = 24,
		};
		var headerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("140,140,*"),
			Margin = new Thickness(8, 0, 8, 0),
		};
		headerGrid.Children.Add(new TextBlock { Text = "선종류 이름", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
		var h2 = new TextBlock { Text = "외형", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(h2, 1);
		headerGrid.Children.Add(h2);
		var h3 = new TextBlock { Text = "선종류 설명", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(h3, 2);
		headerGrid.Children.Add(h3);
		headerBorder.Child = headerGrid;
		Grid.SetRow(headerBorder, 0);
		tableGrid.Children.Add(headerBorder);

		// 목록
		this._lstItems.Background = Brushes.White;
		this._lstItems.BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0));
		this._lstItems.BorderThickness = new Thickness(1);
		this._lstItems.SelectionChanged += (_, _) =>
		{
			this._selectedItem = this._lstItems.SelectedItem as LinetypeRowItem;
		};

		this._lstItems.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LinetypeRowItem>((item, _) =>
		{
			if (item == null) return new TextBlock();
			var row = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("140,140,*"),
				Height = 26,
				Margin = new Thickness(4, 0),
			};
			var t1 = new TextBlock { Text = item.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
			Grid.SetColumn(t1, 0);
			row.Children.Add(t1);

			// 목록이 길면 가상화로 행을 다시 만들므로 견본은 매번 새로 만든다 (이미 부모가 있는 컨트롤 재사용 금지)
			var appearance = item.Appearance();
			Grid.SetColumn(appearance, 1);
			row.Children.Add(appearance);

			var t3 = new TextBlock { Text = item.Description, FontSize = 11, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
			Grid.SetColumn(t3, 2);
			row.Children.Add(t3);

			return row;
		});

		Grid.SetRow(this._lstItems, 1);
		tableGrid.Children.Add(this._lstItems);

		root.Children.Add(tableGrid);
		this.Content = root;

		this.RefreshList();
	}

	private Control BuildScalePanel()
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,80,24,Auto,80"),
			Height = 32,
			Margin = new Thickness(0, 8, 0, 8),
			VerticalAlignment = VerticalAlignment.Center,
		};

		var lbl1 = new TextBlock { Text = "전역 축척 비율:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
		Grid.SetColumn(lbl1, 0);
		grid.Children.Add(lbl1);

		this._txtGlobalScale.Text = this._doc.Document.Header.LineTypeScale.ToString("F4", CultureInfo.InvariantCulture);
		this._txtGlobalScale.Height = 24;
		this._txtGlobalScale.MinHeight = 0;
		this._txtGlobalScale.FontSize = 12;
		this._txtGlobalScale.VerticalContentAlignment = VerticalAlignment.Center;
		this._txtGlobalScale.Padding = new Thickness(4, 0);
		Grid.SetColumn(this._txtGlobalScale, 1);
		grid.Children.Add(this._txtGlobalScale);

		var lbl2 = new TextBlock { Text = "현재 객체 축척:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
		Grid.SetColumn(lbl2, 3);
		grid.Children.Add(lbl2);

		this._txtCurrentScale.Text = this._doc.Document.Header.CurrentEntityLinetypeScale.ToString("F4", CultureInfo.InvariantCulture);
		this._txtCurrentScale.Height = 24;
		this._txtCurrentScale.MinHeight = 0;
		this._txtCurrentScale.FontSize = 12;
		this._txtCurrentScale.VerticalContentAlignment = VerticalAlignment.Center;
		this._txtCurrentScale.Padding = new Thickness(4, 0);
		Grid.SetColumn(this._txtCurrentScale, 4);
		grid.Children.Add(this._txtCurrentScale);

		return grid;
	}

	private Control BuildBottomBar()
	{
		var grid = new Grid
		{
			Height = 32,
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,6,Auto,6,Auto,6,Auto,6,Auto"),
		};

		var btnHelp = new Button
		{
			Content = "?",
			Width = 26,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(btnHelp, 0);
		grid.Children.Add(btnHelp);

		Button MakeBtn(string text, Action onClick, int col)
		{
			var btn = new Button
			{
				Content = text,
				Width = 80,
				Height = 26,
				MinHeight = 0,
				FontSize = 12,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
			};
			btn.Click += (_, _) => onClick();
			Grid.SetColumn(btn, col);
			grid.Children.Add(btn);
			return btn;
		}

		MakeBtn("불러오기...", async () => await this.OpenLoadDialogAsync(), 2);
		MakeBtn("삭제", () => this.DeleteSelectedLinetype(), 4);
		MakeBtn("현재", () => this.SetCurrentLinetype(), 6);
		MakeBtn("확인", () => this.ApplyAndClose(), 8);
		MakeBtn("취소", () => this.Close(), 10);

		return grid;
	}

	private void RefreshList()
	{
		var cad = this._doc.Document;
		var items = new List<LinetypeRowItem>();

		// 1. ByLayer, ByBlock, Continuous 우선
		var ordered = cad.LineTypes.OrderBy(lt =>
		{
			if (string.Equals(lt.Name, "ByLayer", StringComparison.OrdinalIgnoreCase)) return 0;
			if (string.Equals(lt.Name, "ByBlock", StringComparison.OrdinalIgnoreCase)) return 1;
			if (string.Equals(lt.Name, "Continuous", StringComparison.OrdinalIgnoreCase)) return 2;
			return 3;
		}).ThenBy(lt => lt.Name).ToList();

		foreach (var lt in ordered)
		{
			var pattern = lt.Segments.Select(s => s.Length).ToList();
			items.Add(new LinetypeRowItem
			{
				Name = lt.Name,
				Description = lt.Description ?? string.Empty,
				Appearance = () => new LinetypeSampleCanvas(pattern.Count > 0 ? pattern : null),
				LineType = lt,
			});
		}

		this._lstItems.ItemsSource = items;

		string curLt = cad.Header.CurrentLineTypeName ?? "ByLayer";
		this._lstItems.SelectedItem = items.FirstOrDefault(i => string.Equals(i.Name, curLt, StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
	}

	private async Task OpenLoadDialogAsync()
	{
		var dlg = new LinetypeLoadDialog(this._doc);
		bool? res = await dlg.ShowDialog<bool?>(this);
		if (res == true)
		{
			this.RefreshList();
		}
	}

	private void DeleteSelectedLinetype()
	{
		if (this._selectedItem?.LineType == null) return;

		string name = this._selectedItem.Name;
		if (string.Equals(name, "ByLayer", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase))
		{
			_ = ShowMessageAsync(this, "삭제 불가", "시스템 선종류(ByLayer, ByBlock, Continuous)는 삭제할 수 없습니다.");
			return;
		}

		if (string.Equals(this._doc.Document.Header.CurrentLineTypeName, name, StringComparison.OrdinalIgnoreCase))
		{
			_ = ShowMessageAsync(this, "삭제 불가", "현재 사용 중인 선종류는 삭제할 수 없습니다.");
			return;
		}

		bool inLayers = this._doc.Document.Layers.Any(l => string.Equals(l.LineType?.Name, name, StringComparison.OrdinalIgnoreCase));
		if (inLayers)
		{
			_ = ShowMessageAsync(this, "삭제 불가", $"선종류 \"{name}\"은(는) 도면층에서 사용 중이므로 삭제할 수 없습니다.");
			return;
		}

		bool inEntities = this._doc.Document.Entities.Any(e => string.Equals(e.LineType?.Name, name, StringComparison.OrdinalIgnoreCase));
		if (inEntities)
		{
			_ = ShowMessageAsync(this, "삭제 불가", $"선종류 \"{name}\"은(는) 도면 객체에서 사용 중이므로 삭제할 수 없습니다.");
			return;
		}

		bool inDimStyles = this._doc.Document.DimensionStyles.Any(ds =>
			string.Equals(ds.LineType?.Name, name, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(ds.LineTypeExt1?.Name, name, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(ds.LineTypeExt2?.Name, name, StringComparison.OrdinalIgnoreCase));
		if (inDimStyles)
		{
			_ = ShowMessageAsync(this, "삭제 불가", $"선종류 \"{name}\"은(는) 치수 스타일에서 사용 중이므로 삭제할 수 없습니다.");
			return;
		}

		var target = this._selectedItem.LineType;
		this._doc.Execute(new DelegateAction(
			$"선종류 삭제: {name}",
			() =>
			{
				this._doc.Document.LineTypes.Remove(name);
				this.RefreshList();
			},
			() =>
			{
				this._doc.Document.LineTypes.Add(target);
				this.RefreshList();
			}
		));
	}

	private void SetCurrentLinetype()
	{
		if (this._selectedItem == null) return;
		string prev = this._doc.Document.Header.CurrentLineTypeName ?? "ByLayer";
		string next = this._selectedItem.Name;
		this._doc.Execute(new DelegateAction(
			$"현재 선종류 설정: {next}",
			() => this._doc.Document.Header.CurrentLineTypeName = next,
			() => this._doc.Document.Header.CurrentLineTypeName = prev
		));
	}

	private void ApplyAndClose()
	{
		double oldLtScale = this._doc.Document.Header.LineTypeScale;
		double oldCeLtScale = this._doc.Document.Header.CurrentEntityLinetypeScale;

		double newLtScale = double.TryParse(this._txtGlobalScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double s1) && s1 > 0 ? s1 : oldLtScale;
		double newCeLtScale = double.TryParse(this._txtCurrentScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double s2) && s2 > 0 ? s2 : oldCeLtScale;

		if (Math.Abs(oldLtScale - newLtScale) > 0.0001 || Math.Abs(oldCeLtScale - newCeLtScale) > 0.0001)
		{
			this._doc.Execute(new DelegateAction(
				"선종류 축척 변경",
				() =>
				{
					this._doc.Document.Header.LineTypeScale = newLtScale;
					this._doc.Document.Header.CurrentEntityLinetypeScale = newCeLtScale;
				},
				() =>
				{
					this._doc.Document.Header.LineTypeScale = oldLtScale;
					this._doc.Document.Header.CurrentEntityLinetypeScale = oldCeLtScale;
				}
			));
		}

		this.Close(true);
	}

	private static async Task ShowMessageAsync(Window owner, string title, string message)
	{
		var win = new Window
		{
			Title = title,
			Width = 340,
			Height = 130,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var sp = new StackPanel { Margin = new Thickness(14), Spacing = 12 };
		sp.Children.Add(new TextBlock { Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap });
		var btnOk = new Button { Content = "확인", Width = 64, Height = 24, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
		btnOk.Click += (_, _) => win.Close();
		sp.Children.Add(btnOk);
		win.Content = sp;
		await win.ShowDialog(owner);
	}
}

/// <summary>
/// 선종류 불러오기 대화상자 (LinetypeLibrary.All 목록 표시 및 다중 선택).
/// </summary>
public sealed class LinetypeLoadDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly List<LinetypeLoadItem> _items = new();
	private readonly ListBox _lstBox = new();

	public sealed class LinetypeLoadItem
	{
		public bool IsSelected { get; set; }
		public string Name { get; set; } = string.Empty;
		public string Description { get; set; } = string.Empty;
		public Func<Control> Appearance { get; set; } = () => new Control();
	}

	public LinetypeLoadDialog(EditorDocument doc)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));

		this.Title = "선종류 불러오기 또는 다시 로드";
		this.Width = 520;
		this.Height = 420;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(12) };

		// 하단 버튼 모음
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		// 안내 문구
		var lblGuide = new TextBlock
		{
			Text = "불러올 선종류를 선택하십시오:",
			FontSize = 12,
			Margin = new Thickness(0, 0, 0, 6),
		};
		DockPanel.SetDock(lblGuide, Dock.Top);
		root.Children.Add(lblGuide);

		// 목록 헤더
		var headerBorder = new Border
		{
			Background = new SolidColorBrush(AvColor.FromRgb(0xEE, 0xEE, 0xEE)),
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1, 1, 1, 0),
			Height = 24,
		};
		var headerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("150,130,*"),
			Margin = new Thickness(8, 0, 8, 0),
		};
		headerGrid.Children.Add(new TextBlock { Text = "선종류", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
		var h2 = new TextBlock { Text = "외형", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(h2, 1);
		headerGrid.Children.Add(h2);
		var h3 = new TextBlock { Text = "설명", FontSize = 12, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetColumn(h3, 2);
		headerGrid.Children.Add(h3);
		headerBorder.Child = headerGrid;
		DockPanel.SetDock(headerBorder, Dock.Top);
		root.Children.Add(headerBorder);

		// 목록 박스
		this._lstBox.Background = Brushes.White;
		this._lstBox.BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0));
		this._lstBox.BorderThickness = new Thickness(1);

		this._lstBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LinetypeLoadItem>((item, _) =>
		{
			if (item == null) return new TextBlock();
			var row = new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("150,130,*"),
				Height = 26,
				Margin = new Thickness(4, 0),
			};

			var spName = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
			var chk = new CheckBox
			{
				IsChecked = item.IsSelected,
				VerticalAlignment = VerticalAlignment.Center,
			};
			chk.IsCheckedChanged += (_, _) => item.IsSelected = chk.IsChecked == true;
			spName.Children.Add(chk);
			spName.Children.Add(new TextBlock { Text = item.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
			Grid.SetColumn(spName, 0);
			row.Children.Add(spName);

			// 목록이 길면 가상화로 행을 다시 만들므로 견본은 매번 새로 만든다 (이미 부모가 있는 컨트롤 재사용 금지)
			var appearance = item.Appearance();
			Grid.SetColumn(appearance, 1);
			row.Children.Add(appearance);

			var t3 = new TextBlock { Text = item.Description, FontSize = 11, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
			Grid.SetColumn(t3, 2);
			row.Children.Add(t3);

			return row;
		});

		root.Children.Add(this._lstBox);
		this.Content = root;

		this.PopulateLibraryItems();
	}

	private Control BuildBottomBar()
	{
		var grid = new Grid
		{
			Height = 32,
			ColumnDefinitions = new ColumnDefinitions("Auto,6,Auto,*,Auto,6,Auto"),
			Margin = new Thickness(0, 8, 0, 0),
		};

		var btnSelectAll = new Button
		{
			Content = "모두 선택",
			Width = 80,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnSelectAll.Click += (_, _) =>
		{
			foreach (var it in this._items) it.IsSelected = true;
			this._lstBox.ItemsSource = null;
			this._lstBox.ItemsSource = this._items;
		};
		Grid.SetColumn(btnSelectAll, 0);
		grid.Children.Add(btnSelectAll);

		var btnClearAll = new Button
		{
			Content = "모두 지우기",
			Width = 80,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnClearAll.Click += (_, _) =>
		{
			foreach (var it in this._items) it.IsSelected = false;
			this._lstBox.ItemsSource = null;
			this._lstBox.ItemsSource = this._items;
		};
		Grid.SetColumn(btnClearAll, 2);
		grid.Children.Add(btnClearAll);

		var btnOk = new Button
		{
			Content = "확인",
			Width = 74,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnOk.Click += (_, _) => this.LoadSelectedAndClose();
		Grid.SetColumn(btnOk, 4);
		grid.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소",
			Width = 74,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close(false);
		Grid.SetColumn(btnCancel, 6);
		grid.Children.Add(btnCancel);

		return grid;
	}

	private void PopulateLibraryItems()
	{
		this._items.Clear();
		foreach (var def in LinetypeLibrary.All)
		{
			this._items.Add(new LinetypeLoadItem
			{
				IsSelected = false,
				Name = def.Name,
				Description = def.Description,
				Appearance = () => new LinetypeSampleCanvas(def.Pattern),
			});
		}
		this._lstBox.ItemsSource = this._items;
	}

	private void LoadSelectedAndClose()
	{
		var selectedNames = this._items.Where(x => x.IsSelected).Select(x => x.Name).ToList();
		if (selectedNames.Count > 0)
		{
			LinetypeLibrary.Load(this._doc, selectedNames);
		}
		this.Close(true);
	}
}
