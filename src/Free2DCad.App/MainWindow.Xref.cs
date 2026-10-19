using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using ACadSharp.Blocks;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Blocks;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// XREF (Xref 관리자) · XATTACH (외부 참조 부착): 기존 CAD와 같은 구성.
/// 관리자: 참조 이름·상태·참조 수·크기·형식·날짜·저장된 경로 + 부착/분리/다시 로드/언로드/결합/열기 + 외부 참조 경로 찾아보기.
/// 부착: 파일 선택 → 이름·참조 유형(부착/중첩)·경로 유지·삽입점/축척/회전(화면에서 지정).
/// </summary>
public sealed partial class MainWindow
{
	private static readonly FilePickerFileType DwgOnly = new("도면 (*.dwg)") { Patterns = new[] { "*.dwg" } };

	private sealed record XrefRow(BlockRecord Block, string Name, string Status, int Refs, string Size, string Kind, string Date, string Path);

	private async Task ShowXrefManagerAsync()
	{
		if (this._doc == null) return;
		var doc = this._doc;
		var rows = new System.Collections.ObjectModel.ObservableCollection<XrefRow>();
		const string cols = "110,80,60,60,46,70,260";

		void Fill()
		{
			rows.Clear();
			foreach (var b in Xrefs.List(doc.Document))
			{
				string? full = XrefLoader.Resolve(doc.Document, b);
				string size = string.Empty, date = string.Empty;
				if (full != null)
				{
					var fi = new FileInfo(full);
					size = $"{fi.Length / 1024.0:0.#} Kb";
					date = fi.LastWriteTime.ToString("MM/dd/yy", System.Globalization.CultureInfo.InvariantCulture); // 기존 CAD와 같은 10/02/26
				}
				rows.Add(new XrefRow(b, b.Name, XrefLoader.StatusOf(doc.Document, b), Xrefs.RefCount(doc.Document, b), size,
					b.Flags.HasFlag(BlockTypeFlags.XRefOverlay) ? "중첩" : "부착", date, b.BlockEntity.XRefPath ?? string.Empty));
			}
		}

		Grid Cells(params string[] t)
		{
			var g = new Grid { ColumnDefinitions = new ColumnDefinitions(cols) };
			for (int i = 0; i < t.Length; i++)
			{
				var tb = new TextBlock { Text = t[i], FontSize = 12, Margin = new Thickness(3, 1), TextTrimming = TextTrimming.CharacterEllipsis };
				Grid.SetColumn(tb, i);
				g.Children.Add(tb);
			}
			return g;
		}
		var header = Cells("참조 이름", "상태", "Refere...", "크기", "형식", "날짜", "저장된 경로");
		header.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
		var list = new ListBox
		{
			ItemsSource = rows,
			Height = 110,
			[ScrollViewer.HorizontalScrollBarVisibilityProperty] = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			ItemTemplate = new FuncDataTemplate<XrefRow>((r, _) => r == null ? new TextBlock() : Cells(r.Name, r.Status, r.Refs.ToString(), r.Size, r.Kind, r.Date, r.Path)),
		};
		var pathBox = new TextBox { IsReadOnly = true, FontSize = 12 };
		// 외부참조 검색 추가 경로 (기존 CAD와 같음): 고치거나 찾아보기로 폴더 선택, ";"로 여러 폴더. 바꾸면 다시 찾아 그린다
		var searchBox = new TextBox { Text = XrefLoader.SearchPaths, FontSize = 12 };
		var bSearch = new Button { Content = "찾아보기(_W)...", Width = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
		void ApplySearch()
		{
			string v = (searchBox.Text ?? string.Empty).Trim();
			if (v == XrefLoader.SearchPaths) return;
			XrefLoader.SearchPaths = v.Length == 0 ? AppContext.BaseDirectory : v;
			CadAppSettings.FromCanvas(this._canvas).Save();
			XrefLoader.Invalidate();
			foreach (var br in doc.Document.BlockRecords.Where(XrefLoader.IsXref).ToList())
			{
				try { Xrefs.Reload(doc, br); } catch (Exception) { }
			}
		}
		searchBox.LostFocus += (_, _) => ApplySearch();
		bSearch.Click += async (_, _) =>
		{
			var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "외부참조 검색 추가 경로", AllowMultiple = false });
			if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder)
			{
				searchBox.Text = folder;
				ApplySearch();
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};

		Button Btn(string t) => new() { Content = t, Width = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
		var bAttach = Btn("부착(_T)");
		var bDetach = Btn("분리(_D)");
		var bReload = Btn("다시 로드(_R)");
		var bUnload = Btn("언로드(_U)");
		var bBind = Btn("결합(_B)");
		var bOpen = Btn("열기(_N)");
		var bBrowse = new Button { Content = "찾아보기(_O)...", Width = 110 };
		var bClose = new Button { Content = "Close", Width = 96, IsCancel = true };

		var dlg = new Window
		{
			Title = "Xref 관리자",
			Width = 840,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};

		void UpdateButtons()
		{
			bool sel = list.SelectedItem is XrefRow;
			bDetach.IsEnabled = bReload.IsEnabled = bUnload.IsEnabled = bBrowse.IsEnabled = bOpen.IsEnabled = sel;
			bBind.IsEnabled = sel && list.SelectedItem is XrefRow { Status: "로드됨" };
			pathBox.Text = (list.SelectedItem as XrefRow)?.Path ?? string.Empty;
		}

		void Changed()
		{
			var keep = (list.SelectedItem as XrefRow)?.Block;
			Fill();
			list.SelectedItem = rows.FirstOrDefault(r => ReferenceEquals(r.Block, keep));
			UpdateButtons();
			this.RefreshCurrentSpaceView(keepView: true);
		}

		list.SelectionChanged += (_, _) => UpdateButtons();
		bAttach.Click += async (_, _) =>
		{
			dlg.Close();
			await this.XAttachAsync();
		};
		bDetach.Click += (_, _) =>
		{
			if (list.SelectedItem is XrefRow r)
			{
				Xrefs.Detach(doc, r.Block);
				Changed();
			}
		};
		bReload.Click += (_, _) =>
		{
			if (list.SelectedItem is XrefRow r)
			{
				Xrefs.Reload(doc, r.Block);
				Changed();
			}
		};
		bUnload.Click += (_, _) =>
		{
			if (list.SelectedItem is XrefRow r)
			{
				Xrefs.SetUnloaded(doc, r.Block, true);
				Changed();
			}
		};
		bBind.Click += (_, _) =>
		{
			if (list.SelectedItem is XrefRow r)
			{
				if (!Xrefs.Bind(doc, r.Block)) this._cmdLine.Print("참조 파일을 읽을 수 없어 결합하지 못했습니다.");
				Changed();
			}
		};
		bOpen.Click += async (_, _) =>
		{
			if (list.SelectedItem is XrefRow r && XrefLoader.Resolve(doc.Document, r.Block) is { } full)
			{
				dlg.Close();
				await this.LoadAsync(full); // 새 탭으로 열기
			}
		};
		bBrowse.Click += async (_, _) =>
		{
			if (list.SelectedItem is not XrefRow r) return;
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "외부 참조 경로", AllowMultiple = false, FileTypeFilter = new[] { DwgOnly } });
			if (files.FirstOrDefault()?.TryGetLocalPath() is { } p)
			{
				Xrefs.SetPath(doc, r.Block, p, Path.IsPathRooted(r.Path) ? Xrefs.PathMode.Full : Xrefs.PathMode.Relative);
				Xrefs.Reload(doc, r.Block);
				Changed();
			}
		};
		bClose.Click += (_, _) => { ApplySearch(); dlg.Close(); };

		Control Group(string title, Control content) => new StackPanel { Spacing = 3, Children = { new TextBlock { Text = title, FontSize = 12 }, content } };

		Control SearchRow()
		{
			var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
			g.Children.Add(searchBox);
			Grid.SetColumn(bSearch, 2);
			g.Children.Add(bSearch);
			return g;
		}

		var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
		top.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = new StackPanel { Children = { header, list } } });
		var btnCol = new StackPanel { Spacing = 5, Children = { bAttach, bDetach, bReload, bUnload, bBind, bOpen } };
		Grid.SetColumn(btnCol, 2);
		top.Children.Add(btnCol);

		var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
		pathRow.Children.Add(pathBox);
		Grid.SetColumn(bBrowse, 2);
		pathRow.Children.Add(bBrowse);

		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 10,
			Children =
			{
				top,
				Group("외부 참조 경로", pathRow),
				Group("외부참조 검색 추가 경로", SearchRow()),
				new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { bClose } },
			},
		};
		Fill();
		UpdateButtons();
		await dlg.ShowDialog(this);
	}

	/// <summary>XATTACH: 부착할 파일 선택 → 외부 참조 대화상자 → 놓기.</summary>
	private async Task XAttachAsync(string? file = null)
	{
		if (this._doc == null) return;
		if (file == null)
		{
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "부착할 파일 선택", AllowMultiple = false, FileTypeFilter = new[] { DwgOnly } });
			file = files.FirstOrDefault()?.TryGetLocalPath();
			if (file == null) return;
		}

		var name = new TextBox { Text = file, Width = 300, FontSize = 12 };
		var rAttach = new RadioButton { Content = "부착(_A)", IsChecked = true, GroupName = "xreftype" };
		var rOverlay = new RadioButton { Content = "중첩(_O)", GroupName = "xreftype" };
		var pathMode = new ComboBox { ItemsSource = new[] { "전체 경로", "상대 경로", "경로 없음" }, SelectedIndex = 0, Width = 120 };

		CheckBox OnScreen() => new() { Content = "화면에서 지정", IsChecked = true, FontSize = 12 };
		TextBox Num(string v) => new() { Text = v, Width = 70, FontSize = 12, IsEnabled = false };
		var ptOn = OnScreen();
		var px = Num("0");
		var py = Num("0");
		var pz = Num("0");
		var scOn = OnScreen();
		var sx = Num("1");
		var sy = Num("1");
		var sz = Num("1");
		var rotOn = OnScreen();
		var rot = Num("0");
		ptOn.IsCheckedChanged += (_, _) => px.IsEnabled = py.IsEnabled = pz.IsEnabled = ptOn.IsChecked != true;
		scOn.IsCheckedChanged += (_, _) => sx.IsEnabled = sy.IsEnabled = sz.IsEnabled = scOn.IsChecked != true;
		rotOn.IsCheckedChanged += (_, _) => rot.IsEnabled = rotOn.IsChecked != true;

		Control Row(string label, Control c) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = label, Width = 40, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, c } };
		Control Box(string title, params Control[] items)
		{
			var sp = new StackPanel { Spacing = 4, Margin = new Thickness(8, 4) };
			foreach (var i in items) sp.Children.Add(i);
			return new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)), BorderThickness = new Thickness(1), Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(6, 4, 6, 0) }, sp } } };
		}

		bool ok = false;
		var bOk = new Button { Content = "확인", Width = 84, IsDefault = true };
		var bCancel = new Button { Content = "취소", Width = 84, IsCancel = true };
		var bBrowse = new Button { Content = "찾아보기(_B)..." };
		var dlg = new Window
		{
			Title = "외부 참조",
			SizeToContent = SizeToContent.WidthAndHeight,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		bBrowse.Click += async (_, _) =>
		{
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "부착할 파일 선택", AllowMultiple = false, FileTypeFilter = new[] { DwgOnly } });
			if (files.FirstOrDefault()?.TryGetLocalPath() is { } p) name.Text = p;
		};
		bOk.Click += (_, _) => { ok = true; dlg.Close(); };
		bCancel.Click += (_, _) => dlg.Close();

		var mid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
		mid.Children.Add(Box("참조 유형", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 40, Children = { rAttach, rOverlay } }));
		var pm = Box("경로 유지", pathMode);
		Grid.SetColumn(pm, 2);
		mid.Children.Add(pm);
		var three = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
		three.Children.Add(Box("삽입점", ptOn, Row("X:", px), Row("Y:", py), Row("Z:", pz)));
		var sc = Box("축척", scOn, Row("X:", sx), Row("Y:", sy), Row("Z:", sz));
		Grid.SetColumn(sc, 2);
		three.Children.Add(sc);
		var rt = Box("회전", rotOn, Row("각도(G):", rot));
		Grid.SetColumn(rt, 4);
		three.Children.Add(rt);

		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "이름:", VerticalAlignment = VerticalAlignment.Center }, name, bBrowse } },
				mid,
				three,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { bOk, bCancel } },
			},
		};
		await dlg.ShowDialog(this);
		if (!ok || string.IsNullOrWhiteSpace(name.Text) || !File.Exists(name.Text))
		{
			if (ok) this._cmdLine.Print("외부 참조 파일을 찾을 수 없습니다.");
			return;
		}

		static double D(TextBox t, double def) => double.TryParse(t.Text, out double v) ? v : def;
		var mode = pathMode.SelectedIndex switch { 1 => Xrefs.PathMode.Relative, 2 => Xrefs.PathMode.None, _ => Xrefs.PathMode.Full };
		var br = Xrefs.Attach(this._doc, name.Text!, rOverlay.IsChecked == true, mode);
		this.StartCommand(new XrefPlaceCommand(br,
			ptOn.IsChecked == true ? null : new XYZ(D(px, 0), D(py, 0), D(pz, 0)),
			scOn.IsChecked == true ? null : D(sx, 1),
			scOn.IsChecked == true ? null : D(sy, 1),
			rotOn.IsChecked == true ? null : D(rot, 0)));
	}
}
