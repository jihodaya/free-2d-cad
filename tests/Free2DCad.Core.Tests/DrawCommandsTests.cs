using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>그리기 명령 옵션 (원·호·폴리선·다각형·타원·스플라인·등분·도넛·구름).</summary>
public class DrawCommandsTests
{
	private static (EditorDocument Doc, CommandContext Ctx) New(params Entity[] existing)
	{
		var doc = EditorDocument.New();
		if (existing.Length > 0) doc.AddEntities(existing);
		return (doc, new CommandContext(doc));
	}

	private static T Run<T>(EditorCommand cmd, CommandContext ctx, params string[] inputs) where T : Entity
	{
		cmd.Start(ctx);
		foreach (string s in inputs)
		{
			if (cmd.IsFinished) break;
			if (s == "") cmd.OnEnter();
			else if (s.StartsWith("p:"))
			{
				var xy = s[2..].Split(',');
				cmd.OnPoint(double.Parse(xy[0]), double.Parse(xy[1]));
			}
			else cmd.OnText(s);
		}
		return ctx.Doc.Document.Entities.OfType<T>().Last();
	}

	[Fact]
	public void Circle_3P()
	{
		var (_, ctx) = New();
		var c = Run<Circle>(new CircleCommand(), ctx, "3P", "p:10,0", "p:0,10", "p:-10,0");
		Assert.Equal(0, c.Center.X, 6);
		Assert.Equal(10, c.Radius, 6);
	}

	[Fact]
	public void Circle_2P_And_Diameter()
	{
		var (_, ctx) = New();
		Assert.Equal(5, Run<Circle>(new CircleCommand(), ctx, "2P", "p:0,0", "p:10,0").Radius, 6);
		Assert.Equal(4, Run<Circle>(new CircleCommand(), ctx, "p:0,0", "D", "8").Radius, 6);
	}

	[Fact]
	public void Circle_TTR_InCorner()
	{
		var (_, ctx) = New(new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0)), new Line(new XYZ(0, 0, 0), new XYZ(0, 100, 0)));
		var c = Run<Circle>(new CircleCommand(), ctx, "T", "p:20,0", "p:0,20", "5");
		Assert.Equal(5, c.Center.X, 6);
		Assert.Equal(5, c.Center.Y, 6);
	}

	[Fact]
	public void Circle_TTT_Incircle()
	{
		// 3-4-5 직각삼각형의 내접원 반지름 = 1, 중심 (1,1)
		var (_, ctx) = New(new Line(new XYZ(0, 0, 0), new XYZ(4, 0, 0)), new Line(new XYZ(0, 0, 0), new XYZ(0, 3, 0)), new Line(new XYZ(4, 0, 0), new XYZ(0, 3, 0)));
		var cmd = new CircleCommand { StartMode = CircleCommand.CircleMode.TanTanTan };
		var c = Run<Circle>(cmd, ctx, "p:1,0", "p:0,1", "p:2,1.5");
		Assert.Equal(1, c.Radius, 6);
		Assert.Equal(1, c.Center.X, 6);
	}

	[Fact]
	public void Arc_StartCenterAngle()
	{
		var (_, ctx) = New();
		var a = Run<Arc>(new ArcCommand(), ctx, "p:10,0", "C", "p:0,0", "A", "90");
		Assert.Equal(10, a.Radius, 6);
		Assert.Equal(0, a.StartAngle, 6);
		Assert.Equal(Math.PI / 2, a.EndAngle, 6);
	}

	[Fact]
	public void Arc_StartEndRadius()
	{
		var (_, ctx) = New();
		var a = Run<Arc>(new ArcCommand(), ctx, "p:10,0", "E", "p:-10,0", "R", "10");
		Assert.Equal(0, a.Center.X, 6);
		Assert.Equal(0, a.Center.Y, 6);
	}

	[Fact]
	public void Arc_ThreePoint_Clockwise()
	{
		var (_, ctx) = New();
		var a = Run<Arc>(new ArcCommand(), ctx, "p:-10,0", "p:0,10", "p:10,0");
		Assert.Equal(0, a.StartAngle, 6);
		Assert.Equal(Math.PI, a.EndAngle, 6);
	}

	[Fact]
	public void Pline_ArcAndWidth()
	{
		var (_, ctx) = New();
		var pl = Run<LwPolyline>(new PlineCommand(), ctx, "p:0,0", "W", "2", "", "p:10,0", "A", "p:10,10", "");
		Assert.Equal(3, pl.Vertices.Count);
		Assert.Equal(2, pl.ConstantWidth, 6);
		// 직선 뒤 접하는 반원 → bulge = 1
		Assert.Equal(1, pl.Vertices[1].Bulge, 6);
	}

	[Fact]
	public void Pline_StdOptions()
	{
		var (_, ctx) = New();
		var cmd = new PlineCommand();
		cmd.Start(ctx);
		Assert.Equal("마지막 점 사용하려면 ENTER/[따라가기]/<폴리라인의 시작>:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("[호그리기(A)/거리(D)/따라가기(F)/반폭(H)/너비(W)]/<다음 점>:", cmd.Prompt);
		cmd.OnText("W");
		Assert.Equal($"시작 폭 <{ctx.Doc.Document.Header.PolylineWidthDefault:0.0000}>:", cmd.Prompt); // PLINEWID (기존 CAD처럼 새 폴리선의 기본 폭)
		cmd.OnText("2");
		Assert.Equal("끝 폭 <2.0000>:", cmd.Prompt);
		cmd.OnText("0"); // 2 → 0 으로 줄어드는 조각
		cmd.OnText("D");
		Assert.Equal("세그먼트 거리:", cmd.Prompt);
		cmd.OnText("10");
		Assert.Equal("세그먼트의 각도:", cmd.Prompt);
		cmd.OnText("0");
		Assert.Equal("[호그리기(A)/거리(D)/따라가기(F)/반폭(H)/너비(W)/취소(U)]/<다음 점>:", cmd.Prompt);
		cmd.OnText("F");
		cmd.OnText("5"); // 앞 조각 방향(0°)으로 5
		Assert.Equal("[호그리기(A)/닫기(C)/거리(D)/따라가기(F)/반폭(H)/너비(W)/취소(U)]/<다음 점>:", cmd.Prompt);
		cmd.OnText("A");
		Assert.Equal("[각도(A)/중심(CE)/닫기(CL)/방향(D)/반폭(H)/선그리기(L)/반지름(R)/두번째점(S)/너비(W)/취소(U)]/<호의 끝>:", cmd.Prompt);
		cmd.OnText("A");
		cmd.OnText("180");
		Assert.Equal("[중심(C)/반지름(R)]/<호의끝(E)>:", cmd.Prompt);
		cmd.OnText("R");
		cmd.OnText("5");
		Assert.Equal("코드의 방향 <90>:", cmd.Prompt);
		cmd.OnEnter(); // (15,0)에서 반지름 5 반원 → (15,10)
		cmd.OnText("CE");
		Assert.Equal("중심점:", cmd.Prompt);
		cmd.OnPoint(15, 5);
		cmd.OnText("L");
		Assert.Equal("현의 길이:", cmd.Prompt);
		cmd.OnText("10"); // 지름만큼 → 다시 (15,0)
		cmd.OnText("S");
		Assert.Equal("두번째 점:", cmd.Prompt);
		cmd.OnPoint(20, 5);
		cmd.OnPoint(15, 10);
		cmd.OnEnter();
		var pl = ctx.Doc.Document.Entities.OfType<LwPolyline>().Last();
		var v = pl.Vertices;
		Assert.Equal(6, v.Count);
		Assert.Equal((2.0, 0.0), (v[0].StartWidth, v[0].EndWidth));
		Assert.Equal(15, v[2].Location.X, 6);
		Assert.Equal(1, v[2].Bulge, 6); // 반원(180°) bulge = 1
		Assert.Equal(10, v[3].Location.Y, 6);
		Assert.Equal(0, v[4].Location.Y, 6);
		Assert.Equal(1, v[3].Bulge, 6);
		Assert.Equal(10, v[5].Location.Y, 6);
		Assert.Equal(1, v[4].Bulge, 6); // (15,0) → (20,5) → (15,10) 반시계 반원
	}

	[Fact]
	public void Arc_StdPrompts_AngleRadiusDirection_AndEnterTangent()
	{
		var (doc, ctx) = New();
		var cmd = new ArcCommand();
		cmd.Start(ctx);
		Assert.Equal("사용할 최종점/[중심(C)/따라가기(F)]/<호의 시작>:", cmd.Prompt);
		cmd.OnPoint(10, 0);
		Assert.Equal("[각도(A)/중심(C)/방향(D)/끝(E)/반지름(R)]/<두번째 점>:", cmd.Prompt);
		cmd.OnText("A");
		Assert.Equal("포함된 각도:", cmd.Prompt);
		cmd.OnText("180");
		Assert.Equal("[중심(C)]/<호의 끝점>:", cmd.Prompt);
		cmd.OnPoint(-10, 0); // (10,0)→(-10,0) 반시계 반원 = 위쪽
		var a = doc.Document.Entities.OfType<Arc>().Last();
		Assert.Equal(10, a.Radius, 6);
		Assert.Equal(0, a.StartAngle, 6);
		Assert.Equal(Math.PI, a.EndAngle, 6);

		var cmd2 = new ArcCommand();
		cmd2.Start(ctx);
		cmd2.OnPoint(0, 0);
		cmd2.OnText("R");
		Assert.Equal("반지름:", cmd2.Prompt);
		cmd2.OnText("5");
		Assert.Equal("[포함된각도(I)]/<호의 끝>:", cmd2.Prompt);
		cmd2.OnPoint(10, 0);
		Assert.Equal(5, doc.Document.Entities.OfType<Arc>().Last().Radius, 6);

		// Enter: 마지막 선 끝에서 접하는 호
		doc.AddEntities(new Line(new XYZ(0, 50, 0), new XYZ(10, 50, 0)));
		ctx.LastPoint = (10, 50);
		var cmd3 = new ArcCommand();
		cmd3.Start(ctx);
		cmd3.OnEnter();
		Assert.Equal("끝점:", cmd3.Prompt);
		cmd3.OnPoint(10, 60); // 오른쪽으로 가다 위로 반원
		var t = doc.Document.Entities.OfType<Arc>().Last();
		Assert.Equal(5, t.Radius, 6);
		Assert.Equal(10, t.Center.X, 6);
		Assert.Equal(55, t.Center.Y, 6);
	}

	[Fact]
	public void XLine_Offset_Point_Spline_StdPrompts()
	{
		var (doc, ctx) = New(new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)));
		ctx.Picker = (x, y) => doc.Document.Entities.OfType<Line>().First();
		var xl = new XLineCommand(false);
		xl.Start(ctx);
		Assert.Equal("무한선:  [이등분(B)/수평(H)/수직(V)/각도(A)/간격띄우기(O)]/<선 따라 점>:", xl.Prompt);
		xl.OnText("O");
		xl.OnText("5");
		Assert.Equal("평행 무한선의 객체 선택:", xl.Prompt);
		xl.OnPoint(5, 0);
		Assert.Equal("측면:", xl.Prompt);
		xl.OnPoint(5, 3);
		xl.OnEnter();
		var made = doc.Document.Entities.OfType<XLine>().Single();
		Assert.Equal(5, made.FirstPoint.Y, 6);

		var pt = new PointCommand();
		pt.Start(ctx);
		Assert.Equal("[설정(S)/다중(M)]/<점위치(L)>:", pt.Prompt);
		pt.OnPoint(1, 1);
		Assert.True(pt.IsFinished); // 기존 CAD: 점 하나

		var sp = new SplineCommand();
		sp.Start(ctx);
		Assert.Equal("SPLINE 첫번째 점 지정 또는 [순서(M)/매듭(K)/객체(E)]:", sp.Prompt);
		sp.OnPoint(0, 0);
		Assert.Equal("SPLINE 다음 점 지정 또는 [공차(F)]:", sp.Prompt);
		sp.OnPoint(5, 5);
		Assert.Equal("[닫기(C)/적합_허용오차(F)/취소(U)]/<다음점(N)>:", sp.Prompt);
		sp.OnPoint(10, 0);
		sp.OnEnter();
		Assert.Equal("시작 접점 선택:", sp.Prompt);
		sp.OnEnter();
		Assert.Equal("끝 접점의 접선 입력:", sp.Prompt);
		sp.OnEnter();
		Assert.Single(doc.Document.Entities.OfType<Spline>());
	}

	[Fact]
	public void Polygon_Inscribed_And_Circumscribed()
	{
		var (_, ctx) = New();
		var ins = Run<LwPolyline>(new PolygonCommand(), ctx, "6", "p:0,0", "I", "10");
		Assert.Equal(6, ins.Vertices.Count);
		Assert.Equal(10, Math.Sqrt(ins.Vertices[0].Location.X * ins.Vertices[0].Location.X + ins.Vertices[0].Location.Y * ins.Vertices[0].Location.Y), 6);
		var cir = Run<LwPolyline>(new PolygonCommand(), ctx, "4", "p:0,0", "C", "10");
		Assert.Equal(10, Math.Abs(cir.Vertices[0].Location.X), 6);
		var edge = Run<LwPolyline>(new PolygonCommand(), ctx, "4", "E", "p:0,0", "p:10,0");
		Assert.Equal(10, edge.Vertices[2].Location.Y, 6);
	}

	[Fact]
	public void Ellipse_AxisEnd_And_Arc()
	{
		var (_, ctx) = New();
		var e = Run<Ellipse>(new EllipseCommand(), ctx, "p:-10,0", "p:10,0", "p:0,5");
		Assert.Equal(0.5, e.RadiusRatio, 6);
		Assert.Equal(10, e.MajorAxisEndPoint.X, 6);
		var arc = Run<Ellipse>(new EllipseCommand(), ctx, "A", "p:-10,0", "p:10,0", "5", "0", "90");
		Assert.Equal(Math.PI / 2, arc.EndParameter, 6);
	}

	[Fact]
	public void Spline_PassesThroughFitPoints()
	{
		var pts = new List<XY> { new(0, 0), new(10, 5), new(20, -5), new(30, 0) };
		var (ctrl, knots, deg) = SplineCommand.Interpolate(pts);
		var sp = new Spline { Degree = deg };
		foreach (var c in ctrl) sp.ControlPoints.Add(new XYZ(c.X, c.Y, 0));
		foreach (var k in knots) sp.Knots.Add(k);
		var poly = sp.PolygonalVertexes(400);
		foreach (var p in pts)
		{
			double best = poly.Min(v => Math.Sqrt((v.X - p.X) * (v.X - p.X) + (v.Y - p.Y) * (v.Y - p.Y)));
			Assert.True(best < 0.2, $"fit point {p} off by {best}");
		}
	}

	[Fact]
	public void Divide_Line_Into5()
	{
		var (doc, ctx) = New(new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0)));
		var cmd = new DivideMeasureCommand(false);
		Run<Line>(cmd, ctx, "p:50,0", "5");
		var points = doc.Document.Entities.OfType<Point>().Select(p => p.Location.X).OrderBy(x => x).ToList();
		Assert.Equal(new[] { 20.0, 40, 60, 80 }, points.Select(x => Math.Round(x, 6)));
	}

	[Fact]
	public void Measure_Line()
	{
		var (doc, ctx) = New(new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0)));
		Run<Line>(new DivideMeasureCommand(true), ctx, "p:10,0", "30");
		Assert.Equal(3, doc.Document.Entities.OfType<Point>().Count());
	}

	[Fact]
	public void Donut_IsWideClosedPolyline()
	{
		var (_, ctx) = New();
		var pl = Run<LwPolyline>(new DonutCommand(), ctx, "4", "10", "p:0,0", "");
		Assert.True(pl.IsClosed);
		Assert.Equal(3, pl.ConstantWidth, 6);
	}

	[Fact]
	public void RevCloud_Rectangle()
	{
		var (_, ctx) = New();
		var pl = Run<LwPolyline>(new RevCloudCommand(), ctx, "A", "10", "p:0,0", "p:100,50");
		Assert.True(pl.IsClosed);
		Assert.Equal(30, pl.Vertices.Count);
	}

	[Fact]
	public void XLine_Horizontal()
	{
		var (_, ctx) = New();
		var xl = Run<XLine>(new XLineCommand(false), ctx, "H", "p:0,5", "");
		Assert.Equal(1, xl.Direction.X, 6);
		Assert.Equal(5, xl.FirstPoint.Y, 6);
	}
}

public class CommandPresetTests
{
	[Fact]
	public void EveryPreset_StartsAndAcceptsInput()
	{
		foreach (string key in CommandPresets.Keys)
		{
			var doc = EditorDocument.New();
			var line = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
			doc.AddEntities(line, new Line(new XYZ(0, 0, 0), new XYZ(0, 100, 0)), new Line(new XYZ(100, 0, 0), new XYZ(0, 100, 0)));
			var cmd = CommandPresets.Create(key);
			Assert.NotNull(cmd);
			var ctx = new CommandContext(doc, new HashSet<Entity> { line }) { Picker = (_, _) => line };
			cmd!.Start(ctx);
			foreach (var (x, y) in new[] { (10.0, 1.0), (50.0, 30.0), (80.0, 5.0), (20.0, 60.0) })
			{
				if (cmd.IsFinished) break;
				_ = cmd.Preview(x, y).ToList();
				cmd.OnPoint(x, y);
			}
			if (!cmd.IsFinished) cmd.OnText("45");
			if (!cmd.IsFinished) cmd.OnEnter();
		}
	}
}
