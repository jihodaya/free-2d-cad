using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>FILLET/CHAMFER: 선·호·원·폴리선, 자르기/다중/폴리선 옵션 (기존 CAD와 같은 동작).</summary>
public class FilletChamferTests
{
	private static (EditorDocument Doc, CommandContext Ctx) New(params Entity[] ents)
	{
		var doc = EditorDocument.New();
		doc.AddEntities(ents);
		var queue = new Queue<Entity>();
		var ctx = new CommandContext(doc);
		return (doc, ctx);
	}

	private static void Run(EditorCommand cmd, CommandContext ctx, Func<double, double, Entity?> picker, params string[] inputs)
	{
		ctx.Picker = picker;
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
	}

	private static Func<double, double, Entity?> Pick(EditorDocument doc) => (x, y) =>
	{
		Entity? best = null;
		double bd = 1.0;
		foreach (var e in doc.Document.Entities)
		{
			foreach (var sg in Free2DCad.Core.Geometry.Geom.SegmentsOf(e))
			{
				var q = sg.At(sg.Project(new XY(x, y)));
				double d = Math.Sqrt((q.X - x) * (q.X - x) + (q.Y - y) * (q.Y - y));
				if (d < bd) { bd = d; best = e; }
			}
		}
		return best;
	};

	[Fact]
	public void Fillet_TwoLines_TrimsInPlace_KeepsPickedSides()
	{
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(80, -20, 0), new XYZ(80, 100, 0)); // 교차: 고른 쪽만 남는다
		var (doc, ctx) = New(l1, l2);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "10", "p:20,0", "p:80,60");
		var arc = Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(10, arc.Radius, 6);
		Assert.Equal(70, arc.Center.X, 6);
		Assert.Equal(10, arc.Center.Y, 6);
		// 같은 객체가 제자리에서 바뀜 (핸들 유지)
		Assert.Contains(l1, doc.Document.Entities);
		Assert.Equal(70, Math.Max(l1.StartPoint.X, l1.EndPoint.X), 6);
		Assert.Equal(0, l1.StartPoint.X, 6);
		Assert.Equal(10, Math.Min(l2.StartPoint.Y, l2.EndPoint.Y), 6);
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(100, l1.EndPoint.X, 6);
		Assert.Equal(-20, l2.StartPoint.Y, 6);
	}

	[Fact]
	public void Fillet_Radius0_ExtendsToCorner()
	{
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(50, 0, 0));
		var l2 = new Line(new XYZ(80, 10, 0), new XYZ(80, 100, 0));
		var (doc, ctx) = New(l1, l2);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "0", "p:20,0", "p:80,60");
		Assert.Equal(80, l1.EndPoint.X, 6);
		Assert.Equal(0, l2.StartPoint.Y, 6);
		Assert.Empty(doc.Document.Entities.OfType<Arc>());
	}

	[Fact]
	public void Fillet_LineAndArc()
	{
		// 반지름 50 호 (중심 0,0, 0°~90°)와 y=60 수평선 → 두 객체에 접하는 반지름 5 호
		var arc = new Arc { Center = XYZ.Zero, Radius = 50, StartAngle = 0, EndAngle = Math.PI / 2 };
		var line = new Line(new XYZ(-100, 60, 0), new XYZ(100, 60, 0));
		var (doc, ctx) = New(arc, line);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "5", "p:35.36,35.36", "p:60,60");
		var f = doc.Document.Entities.OfType<Arc>().Single(a => !ReferenceEquals(a, arc));
		Assert.Equal(5, f.Radius, 6);
		var c = new XY(f.Center.X, f.Center.Y);
		Assert.Equal(55, Math.Sqrt(c.X * c.X + c.Y * c.Y), 5); // 호 바깥쪽에 접함
		Assert.Equal(55, c.Y, 5); // 선 아래쪽에 접함
	}

	[Fact]
	public void Fillet_CircleIsNotTrimmed()
	{
		var circle = new Circle { Center = XYZ.Zero, Radius = 20 };
		var line = new Line(new XYZ(-100, 30, 0), new XYZ(100, 30, 0));
		var (doc, ctx) = New(circle, line);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "5", "p:0,20", "p:40,30");
		Assert.Single(doc.Document.Entities.OfType<Circle>(), e => e is not Arc);
		Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(20, circle.Radius);
	}

	[Fact]
	public void Fillet_ParallelLines_Semicircle()
	{
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(0, 20, 0), new XYZ(120, 20, 0));
		var (doc, ctx) = New(l1, l2);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "3", "p:90,0", "p:90,20");
		var arc = Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(10, arc.Radius, 6);
		Assert.Equal(100, arc.Center.X, 6);
		Assert.Equal(100, l2.EndPoint.X, 6);
	}

	[Fact]
	public void Fillet_NoTrimMode_KeepsLines()
	{
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		var (doc, ctx) = New(l1, l2);
		Run(new FilletCommand(), ctx, Pick(doc), "T", "N", "R", "10", "p:50,0", "p:100,50");
		Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(100, l1.EndPoint.X, 6);
		Run(new FilletCommand(), ctx, Pick(doc), "T", "T", "");
	}

	[Fact]
	public void Fillet_SamePolyline_AdjacentSegments_AndPolylineOption()
	{
		var pl = new LwPolyline();
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(100, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(100, 100)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 100)));
		var (doc, ctx) = New(pl);
		Run(new FilletCommand(), ctx, Pick(doc), "R", "10", "p:50,0", "p:100,50");
		Assert.Equal(5, pl.Vertices.Count);
		Assert.Equal(90, pl.Vertices[1].Location.X, 6);
		Assert.Equal(Math.Tan(Math.PI / 8), pl.Vertices[1].Bulge, 6); // 반시계로 꺾임
		Assert.Equal(10, pl.Vertices[2].Location.Y, 6);
		doc.Undo();
		Assert.Equal(4, pl.Vertices.Count);

		// P 옵션: 닫힌 사각형 네 모서리
		pl.IsClosed = true;
		var cmd = new FilletCommand();
		Run(cmd, ctx, Pick(doc), "R", "5", "P", "p:50,0");
		Assert.Equal(8, pl.Vertices.Count);
		Assert.True(cmd.IsFinished);
	}

	[Fact]
	public void Fillet_Multiple_KeepsRunning()
	{
		var a = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var b = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		var c = new Line(new XYZ(200, 0, 0), new XYZ(300, 0, 0));
		var d = new Line(new XYZ(300, 0, 0), new XYZ(300, 100, 0));
		var (doc, ctx) = New(a, b, c, d);
		var cmd = new FilletCommand();
		Run(cmd, ctx, Pick(doc), "M", "R", "5", "p:50,0", "p:100,50", "p:250,0", "p:300,50");
		Assert.False(cmd.IsFinished);
		Assert.Equal(2, doc.Document.Entities.OfType<Arc>().Count());
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);
	}

	[Fact]
	public void Chamfer_DistanceAndAngle()
	{
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		var (doc, ctx) = New(l1, l2);
		Run(new ChamferCommand(), ctx, Pick(doc), "D", "10", "20", "p:50,0", "p:100,50");
		Assert.Equal(90, l1.EndPoint.X, 6);
		Assert.Equal(20, l2.StartPoint.Y, 6);
		Assert.Equal(3, doc.Document.Entities.OfType<Line>().Count());
		doc.Undo();

		// 각도 방법: 길이 10, 각도 45 → 두 거리 모두 10 (직각)
		Run(new ChamferCommand(), ctx, Pick(doc), "A", "10", "45", "p:50,0", "p:100,50");
		Assert.Equal(90, l1.EndPoint.X, 6);
		Assert.Equal(10, l2.StartPoint.Y, 6);
		doc.Undo();
		Run(new ChamferCommand(), ctx, Pick(doc), "E", "D", "");
	}

	[Fact]
	public void Chamfer_Polyline()
	{
		var pl = new LwPolyline { IsClosed = true };
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(100, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(100, 100)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 100)));
		var (doc, ctx) = New(pl);
		Run(new ChamferCommand(), ctx, Pick(doc), "D", "5", "5", "P", "p:50,0");
		Assert.Equal(8, pl.Vertices.Count);
		Assert.All(pl.Vertices, v => Assert.Equal(0, v.Bulge));
	}

	[Fact]
	public void Chamfer_ArcIsRejected()
	{
		var arc = new Arc { Center = XYZ.Zero, Radius = 50, StartAngle = 0, EndAngle = Math.PI / 2 };
		var line = new Line(new XYZ(-100, 60, 0), new XYZ(100, 60, 0));
		var (doc, ctx) = New(arc, line);
		var msgs = new List<string>();
		var c2 = new CommandContext(doc, print: msgs.Add);
		var cmd = new ChamferCommand();
		Run(cmd, c2, Pick(doc), "p:35.36,35.36");
		Assert.Contains(msgs, m => m.Contains("사용할 수 없습니다"));
	}
}
