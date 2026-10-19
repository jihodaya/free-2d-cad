using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Entities;
using Free2DCad.Core.Text;

namespace Free2DCad.App;

/// <summary>
/// FIND (찾기 및 바꾸기, 기존 CAD와 같은 구성): 찾을 내용·바꿀 내용·어디에서 찾기, 결과 목록(위치·객체 유형·텍스트),
/// 검색 옵션, 텍스트 유형, 바꾸기·모두 바꾸기·찾기·완료. 목록에서 고르면 그 문자로 확대.
/// </summary>
public sealed partial class MainWindow
{
	private static readonly List<string> FindHistory = new();
	private static readonly List<string> ReplaceHistory = new();
	private static FindOptions _findOptions = new();

	private async Task ShowFindDialogAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		var doc = this._doc;
		var preSelection = this._canvas.SelectedEntities.ToList();

		// 찾을 내용·바꿀 내용: 직접 입력 + 지난 기록 (기존 CAD처럼 편집 가능한 목록)
		var find = new ComboBox { ItemsSource = FindHistory.ToList(), IsEditable = true, Width = 262 };
		var repl = new ComboBox { ItemsSource = ReplaceHistory.ToList(), IsEditable = true, Width = 262 };
		var where = new ComboBox { Width = 120, ItemsSource = preSelection.Count > 0 ? new[] { "전체 도면", "현재 선택" } : new[] { "전체 도면" }, SelectedIndex = preSelection.Count > 0 ? 1 : 0 };

		// 결과 목록: 위치 · 객체 유형 · 텍스트
		var results = new System.Collections.ObjectModel.ObservableCollection<FindMatch>();
		Control Header() => new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("70,100,*"),
			Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
			Children =
			{
				new TextBlock { Text = "위치", FontSize = 12, Margin = new Thickness(4, 2) },
				new TextBlock { Text = "객체 유형", FontSize = 12, Margin = new Thickness(4, 2), [Grid.ColumnProperty] = 1 },
				new TextBlock { Text = "텍스트", FontSize = 12, Margin = new Thickness(4, 2), [Grid.ColumnProperty] = 2 },
			},
		};
		var list = new ListBox
		{
			ItemsSource = results,
			Height = 130,
			ItemTemplate = new FuncDataTemplate<FindMatch>((m, _) => m == null ? new TextBlock() : new Grid
			{
				ColumnDefinitions = new ColumnDefinitions("70,100,*"),
				Children =
				{
					new TextBlock { Text = m.Location, FontSize = 12 },
					new TextBlock { Text = m.Kind, FontSize = 12, [Grid.ColumnProperty] = 1 },
					new TextBlock { Text = m.Text.Replace('\n', ' '), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 2 },
				},
			}),
		};

		// 검색 옵션 / 텍스트 유형 (기존 CAD와 같은 항목, 외부 참조·블록 검색·하이퍼링크는 지원하지 않아 꺼 둠)
		var o = _findOptions;
		CheckBox Cb(string text, bool on, bool enabled = true) => new() { Content = text, IsChecked = on, IsEnabled = enabled, FontSize = 12, MinHeight = 22 };
		var cbCase = Cb("대소문자 구분", o.MatchCase);
		var cbWord = Cb("전체 단어만 찾기", o.WholeWord);
		var cbWild = Cb("와일드카드 사용", o.Wildcards);
		var cbXref = Cb("외부 참조 검색", false, false);
		var cbBlock = Cb("블록 검색", true, false);
		var cbHidden = Cb("숨겨진 항목 무시", o.IgnoreHidden);
		var cbAttr = Cb("블록 속성 값", o.Attributes);
		var cbDim = Cb("치수 또는 leader 텍스트", o.DimensionsAndLeaders);
		var cbText = Cb("단일 라인 또는 멀티 라인 텍스트", o.Texts);
		var cbTable = Cb("표 텍스트", o.Tables);
		var cbLink = Cb("하이퍼링크", true, false);
		var cbLinkDesc = Cb("하이퍼링크 설명", true, false);

		FindOptions Options() => _findOptions = new FindOptions(
			cbCase.IsChecked == true, cbWord.IsChecked == true, cbWild.IsChecked == true, cbHidden.IsChecked == true,
			cbAttr.IsChecked == true, cbDim.IsChecked == true, cbText.IsChecked == true, cbTable.IsChecked == true);

		var status = new TextBlock { FontSize = 12, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center };
		var btnReplace = new Button { Content = "바꾸기(_R)", Width = 84, IsEnabled = false };
		var btnAll = new Button { Content = "모두 바꾸기(_A)", Width = 100, IsEnabled = false };
		var btnFind = new Button { Content = "찾기(_F)", Width = 84, IsDefault = true };
		var btnDone = new Button { Content = "완료", Width = 84, IsCancel = true };

		void ZoomTo(FindMatch m)
		{
			var target = m.Entity is AttributeEntity a && a.Owner is Entity ins ? ins : m.Entity;
			if (this._canvas.EntityWorldBounds(target) is { } b)
			{
				double pad = Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY) * 1.5 + 1e-6;
				this.ViewController.ZoomWindow(b.MinX - pad, b.MinY - pad, b.MaxX + pad, b.MaxY + pad);
			}
			this._canvas.SelectedEntities.Clear();
			this._canvas.SelectedEntities.Add(target);
			this._canvas.InvalidateVisual();
		}

		string? lastPattern = null;
		void Search()
		{
			string pattern = find.Text ?? string.Empty;
			results.Clear();
			lastPattern = pattern;
			if (pattern.Length == 0)
			{
				status.Text = "찾을 내용을 입력하십시오.";
				return;
			}
			if (!FindHistory.Contains(pattern)) FindHistory.Insert(0, pattern);
			var scope = where.SelectedIndex == 1 ? preSelection : null;
			foreach (var m in TextSearch.Find(doc.Document, pattern, Options(), scope)) results.Add(m);
			status.Text = results.Count == 0 ? "찾는 문자가 없습니다." : $"{results.Count}개를 찾았습니다.";
			btnReplace.IsEnabled = btnAll.IsEnabled = results.Count > 0;
			if (results.Count > 0) list.SelectedIndex = 0;
		}

		list.SelectionChanged += (_, _) =>
		{
			if (list.SelectedItem is FindMatch m) ZoomTo(m);
		};
		btnFind.Click += (_, _) =>
		{
			// 같은 내용으로 다시 누르면 다음 찾기 (기존 CAD와 같음)
			if (lastPattern == (find.Text ?? string.Empty) && results.Count > 0)
			{
				list.SelectedIndex = (list.SelectedIndex + 1) % results.Count;
				return;
			}
			Search();
		};
		btnReplace.Click += (_, _) =>
		{
			if (list.SelectedItem is not FindMatch m) return;
			string r = repl.Text ?? string.Empty;
			if (r.Length > 0 && !ReplaceHistory.Contains(r)) ReplaceHistory.Insert(0, r);
			int idx = list.SelectedIndex;
			if (TextSearch.Replace(doc, m, lastPattern ?? string.Empty, r, Options()))
			{
				results.RemoveAt(idx);
				status.Text = "1개를 바꿨습니다.";
				if (results.Count > 0) list.SelectedIndex = Math.Min(idx, results.Count - 1);
				btnReplace.IsEnabled = btnAll.IsEnabled = results.Count > 0;
			}
		};
		btnAll.Click += (_, _) =>
		{
			string r = repl.Text ?? string.Empty;
			if (r.Length > 0 && !ReplaceHistory.Contains(r)) ReplaceHistory.Insert(0, r);
			int n = TextSearch.ReplaceAll(doc, results.ToList(), lastPattern ?? string.Empty, r, Options());
			results.Clear();
			status.Text = $"{n}개를 바꿨습니다.";
			btnReplace.IsEnabled = btnAll.IsEnabled = false;
		};

		var btnZoom = new Button { Content = "🔍", Width = 30, Padding = new Thickness(0), [ToolTip.TipProperty] = "선택한 문자로 확대" };
		btnZoom.Click += (_, _) =>
		{
			if (list.SelectedItem is FindMatch m) ZoomTo(m);
		};
		var btnSelectAll = new Button { Content = "▣", Width = 30, Padding = new Thickness(0), [ToolTip.TipProperty] = "찾은 객체로 선택 세트 만들기" };
		var dlg = new Window
		{
			Title = "찾기 및 바꾸기",
			Width = 430,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		btnSelectAll.Click += (_, _) =>
		{
			this._canvas.SelectedEntities.Clear();
			foreach (var m in results)
			{
				this._canvas.SelectedEntities.Add(m.Entity is AttributeEntity a && a.Owner is Entity ins ? ins : m.Entity);
			}
			this._canvas.InvalidateVisual();
			this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
			dlg.Close();
		};
		btnDone.Click += (_, _) => dlg.Close();

		Control Group(string title, params Control[] items)
		{
			var sp = new StackPanel { Spacing = 0, Margin = new Thickness(8, 4) };
			foreach (var c in items) sp.Children.Add(c);
			return new Border
			{
				BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
				BorderThickness = new Thickness(1),
				Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(6, 4, 6, 0) }, sp } },
			};
		}

		var topGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
		topGrid.Children.Add(new TextBlock { Text = "찾을 내용:", FontSize = 12 });
		topGrid.Children.Add(new TextBlock { Text = "어디에서 찾기:", FontSize = 12, [Grid.ColumnProperty] = 1, Margin = new Thickness(10, 0, 0, 0) });
		find[Grid.RowProperty] = 1;
		topGrid.Children.Add(find);
		where[Grid.RowProperty] = 1;
		where[Grid.ColumnProperty] = 1;
		where.Margin = new Thickness(10, 0, 0, 0);
		topGrid.Children.Add(where);
		topGrid.Children.Add(new TextBlock { Text = "바꿀 내용:", FontSize = 12, Margin = new Thickness(0, 6, 0, 0), [Grid.RowProperty] = 2 });
		repl[Grid.RowProperty] = 3;
		topGrid.Children.Add(repl);

		var listRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		listRow.Children.Add(new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
			BorderThickness = new Thickness(1),
			Child = new StackPanel { Children = { Header(), list } },
		});
		listRow.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(6, 0, 0, 0), [Grid.ColumnProperty] = 1, Children = { btnZoom, btnSelectAll } });

		var groups = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
		groups.Children.Add(Group("검색 옵션", cbCase, cbWord, cbWild, cbXref, cbBlock, cbHidden));
		var g2 = Group("텍스트 유형", cbAttr, cbDim, cbText, cbTable, cbLink, cbLinkDesc);
		g2[Grid.ColumnProperty] = 2;
		groups.Children.Add(g2);

		dlg.Opened += (_, _) => find.Focus();
		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				topGrid,
				listRow,
				groups,
				status,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { btnReplace, btnAll, btnFind, btnDone } },
			},
		};
		await dlg.ShowDialog(this);
		this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
	}
}
