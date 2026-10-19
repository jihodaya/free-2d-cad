using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using CSMath;
using Free2DCad.Core.Blocks;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Tools;
using Free2DCad.Rendering;
using SkiaSharp;

namespace Free2DCad.App;

/// <summary>
/// BLOCK (블록 정의) · INSERT (블록 삽입): 기존 CAD와 같은 대화상자.
/// 블록 정의: 이름·설명, 기준점(화면에서 지정/기준점 선택/X·Y·Z), 객체(화면에서 지정/객체 선택/유지·블록으로 변환·삭제),
/// 동작(주석·균일한 축척·분해 허용·단위). 블록 삽입: 파일/블록 이름, 삽입 지점·축척·회전(화면에서 지정), 블록 단위, 미리보기, 분해.
/// </summary>
public sealed partial class MainWindow
{
	private sealed class BlockDefineState
	{
		public string Name = string.Empty;
		public string Description = string.Empty;
		public bool BaseOnScreen;
		public XYZ BasePoint = XYZ.Zero;
		public bool ObjectsOnScreen;
		public List<Entity> Objects = new();
		public BlockSourceMode Mode = BlockSourceMode.Convert;
		public bool Annotative;
		public bool Uniform;
		public bool Explodable = true;
		public int UnitIndex = 1;
	}

	private static readonly (string Name, UnitsType Units)[] BlockUnits =
	{
		("단위 없음", UnitsType.Unitless),
		("밀리미터", UnitsType.Millimeters),
		("센티미터", UnitsType.Centimeters),
		("미터", UnitsType.Meters),
		("인치", UnitsType.Inches),
		("피트", UnitsType.Feet),
	};

	private static string F4(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

	private static double ParseOr(string? text, double def) =>
		double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : def;

	private static Control GroupBox(string title, params Control[] items)
	{
		var sp = new StackPanel { Spacing = 5, Margin = new Thickness(8, 4, 8, 8) };
		foreach (var i in items) sp.Children.Add(i);
		return new Border
		{
			BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
			BorderThickness = new Thickness(1),
			Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 12, Margin = new Thickness(6, 4, 6, 0) }, sp } },
		};
	}

	private static Control LabeledRow(string label, Control c, double width = 18) => new StackPanel
	{
		Orientation = Orientation.Horizontal,
		Spacing = 6,
		Children = { new TextBlock { Text = label, Width = width, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, c },
	};

	private Window NewCadDialog(string title) => new()
	{
		Title = title,
		SizeToContent = SizeToContent.WidthAndHeight,
		CanResize = false,
		WindowStartupLocation = WindowStartupLocation.CenterOwner,
		RequestedThemeVariant = ThemeVariant.Light,
		Background = CadTheme.DialogBg,
		ShowInTaskbar = false,
	};

	// ===== BLOCK =====

	private async Task ShowBlockDefineDialogAsync(BlockDefineState? state = null)
	{
		if (this._doc == null) return;
		var doc = this._doc;
		if (state == null)
		{
			state = new BlockDefineState();
			state.Objects.AddRange(this._canvas.SelectedEntities); // 미리 고른 객체
		}

		var names = doc.Document.BlockRecords.Where(b => !b.Name.StartsWith('*') && !b.IsAnonymous && !XrefLoader.IsXref(b)).Select(b => b.Name).OrderBy(n => n).ToList();
		var name = new ComboBox { ItemsSource = names, IsEditable = true, Text = state.Name, Width = 380, FontSize = 12 };
		var desc = new TextBox { Text = state.Description, Width = 380, FontSize = 12 };

		var baseOn = new CheckBox { Content = "화면에서 지정", IsChecked = state.BaseOnScreen, FontSize = 12 };
		var bx = new NumericUpDown { Value = (decimal)state.BasePoint.X, Width = 110, FontSize = 12, FormatString = "0.####", Increment = 1 };
		var by = new NumericUpDown { Value = (decimal)state.BasePoint.Y, Width = 110, FontSize = 12, FormatString = "0.####", Increment = 1 };
		var bz = new NumericUpDown { Value = (decimal)state.BasePoint.Z, Width = 110, FontSize = 12, FormatString = "0.####", Increment = 1 };
		var pickBase = new Button { Content = "기준점 선택", FontSize = 12 };

		var objOn = new CheckBox { Content = "화면에서 지정", IsChecked = state.ObjectsOnScreen, FontSize = 12 };
		var pickObj = new Button { Content = "객체 선택", FontSize = 12 };
		var rRetain = new RadioButton { Content = "객체 유지", GroupName = "blkmode", IsChecked = state.Mode == BlockSourceMode.Retain, FontSize = 12 };
		var rConvert = new RadioButton { Content = "블록으로 변환", GroupName = "blkmode", IsChecked = state.Mode == BlockSourceMode.Convert, FontSize = 12 };
		var rDelete = new RadioButton { Content = "객체 삭제", GroupName = "blkmode", IsChecked = state.Mode == BlockSourceMode.Delete, FontSize = 12 };
		var objStatus = new TextBlock { FontSize = 12, Foreground = Brushes.DimGray };
		objStatus.Text = state.Objects.Count == 0 ? "⚠ 선택된 객체가 없습니다" : $"{state.Objects.Count}개 객체가 선택됨";

		var annot = new CheckBox { Content = "주석", IsChecked = state.Annotative, FontSize = 12 };
		var matchOrient = new CheckBox { Content = "배치 방향 일치", IsEnabled = false, FontSize = 12, Margin = new Thickness(16, 0, 0, 0) };
		var uniform = new CheckBox { Content = "균일한 축척", IsChecked = state.Uniform, FontSize = 12 };
		var explodable = new CheckBox { Content = "분해 허용", IsChecked = state.Explodable, FontSize = 12 };
		var units = new ComboBox { ItemsSource = BlockUnits.Select(u => u.Name).ToList(), SelectedIndex = state.UnitIndex, Width = 120, FontSize = 12 };
		annot.IsCheckedChanged += (_, _) => matchOrient.IsEnabled = annot.IsChecked == true;

		void SyncEnabled()
		{
			bool b = baseOn.IsChecked != true;
			bx.IsEnabled = by.IsEnabled = bz.IsEnabled = pickBase.IsEnabled = b;
			pickObj.IsEnabled = objOn.IsChecked != true;
		}
		baseOn.IsCheckedChanged += (_, _) => SyncEnabled();
		objOn.IsCheckedChanged += (_, _) => SyncEnabled();
		SyncEnabled();

		string? action = null;
		var dlg = this.NewCadDialog("블록 정의");

		void Save()
		{
			state.Name = name.Text?.Trim() ?? string.Empty;
			state.Description = desc.Text ?? string.Empty;
			state.BaseOnScreen = baseOn.IsChecked == true;
			state.BasePoint = new XYZ((double)(bx.Value ?? 0), (double)(by.Value ?? 0), (double)(bz.Value ?? 0));
			state.ObjectsOnScreen = objOn.IsChecked == true;
			state.Mode = rRetain.IsChecked == true ? BlockSourceMode.Retain : rDelete.IsChecked == true ? BlockSourceMode.Delete : BlockSourceMode.Convert;
			state.Annotative = annot.IsChecked == true;
			state.Uniform = uniform.IsChecked == true;
			state.Explodable = explodable.IsChecked == true;
			state.UnitIndex = Math.Max(0, units.SelectedIndex);
		}

		pickBase.Click += (_, _) => { Save(); action = "base"; dlg.Close(); };
		pickObj.Click += (_, _) => { Save(); action = "objects"; dlg.Close(); };
		var bOk = new Button { Content = "확인", Width = 84, IsDefault = true };
		var bCancel = new Button { Content = "취소", Width = 84, IsCancel = true };
		var bHelp = new Button { Content = "?", Width = 26 };
		bOk.Click += (_, _) => { Save(); action = "ok"; dlg.Close(); };
		bCancel.Click += (_, _) => dlg.Close();

		var top = new Grid { ColumnDefinitions = new ColumnDefinitions("40,8,Auto"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
		top.Children.Add(new TextBlock { Text = "이름:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
		Grid.SetColumn(name, 2);
		top.Children.Add(name);
		var dl = new TextBlock { Text = "설명:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(dl, 2);
		top.Children.Add(dl);
		Grid.SetRow(desc, 2);
		Grid.SetColumn(desc, 2);
		top.Children.Add(desc);

		var three = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*") };
		three.Children.Add(GroupBox("기준점", baseOn, pickBase, LabeledRow("X:", bx), LabeledRow("Y:", by), LabeledRow("Z:", bz)));
		var g2 = GroupBox("객체", objOn, pickObj, rRetain, rConvert, rDelete, objStatus);
		Grid.SetColumn(g2, 2);
		three.Children.Add(g2);
		var g3 = GroupBox("동작", annot, matchOrient, uniform, explodable, LabeledRow("단위:", units, 34));
		Grid.SetColumn(g3, 4);
		three.Children.Add(g3);

		var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,6,Auto") };
		buttons.Children.Add(bHelp);
		Grid.SetColumn(bOk, 2);
		buttons.Children.Add(bOk);
		Grid.SetColumn(bCancel, 4);
		buttons.Children.Add(bCancel);

		dlg.Content = new StackPanel { Margin = new Thickness(12), Spacing = 10, Children = { top, three, buttons } };
		await dlg.ShowDialog(this);

		switch (action)
		{
			case "base":
				{
					var pick = new PickPointCommand("기준점 지정:");
					pick.Picked += p => Dispatcher.UIThread.Post(() =>
					{
						if (p is { } v) state.BasePoint = v;
						_ = this.ShowBlockDefineDialogAsync(state);
					});
					this.StartCommand(pick);
					return;
				}
			case "objects":
				{
					this._canvas.ClearSelection();
					var sel = new SelectEntitiesCommand("객체 선택:");
					sel.Selected += list => Dispatcher.UIThread.Post(() =>
					{
						if (list.Count > 0)
						{
							state.Objects.Clear();
							state.Objects.AddRange(list);
						}
						this._canvas.ClearSelection();
						_ = this.ShowBlockDefineDialogAsync(state);
					});
					this.StartCommand(sel);
					return;
				}
			case "ok":
				await this.FinishBlockDefineAsync(state);
				return;
		}
	}

	private async Task FinishBlockDefineAsync(BlockDefineState state)
	{
		if (this._doc == null) return;
		var doc = this._doc;
		if (string.IsNullOrWhiteSpace(state.Name))
		{
			this._cmdLine.Print("블록 이름을 입력하십시오.");
			await this.ShowBlockDefineDialogAsync(state);
			return;
		}
		if (state.Name.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' }) >= 0)
		{
			this._cmdLine.Print("블록 이름에는 다음 문자를 포함할 수 없습니다: < > / \\ \" : ; ? * | , = `");
			await this.ShowBlockDefineDialogAsync(state);
			return;
		}

		void Define()
		{
			if (state.Objects.Count == 0)
			{
				this._cmdLine.Print("선택된 객체가 없습니다.");
				return;
			}
			var br = BlockDefinition.Define(doc, state.Name, state.Description, state.BasePoint, state.Objects, state.Mode,
				state.Explodable, state.Uniform, BlockUnits[state.UnitIndex].Units);
			this._cmdLine.Print($"블록 \"{br.Name}\" 정의됨.");
			this._canvas.ClearSelection();
			this.RefreshCurrentSpaceView(keepView: true);
		}

		void AfterBase()
		{
			if (state.ObjectsOnScreen)
			{
				var sel = new SelectEntitiesCommand("객체 선택:");
				sel.Selected += list => Dispatcher.UIThread.Post(() =>
				{
					state.Objects.Clear();
					state.Objects.AddRange(list);
					Define();
				});
				this.StartCommand(sel);
				return;
			}
			Define();
		}

		if (state.BaseOnScreen)
		{
			var pick = new PickPointCommand("삽입 기준점 지정:");
			pick.Picked += p => Dispatcher.UIThread.Post(() =>
			{
				if (p is not { } v) return;
				state.BasePoint = v;
				AfterBase();
			});
			this.StartCommand(pick);
			return;
		}
		AfterBase();
	}

	// ===== INSERT =====

	private async Task ShowBlockInsertDialogAsync()
	{
		if (this._doc == null) return;
		var doc = this._doc;
		var names = doc.Document.BlockRecords.Where(b => !b.Name.StartsWith('*') && !b.IsAnonymous && !XrefLoader.IsXref(b)).Select(b => b.Name).OrderBy(n => n).ToList();

		var rFile = new RadioButton { Content = "From file:", GroupName = "insfrom", FontSize = 12 };
		var rName = new RadioButton { Content = "Block name:", GroupName = "insfrom", IsChecked = true, FontSize = 12 };
		var filePath = new TextBox { Width = 260, FontSize = 12, IsEnabled = false };
		var browse = new Button { Content = "찾아보기...", FontSize = 12 };
		var blockName = new ComboBox { ItemsSource = names, SelectedIndex = names.Count > 0 ? 0 : -1, Width = 260, FontSize = 12 };
		rFile.IsCheckedChanged += (_, _) => { filePath.IsEnabled = rFile.IsChecked == true; blockName.IsEnabled = rFile.IsChecked != true; };

		CheckBox OnScreen() => new() { Content = "화면에서 지정", IsChecked = true, FontSize = 12 };
		TextBox Num(string v) => new() { Text = v, Width = 90, FontSize = 12, IsEnabled = false };
		var ptOn = OnScreen();
		var px = Num("0.0000");
		var py = Num("0.0000");
		var pz = Num("0.0000");
		var scOn = OnScreen();
		var sx = Num("1.0000");
		var sy = Num("1.0000");
		var sz = Num("1.0000");
		var uniformScale = new CheckBox { Content = "동일한 축척", IsChecked = true, FontSize = 12 };
		var rotOn = OnScreen();
		var rot = Num("0");
		var unitText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		var factorText = new TextBlock { Text = "1", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
		var explode = new CheckBox { Content = "분해", FontSize = 12 };
		var preview = new Image { Width = 140, Height = 110, Stretch = Stretch.Uniform };
		var previewBox = new Border { Background = Brushes.Black, Width = 146, Height = 116, Child = preview };

		void SyncScale()
		{
			bool manual = scOn.IsChecked != true;
			sx.IsEnabled = manual;
			sy.IsEnabled = sz.IsEnabled = manual && uniformScale.IsChecked != true;
		}
		ptOn.IsCheckedChanged += (_, _) => px.IsEnabled = py.IsEnabled = pz.IsEnabled = ptOn.IsChecked != true;
		scOn.IsCheckedChanged += (_, _) => SyncScale();
		uniformScale.IsCheckedChanged += (_, _) => SyncScale();
		rotOn.IsCheckedChanged += (_, _) => rot.IsEnabled = rotOn.IsChecked != true;
		SyncScale();

		BlockRecord? Current()
		{
			if (rFile.IsChecked == true) return null;
			return blockName.SelectedItem is string n && doc.Document.BlockRecords.TryGetValue(n, out var b) ? b : null;
		}

		void ShowBlock()
		{
			var b = Current();
			var unit = b?.Units ?? UnitsType.Unitless;
			unitText.Text = BlockUnits.FirstOrDefault(u => u.Units == unit).Name ?? (unit == UnitsType.Unitless ? "정의되지 않음" : unit.ToString());
			if (unit == UnitsType.Unitless) unitText.Text = "정의되지 않음";
			preview.Source = b != null ? RenderBlockPreview(doc, b, 140, 110) : null;
		}
		blockName.SelectionChanged += (_, _) => ShowBlock();
		ShowBlock();

		bool ok = false;
		var dlg = this.NewCadDialog("블록 삽입");
		browse.Click += async (_, _) =>
		{
			var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "삽입할 도면 선택", AllowMultiple = false, FileTypeFilter = new[] { DwgOnly } });
			if (files.FirstOrDefault()?.TryGetLocalPath() is { } p)
			{
				filePath.Text = p;
				rFile.IsChecked = true;
			}
		};
		var bIns = new Button { Content = "Insert", Width = 84, IsDefault = true };
		var bCancel = new Button { Content = "Cancel", Width = 84, IsCancel = true };
		var bHelp = new Button { Content = "?", Width = 26 };
		bIns.Click += (_, _) => { ok = true; dlg.Close(); };
		bCancel.Click += (_, _) => dlg.Close();

		var src = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
		src.Children.Add(rFile);
		Grid.SetColumn(filePath, 2);
		src.Children.Add(filePath);
		Grid.SetColumn(browse, 4);
		src.Children.Add(browse);
		Grid.SetRow(rName, 2);
		src.Children.Add(rName);
		Grid.SetRow(blockName, 2);
		Grid.SetColumn(blockName, 2);
		src.Children.Add(blockName);

		var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*,8,Auto") };
		row.Children.Add(GroupBox("삽입 지점", ptOn, LabeledRow("X:", px), LabeledRow("Y:", py), LabeledRow("Z:", pz)));
		var g2 = GroupBox("축척", scOn, LabeledRow("X:", sx), LabeledRow("Y:", sy), LabeledRow("Z:", sz), uniformScale);
		Grid.SetColumn(g2, 2);
		row.Children.Add(g2);
		var g3 = new StackPanel
		{
			Spacing = 8,
			Children =
			{
				GroupBox("회전", rotOn, LabeledRow("각도:", rot, 34)),
				GroupBox("블록 단위", LabeledRow("단위:", unitText, 34), LabeledRow("계수:", factorText, 34)),
			},
		};
		Grid.SetColumn(g3, 4);
		row.Children.Add(g3);
		Grid.SetColumn(previewBox, 6);
		previewBox.VerticalAlignment = VerticalAlignment.Top;
		row.Children.Add(previewBox);

		var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,6,Auto") };
		buttons.Children.Add(new StackPanel { Spacing = 4, Children = { explode, bHelp } });
		Grid.SetColumn(bIns, 2);
		bIns.VerticalAlignment = VerticalAlignment.Bottom;
		buttons.Children.Add(bIns);
		Grid.SetColumn(bCancel, 4);
		bCancel.VerticalAlignment = VerticalAlignment.Bottom;
		buttons.Children.Add(bCancel);

		dlg.Content = new StackPanel { Margin = new Thickness(12), Spacing = 10, Children = { GroupBox("Insert", src), row, buttons } };
		await dlg.ShowDialog(this);
		if (!ok) return;

		BlockRecord? block = Current();
		if (rFile.IsChecked == true)
		{
			if (string.IsNullOrWhiteSpace(filePath.Text) || !File.Exists(filePath.Text))
			{
				this._cmdLine.Print("삽입할 파일을 찾을 수 없습니다.");
				return;
			}
			try
			{
				block = BlockLibrary.Import(doc, filePath.Text!).Block;
			}
			catch (Exception ex)
			{
				this._cmdLine.Print($"파일을 읽을 수 없습니다: {ex.Message}");
				return;
			}
		}
		if (block == null)
		{
			this._cmdLine.Print("삽입할 블록을 고르십시오.");
			return;
		}

		XYZ? point = ptOn.IsChecked == true ? null : new XYZ(ParseOr(px.Text, 0), ParseOr(py.Text, 0), ParseOr(pz.Text, 0));
		XYZ? scale = null;
		if (scOn.IsChecked != true)
		{
			double x = ParseOr(sx.Text, 1);
			scale = uniformScale.IsChecked == true ? new XYZ(x, x, x) : new XYZ(x, ParseOr(sy.Text, x), ParseOr(sz.Text, x));
		}
		double? rotation = rotOn.IsChecked == true ? null : ParseOr(rot.Text, 0);
		this.StartCommand(new BlockPlaceCommand(block, point, scale, rotation, uniformScale.IsChecked == true || block.CanScale, explode.IsChecked == true));
	}

	/// <summary>블록 미리보기 그림 (검은 바탕, 기존 CAD 삽입 대화상자처럼).</summary>
	private static Bitmap? RenderBlockPreview(Free2DCad.Core.EditorDocument doc, BlockRecord block, int width, int height)
	{
		try
		{
			var insert = BlockDefinition.MakeInsert(block, XYZ.Zero);
			var scene = SceneBuilder.BuildEntities(doc.Document, new Entity[] { insert });
			var ext = scene.Extents;
			if (ext.IsEmpty || ext.Width <= 0 && ext.Height <= 0) return null;
			double w = Math.Max(ext.Width, 1e-6), h = Math.Max(ext.Height, 1e-6);
			double s = Math.Min((width - 10) / w, (height - 10) / h);
			var view = new ViewTransform((ext.MinX + ext.MaxX) / 2, (ext.MinY + ext.MaxY) / 2, s, width, height);
			using var bmp = new SKBitmap(width, height);
			using (var canvas = new SKCanvas(bmp))
			{
				using var renderer = new SkiaSceneRenderer(scene);
				renderer.Render(canvas, view);
			}
			using var img = SKImage.FromBitmap(bmp);
			using var data = img.Encode(SKEncodedImageFormat.Png, 100);
			using var ms = new MemoryStream(data.ToArray());
			return new Bitmap(ms);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
