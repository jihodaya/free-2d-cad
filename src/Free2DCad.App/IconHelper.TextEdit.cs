using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free2DCad.App;

/// <summary>문자 편집기 탭 아이콘 (기존 CAD "텍스트 편집" 탭 모양). 24x24 좌표계, 글자 아이콘은 글꼴로 그림.</summary>
public static partial class IconHelper
{
	public const string TeJustify = "TeJustify";
	public const string TeAlignLeft = "TeAlignLeft";
	public const string TeAlignCenter = "TeAlignCenter";
	public const string TeAlignRight = "TeAlignRight";
	public const string TeAlignJustify = "TeAlignJustify";
	public const string TeAlignDistribute = "TeAlignDistribute";
	public const string TeLineSpacing = "TeLineSpacing";
	public const string TeList = "TeList";
	public const string TeParagraph = "TeParagraph";
	public const string TeSymbol = "TeSymbol";
	public const string TeField = "TeField";
	public const string TeColumns = "TeColumns";
	public const string TeFind = "TeFind";
	public const string TeImport = "TeImport";
	public const string TeAutoCaps = "TeAutoCaps";
	public const string TeSpell = "TeSpell";
	public const string TeRuler = "TeRuler";
	public const string TeStack = "TeStack";
	public const string TeMore = "TeMore";
	public const string TeClearFormat = "TeClearFormat";
	public const string TeCombine = "TeCombine";
	public const string TeOblique = "TeOblique";
	public const string TeTracking = "TeTracking";
	public const string TeWidth = "TeWidth";
	public const string TeMask = "TeMask";

	private static Control Glyph(double size, string text, IBrush brush, FontWeight weight = FontWeight.Bold, FontStyle style = FontStyle.Normal, double scale = 0.62) => new TextBlock
	{
		Text = text,
		FontSize = size * scale,
		FontWeight = weight,
		FontStyle = style,
		Foreground = brush,
		Width = size,
		Height = size,
		TextAlignment = TextAlignment.Center,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center,
		Padding = new Thickness(0, size * 0.12, 0, 0),
	};

	private static string Lines(params (double X0, double X1, double Y)[] l) => string.Join(" ", l.Select(v => $"M {v.X0},{v.Y} H {v.X1}"));

	private static readonly Dictionary<string, Func<double, Control>> TextIcons = new(StringComparer.OrdinalIgnoreCase)
	{
		[TeJustify] = size => CreateLayered(size,
			new IconLayer("M 3,3 H 21 V 21 H 3 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer(Lines((5, 15, 7), (5, 19, 10), (5, 13, 13)), BlueText, null, 1.6),
			new IconLayer("M 3,3 H 8 V 8 H 3 Z", BlueGrip, BlueGrip, 1.0)),
		[TeAlignLeft] = size => CreateLayered(size, new IconLayer(Lines((3, 21, 5), (3, 15, 9), (3, 21, 13), (3, 13, 17)), WhiteLine, null, 1.8)),
		[TeAlignCenter] = size => CreateLayered(size, new IconLayer(Lines((3, 21, 5), (6, 18, 9), (3, 21, 13), (7, 17, 17)), WhiteLine, null, 1.8)),
		[TeAlignRight] = size => CreateLayered(size, new IconLayer(Lines((3, 21, 5), (9, 21, 9), (3, 21, 13), (11, 21, 17)), WhiteLine, null, 1.8)),
		[TeAlignJustify] = size => CreateLayered(size, new IconLayer(Lines((3, 21, 5), (3, 21, 9), (3, 21, 13), (3, 14, 17)), WhiteLine, null, 1.8)),
		[TeAlignDistribute] = size => CreateLayered(size, new IconLayer(Lines((3, 21, 5), (3, 21, 9), (3, 21, 13), (3, 21, 17)), WhiteLine, null, 1.8),
			new IconLayer("M 3,20 H 21 M 5,18 L 3,20 L 5,22 M 19,18 L 21,20 L 19,22", BlueText, null, 1.2)),
		[TeLineSpacing] = size => CreateLayered(size,
			new IconLayer(Lines((10, 21, 5), (10, 21, 10), (10, 21, 15), (10, 21, 20)), WhiteLine, null, 1.6),
			new IconLayer("M 5,3 V 21 M 2,6 L 5,3 L 8,6 M 2,18 L 5,21 L 8,18", BlueText, null, 1.6)),
		[TeList] = size => CreateLayered(size,
			new IconLayer(Lines((9, 21, 6), (9, 21, 12), (9, 21, 18)), WhiteLine, null, 1.8),
			new IconLayer("M 3,5 H 6 V 8 H 3 Z M 3,11 H 6 V 14 H 3 Z M 3,17 H 6 V 20 H 3 Z", BlueText, BlueText, 0.8)),
		[TeParagraph] = size => CreateLayered(size,
			new IconLayer("M 12,4 V 21 M 17,4 V 21 M 10,4 H 20 M 12,4 A 5,5 0 0 0 12,13", WhiteLine, null, 1.8)),
		[TeSymbol] = size => Glyph(size, "@", BlueText, FontWeight.Bold, scale: 0.8),
		[TeField] = size => CreateLayered(size,
			new IconLayer("M 3,6 H 21 V 18 H 3 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 7,9 V 15 M 11,9 V 15 M 15,9 V 15", BlueText, null, 1.6)),
		[TeColumns] = size => CreateLayered(size,
			new IconLayer(Lines((3, 10, 5), (3, 10, 9), (3, 10, 13), (3, 10, 17), (14, 21, 5), (14, 21, 9), (14, 21, 13)), BlueText, null, 1.8)),
		[TeFind] = size => CreateLayered(size,
			new IconLayer("M 2,15 A 4,4 0 1 0 10,15 A 4,4 0 1 0 2,15 Z M 14,15 A 4,4 0 1 0 22,15 A 4,4 0 1 0 14,15 Z", WhiteLine, GrayBody, 1.4),
			new IconLayer("M 4,13 L 6,4 H 9 L 10,13 M 14,13 L 15,4 H 18 L 20,13 M 10,12 H 14", WhiteLine, null, 1.6)),
		[TeImport] = size => CreateLayered(size,
			new IconLayer("M 5,3 H 15 L 19,7 V 21 H 5 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 12,9 V 18 M 8,14 L 12,18 L 16,14", BlueText, null, 2.0)),
		[TeAutoCaps] = size => Glyph(size, "AA", BlueText, scale: 0.5),
		[TeSpell] = size => CreateLayered(size,
			new IconLayer("M 3,12 L 9,18 L 21,5", GreenCheck, null, 3.0)),
		[TeRuler] = size => CreateLayered(size,
			new IconLayer("M 2,8 H 22 V 16 H 2 Z", SilverMetal, WhiteLine, 1.0),
			new IconLayer("M 5,8 V 12 M 8,8 V 11 M 11,8 V 12 M 14,8 V 11 M 17,8 V 12 M 20,8 V 11", DarkGray, null, 1.0)),
		[TeStack] = size => Glyph(size, "a⁄b", BlueText, FontWeight.Normal, scale: 0.55),
		[TeMore] = size => CreateLayered(size,
			new IconLayer("M 3,4 H 17 V 20 H 3 Z", GrayBody, WhiteLine, 1.0),
			new IconLayer("M 13,13 H 21 V 21 H 13 Z", BlueText, BlueText, 1.0)),
		[TeClearFormat] = size => CreateLayered(size,
			new IconLayer("M 4,18 L 12,5 L 20,18 M 7,13 H 17", BlueText, null, 2.0),
			new IconLayer("M 14,14 L 21,21 M 21,14 L 14,21", RedCross, null, 2.2)),
		[TeCombine] = size => CreateLayered(size,
			new IconLayer("M 9,4 V 14 M 13,4 V 14 M 7,4 H 15 M 9,4 A 3,3 0 0 0 9,10", WhiteLine, null, 1.6),
			new IconLayer("M 4,19 H 20 M 16,16 L 20,19 L 16,22", GreenCheck, null, 1.8)),
		[TeOblique] = size => Glyph(size, "o", BlueText, FontWeight.Bold, FontStyle.Italic, 0.8),
		[TeTracking] = size => Glyph(size, "a·b", BlueText, FontWeight.Bold, scale: 0.5),
		[TeWidth] = size => Glyph(size, "O", BlueText, FontWeight.Bold, scale: 0.75),
		[TeMask] = size => CreateLayered(size,
			new IconLayer("M 3,3 H 21 V 21 H 3 Z", WhiteLine, SilverMetal, 1.0),
			new IconLayer("M 7,18 L 12,6 L 17,18 M 9,14 H 15", DarkGray, null, 2.0)),
	};
}
