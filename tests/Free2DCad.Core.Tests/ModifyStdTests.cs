using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Geometry;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>MOVE·COPY·ROTATE·SCALE·MIRROR·OFFSET: 기존 CAD와 같은 문구·옵션.</summary>
public class ModifyStdTests
{
	private static (EditorDocument Doc, CommandContext Ctx, Line Line, List<string> Printed) New()
	{
		var doc = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		doc.AddEntities(line);
		var printed = new List<string>();
		return (doc, new CommandContext(doc, null, false, null, printed.Add), line, printed);
	}

	[Fact]
	public void Move_Prompts_SelectionMessages_AndDisplacement()
	{
		var (doc, ctx, line, printed) = New();
		var cmd = new MoveCommand();
		cmd.Start(ctx);
		Assert.Equal("Move 객체선택 :", cmd.Prompt);
		cmd.OnSelection(new[] { line });
		Assert.Equal("Move 객체선택 :", cmd.Prompt);
		Assert.Contains("세트 내의 객체: 1", printed);
		cmd.OnEnter();
		Assert.Contains("1 찾음", printed);
		Assert.Equal("기준점 또는 [거리(D)] <거리(D)>:", cmd.Prompt);
		cmd.OnText("D");
		Assert.StartsWith("방향을 지정하세요 <", cmd.Prompt);
		cmd.OnPoint(5, 5);
		Assert.Equal(5, line.StartPoint.Y, 6);
		Assert.Equal(15, line.EndPoint.X, 6);
		_ = doc;
	}

	[Fact]
	public void Copy_Multiple_Array_Undo()
	{
		var (doc, ctx, line, printed) = New();
		var cmd = new CopyCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { line }, false, null, printed.Add));
		Assert.Contains("현재 설정: 복사 모드 = 다중", printed);
		Assert.Equal("COPY 기준점 지정 or [거리(D)/모드(O)] <거리>:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("두번째 지점 또는 [배열(A)] <첫번째 지점을 방향으로 사용>:", cmd.Prompt);
		cmd.OnPoint(0, 10);
		Assert.Equal("두번째 지점 또는 [배열(A)/종료(E)/취소(U)] <종료(E)>:", cmd.Prompt);
		cmd.OnText("A");
		Assert.Equal("배열할 항목의 수를 입력하세요:", cmd.Prompt);
		cmd.OnText("3");
		Assert.Equal("두번째 지점 또는 [맞춤(F)]:", cmd.Prompt);
		cmd.OnText("F");
		cmd.OnPoint(0, -20); // 맞춤: 2개를 -10, -20에
		Assert.Equal(4, doc.Document.Entities.OfType<Line>().Count());
		Assert.Contains(doc.Document.Entities.OfType<Line>(), l => Math.Abs(l.StartPoint.Y + 10) < 1e-9);
		cmd.OnText("U");
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);
	}

	[Fact]
	public void Rotate_Reference_And_Copy()
	{
		var (doc, ctx, line, printed) = New();
		var cmd = new RotateCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { line }, false, null, printed.Add));
		Assert.Contains("Current positive angle in UCS: ANGDIR=counterclockwise  ANGBASE=0", printed);
		Assert.Equal("회전 점:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.StartsWith("회전 각도를 지정하거나 [복사(C)/기준각도(B)] <", cmd.Prompt);
		cmd.OnText("C");
		cmd.OnText("B");
		Assert.StartsWith("기준각도 <", cmd.Prompt);
		cmd.OnText("30");
		Assert.Equal("[점(P)]/<새 각도 지정>:", cmd.Prompt);
		cmd.OnText("120"); // 90° 회전한 복사본
		var lines = doc.Document.Entities.OfType<Line>().ToList();
		Assert.Equal(2, lines.Count);
		Assert.Equal(10, lines[1].EndPoint.Y, 6);
		Assert.Equal(10, line.EndPoint.X, 6); // 원본 그대로
	}

	[Fact]
	public void Scale_Reference()
	{
		var (doc, ctx, line, _) = New();
		var cmd = new ScaleCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { line }));
		Assert.Equal("기준점:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.StartsWith("축척 비율 [기준점(B)/복사(C)/참조(R)]/<", cmd.Prompt);
		cmd.OnText("R");
		cmd.OnText("10");
		Assert.Equal("[점(P)]/<새 길이 지정 (10.0000)>:", cmd.Prompt);
		cmd.OnText("25");
		Assert.Equal(25, line.EndPoint.X, 6);
	}

	[Fact]
	public void Mirror_Prompts()
	{
		var (doc, ctx, line, _) = New();
		var cmd = new MirrorCommand();
		cmd.Start(ctx);
		Assert.Equal("Mirror 객체선택:", cmd.Prompt);
		cmd.OnSelection(new[] { line });
		cmd.OnEnter();
		Assert.Equal("대칭선 시작:", cmd.Prompt);
		cmd.OnPoint(0, 5);
		Assert.Equal("대칭선 끝:", cmd.Prompt);
		cmd.OnPoint(10, 5);
		Assert.Equal("원래 객체를 삭제합니까? [예(Y)/아니오(N)] <N>", cmd.Prompt);
		cmd.OnText("Y");
		var l = doc.Document.Entities.OfType<Line>().Single();
		Assert.Equal(10, l.StartPoint.Y, 6);
	}

	[Fact]
	public void Offset_Polyline_JoinsCorners_BothSides_Through_Undo()
	{
		var doc = EditorDocument.New();
		var pl = new LwPolyline(new[] { new LwPolyline.Vertex(new XY(0, 0)), new LwPolyline.Vertex(new XY(10, 0)), new LwPolyline.Vertex(new XY(10, 10)) });
		doc.AddEntities(pl);
		var printed = new List<string>();
		var ctx = new CommandContext(doc, null, false, null, printed.Add) { Picker = (x, y) => pl };
		var cmd = new OffsetCommand();
		cmd.Start(ctx);
		Assert.StartsWith("현재 설정: 원본 지우기=", printed[0]);
		Assert.StartsWith("간격띄우기 거리 지정 또는 [통과점(T)/지우기(E)/도면층(L)] <", cmd.Prompt);
		cmd.OnText("2");
		Assert.Equal("객체선택 [Undo]:", cmd.Prompt);
		cmd.OnPoint(5, 0);
		Assert.Equal("평행 복사를 위한 측면 [양쪽(B)]:", cmd.Prompt);
		cmd.OnPoint(5, 1); // 안쪽(왼쪽)
		var off = doc.Document.Entities.OfType<LwPolyline>().Last();
		Assert.NotSame(pl, off);
		Assert.Equal(3, off.Vertices.Count);
		Assert.Equal(0, off.Vertices[0].Location.X, 6);
		Assert.Equal(2, off.Vertices[0].Location.Y, 6);
		Assert.Equal(8, off.Vertices[1].Location.X, 6); // 모서리는 만나는 점까지
		Assert.Equal(2, off.Vertices[1].Location.Y, 6);
		Assert.Equal(8, off.Vertices[2].Location.X, 6);
		Assert.Equal(10, off.Vertices[2].Location.Y, 6);

		cmd.OnPoint(5, 0);
		cmd.OnText("B");
		Assert.Equal(4, doc.Document.Entities.OfType<LwPolyline>().Count());
		cmd.OnText("U");
		Assert.Equal(2, doc.Document.Entities.OfType<LwPolyline>().Count());
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);

		// 통과점
		var cmd2 = new OffsetCommand();
		cmd2.Start(ctx);
		cmd2.OnText("T");
		cmd2.OnPoint(5, 0);
		Assert.Equal("통과점 지정:", cmd2.Prompt);
		cmd2.OnPoint(5, -3);
		var thr = doc.Document.Entities.OfType<LwPolyline>().Last();
		Assert.Equal(-3, thr.Vertices[0].Location.Y, 6);
		Assert.Equal(13, thr.Vertices[1].Location.X, 6);
	}

	[Fact]
	public void OffsetGeometry_ArcPolyline_And_Circle()
	{
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 5 };
		var inner = (Circle)OffsetGeometry.Offset(c, 2, new XY(1, 0))!;
		Assert.Equal(3, inner.Radius, 6);
		Assert.Null(OffsetGeometry.Offset(c, 6, new XY(1, 0)));
		// 직선 + 반원(볼록 1) 폴리선을 바깥으로
		var pl = new LwPolyline(new[] { new LwPolyline.Vertex(new XY(0, 0)), new LwPolyline.Vertex(new XY(10, 0)) { Bulge = 1 }, new LwPolyline.Vertex(new XY(10, 10)) });
		var o = (LwPolyline)OffsetGeometry.Offset(pl, 1, new XY(5, -1))!;
		Assert.Equal(-1, o.Vertices[0].Location.Y, 6);
		Assert.Equal(10, o.Vertices[1].Location.X, 6);
		Assert.Equal(-1, o.Vertices[1].Location.Y, 6);
		Assert.Equal(11, o.Vertices[2].Location.Y, 6);
		Assert.Equal(1, o.Vertices[1].Bulge, 6);
	}

	[Fact]
	public void Trim_EdgeExtend_And_Fence()
	{
		var doc = EditorDocument.New();
		var edge = new Line(new XYZ(0, 0, 0), new XYZ(0, 5, 0)); // 짧은 절단 모서리 (늘이면 x=0 전체)
		var t1 = new Line(new XYZ(-10, 10, 0), new XYZ(10, 10, 0));
		var t2 = new Line(new XYZ(-10, 20, 0), new XYZ(10, 20, 0));
		doc.AddEntities(edge, t1, t2);
		var ctx = new CommandContext(doc, new HashSet<Entity> { edge });
		var cmd = new TrimCommand();
		cmd.Start(ctx);
		Assert.Equal("[모서리모드(E)/울타리(F)/투영(P)/취소(U)]/< 자르기 할 객체 선택 또는 shift를 눌러 연장할 객체 선택>:", cmd.Prompt);
		cmd.OnText("E");
		Assert.Equal("[Extend/No extend] <No extend>:", cmd.Prompt);
		cmd.OnText("E");
		cmd.OnText("F");
		Assert.Equal("울타리의 첫번째 점:", cmd.Prompt);
		cmd.OnPoint(5, 5);
		Assert.Equal("다음 울타리 점:", cmd.Prompt);
		cmd.OnPoint(5, 25);
		cmd.OnEnter(); // 울타리가 지나간 오른쪽 반을 모두 잘라 냄
		var lines = doc.Document.Entities.OfType<Line>().Where(l => l != edge).ToList();
		Assert.Equal(2, lines.Count);
		Assert.All(lines, l => Assert.True(Math.Max(l.StartPoint.X, l.EndPoint.X) <= 1e-9));
		cmd.OnText("E");
		cmd.OnText("N"); // 다음 시험을 위해 되돌림
		cmd.OnEnter();
	}

	[Fact]
	public void Break_Polyline_And_Split()
	{
		var doc = EditorDocument.New();
		var pl = new LwPolyline(new[] { new LwPolyline.Vertex(new XY(0, 0)), new LwPolyline.Vertex(new XY(10, 0)), new LwPolyline.Vertex(new XY(10, 10)) });
		doc.AddEntities(pl);
		var printed = new List<string>();
		var ctx = new CommandContext(doc, null, false, null, printed.Add) { Picker = (x, y) => doc.Document.Entities.OfType<LwPolyline>().FirstOrDefault() };
		var cmd = new BreakCommand();
		cmd.Start(ctx);
		cmd.OnPoint(5, 0);
		Assert.Equal("두번째 끊기점 지정 or [첫번째점(F)/분할(S)]:", cmd.Prompt);
		cmd.OnPoint(10, 5);
		var parts = doc.Document.Entities.OfType<Entity>().ToList();
		Assert.Equal(2, parts.Count);
		Assert.Contains(parts, e => e is LwPolyline p0 && Math.Abs(p0.Vertices[^1].Location.X - 5) < 1e-9);

		// 분할(S)
		var cmd2 = new BreakCommand();
		cmd2.Start(ctx);
		cmd2.OnPoint(2, 0);
		cmd2.OnText("S");
		Assert.True(cmd2.IsFinished);
		Assert.Equal(3, doc.Document.Entities.Count());
	}

	[Fact]
	public void RadialDims_DefinitionPoints_Text_Sample()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 20 };
		doc.AddEntities(c);
		var printed = new List<string>();
		var ctx = new CommandContext(doc, null, false, null, printed.Add) { Picker = (x, y) => c };
		var r = new DimRadiusCommand();
		r.Start(ctx);
		r.OnPoint(20, 0);
		Assert.Equal("[각도(A)/Mtext/텍스트(T)]/<치수선의위치(L)>:", r.Prompt);
		r.OnPoint(40, 30);
		var d = new DimDiameterCommand();
		d.Start(ctx);
		d.OnPoint(-20, 0);
		d.OnPoint(-40, -30);
		Assert.Contains(printed, t => t.StartsWith("치수 텍스트 = ∅") && !t.EndsWith("°"));
		var rad = doc.Document.Entities.OfType<DimensionRadius>().Single();
		Assert.Equal(0, rad.DefinitionPoint.X, 6); // 10 = 중심
		Assert.Equal(20, rad.Measurement, 6);
		var dia = doc.Document.Entities.OfType<DimensionDiameter>().Single();
		Assert.Equal(40, dia.Measurement, 6);
		Assert.StartsWith("∅", dia.GetMeasurementText());
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir != null) doc.Save(Path.Combine(dir, "radial_dims.dwg"));
	}
}
