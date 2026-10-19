using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Styles;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// 명령에 딸린 앱 기능: 화면 보기 조작(ZOOM·PAN), 블록 편집기(BEDIT·BCLOSE), 도면층 상태 창(LAYERSTATE),
/// 블록 파일 저장(WBLOCK), 속성 추출(ATTEXT).
/// </summary>
public sealed partial class MainWindow
{
	/// <summary>명령이 쓰는 화면 보기 조작. 이전 보기(ZOOM P)를 위해 바꾸기 전 보기를 쌓아 둔다.</summary>
	private sealed class CanvasView : IViewController
	{
		private readonly DrawingCanvas _canvas;
		private readonly Stack<(double X, double Y, double S)> _history = new();

		public CanvasView(DrawingCanvas canvas) => this._canvas = canvas;

		private ViewTransform? V => this._canvas.View;

		public double ViewHeight => this.V is { } v ? v.Height / v.Scale : 0;

		private void Push()
		{
			if (this.V is { } v) this._history.Push((v.CenterX, v.CenterY, v.Scale));
		}

		private void Changed() => this._canvas.View = this._canvas.View;

		public void ZoomExtents()
		{
			this.Push();
			this._canvas.ZoomExtents();
		}

		public void ZoomWindow(double x1, double y1, double x2, double y2)
		{
			if (this.V is not { } v) return;
			double w = Math.Abs(x2 - x1), h = Math.Abs(y2 - y1);
			if (w < 1e-12 && h < 1e-12) return;
			this.Push();
			v.CenterX = (x1 + x2) / 2;
			v.CenterY = (y1 + y2) / 2;
			v.Scale = Math.Min(v.Width / Math.Max(w, 1e-12), v.Height / Math.Max(h, 1e-12)) * 0.97;
			this.Changed();
		}

		public void ZoomBy(double factor)
		{
			if (this.V is not { } v || !(factor > 0)) return;
			this.Push();
			v.Scale *= factor;
			this.Changed();
		}

		public void ZoomCenter(double x, double y, double? height)
		{
			if (this.V is not { } v) return;
			this.Push();
			v.CenterX = x;
			v.CenterY = y;
			if (height is > 0) v.Scale = v.Height / height.Value;
			this.Changed();
		}

		public bool ZoomPrevious()
		{
			if (this.V is not { } v || this._history.Count == 0) return false;
			var (x, y, s) = this._history.Pop();
			v.CenterX = x;
			v.CenterY = y;
			v.Scale = s;
			this.Changed();
			return true;
		}

		public void PanBy(double dx, double dy)
		{
			if (this.V is not { } v) return;
			this.Push();
			v.CenterX -= dx;
			v.CenterY -= dy;
			this.Changed();
		}
	}

	private CanvasView? _canvasView;

	private IViewController ViewController => this._canvasView ??= new CanvasView(this._canvas);

	// ===== 블록 편집기 =====

	private BlockRecord? _beditBlock;
	private BlockRecord? _beditPrevSpace;
	private int _beditUndoCount;
	private Border? _beditBanner;

	/// <summary>블록 편집 중이면 그 블록 (장면은 이 블록의 객체로 만든다).</summary>
	internal BlockRecord? EditingBlock => this._beditBlock;

	private async Task BeginBlockEditAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		if (this._beditBlock != null)
		{
			this._cmdLine.Print($"이미 블록 \"{this._beditBlock.Name}\"을(를) 편집하고 있습니다. BCLOSE로 닫으십시오.");
			return;
		}
		BlockRecord? block = this._canvas.SelectedEntities.OfType<Insert>().FirstOrDefault()?.Block;
		if (block == null)
		{
			var names = this._doc.Document.BlockRecords
				.Where(b => !b.Name.StartsWith('*') && !b.IsAnonymous && !b.Name.Equals(BlockRecord.ModelSpaceName, StringComparison.OrdinalIgnoreCase))
				.Select(b => b.Name).OrderBy(n => n).ToList();
			if (names.Count == 0)
			{
				this._cmdLine.Print("편집할 블록이 없습니다.");
				return;
			}
			string? chosen = await this.PickNameAsync("블록 정의 편집", "작성 또는 편집할 블록", names);
			if (chosen == null || !this._doc.Document.BlockRecords.TryGetValue(chosen, out block))
			{
				return;
			}
		}
		this.EnterBlockEditor(block);
	}

	private void EnterBlockEditor(BlockRecord block)
	{
		if (this._doc == null) return;
		if (this._activeLayout != null)
		{
			this.SwitchToModel();
		}
		this._canvas.SelectedEntities.Clear();
		this._beditPrevSpace = this._doc.CurrentSpace;
		this._beditUndoCount = this._doc.UndoCount;
		this._beditBlock = block;
		this._doc.CurrentSpace = block;
		this.ShowBlockEditorBanner(block.Name);
		this.RefreshCurrentSpaceView(keepView: false);
		Dispatcher.UIThread.Post(() => this._canvas.ZoomExtents(), DispatcherPriority.Background);
		this._cmdLine.Print($"블록 편집기: \"{block.Name}\" — 그리기·수정 명령으로 블록을 고친 뒤 BCLOSE(블록 편집기 닫기)로 돌아갑니다.");
	}

	private async Task CloseBlockEditorAsync(bool? save = null)
	{
		if (this._doc == null || this._beditBlock == null)
		{
			this._cmdLine.Print("블록 편집기가 열려 있지 않습니다.");
			return;
		}
		bool changed = this._doc.UndoCount > this._beditUndoCount;
		if (save == null && changed)
		{
			var dlg = new SaveChangesDialog($"블록 \"{this._beditBlock.Name}\"");
			await dlg.ShowDialog(this);
			if (dlg.Choice == SaveChoice.Cancel) return;
			save = dlg.Choice == SaveChoice.Save;
		}
		if (save == false)
		{
			while (this._doc.UndoCount > this._beditUndoCount && this._doc.CanUndo)
			{
				this._doc.Undo();
			}
		}
		string name = this._beditBlock.Name;
		this._beditBlock = null;
		this._doc.CurrentSpace = this._beditPrevSpace ?? this._doc.Document.ModelSpace;
		this._beditPrevSpace = null;
		if (this._beditBanner != null) this._beditBanner.IsVisible = false;
		this._canvas.SelectedEntities.Clear();
		this.RefreshCurrentSpaceView(keepView: false);
		Dispatcher.UIThread.Post(() => this._canvas.ZoomExtents(), DispatcherPriority.Background);
		this._cmdLine.Print(save == false ? $"블록 \"{name}\"의 변경 내용을 버렸습니다." : $"블록 \"{name}\"을(를) 저장했습니다. 속성을 바꿨다면 ATTSYNC로 참조를 맞추십시오.");
	}

	private void ShowBlockEditorBanner(string name)
	{
		if (this._beditBanner == null)
		{
			var close = new Button { Content = "블록 편집기 닫기", Height = 24, FontSize = 12, Padding = new Thickness(10, 0), Margin = new Thickness(6, 0, 0, 0) };
			close.Click += (_, _) => _ = this.CloseBlockEditorAsync();
			var save = new Button { Content = "블록 저장", Height = 24, FontSize = 12, Padding = new Thickness(10, 0) };
			save.Click += (_, _) => this._cmdLine.Print("블록 정의를 저장했습니다 (편집 내용은 바로 반영됩니다).");
			var text = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
			var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, save, close } };
			this._beditBanner = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x5A, 0x9E)),
				Padding = new Thickness(10, 4),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Top,
				CornerRadius = new CornerRadius(0, 0, 4, 4),
				Child = row,
				ZIndex = 50,
			};
			if (this._canvas.Parent is Panel panel)
			{
				panel.Children.Add(this._beditBanner);
			}
		}
		if (this._beditBanner.Child is StackPanel sp && sp.Children[0] is TextBlock tb)
		{
			tb.Text = $"블록 편집기: {name}";
		}
		this._beditBanner.IsVisible = true;
	}

	/// <summary>간단한 목록 선택 창.</summary>
	private async Task<string?> PickNameAsync(string title, string label, IReadOnlyList<string> items)
	{
		string? result = null;
		var list = new ListBox { ItemsSource = items, Height = 260, SelectedIndex = 0 };
		var dlg = new Window
		{
			Title = title,
			Width = 360,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var ok = new Button { Content = "확인", Width = 84, IsDefault = true };
		var cancel = new Button { Content = "취소", Width = 84, IsCancel = true };
		ok.Click += (_, _) => { result = list.SelectedItem as string; dlg.Close(); };
		cancel.Click += (_, _) => dlg.Close();
		list.DoubleTapped += (_, _) => { result = list.SelectedItem as string; dlg.Close(); };
		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				new TextBlock { Text = label, FontSize = 12 },
				list,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
			},
		};
		await dlg.ShowDialog(this);
		return result;
	}

	// ===== 도면층 상태 관리자 =====

	private async Task ShowLayerStateManagerAsync()
	{
		if (this._doc == null) return;
		var doc = this._doc;
		var dlg = new Window
		{
			Title = "도면층 상태 관리자",
			Width = 460,
			Height = 380,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var list = new ListBox { Height = 240 };
		void Refill() => list.ItemsSource = LayerStates.List(doc.Document).Select(s => s.Name).ToList();
		Refill();
		var nameBox = new TextBox { Width = 200, Watermark = "새 상태 이름" };
		var btnNew = new Button { Content = "새로 만들기(N)", Width = 120 };
		var btnRestore = new Button { Content = "복원(R)", Width = 120 };
		var btnDelete = new Button { Content = "삭제(D)", Width = 120 };
		var btnClose = new Button { Content = "닫기", Width = 120, IsCancel = true };
		btnNew.Click += (_, _) =>
		{
			string name = (nameBox.Text ?? string.Empty).Trim();
			if (name.Length == 0) name = $"상태{LayerStates.List(doc.Document).Count + 1}";
			LayerStates.Save(doc, name);
			nameBox.Text = string.Empty;
			Refill();
		};
		btnRestore.Click += (_, _) =>
		{
			if (list.SelectedItem is string n && LayerStates.Restore(doc, n))
			{
				this._ribbon.UpdateLayers(doc.Document.Layers, doc.CurrentLayer);
				this.UpdateTitleLayerCombo();
				this.RefreshCurrentSpaceView(keepView: true);
				this._cmdLine.Print($"도면층 상태 \"{n}\"을(를) 복원했습니다.");
			}
		};
		btnDelete.Click += (_, _) =>
		{
			if (list.SelectedItem is string n)
			{
				LayerStates.Delete(doc, n);
				Refill();
			}
		};
		btnClose.Click += (_, _) => dlg.Close();
		var right = new StackPanel { Spacing = 8, Margin = new Thickness(10, 0, 0, 0), Children = { btnNew, btnRestore, btnDelete, btnClose } };
		var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		grid.Children.Add(list);
		Grid.SetColumn(right, 1);
		grid.Children.Add(right);
		dlg.Content = new StackPanel
		{
			Margin = new Thickness(12),
			Spacing = 8,
			Children =
			{
				new TextBlock { Text = "도면층 상태 (도면 파일에 저장됩니다):", FontSize = 12 },
				grid,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new TextBlock { Text = "새 상태 이름:", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 }, nameBox } },
			},
		};
		await dlg.ShowDialog(this);
	}

	// ===== WBLOCK / ATTEXT =====

	private async Task SaveWblockAsync(EditorDocument exported)
	{
		var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "블록을 파일로 저장",
			SuggestedFileName = "새 블록.dwg",
			DefaultExtension = "dwg",
			FileTypeChoices = new[]
			{
				new FilePickerFileType("기존 CAD 2004 도면 (*.dwg)") { Patterns = new[] { "*.dwg" } },
				new FilePickerFileType("CAD 2018 DXF (*.dxf)") { Patterns = new[] { "*.dxf" } },
			},
		});
		string? path = file?.TryGetLocalPath();
		if (path == null) return;
		bool dxf = path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase);
		exported.Save(path, dxf ? ACadSharp.ACadVersion.AC1032 : ACadSharp.ACadVersion.AC1018);
		this._cmdLine.Print($"블록 파일을 저장했습니다: {path}");
	}

	/// <summary>ATTEXT: 도면의 모든 블록 참조 속성을 CSV로 (블록 이름, 핸들, X, Y, 태그별 값).</summary>
	private async Task ExportAttributesAsync()
	{
		if (this._doc == null) return;
		var inserts = this._doc.Document.ModelSpace.Entities.OfType<Insert>().Where(i => i.HasAttributes).ToList();
		if (inserts.Count == 0)
		{
			this._cmdLine.Print("속성이 있는 블록 참조가 없습니다.");
			return;
		}
		var tags = inserts.SelectMany(i => i.Attributes.Select(a => a.Tag)).Distinct().ToList();
		var sb = new StringBuilder();
		sb.AppendLine(string.Join(",", new[] { "블록", "핸들", "X", "Y" }.Concat(tags).Select(Csv)));
		foreach (var ins in inserts)
		{
			var values = ins.Attributes.GroupBy(a => a.Tag).ToDictionary(g => g.Key, g => g.First().Value);
			var row = new[] { ins.Block?.Name ?? "", ins.Handle.ToString("X"), ins.InsertPoint.X.ToString("0.####"), ins.InsertPoint.Y.ToString("0.####") }
				.Concat(tags.Select(t => values.TryGetValue(t, out var v) ? v : ""));
			sb.AppendLine(string.Join(",", row.Select(Csv)));
		}
		var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "속성 추출",
			SuggestedFileName = "attributes.csv",
			DefaultExtension = "csv",
			FileTypeChoices = new[] { new FilePickerFileType("CSV (쉼표로 구분) (*.csv)") { Patterns = new[] { "*.csv" } } },
		});
		string? path = file?.TryGetLocalPath();
		if (path == null) return;
		await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(true));
		this._cmdLine.Print($"블록 참조 {inserts.Count}개의 속성을 내보냈습니다: {path}");
	}

	private static string Csv(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
