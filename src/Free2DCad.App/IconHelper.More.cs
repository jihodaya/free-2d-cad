using Avalonia.Controls;

namespace Free2DCad.App;

/// <summary>추가 명령 아이콘 (그리기·수정·도면층·주석·블록·그룹·유틸리티 확장). 24x24 좌표계.</summary>
public static partial class IconHelper
{
	public const string Ray = "Ray";
	public const string XLine = "XLine";
	public const string CircleDiameter = "CircleDiameter";
	public const string Circle2P = "Circle2P";
	public const string Circle3P = "Circle3P";
	public const string CircleTtr = "CircleTtr";
	public const string CircleTtt = "CircleTtt";
	public const string Arc3P = "Arc3P";
	public const string ArcSce = "ArcSce";
	public const string ArcSca = "ArcSca";
	public const string ArcScl = "ArcScl";
	public const string ArcSea = "ArcSea";
	public const string ArcSed = "ArcSed";
	public const string ArcSer = "ArcSer";
	public const string ArcSec = "ArcSec";
	public const string ArcCse = "ArcCse";
	public const string ArcCsa = "ArcCsa";
	public const string ArcCsl = "ArcCsl";
	public const string Polygon = "Polygon";
	public const string PolygonCircum = "PolygonCircum";
	public const string PolygonEdge = "PolygonEdge";
	public const string Ellipse = "Ellipse";
	public const string EllipseCenter = "EllipseCenter";
	public const string EllipseArc = "EllipseArc";
	public const string Spline = "Spline";
	public const string SplineCv = "SplineCv";
	public const string PointIcon = "PointIcon";
	public const string Donut = "Donut";
	public const string RevCloud = "RevCloud";
	public const string Plane = "Plane";
	public const string Boundary = "Boundary";
	public const string Divide = "Divide";
	public const string Measure = "Measure";
	public const string Clone = "Clone";
	public const string ArrayPolar = "ArrayPolar";
	public const string ArrayPath = "ArrayPath";
	public const string SetByLayer = "SetByLayer";
	public const string Lengthen = "Lengthen";
	public const string EditPline = "EditPline";
	public const string Align = "Align";
	public const string BreakAtPoint = "BreakAtPoint";
	public const string Reverse = "Reverse";
	public const string DrawFront = "DrawFront";
	public const string DrawBack = "DrawBack";
	public const string DrawAbove = "DrawAbove";
	public const string DrawUnder = "DrawUnder";
	public const string HatchBack = "HatchBack";
	public const string TextFront = "TextFront";
	public const string LayerMakeCurrent = "LayerMakeCurrent";
	public const string LayerChangeCurrent = "LayerChangeCurrent";
	public const string LayerPrevious = "LayerPrevious";
	public const string DimArc = "DimArc";
	public const string DimOrdinate = "DimOrdinate";
	public const string DimBaseline = "DimBaseline";
	public const string DimContinue = "DimContinue";
	public const string DimCenter = "DimCenter";
	public const string Leader = "Leader";
	public const string Txt2Mtxt = "Txt2Mtxt";
	public const string TextEdit = "TextEdit";
	public const string AttDef = "AttDef";
	public const string AttSync = "AttSync";
	public const string AttDisp = "AttDisp";
	public const string BasePoint = "BasePoint";
	public const string Group = "Group";
	public const string QGroup = "QGroup";
	public const string Ungroup = "Ungroup";
	public const string GroupSelect = "GroupSelect";
	public const string PointStyle = "PointStyle";

	// 공통 조각
	private const string Grip = "h 3 v 3 h -3 z";

	private static string G(double x, double y) => $"M {x - 1.5},{y - 1.5} {Grip}";

	private static readonly Dictionary<string, Func<double, Control>> MoreIcons = new(StringComparer.OrdinalIgnoreCase)
	{
		[Ray] = size => CreateLayered(size,
			new IconLayer("M 4,20 L 22,2", WhiteLine, null, 1.8),
			new IconLayer(G(4, 20), BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 17,2 H 22 V 7", YellowArrow, null, 1.6)),

		[XLine] = size => CreateLayered(size,
			new IconLayer("M 1,21 L 23,3", WhiteLine, null, 1.8),
			new IconLayer(G(12, 12), BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 18,3 H 23 V 8 M 1,16 V 21 H 6", YellowArrow, null, 1.4)),

		[CircleDiameter] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.8),
			new IconLayer("M 5.6,18.4 L 18.4,5.6", CyanDim, null, 1.4),
			new IconLayer(G(12, 12), BlueGrip, BlueGrip, 1.0)),

		[Circle2P] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.8),
			new IconLayer(G(3, 12) + " " + G(21, 12), BlueGrip, BlueGrip, 1.0)),

		[Circle3P] = size => CreateLayered(size,
			new IconLayer("M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3", WhiteLine, null, 1.8),
			new IconLayer(G(12, 3) + " " + G(4.2, 16.5) + " " + G(19.8, 16.5), BlueGrip, BlueGrip, 1.0)),

		[CircleTtr] = size => CreateLayered(size,
			new IconLayer("M 2,21 H 22 M 2,21 V 2", CyanDim, null, 1.6),
			new IconLayer("M 10,4 A 7,7 0 1 0 10,18 A 7,7 0 1 0 10,4", WhiteLine, null, 1.8),
			new IconLayer("M 10,11 L 15,6", YellowArrow, null, 1.4)),

		[CircleTtt] = size => CreateLayered(size,
			new IconLayer("M 2,21 H 22 L 12,2 Z", CyanDim, null, 1.5),
			new IconLayer("M 12,9 A 5.5,5.5 0 1 0 12,20 A 5.5,5.5 0 1 0 12,9", WhiteLine, null, 1.8)),

		[Arc3P] = size => CreateLayered(size,
			new IconLayer("M 3,18 A 10,10 0 0 1 21,18", WhiteLine, null, 1.8),
			new IconLayer(G(3, 18) + " " + G(12, 8) + " " + G(21, 18), BlueGrip, BlueGrip, 1.0)),

		[ArcSce] = size => ArcIcon(size, "M 12,18 L 3,18 M 12,18 L 21,18", G(3, 18) + " " + G(12, 18) + " " + G(21, 18)),
		[ArcSca] = size => ArcIcon(size, "M 12,18 L 3,18 M 15,18 A 3,3 0 0 0 9,18", G(3, 18) + " " + G(12, 18)),
		[ArcScl] = size => ArcIcon(size, "M 3,18 L 21,18", G(3, 18) + " " + G(12, 18)),
		[ArcSea] = size => ArcIcon(size, "M 3,18 L 21,18 M 9,18 A 6,6 0 0 0 7,13", G(3, 18) + " " + G(21, 18)),
		[ArcSed] = size => ArcIcon(size, "M 3,18 L 3,10", G(3, 18) + " " + G(21, 18)),
		[ArcSer] = size => ArcIcon(size, "M 12,18 L 17,11", G(3, 18) + " " + G(21, 18)),
		[ArcSec] = size => ArcIcon(size, "M 12,18 L 21,18", G(3, 18) + " " + G(21, 18) + " " + G(12, 18)),
		[ArcCse] = size => ArcIcon(size, "M 12,18 L 3,18 M 12,18 L 21,18", G(12, 18) + " " + G(3, 18)),
		[ArcCsa] = size => ArcIcon(size, "M 12,18 L 3,18 M 15,18 A 3,3 0 0 0 9,18", G(12, 18)),
		[ArcCsl] = size => ArcIcon(size, "M 3,18 L 21,18", G(12, 18) + " " + G(21, 18)),

		[Polygon] = size => CreateLayered(size,
			new IconLayer("M 12,2 L 21,7 V 17 L 12,22 L 3,17 V 7 Z", WhiteLine, null, 1.8),
			new IconLayer("M 12,12 L 12,2", CyanDim, null, 1.2),
			new IconLayer(G(12, 12), BlueGrip, BlueGrip, 1.0)),

		[PolygonCircum] = size => CreateLayered(size,
			new IconLayer("M 12,2 L 21,7 V 17 L 12,22 L 3,17 V 7 Z", WhiteLine, null, 1.8),
			new IconLayer("M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4", CyanDim, null, 1.0),
			new IconLayer(G(12, 12), BlueGrip, BlueGrip, 1.0)),

		[PolygonEdge] = size => CreateLayered(size,
			new IconLayer("M 7,21 H 17 L 21,12 L 12,3 L 3,12 Z", WhiteLine, null, 1.8),
			new IconLayer(G(7, 21) + " " + G(17, 21), BlueGrip, BlueGrip, 1.0)),

		[Ellipse] = size => CreateLayered(size,
			new IconLayer("M 2,12 A 10,6 0 1 0 22,12 A 10,6 0 1 0 2,12", WhiteLine, null, 1.8),
			new IconLayer(G(2, 12) + " " + G(22, 12) + " " + G(12, 6), BlueGrip, BlueGrip, 1.0)),

		[EllipseCenter] = size => CreateLayered(size,
			new IconLayer("M 2,12 A 10,6 0 1 0 22,12 A 10,6 0 1 0 2,12", WhiteLine, null, 1.8),
			new IconLayer(G(12, 12) + " " + G(22, 12), BlueGrip, BlueGrip, 1.0)),

		[EllipseArc] = size => CreateLayered(size,
			new IconLayer("M 2,12 A 10,6 0 0 1 22,12", WhiteLine, null, 1.8),
			new IconLayer("M 22,12 A 10,6 0 0 1 2,12", DarkGray, null, 1.0),
			new IconLayer(G(2, 12) + " " + G(22, 12), BlueGrip, BlueGrip, 1.0)),

		[Spline] = size => CreateLayered(size,
			new IconLayer("M 2,18 C 6,2 10,2 12,12 S 18,22 22,6", WhiteLine, null, 1.8),
			new IconLayer(G(2, 18) + " " + G(12, 12) + " " + G(22, 6), BlueGrip, BlueGrip, 1.0)),

		[SplineCv] = size => CreateLayered(size,
			new IconLayer("M 2,20 L 7,4 L 17,20 L 22,4", DarkGray, null, 1.0),
			new IconLayer("M 2,20 C 7,4 17,20 22,4", WhiteLine, null, 1.8),
			new IconLayer(G(7, 4) + " " + G(17, 20), BlueGrip, BlueGrip, 1.0)),

		[PointIcon] = size => CreateLayered(size,
			new IconLayer("M 12,4 V 20 M 4,12 H 20", WhiteLine, null, 1.4),
			new IconLayer("M 10,10 H 14 V 14 H 10 Z", BlueGrip, BlueGrip, 1.0)),

		[Donut] = size => CreateLayered(size,
			new IconLayer("M 12,2 A 10,10 0 1 0 12,22 A 10,10 0 1 0 12,2 Z M 12,7 A 5,5 0 1 1 12,17 A 5,5 0 1 1 12,7 Z", WhiteLine, WhiteLine, 0.5)),

		[RevCloud] = size => CreateLayered(size,
			new IconLayer("M 4,8 A 3,3 0 0 1 9,5 A 3,3 0 0 1 15,5 A 3,3 0 0 1 20,8 A 3,3 0 0 1 20,14 A 3,3 0 0 1 16,19 A 3,3 0 0 1 9,19 A 3,3 0 0 1 4,14 A 3,3 0 0 1 4,8 Z", RedCross, null, 1.6)),

		[Plane] = size => CreateLayered(size,
			new IconLayer("M 3,18 L 8,5 H 21 L 16,18 Z", GrayBody, GrayBody, 1.0),
			new IconLayer("M 3,18 L 8,5 H 21 L 16,18 Z", WhiteLine, null, 1.4)),

		[Boundary] = size => CreateLayered(size,
			new IconLayer("M 3,5 H 21 V 19 H 3 Z", DarkGray, null, 1.2),
			new IconLayer("M 5,7 H 19 V 17 H 5 Z", CyanDim, null, 1.8),
			new IconLayer("M 11,11 L 13,13 M 13,11 L 11,13", YellowArrow, null, 1.4)),

		[Divide] = size => CreateLayered(size,
			new IconLayer("M 2,12 H 22", WhiteLine, null, 1.6),
			new IconLayer("M 7,9 V 15 M 12,9 V 15 M 17,9 V 15", YellowArrow, null, 1.6),
			new IconLayer(G(2, 12) + " " + G(22, 12), BlueGrip, BlueGrip, 1.0)),

		[Measure] = size => CreateLayered(size,
			new IconLayer("M 2,15 H 22", WhiteLine, null, 1.6),
			new IconLayer("M 2,15 V 9 M 9,15 V 9 M 16,15 V 9", YellowArrow, null, 1.6),
			new IconLayer("M 2,6 H 9 M 4,4 L 2,6 L 4,8 M 7,4 L 9,6 L 7,8", CyanDim, null, 1.2)),

		[Clone] = size => CreateLayered(size,
			new IconLayer("M 3,13 A 5,5 0 1 0 13,13 A 5,5 0 1 0 3,13", WhiteLine, null, 1.6),
			new IconLayer("M 11,9 A 5,5 0 1 0 21,9 A 5,5 0 1 0 11,9", BlueGrip, null, 1.6),
			new IconLayer("M 18,16 V 22 M 15,19 H 21", YellowArrow, null, 2.0)),

		[ArrayPolar] = size => CreateLayered(size,
			new IconLayer("M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4", DarkGray, null, 1.0),
			new IconLayer("M 10,2 H 14 V 6 H 10 Z M 18,10 H 22 V 14 H 18 Z M 10,18 H 14 V 22 H 10 Z M 2,10 H 6 V 14 H 2 Z", WhiteLine, null, 1.4),
			new IconLayer(G(12, 12), BlueGrip, BlueGrip, 1.0)),

		[ArrayPath] = size => CreateLayered(size,
			new IconLayer("M 2,20 C 8,20 8,4 22,4", DarkGray, null, 1.0),
			new IconLayer("M 1,18 H 5 V 22 H 1 Z M 8,11 H 12 V 15 H 8 Z M 18,2 H 22 V 6 H 18 Z", WhiteLine, null, 1.4)),

		[SetByLayer] = size => CreateLayered(size,
			new IconLayer("M 12,13 L 3,17 L 12,21 L 21,17 Z", WhiteLine, null, 1.4),
			new IconLayer("M 12,7 L 3,11 L 12,15 L 21,11 Z", BlueGrip, null, 1.4),
			new IconLayer("M 12,2 V 9 M 9,6 L 12,9 L 15,6", YellowArrow, null, 1.8)),

		[Lengthen] = size => CreateLayered(size,
			new IconLayer("M 2,14 H 14", WhiteLine, null, 1.8),
			new IconLayer("M 14,14 H 22 M 19,11 L 22,14 L 19,17", YellowArrow, null, 1.8),
			new IconLayer("M 2,8 H 22", CyanDim, null, 1.0)),

		[EditPline] = size => CreateLayered(size,
			new IconLayer("M 3,19 L 9,7 L 15,16 L 21,5", WhiteLine, null, 1.8),
			new IconLayer("M 14,20 L 21,13 L 23,15 L 16,22 H 14 Z", YellowArrow, YellowArrow, 1.0)),

		[Align] = size => CreateLayered(size,
			new IconLayer("M 3,21 H 21", CyanDim, null, 1.6),
			new IconLayer("M 6,14 L 12,6 L 18,14 Z", WhiteLine, null, 1.6),
			new IconLayer("M 12,15 V 20 M 10,18 L 12,20 L 14,18", YellowArrow, null, 1.6)),

		[BreakAtPoint] = size => CreateLayered(size,
			new IconLayer("M 2,12 H 11 M 13,12 H 22", WhiteLine, null, 1.8),
			new IconLayer(G(12, 12), YellowArrow, YellowArrow, 1.0)),

		[Reverse] = size => CreateLayered(size,
			new IconLayer("M 3,8 H 21 M 17,4 L 21,8 L 17,12", WhiteLine, null, 1.6),
			new IconLayer("M 21,16 H 3 M 7,12 L 3,16 L 7,20", YellowArrow, null, 1.6)),

		[DrawFront] = size => OrderIcon(size, front: true),
		[DrawBack] = size => OrderIcon(size, front: false),
		[DrawAbove] = size => CreateLayered(size,
			new IconLayer("M 3,10 H 15 V 21 H 3 Z", GrayBody, GrayBody, 1.0),
			new IconLayer("M 9,4 H 21 V 15 H 9 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 15,1 V 3", YellowArrow, null, 1.0)),
		[DrawUnder] = size => CreateLayered(size,
			new IconLayer("M 9,4 H 21 V 15 H 9 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 3,10 H 15 V 21 H 3 Z", GrayBody, GrayBody, 1.0)),

		[HatchBack] = size => CreateLayered(size,
			new IconLayer("M 3,3 H 15 V 15 H 3 Z M 3,7 L 7,3 M 3,11 L 11,3 M 3,15 L 15,3", CyanDim, null, 1.0),
			new IconLayer("M 9,9 H 21 V 21 H 9 Z", WhiteLine, GrayBody, 1.2)),

		[TextFront] = size => CreateLayered(size,
			new IconLayer("M 3,3 H 15 V 15 H 3 Z", GrayBody, GrayBody, 1.0),
			new IconLayer("M 8,21 L 14,8 L 20,21 M 10,17 H 18", BlueText, null, 2.2)),

		[LayerMakeCurrent] = size => CreateLayered(size,
			new IconLayer("M 12,12 L 2,17 L 12,22 L 22,17 Z", WhiteLine, null, 1.4),
			new IconLayer("M 12,7 L 2,12 L 12,17 L 22,12 Z", GreenCheck, GreenCheck, 1.0),
			new IconLayer("M 15,2 L 19,6 L 13,8 Z", YellowArrow, YellowArrow, 1.0)),

		[LayerChangeCurrent] = size => CreateLayered(size,
			new IconLayer("M 12,12 L 2,17 L 12,22 L 22,17 Z", GreenCheck, null, 1.4),
			new IconLayer("M 4,4 H 10 V 10 H 4 Z", WhiteLine, null, 1.4),
			new IconLayer("M 12,7 H 19 M 16,4 L 19,7 L 16,10", YellowArrow, null, 1.6)),

		[LayerPrevious] = size => CreateLayered(size,
			new IconLayer("M 12,12 L 2,17 L 12,22 L 22,17 Z", WhiteLine, null, 1.4),
			new IconLayer("M 8,3 L 4,7 L 8,11 M 4,7 H 14 A 4,4 0 0 1 18,11", BlueText, null, 1.8)),

		[DimArc] = size => CreateLayered(size,
			new IconLayer("M 4,20 A 10,10 0 0 1 20,20", WhiteLine, null, 1.6),
			new IconLayer("M 2,14 A 14,14 0 0 1 22,14", CyanDim, null, 1.4),
			new IconLayer("M 9,6 A 3,3 0 0 1 15,6", YellowArrow, null, 1.4)),

		[DimOrdinate] = size => CreateLayered(size,
			new IconLayer("M 4,20 V 4 M 4,20 H 20", DarkGray, null, 1.2),
			new IconLayer("M 12,16 V 6 H 14", CyanDim, null, 1.4),
			new IconLayer(G(12, 16), BlueGrip, BlueGrip, 1.0)),

		[DimBaseline] = size => CreateLayered(size,
			new IconLayer("M 3,21 V 4 M 12,21 V 12 M 21,21 V 6", WhiteLine, null, 1.2),
			new IconLayer("M 3,14 H 12 M 3,8 H 21", CyanDim, null, 1.5)),

		[DimContinue] = size => CreateLayered(size,
			new IconLayer("M 3,21 V 8 M 12,21 V 8 M 21,21 V 8", WhiteLine, null, 1.2),
			new IconLayer("M 3,11 H 21 M 12,9 V 13", CyanDim, null, 1.5)),

		[DimCenter] = size => CreateLayered(size,
			new IconLayer("M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4", WhiteLine, null, 1.4),
			new IconLayer("M 9,12 H 15 M 12,9 V 15 M 1,12 H 6 M 18,12 H 23 M 12,1 V 6 M 12,18 V 23", CyanDim, null, 1.4)),

		[Leader] = size => CreateLayered(size,
			new IconLayer("M 3,21 L 11,9 H 15", WhiteLine, null, 1.6),
			new IconLayer("M 3,21 L 4,16 L 7,18 Z", WhiteLine, WhiteLine, 1.0),
			new IconLayer("M 16,6 H 22 M 16,10 H 22 M 16,14 H 20", BlueText, null, 1.8)),

		[Txt2Mtxt] = size => CreateLayered(size,
			new IconLayer("M 2,4 H 9 M 2,8 H 9 M 2,12 H 9", WhiteLine, null, 1.6),
			new IconLayer("M 10,8 H 14 M 12,6 L 14,8 L 12,10", YellowArrow, null, 1.4),
			new IconLayer("M 15,3 H 22 V 14 H 15 Z M 17,6 H 20 M 17,9 H 20", BlueText, null, 1.4)),

		[TextEdit] = size => CreateLayered(size,
			new IconLayer("M 3,18 L 9,4 L 15,18 M 5,13 H 13", BlueText, null, 2.0),
			new IconLayer("M 14,21 L 21,14 L 23,16 L 16,23 H 14 Z", YellowArrow, YellowArrow, 1.0)),

		[AttDef] = size => CreateLayered(size,
			new IconLayer("M 3,5 H 21 V 19 H 3 Z", WhiteLine, null, 1.4),
			new IconLayer("M 6,15 L 9,8 L 12,15 M 7,13 H 11", BlueText, null, 1.6),
			new IconLayer("M 14,10 H 19 M 14,14 H 18", CyanDim, null, 1.4)),

		[AttSync] = size => CreateLayered(size,
			new IconLayer("M 3,5 H 13 V 13 H 3 Z", WhiteLine, null, 1.4),
			new IconLayer("M 16,8 A 6,6 0 0 1 11,20 M 13,18 L 11,20 L 13,22", YellowArrow, null, 1.6),
			new IconLayer("M 5,10 H 11", BlueText, null, 1.4)),

		[AttDisp] = size => CreateLayered(size,
			new IconLayer("M 2,12 Q 12,3 22,12 Q 12,21 2,12 Z", WhiteLine, null, 1.4),
			new IconLayer("M 12,9 A 3,3 0 1 0 12,15 A 3,3 0 1 0 12,9", BlueText, BlueText, 1.0)),

		[BasePoint] = size => CreateLayered(size,
			new IconLayer("M 4,4 H 20 V 20 H 4 Z", DarkGray, null, 1.0),
			new IconLayer("M 4,20 H 14 M 4,20 V 10", WhiteLine, null, 1.4),
			new IconLayer(G(4, 20), YellowArrow, YellowArrow, 1.0)),

		[Group] = size => CreateLayered(size,
			new IconLayer("M 2,2 H 22 V 22 H 2 Z", CyanDim, null, 1.0),
			new IconLayer("M 5,15 L 10,5 L 15,15 Z M 13,13 H 20 V 19 H 13 Z", WhiteLine, null, 1.4)),

		[QGroup] = size => CreateLayered(size,
			new IconLayer("M 2,2 H 22 V 22 H 2 Z", CyanDim, null, 1.0),
			new IconLayer("M 5,15 L 10,5 L 15,15 Z", WhiteLine, null, 1.4),
			new IconLayer("M 17,9 L 14,15 H 18 L 15,21", YellowArrow, null, 1.6)),

		[Ungroup] = size => CreateLayered(size,
			new IconLayer("M 2,2 H 10 M 14,2 H 22 V 10 M 22,14 V 22 H 14 M 10,22 H 2 V 14 M 2,10 V 2", CyanDim, null, 1.0),
			new IconLayer("M 5,15 L 10,5 L 15,15 Z M 13,13 H 20 V 19 H 13 Z", WhiteLine, null, 1.4)),

		[GroupSelect] = size => CreateLayered(size,
			new IconLayer("M 2,2 H 18 V 18 H 2 Z", CyanDim, null, 1.0),
			new IconLayer("M 12,10 L 21,14 L 17,15.5 L 20,21 L 18,22 L 15,16.5 L 12,19 Z", WhiteLine, WhiteLine, 1.0)),

		[PointStyle] = size => CreateLayered(size,
			new IconLayer("M 3,3 L 9,9 M 9,3 L 3,9 M 18,2 V 10 M 14,6 H 22", WhiteLine, null, 1.4),
			new IconLayer("M 6,14 A 3,3 0 1 0 6,20 A 3,3 0 1 0 6,14 M 15,14 H 21 V 20 H 15 Z", CyanDim, null, 1.4)),
	};

	private static Control ArcIcon(double size, string helper, string grips) => CreateLayered(size,
		new IconLayer(helper, CyanDim, null, 1.0),
		new IconLayer("M 3,18 A 9,9 0 0 1 21,18", WhiteLine, null, 1.8),
		new IconLayer(grips, BlueGrip, BlueGrip, 1.0));

	private static Control OrderIcon(double size, bool front) => front
		? CreateLayered(size,
			new IconLayer("M 3,3 H 15 V 15 H 3 Z", GrayBody, GrayBody, 1.0),
			new IconLayer("M 9,9 H 21 V 21 H 9 Z", BlueGrip, BlueGrip, 1.0))
		: CreateLayered(size,
			new IconLayer("M 9,9 H 21 V 21 H 9 Z", BlueGrip, BlueGrip, 1.0),
			new IconLayer("M 3,3 H 15 V 15 H 3 Z", GrayBody, GrayBody, 1.0));
}
