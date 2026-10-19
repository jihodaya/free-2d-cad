using Avalonia.Controls;
using Avalonia.Media;
using Color = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 아이콘 레이어 정의: 경로 데이터, 선 브러시, 채우기 브러시, 선 두께.
/// </summary>
public sealed record IconLayer(string PathData, IBrush? Stroke, IBrush? Fill = null, double StrokeThickness = 1.8);

/// <summary>
/// 기존 CAD 스타일 다층 컬러 벡터 아이콘 헬퍼.
/// 모든 아이콘은 24x24 좌표계로 설계되며, 여러 색상의 층(IconLayer)으로 구성되어 풍부한 시각적 효과를 제공한다.
/// </summary>
public static partial class IconHelper
{
	// 표준 컬러 팔레트 브러시 (기존 CAD 테마)
	public static readonly IBrush WhiteLine = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
	public static readonly IBrush BlueGrip = new SolidColorBrush(Color.FromRgb(0x3A, 0x8E, 0xE6));
	public static readonly IBrush BlueText = new SolidColorBrush(Color.FromRgb(0x2F, 0x7B, 0xD6));
	public static readonly IBrush YellowArrow = new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C));
	public static readonly IBrush GoldFolder = new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C));
	public static readonly IBrush GoldFolderLight = new SolidColorBrush(Color.FromRgb(0xFD, 0xE2, 0x8B));
	public static readonly IBrush RedCross = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
	public static readonly IBrush CyanDim = new SolidColorBrush(Color.FromRgb(0x35, 0xC2, 0xC1));
	public static readonly IBrush GreenCheck = new SolidColorBrush(Color.FromRgb(0x4B, 0xB5, 0x43));
	public static readonly IBrush GrayBody = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
	public static readonly IBrush SilverMetal = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
	public static readonly IBrush DarkGray = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
	public static readonly IBrush OrangeAccent = new SolidColorBrush(Color.FromRgb(0xFA, 0x8C, 0x16));

	// 아이콘 식별자 상수
	public const string Line = "Line";
	public const string Polyline = "Polyline";
	public const string Circle = "Circle";
	public const string Arc = "Arc";
	public const string Rectang = "Rectang";
	public const string Text = "Text";
	public const string MText = "MText";

	public const string Move = "Move";
	public const string Copy = "Copy";
	public const string Rotate = "Rotate";
	public const string Mirror = "Mirror";
	public const string Scale = "Scale";
	public const string Offset = "Offset";
	public const string Trim = "Trim";
	public const string Extend = "Extend";
	public const string Fillet = "Fillet";
	public const string Chamfer = "Chamfer";
	public const string Break = "Break";
	public const string Join = "Join";
	public const string Stretch = "Stretch";
	public const string Array = "Array";
	public const string Explode = "Explode";
	public const string Erase = "Erase";
	public const string Hatch = "Hatch";

	public const string DimLinear = "DimLinear";
	public const string DimAligned = "DimAligned";
	public const string DimRadius = "DimRadius";
	public const string DimDiameter = "DimDiameter";
	public const string DimAngular = "DimAngular";
	public const string Dist = "Dist";
	public const string TableOut = "TableOut";
	public const string Table = "Table";

	public const string Layer = "Layer";
	public const string Properties = "Properties";
	public const string MatchProp = "MatchProp";

	public const string ZoomExtents = "ZoomExtents";
	public const string ZoomIn = "ZoomIn";
	public const string ZoomOut = "ZoomOut";

	public const string New = "New";
	public const string Open = "Open";
	public const string Save = "Save";
	public const string SaveAs = "SaveAs";
	public const string Print = "Print";
	public const string Plot = "Plot";
	public const string Undo = "Undo";
	public const string Redo = "Redo";
	public const string Paste = "Paste";
	public const string Cut = "Cut";

	public const string Id = "Id";
	public const string Area = "Area";
	public const string List = "List";

	public const string BulbOn = "BulbOn";
	public const string BulbOff = "BulbOff";
	public const string Snowflake = "Snowflake";
	public const string Sun = "Sun";
	public const string Lock = "Lock";
	public const string Unlock = "Unlock";
	public const string Checkmark = "Checkmark";
	public const string AppIcon = "AppIcon";

	public const string ColorWheel = "ColorWheel";
	public const string Linetype = "Linetype";
	public const string Lineweight = "Lineweight";
	public const string CornerArrow = "CornerArrow";

	public const string QuickSelect = "QuickSelect";
	public const string PickObject = "PickObject";
	public const string PickAdd = "PickAdd";
	public const string Deselect = "Deselect";
	public const string SelectAll = "SelectAll";

	public const string SetCurrent = "SetCurrent";
	public const string PrevLayer = "PrevLayer";
	public const string LayerOff = "LayerOff";
	public const string LayerOn = "LayerOn";
	public const string LayerFreeze = "LayerFreeze";
	public const string LayerThaw = "LayerThaw";
	public const string LayerLock = "LayerLock";
	public const string LayerUnlock = "LayerUnlock";
	public const string MatchLayer = "MatchLayer";
	public const string LayerDelete = "LayerDelete";
	public const string LayerIsolate = "LayerIsolate";
	public const string LayerState = "LayerState";

	public const string BlockInsert = "BlockInsert";
	public const string EditAttribute = "EditAttribute";
	public const string BlockCreate = "BlockCreate";
	public const string BlockEditor = "BlockEditor";

	// 메뉴용 추가 아이콘
	public const string Exit = "Exit";
	public const string Pan = "Pan";
	public const string CommandLine = "CommandLine";
	public const string TextWindow = "TextWindow";
	public const string Help = "Help";
	public const string Gradient = "Gradient";
	public const string TextStyle = "TextStyle";
	public const string DimStyle = "DimStyle";
	public const string Settings = "Settings";
	public const string Units = "Units";
	public const string CloseDoc = "CloseDoc";
	public const string ResetSettings = "ResetSettings";
	public const string Regen = "Regen";

	/// <summary>
	/// 여러 층의 IconLayer로 구성된 컬러 벡터 아이콘을 생성한다.
	/// </summary>
	public static Control CreateLayered(double size, params IconLayer[] layers)
	{
		var panel = new Panel
		{
			Width = 24,
			Height = 24,
		};

		foreach (var layer in layers)
		{
			var path = new Avalonia.Controls.Shapes.Path
			{
				Data = StreamGeometry.Parse(layer.PathData),
				Stroke = layer.Stroke,
				Fill = layer.Fill,
				StrokeThickness = layer.StrokeThickness,
				StrokeLineCap = PenLineCap.Round,
				StrokeJoin = PenLineJoin.Round,
				Width = 24,
				Height = 24,
			};
			panel.Children.Add(path);
		}

		return new Viewbox
		{
			Child = panel,
			Width = size,
			Height = size,
			Stretch = Avalonia.Media.Stretch.Uniform,
		};
	}

	/// <summary>
	/// 단일 경로 데이터 또는 아이콘 이름으로부터 아이콘 Control을 생성한다 (기존 API 호환).
	/// </summary>
	public static Control Create(string pathOrName, double size = 16, IBrush? foreground = null)
	{
		if (TryCreateKnownIcon(pathOrName, size, out var layeredIcon))
		{
			return layeredIcon;
		}

		var path = new Avalonia.Controls.Shapes.Path
		{
			Data = StreamGeometry.Parse(pathOrName),
			Stroke = foreground ?? Brushes.LightGray,
			StrokeThickness = 1.8,
			StrokeLineCap = PenLineCap.Round,
			StrokeJoin = PenLineJoin.Round,
			Width = 24,
			Height = 24,
		};
		return new Viewbox { Child = path, Width = size, Height = size, Stretch = Avalonia.Media.Stretch.Uniform };
	}

	private static readonly Lazy<Avalonia.Media.Imaging.Bitmap?> LogoBitmap = new(() =>
	{
		using var s = typeof(IconHelper).Assembly.GetManifestResourceStream("Free2DCad.App.Free2DCad.png");
		return s == null ? null : new Avalonia.Media.Imaging.Bitmap(s);
	});

	/// <summary>Free 2D CAD 로고 (실행 파일 아이콘과 같은 그림, Assets\Free2DCad.png).</summary>
	public static Control AppLogo(double size) => new Image
	{
		Source = LogoBitmap.Value,
		Width = size,
		Height = size,
		Stretch = Avalonia.Media.Stretch.Uniform,
	};

	/// <summary>
	/// 등록된 컬러 아이콘을 생성한다.
	/// </summary>
	public static Control GetIcon(string name, double size = 16)
	{
		if (TryCreateKnownIcon(name, size, out var ctrl))
		{
			return ctrl;
		}
		return Create(name, size);
	}

	/// <summary>등록된 아이콘이면 만들고, 없으면 null (이름을 경로 데이터로 해석하다 예외 나는 것 방지).</summary>
	public static Control? TryGetIcon(string name, double size = 16)
	{
		try
		{
			return TryCreateKnownIcon(name, size, out var c) ? c : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool TryCreateKnownIcon(string key, double size, out Control icon)
	{
		if (IconFactories.TryGetValue(key, out var factory))
		{
			icon = factory(size);
			return true;
		}
		if (MoreIcons.TryGetValue(key, out var more))
		{
			icon = more(size);
			return true;
		}
		if (TextIcons.TryGetValue(key, out var te))
		{
			icon = te(size);
			return true;
		}

		// 대소문자 무시 검색
		foreach (var kvp in IconFactories)
		{
			if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				icon = kvp.Value(size);
				return true;
			}
		}

		icon = null!;
		return false;
	}

	private static readonly Dictionary<string, Func<double, Control>> IconFactories = new(StringComparer.OrdinalIgnoreCase)
	{
		[Line] = size => CreateLayered(size,
			new IconLayer("M 4,20 L 20,4", WhiteLine, null, 2.0),
			new IconLayer("M 2,18 H 6 V 22 H 2 Z M 18,2 H 22 V 6 H 18 Z", BlueGrip, BlueGrip, 1.0)),

		[Polyline] = size => CreateLayered(size,
			new IconLayer("M 3,19 L 9,7 L 15,16 L 21,5", WhiteLine, null, 1.8),
			new IconLayer("M 1.5,17.5 H 4.5 V 20.5 H 1.5 Z M 7.5,5.5 H 10.5 V 8.5 H 7.5 Z M 13.5,14.5 H 16.5 V 17.5 H 13.5 Z M 19.5,3.5 H 22.5 V 6.5 H 19.5 Z", BlueGrip, BlueGrip, 1.0)),

		[Circle] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.8),
			new IconLayer("M 10.5,10.5 H 13.5 V 13.5 H 10.5 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 12,12 L 18.36,5.64", CyanDim, null, 1.4)),

		[Arc] = size => CreateLayered(size,
			new IconLayer("M 4,20 A 16,16 0 0 1 20,4", WhiteLine, null, 1.8),
			new IconLayer("M 2.5,18.5 H 5.5 V 21.5 H 2.5 Z M 8.5,8.5 H 11.5 V 11.5 H 8.5 Z M 18.5,2.5 H 21.5 V 5.5 H 18.5 Z", BlueGrip, BlueGrip, 1.0)),

		[Rectang] = size => CreateLayered(size,
			new IconLayer("M 3,6 H 21 V 18 H 3 Z", WhiteLine, null, 1.8),
			new IconLayer("M 1.5,4.5 H 4.5 V 7.5 H 1.5 Z M 19.5,4.5 H 22.5 V 7.5 H 19.5 Z M 19.5,16.5 H 22.5 V 19.5 H 19.5 Z M 1.5,16.5 H 4.5 V 19.5 H 1.5 Z", BlueGrip, BlueGrip, 1.0)),

		[Text] = size => CreateLayered(size,
			new IconLayer("M 4,20 L 12,3 L 20,20 M 7,14 H 17", BlueText, null, 2.8)),

		[MText] = size => CreateLayered(size,
			new IconLayer("M 3,19 L 8.5,4 L 14,19 M 5,14 H 12", BlueText, null, 2.4),
			new IconLayer("M 16,6 H 22 M 16,11 H 22 M 16,16 H 20", CyanDim, null, 1.8)),

		[Move] = size => CreateLayered(size,
			new IconLayer("M 12,2 L 15,5 H 13 V 11 H 19 V 9 L 22,12 L 19,15 V 13 H 13 V 19 H 15 L 12,22 L 9,19 H 11 V 13 H 5 V 15 L 2,12 L 5,9 V 11 H 11 V 5 H 9 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 10.5,10.5 H 13.5 V 13.5 H 10.5 Z", WhiteLine, WhiteLine, 1.0)),

		[Copy] = size => CreateLayered(size,
			new IconLayer("M 4,8 H 14 V 18 H 4 Z", WhiteLine, null, 1.6),
			new IconLayer("M 10,4 H 20 V 14 H 10 Z", BlueGrip, null, 1.6),
			new IconLayer("M 8,14 L 14,8 M 10,8 H 14 V 12", YellowArrow, null, 1.6)),

		[Rotate] = size => CreateLayered(size,
			new IconLayer("M 10.5,10.5 H 13.5 V 13.5 H 10.5 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 12,4 A 8,8 0 1 1 5,14 L 2,11 M 5,14 L 8,11", YellowArrow, null, 2.0)),

		[Mirror] = size => CreateLayered(size,
			new IconLayer("M 12,2 V 22", CyanDim, null, 1.8),
			new IconLayer("M 11,5 L 3,14 H 11 Z", WhiteLine, null, 1.6),
			new IconLayer("M 13,5 L 21,14 H 13 Z", BlueGrip, BlueGrip, 1.0)),

		[Scale] = size => CreateLayered(size,
			new IconLayer("M 4,20 H 11 V 13 H 4 Z", WhiteLine, WhiteLine, 1.0),
			new IconLayer("M 4,20 H 20 V 4 H 4 Z", CyanDim, null, 1.5),
			new IconLayer("M 11,13 L 18,6 M 14,6 H 18 V 10", YellowArrow, null, 1.8)),

		[Offset] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", CyanDim, null, 1.6),
			new IconLayer("M 8,8 H 16 V 16 H 8 Z", YellowArrow, null, 1.6)),

		[Trim] = size => CreateLayered(size,
			new IconLayer("M 4,12 H 20", WhiteLine, null, 2.0),
			new IconLayer("M 12,4 V 20", CyanDim, null, 1.8),
			new IconLayer("M 15,9 L 19,15 M 19,9 L 15,15", RedCross, null, 2.4)),

		[Extend] = size => CreateLayered(size,
			new IconLayer("M 20,4 V 20", CyanDim, null, 2.2),
			new IconLayer("M 4,12 H 14", WhiteLine, null, 2.0),
			new IconLayer("M 13,9 L 18,12 L 13,15 Z", YellowArrow, YellowArrow, 1.0)),

		[Fillet] = size => CreateLayered(size,
			new IconLayer("M 4,20 V 12 M 12,4 H 20", WhiteLine, null, 1.4),
			new IconLayer("M 4,12 A 8,8 0 0 1 12,4", YellowArrow, null, 2.2)),

		[Chamfer] = size => CreateLayered(size,
			new IconLayer("M 4,20 V 12 M 12,4 H 20", WhiteLine, null, 1.4),
			new IconLayer("M 4,12 L 12,4", YellowArrow, null, 2.2)),

		[Break] = size => CreateLayered(size,
			new IconLayer("M 3,12 H 8 M 16,12 H 21", WhiteLine, null, 2.0),
			new IconLayer("M 10,7 L 14,17", RedCross, null, 2.2),
			new IconLayer("M 6.5,10.5 H 9.5 V 13.5 H 6.5 Z M 14.5,10.5 H 17.5 V 13.5 H 14.5 Z", BlueGrip, BlueGrip, 1.0)),

		[Join] = size => CreateLayered(size,
			new IconLayer("M 3,12 H 8 M 16,12 H 21", WhiteLine, null, 2.0),
			new IconLayer("M 8,12 L 11,9 V 15 Z M 16,12 L 13,9 V 15 Z", CyanDim, CyanDim, 1.0)),

		[Stretch] = size => CreateLayered(size,
			new IconLayer("M 4,8 H 14 V 16 H 4 Z", CyanDim, null, 1.5),
			new IconLayer("M 14,12 H 21 M 18,9 L 21,12 L 18,15", YellowArrow, null, 1.8)),

		[Array] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 8 V 8 H 4 Z", WhiteLine, WhiteLine, 1.0),
			new IconLayer("M 10,4 H 14 V 8 H 10 Z M 16,4 H 20 V 8 H 16 Z M 4,10 H 8 V 14 H 4 Z M 10,10 H 14 V 14 H 10 Z M 16,10 H 20 V 14 H 16 Z M 4,16 H 8 V 20 H 4 Z M 10,16 H 14 V 20 H 10 Z M 16,16 H 20 V 20 H 16 Z", BlueGrip, BlueGrip, 1.0)),

		[Explode] = size => CreateLayered(size,
			new IconLayer("M 12,2 L 14,8 L 20,6 L 16,11 L 22,14 L 16,16 L 18,22 L 12,18 L 6,22 L 8,16 L 2,14 L 8,11 L 4,6 L 10,8 Z", YellowArrow, null, 1.8),
			new IconLayer("M 10.5,10.5 H 13.5 V 13.5 H 10.5 Z", RedCross, RedCross, 1.0)),

		[Erase] = size => CreateLayered(size,
			new IconLayer("M 4,4 L 20,20 M 20,4 L 4,20", RedCross, null, 3.2)),

		[Hatch] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", CyanDim, null, 1.8),
			new IconLayer("M 4,10 L 10,4 M 4,16 L 16,4 M 4,20 L 20,4 M 8,20 L 20,8 M 14,20 L 20,14", YellowArrow, null, 1.4)),

		[DimLinear] = size => CreateLayered(size,
			new IconLayer("M 4,4 V 16 M 20,4 V 16 M 4,10 H 20", CyanDim, null, 1.6),
			new IconLayer("M 7,8 L 4,10 L 7,12 Z M 17,8 L 20,10 L 17,12 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 10,6 H 14", WhiteLine, null, 2.0)),

		[DimAligned] = size => CreateLayered(size,
			new IconLayer("M 3,7 L 7,3 M 17,21 L 21,17 M 5,5 L 19,19", CyanDim, null, 1.6),
			new IconLayer("M 7,5 L 5,5 L 5,7 Z M 17,19 L 19,19 L 19,17 Z", CyanDim, CyanDim, 1.0)),

		[DimRadius] = size => CreateLayered(size,
			new IconLayer("M 4,20 A 16,16 0 0 1 20,4", WhiteLine, null, 1.6),
			new IconLayer("M 10,14 L 18,6 M 15,6 H 18 V 9", CyanDim, null, 1.6),
			new IconLayer("M 3,20 H 5 M 4,19 V 21", BlueGrip, null, 1.5)),

		[DimDiameter] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.6),
			new IconLayer("M 5.64,18.36 L 18.36,5.64 M 6,15 L 5.64,18.36 L 9,18 M 15,6 L 18.36,5.64 L 18,9", CyanDim, null, 1.5)),

		[DimAngular] = size => CreateLayered(size,
			new IconLayer("M 4,20 L 20,20 M 4,20 L 16,6", WhiteLine, null, 1.6),
			new IconLayer("M 13,20 A 9,9 0 0 0 11,12", CyanDim, null, 1.6),
			new IconLayer("M 13,18 L 13,20 L 11,20 M 12,14 L 11,12 L 9,13", CyanDim, null, 1.4)),

		[Dist] = size => CreateLayered(size,
			new IconLayer("M 4,20 L 20,4", YellowArrow, null, 2.4),
			new IconLayer("M 2,18 L 6,22 M 18,2 L 22,6", CyanDim, null, 2.0),
			new IconLayer("M 7,15 L 9,17 M 11,11 L 13,13 M 15,7 L 17,9", WhiteLine, null, 1.5)),

		[Table] = size => CreateLayered(size,
			new IconLayer("M 2,4 H 22 V 20 H 2 Z M 2,8.5 H 22 M 2,12.5 H 22 M 2,16.5 H 22 M 8.5,8.5 V 20 M 15.5,8.5 V 20", WhiteLine, null, 1.4),
			new IconLayer("M 2,4 H 22 V 8.5 H 2 Z", BlueGrip, BlueGrip, 1.0)),

		[TableOut] = size => CreateLayered(size,
			new IconLayer("M 3,4 H 21 V 20 H 3 Z", WhiteLine, null, 1.6),
			new IconLayer("M 3,4 H 21 V 9 H 3 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 3,14 H 21 M 9,4 V 20 M 15,4 V 20", CyanDim, null, 1.2)),

		[Layer] = size => CreateLayered(size,
			new IconLayer("M 12,12 L 2,17 L 12,22 L 22,17 Z", WhiteLine, null, 1.5),
			new IconLayer("M 12,7 L 2,12 L 12,17 L 22,12 Z", BlueGrip, null, 1.5),
			new IconLayer("M 12,2 L 2,7 L 12,12 L 22,7 Z", YellowArrow, YellowArrow, 1.0)),

		[Properties] = size => CreateLayered(size,
			new IconLayer("M 4,3 H 20 V 21 H 4 Z", GrayBody, WhiteLine, 1.4),
			new IconLayer("M 4,3 H 20 V 7 H 4 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 7,11 H 17 M 7,15 H 17 M 7,19 H 13", BlueText, null, 1.6)),

		[MatchProp] = size => CreateLayered(size,
			new IconLayer("M 14,4 L 19,9 L 11,17 H 7 V 13 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 7,13 L 11,17 L 9,19 H 5 V 15 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 4,21 H 20", CyanDim, null, 2.0)),

		[ZoomExtents] = size => CreateLayered(size,
			new IconLayer("M 3,3 H 8 M 3,3 V 8 M 21,3 H 16 M 21,3 V 8 M 3,21 H 8 M 3,21 V 16 M 21,21 H 16 M 21,21 V 16", WhiteLine, null, 1.8),
			new IconLayer("M 8,8 H 16 V 16 H 8 Z", BlueGrip, null, 1.5)),

		[ZoomIn] = size => CreateLayered(size,
			new IconLayer("M 10,2 A 8,8 0 1 0 10,18 A 8,8 0 1 0 10,2", BlueGrip, null, 2.0),
			new IconLayer("M 16,16 L 22,22", GrayBody, null, 2.4),
			new IconLayer("M 10,6 V 14 M 6,10 H 14", YellowArrow, null, 2.0)),

		[ZoomOut] = size => CreateLayered(size,
			new IconLayer("M 10,2 A 8,8 0 1 0 10,18 A 8,8 0 1 0 10,2", BlueGrip, null, 2.0),
			new IconLayer("M 16,16 L 22,22", GrayBody, null, 2.4),
			new IconLayer("M 6,10 H 14", YellowArrow, null, 2.0)),

		[New] = size => CreateLayered(size,
			new IconLayer("M 5,2 H 14 L 19,7 V 22 H 5 Z", GrayBody, WhiteLine, 1.2),
			new IconLayer("M 14,2 V 7 H 19 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 17,14 L 18,17 L 21,18 L 18,19 L 17,22 L 16,19 L 13,18 L 16,17 Z", YellowArrow, YellowArrow, 1.0)),

		[Open] = size => CreateLayered(size,
			new IconLayer("M 2,5 H 9 L 11,7 H 21 V 19 H 2 Z", GoldFolder, GoldFolder, 1.0),
			new IconLayer("M 6,7 H 17 V 17 H 6 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 2,10 L 5,19 H 22 L 19,10 Z", GoldFolderLight, GoldFolderLight, 1.0)),

		[Save] = size => CreateLayered(size,
			new IconLayer("M 4,3 H 17 L 20,6 V 21 H 4 Z", BlueText, BlueText, 1.0),
			new IconLayer("M 7,3 H 15 V 9 H 7 Z", SilverMetal, SilverMetal, 1.0),
			new IconLayer("M 7,12 H 17 V 21 H 7 Z", GrayBody, WhiteLine, 1.0)),

		[SaveAs] = size => CreateLayered(size,
			new IconLayer("M 4,3 H 17 L 20,6 V 21 H 4 Z", BlueText, BlueText, 1.0),
			new IconLayer("M 7,3 H 15 V 9 H 7 Z", SilverMetal, SilverMetal, 1.0),
			new IconLayer("M 7,12 H 17 V 21 H 7 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 15,15 L 21,15 M 18,12 L 21,15 L 18,18", YellowArrow, null, 2.0)),

		[Print] = size => CreateLayered(size,
			new IconLayer("M 4,8 H 20 V 16 H 18 V 21 H 6 V 16 H 4 Z", GrayBody, GrayBody, 1.0),
			new IconLayer("M 7,3 H 17 V 8 H 7 Z M 7,15 H 17 V 22 H 7 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 17,10 H 19 V 12 H 17 Z", CyanDim, CyanDim, 1.0)),

		[Plot] = size => IconFactories[Print](size),

		[Undo] = size => CreateLayered(size,
			new IconLayer("M 8,6 L 3,11 L 8,16 M 4,11 H 13 A 6,6 0 0 1 19,17", BlueText, null, 2.4)),

		[Redo] = size => CreateLayered(size,
			new IconLayer("M 16,6 L 21,11 L 16,16 M 20,11 H 11 A 6,6 0 0 0 5,17", BlueText, null, 2.4)),

		[Paste] = size => CreateLayered(size,
			new IconLayer("M 5,5 H 19 V 22 H 5 Z", GoldFolder, GoldFolder, 1.0),
			new IconLayer("M 9,3 H 15 V 6 H 9 Z", SilverMetal, SilverMetal, 1.0),
			new IconLayer("M 7,8 H 17 V 20 H 7 Z", GrayBody, WhiteLine, 1.0)),

		[Cut] = size => CreateLayered(size,
			new IconLayer("M 6,4 A 3,3 0 1 0 6,10 L 14,14 L 18,20 M 6,20 A 3,3 0 1 0 6,14 L 14,10 L 18,4", BlueText, null, 2.0)),

		[Id] = size => CreateLayered(size,
			new IconLayer("M 12,2 V 22 M 2,12 H 22", WhiteLine, null, 1.5),
			new IconLayer("M 12,7 A 5,5 0 1 0 12,17 A 5,5 0 1 0 12,7", CyanDim, null, 1.8),
			new IconLayer("M 10.5,10.5 H 13.5 V 13.5 H 10.5 Z", YellowArrow, YellowArrow, 1.0)),

		[Area] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 7,7 H 17 V 17 H 7 Z", WhiteLine, WhiteLine, 1.0)),

		[List] = size => CreateLayered(size,
			new IconLayer("M 4,5 H 6 V 7 H 4 Z M 4,11 H 6 V 13 H 4 Z M 4,17 H 6 V 19 H 4 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 9,6 H 20 M 9,12 H 20 M 9,18 H 20", WhiteLine, null, 1.8)),

		[BulbOn] = size => CreateLayered(size,
			new IconLayer("M 9,21 H 15 M 10,23 H 14", SilverMetal, null, 1.5),
			new IconLayer("M 12,2 A 7,7 0 0 0 7,14 L 8,17 H 16 L 17,14 A 7,7 0 0 0 12,2", YellowArrow, YellowArrow, 1.2)),

		[BulbOff] = size => CreateLayered(size,
			new IconLayer("M 9,21 H 15 M 10,23 H 14", DarkGray, null, 1.5),
			new IconLayer("M 12,2 A 7,7 0 0 0 7,14 L 8,17 H 16 L 17,14 A 7,7 0 0 0 12,2", DarkGray, null, 1.4),
			new IconLayer("M 3,3 L 21,21", RedCross, null, 2.0)),

		[Snowflake] = size => CreateLayered(size,
			new IconLayer("M 12,2 V 22 M 2,12 H 22 M 5,5 L 19,19 M 5,19 L 19,5", CyanDim, null, 1.8)),

		[Sun] = size => CreateLayered(size,
			new IconLayer("M 12,7 A 5,5 0 1 0 12,17 A 5,5 0 1 0 12,7", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 12,2 V 5 M 12,19 V 22 M 2,12 H 5 M 19,12 H 22 M 5,5 L 7,7 M 17,17 L 19,19 M 5,19 L 7,17 M 17,7 L 19,5", YellowArrow, null, 1.8)),

		[Lock] = size => CreateLayered(size,
			new IconLayer("M 6,10 V 7 A 6,6 0 0 1 18,7 V 10", SilverMetal, null, 2.0),
			new IconLayer("M 4,10 H 20 V 22 H 4 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 12,14 V 18", DarkGray, null, 2.0)),

		[Unlock] = size => CreateLayered(size,
			new IconLayer("M 6,10 V 7 A 6,6 0 0 1 18,7 M 18,7 V 4", SilverMetal, null, 2.0),
			new IconLayer("M 4,10 H 20 V 22 H 4 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 12,14 V 18", DarkGray, null, 2.0)),

		[Checkmark] = size => CreateLayered(size,
			new IconLayer("M 4,12 L 9,17 L 20,6", GreenCheck, null, 2.6)),

		[AppIcon] = size => CreateLayered(size,
			new IconLayer("M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2", GreenCheck, GreenCheck, 1.0),
			new IconLayer("M 12,6 A 6,6 0 1 1 6,12 H 12 Z", WhiteLine, WhiteLine, 1.0)),

		[ColorWheel] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.2),
			new IconLayer("M 12,12 L 12,3 A 9,9 0 0 1 20,8 Z", RedCross, RedCross, 1.0),
			new IconLayer("M 12,12 L 20,8 A 9,9 0 0 1 20,16 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 12,12 L 20,16 A 9,9 0 0 1 12,21 Z", GreenCheck, GreenCheck, 1.0),
			new IconLayer("M 12,12 L 12,21 A 9,9 0 0 1 4,16 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 12,12 L 4,16 A 9,9 0 0 1 4,8 Z", BlueText, BlueText, 1.0),
			new IconLayer("M 12,12 L 4,8 A 9,9 0 0 1 12,3 Z", OrangeAccent, OrangeAccent, 1.0)),

		[Linetype] = size => CreateLayered(size,
			new IconLayer("M 2,6 H 22", WhiteLine, null, 1.8),
			new IconLayer("M 2,12 H 7 M 10,12 H 15 M 18,12 H 22", CyanDim, null, 1.8),
			new IconLayer("M 2,18 H 11 M 14,18 H 16 M 19,18 H 22", YellowArrow, null, 1.8)),

		[Lineweight] = size => CreateLayered(size,
			new IconLayer("M 2,6 H 22", WhiteLine, null, 1.0),
			new IconLayer("M 2,12 H 22", WhiteLine, null, 2.2),
			new IconLayer("M 2,18 H 22", WhiteLine, null, 3.8)),

		[CornerArrow] = size => CreateLayered(size,
			new IconLayer("M 7,7 L 17,17 M 11,17 H 17 V 11", CyanDim, null, 1.8)),

		[QuickSelect] = size => CreateLayered(size,
			new IconLayer("M 3,4 H 21 L 14,13 V 20 L 10,17 V 13 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 16,14 L 18,17 L 22,17 L 19,19 L 20,22 L 17,20 L 14,22 L 15,19 Z", YellowArrow, YellowArrow, 1.0)),

		[PickObject] = size => CreateLayered(size,
			new IconLayer("M 8,8 H 16 V 16 H 8 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 12,2 V 7 M 12,17 V 22 M 2,12 H 7 M 17,12 H 22", WhiteLine, null, 1.5),
			new IconLayer("M 17,5 H 23 M 20,2 V 8", YellowArrow, null, 2.0)),

		[PickAdd] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", GrayBody, null, 1.5),
			new IconLayer("M 7,12 H 17 M 12,7 V 17", CyanDim, null, 2.0)),

		[Deselect] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", GrayBody, null, 1.5),
			new IconLayer("M 4,4 L 20,20", RedCross, null, 2.4)),

		[SelectAll] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", BlueGrip, null, 1.8),
			new IconLayer("M 7,12 L 11,16 L 17,8", GreenCheck, null, 2.4)),

		[SetCurrent] = size => CreateLayered(size,
			new IconLayer("M 12,3 L 3,8 L 12,13 L 21,8 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 3,13 L 12,18 L 21,13", WhiteLine, null, 1.5),
			new IconLayer("M 6,10 L 10,14 L 18,6", GreenCheck, null, 2.4)),

		[PrevLayer] = size => CreateLayered(size,
			new IconLayer("M 12,3 L 3,8 L 12,13 L 21,8 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 3,13 L 12,18 L 21,13", WhiteLine, null, 1.5),
			new IconLayer("M 16,5 L 11,9 L 16,13 M 12,9 H 20", YellowArrow, null, 2.0)),

		[LayerOff] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", GrayBody, null, 1.5),
			new IconLayer("M 14,14 A 5,5 0 0 1 10,18", DarkGray, null, 1.5),
			new IconLayer("M 6,6 L 18,18", RedCross, null, 2.2)),

		[LayerOn] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 12,13 A 4,4 0 1 0 12,21 A 4,4 0 1 0 12,13", YellowArrow, YellowArrow, 1.0)),

		[LayerFreeze] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", CyanDim, null, 1.5),
			new IconLayer("M 12,13 V 21 M 8,17 H 16 M 9,14 L 15,20 M 9,20 L 15,14", CyanDim, null, 1.6)),

		[LayerThaw] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", YellowArrow, null, 1.5),
			new IconLayer("M 12,14 A 3,3 0 1 0 12,20 A 3,3 0 1 0 12,14", YellowArrow, YellowArrow, 1.0)),

		[LayerLock] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", WhiteLine, null, 1.5),
			new IconLayer("M 10,14 V 12 A 2,2 0 0 1 14,12 V 14 M 9,14 H 15 V 19 H 9 Z", YellowArrow, YellowArrow, 1.0)),

		[LayerUnlock] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", WhiteLine, null, 1.5),
			new IconLayer("M 10,14 V 12 A 2,2 0 0 1 14,12 M 9,14 H 15 V 19 H 9 Z", YellowArrow, YellowArrow, 1.0)),

		[MatchLayer] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", CyanDim, null, 1.5),
			new IconLayer("M 14,11 L 18,15 L 12,21 H 9 V 18 Z", YellowArrow, YellowArrow, 1.0)),

		[LayerDelete] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", WhiteLine, null, 1.5),
			new IconLayer("M 10,10 L 20,20 M 20,10 L 10,20", RedCross, null, 2.4)),

		[LayerIsolate] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 3,14 L 12,19 L 21,14", GrayBody, null, 1.5)),

		[LayerState] = size => CreateLayered(size,
			new IconLayer("M 12,4 L 3,9 L 12,14 L 21,9 Z", WhiteLine, null, 1.5),
			new IconLayer("M 4,12 H 20 M 4,16 H 20", CyanDim, null, 1.5)),

		[BlockInsert] = size => CreateLayered(size,
			new IconLayer("M 3,6 H 15 V 18 H 3 Z", WhiteLine, null, 1.8),
			new IconLayer("M 8,3 H 20 V 15 H 8 Z", BlueGrip, null, 1.8),
			new IconLayer("M 6,10 L 12,10 M 9,7 L 12,10 L 9,13", YellowArrow, null, 2.0)),

		[EditAttribute] = size => CreateLayered(size,
			new IconLayer("M 3,12 L 10,5 H 17 V 12 L 10,19 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 14,8 A 1.5,1.5 0 1 0 14,5 A 1.5,1.5 0 1 0 14,8", WhiteLine, WhiteLine, 1.0),
			new IconLayer("M 14,14 L 21,21 M 20,13 L 22,15 L 15,22 L 13,22 L 13,20 Z", YellowArrow, YellowArrow, 1.0)),

		[BlockCreate] = size => CreateLayered(size,
			new IconLayer("M 4,8 L 12,4 L 20,8 L 12,12 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 4,8 V 16 L 12,20 V 12 Z", WhiteLine, null, 1.5),
			new IconLayer("M 12,12 V 20 L 20,16 V 8 Z", SilverMetal, SilverMetal, 1.0),
			new IconLayer("M 18,3 L 19,5 L 21,6 L 19,7 L 18,9 L 17,7 L 15,6 L 17,5 Z", YellowArrow, YellowArrow, 1.0)),

		[BlockEditor] = size => CreateLayered(size,
			new IconLayer("M 3,7 L 11,3 L 19,7 L 11,11 Z", WhiteLine, null, 1.5),
			new IconLayer("M 3,7 V 15 L 11,19 V 11 Z", WhiteLine, null, 1.5),
			new IconLayer("M 11,11 V 19 L 19,15 V 7 Z", WhiteLine, null, 1.5),
			new IconLayer("M 14,12 L 21,19 M 19,10 L 22,13 L 16,19 L 13,19 L 13,16 Z", OrangeAccent, OrangeAccent, 1.0)),

		[Exit] = size => CreateLayered(size,
			new IconLayer("M 13,4 H 5 V 20 H 13", GrayBody, null, 1.8),
			new IconLayer("M 10,12 H 21 M 17,8 L 21,12 L 17,16", RedCross, null, 2.2)),

		[Pan] = size => CreateLayered(size,
			new IconLayer("M 12,3 V 21 M 3,12 H 21", WhiteLine, null, 1.6),
			new IconLayer("M 9,6 L 12,3 L 15,6 M 9,18 L 12,21 L 15,18 M 6,9 L 3,12 L 6,15 M 18,9 L 21,12 L 18,15", BlueGrip, null, 2.0)),

		[CommandLine] = size => CreateLayered(size,
			new IconLayer("M 3,5 H 21 V 19 H 3 Z", GrayBody, DarkGray, 1.4),
			new IconLayer("M 6,10 L 9,12 L 6,14", GreenCheck, null, 1.8),
			new IconLayer("M 11,15 H 17", WhiteLine, null, 1.8)),

		[TextWindow] = size => CreateLayered(size,
			new IconLayer("M 4,3 H 20 V 21 H 4 Z", GrayBody, DarkGray, 1.4),
			new IconLayer("M 7,8 H 17 M 7,12 H 17 M 7,16 H 13", WhiteLine, null, 1.6)),

		[Help] = size => CreateLayered(size,
			new IconLayer("M 12,2.5 A 9.5,9.5 0 1 1 11.99,2.5 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 9,9.5 A 3,3 0 1 1 13,12 Q 12,12.8 12,14.5 M 12,17 V 18", WhiteLine, null, 2.0)),

		[Gradient] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", CyanDim, BlueGrip, 1.6),
			new IconLayer("M 12,4 H 20 V 20 H 12 Z", YellowArrow, YellowArrow, 1.0),
			new IconLayer("M 8,4 H 12 V 20 H 8 Z", GreenCheck, GreenCheck, 1.0)),

		[TextStyle] = size => CreateLayered(size,
			new IconLayer("M 3,20 L 9,4 L 15,20 M 5.5,14 H 12.5", BlueText, null, 2.0),
			new IconLayer("M 15,11 H 21 M 18,11 V 20", OrangeAccent, null, 2.0)),

		[DimStyle] = size => CreateLayered(size,
			new IconLayer("M 3,5 V 15 M 21,5 V 15 M 3,10 H 21", CyanDim, null, 1.6),
			new IconLayer("M 6,8 L 3,10 L 6,12 Z M 18,8 L 21,10 L 18,12 Z", CyanDim, CyanDim, 1.0),
			new IconLayer("M 12,14 L 14,17 L 18,17 L 20,20 M 12,20 L 14,17", OrangeAccent, null, 1.6)),

		[Settings] = size => CreateLayered(size,
			new IconLayer("M 12,3 L 14,6 L 17.5,5 L 18,8.5 L 21,10.5 L 19,13.5 L 20,17 L 16.5,17.5 L 14.5,20.5 L 12,18.5 L 9.5,20.5 L 7.5,17.5 L 4,17 L 5,13.5 L 3,10.5 L 6,8.5 L 6.5,5 L 10,6 Z", GrayBody, SilverMetal, 1.2),
			new IconLayer("M 12,9 A 3,3 0 1 1 11.99,9 Z", DarkGray, DarkGray, 1.0)),

		[Units] = size => CreateLayered(size,
			new IconLayer("M 2,9 H 22 V 16 H 2 Z", GoldFolder, GoldFolderLight, 1.2),
			new IconLayer("M 5,9 V 12 M 8,9 V 13.5 M 11,9 V 12 M 14,9 V 13.5 M 17,9 V 12 M 20,9 V 13.5", DarkGray, null, 1.2)),

		[ResetSettings] = size => CreateLayered(size,
			new IconLayer("M 19,12 A 7,7 0 1 1 16.9,7 M 17,2.5 V 7.2 H 12.3", OrangeAccent, null, 2.2),
			new IconLayer("M 9.5,12 H 14.5", WhiteLine, null, 2.0)),

		[Regen] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", GrayBody, null, 1.4),
			new IconLayer("M 16,12 A 4,4 0 1 1 14.8,9.2 M 15,6.5 V 9.5 H 12", GreenCheck, null, 1.8)),

		[CloseDoc] = size => CreateLayered(size,
			new IconLayer("M 5,3 H 15 L 19,7 V 21 H 5 Z", GrayBody, WhiteLine, 1.2),
			new IconLayer("M 9,11 L 15,17 M 15,11 L 9,17", RedCross, null, 2.0)),
	};
}
