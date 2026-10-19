namespace Free2DCad.Core.Commands;

/// <summary>
/// 리본·메뉴에서 쓰는 "방식이 정해진" 명령 (예: 원 2점, 호 시작-중심-끝, 맨 앞으로).
/// 명령줄에서 옵션을 고르는 것과 같은 명령을 그 방식으로 바로 시작한다.
/// </summary>
public static class CommandPresets
{
	private static readonly Dictionary<string, Func<EditorCommand>> Presets = new(StringComparer.OrdinalIgnoreCase)
	{
		["CIRCLE_D"] = () => new CircleCommand { StartMode = CircleCommand.CircleMode.CenterDiameter },
		["CIRCLE_2P"] = () => new CircleCommand { StartMode = CircleCommand.CircleMode.TwoPoint },
		["CIRCLE_3P"] = () => new CircleCommand { StartMode = CircleCommand.CircleMode.ThreePoint },
		["CIRCLE_TTR"] = () => new CircleCommand { StartMode = CircleCommand.CircleMode.TanTanRadius },
		["CIRCLE_TTT"] = () => new CircleCommand { StartMode = CircleCommand.CircleMode.TanTanTan },
		["ARC_SCE"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartCenterEnd },
		["ARC_SCA"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartCenterAngle },
		["ARC_SCL"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartCenterLength },
		["ARC_SEA"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartEndAngle },
		["ARC_SED"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartEndDirection },
		["ARC_SER"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartEndRadius },
		["ARC_SEC"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.StartEndCenter },
		["ARC_CSE"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.CenterStartEnd },
		["ARC_CSA"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.CenterStartAngle },
		["ARC_CSL"] = () => new ArcCommand { StartMode = ArcCommand.ArcMode.CenterStartLength },
		["POLYGON_I"] = () => new PolygonCommand { PresetInscribed = true },
		["POLYGON_C"] = () => new PolygonCommand { PresetInscribed = false },
		["POLYGON_E"] = () => new PolygonCommand { PresetEdge = true },
		["ELLIPSE_R"] = () => new EllipseCommand { PresetRotation = true },
		["ELLIPSE_C"] = () => new EllipseCommand { PresetCenter = true },
		["ELLIPSE_CR"] = () => new EllipseCommand { PresetCenter = true, PresetRotation = true },
		["ELLIPSE_A"] = () => new EllipseCommand { PresetArc = true },
		["ELLIPSE_AR"] = () => new EllipseCommand { PresetArc = true, PresetRotation = true },
		["ELLIPSE_AC"] = () => new EllipseCommand { PresetArc = true, PresetCenter = true },
		["SPLINE_CV"] = () => new SplineCommand { ControlVertices = true },
		["DRAWORDER_F"] = () => new DrawOrderCommand { Preset = "F" },
		["DRAWORDER_B"] = () => new DrawOrderCommand { Preset = "B" },
		["DRAWORDER_A"] = () => new DrawOrderCommand { Preset = "A" },
		["DRAWORDER_U"] = () => new DrawOrderCommand { Preset = "U" },
		["TEXTTOFRONT_T"] = () => new TextToFrontCommand { Preset = "T" },
		["TEXTTOFRONT_D"] = () => new TextToFrontCommand { Preset = "D" },
		["TEXTTOFRONT_L"] = () => new TextToFrontCommand { Preset = "L" },
		["TEXTTOFRONT_A"] = () => new TextToFrontCommand { Preset = "A" },
		["ATTDISP_N"] = () => new AttDispCommand { Preset = "N" },
		["ATTDISP_ON"] = () => new AttDispCommand { Preset = "ON" },
		["ATTDISP_OFF"] = () => new AttDispCommand { Preset = "OFF" },
		["ZOOM_W"] = () => new ZoomCommand { AutoInput = new[] { "W" } },
		["POINT_M"] = () => new PointCommand { AutoInput = new[] { "M" } },
		["ZOOM_E"] = () => new ZoomCommand { AutoInput = new[] { "E" } },
		["ZOOM_P"] = () => new ZoomCommand { AutoInput = new[] { "P" } },
		["ZOOM_A"] = () => new ZoomCommand { AutoInput = new[] { "A" } },
		["ZOOM_C"] = () => new ZoomCommand { AutoInput = new[] { "C" } },
		["ZOOM_O"] = () => new ZoomCommand { AutoInput = new[] { "O" } },
		["ZOOM_2X"] = () => new ZoomCommand { AutoInput = new[] { "2X" } },
		["ZOOM_HALF"] = () => new ZoomCommand { AutoInput = new[] { ".5X" } },
		["XLINE_H"] = () => new XLineCommand(false) { AutoInput = new[] { "H" } },
		["XLINE_V"] = () => new XLineCommand(false) { AutoInput = new[] { "V" } },
		["XLINE_A"] = () => new XLineCommand(false) { AutoInput = new[] { "A" } },
		["XLINE_B"] = () => new XLineCommand(false) { AutoInput = new[] { "B" } },
		["DIMEDIT_N"] = () => new DimEditCommand { Preset = "N" },
		["DIMEDIT_R"] = () => new DimEditCommand { Preset = "R" },
		["DIMEDIT_H"] = () => new DimEditCommand { Preset = "H" },
		["DIMEDIT_O"] = () => new DimEditCommand { Preset = "O" },
	};

	public static IEnumerable<string> Keys => Presets.Keys;

	public static EditorCommand? Create(string key) => Presets.TryGetValue(key.Trim(), out var f) ? f() : null;
}
