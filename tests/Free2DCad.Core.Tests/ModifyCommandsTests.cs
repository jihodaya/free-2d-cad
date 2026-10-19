using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>수정 명령 (길이 조정, 점에서 끊기, 반대로, 정렬, 폴리선 편집, 그리기 순서, 배열).</summary>
public class ModifyCommandsTests
{
	private static Line L(double x1, double y1, double x2, double y2) => new(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0));

	private static void Feed(EditorCommand cmd, params string[] inputs)
	{
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

	[Fact]
	public void Lengthen_Delta_And_Total()
	{
		var line = L(0, 0, 10, 0);
		var r = Assert.IsType<Line>(LengthenCommand.Lengthen(line, new XY(9, 0), t => t + 5));
		Assert.Equal(15, r.EndPoint.X, 6);
		var r2 = Assert.IsType<Line>(LengthenCommand.Lengthen(line, new XY(1, 0), _ => 20));
		Assert.Equal(-10, r2.StartPoint.X, 6);
		var arc = new Arc { Center = XYZ.Zero, Radius = 10, StartAngle = 0, EndAngle = Math.PI / 2 };
		var ra = Assert.IsType<Arc>(LengthenCommand.Lengthen(arc, new XY(0, 10), t => t * 2));
		Assert.Equal(Math.PI, ra.EndAngle, 6);
	}

	[Fact]
	public void Lengthen_Command_UsesPickedEnd()
	{
		var doc = EditorDocument.New();
		var line = L(0, 0, 10, 0);
		doc.AddEntities(line);
		var cmd = new LengthenCommand();
		cmd.Start(new CommandContext(doc) { Picker = (_, _) => doc.Document.Entities.OfType<Line>().First() });
		Feed(cmd, "DE", "5", "p:9,0", "");
		Assert.Equal(15, doc.Document.Entities.OfType<Line>().Single().EndPoint.X, 6);
	}

	[Fact]
	public void BreakAtPoint_SplitsLine()
	{
		var parts = BreakAtPointCommand.Split(L(0, 0, 10, 0), new XY(4, 0))!;
		Assert.Equal(2, parts.Count);
		Assert.Equal(4, ((Line)parts[0]).EndPoint.X, 6);
		Assert.Equal(4, ((Line)parts[1]).StartPoint.X, 6);
		Assert.Null(BreakAtPointCommand.Split(new Circle { Radius = 5 }, new XY(5, 0)));
	}

	[Fact]
	public void Reverse_Polyline_KeepsShape_AndRoundTrips()
	{
		var pl = new LwPolyline();
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)) { Bulge = 0.5 });
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)));
		ReverseCommand.Reverse(pl);
		Assert.Equal(10, pl.Vertices[0].Location.Y, 6);
		Assert.Equal(-0.5, pl.Vertices[1].Bulge, 6); // (10,0)→(0,0) 조각
		ReverseCommand.Reverse(pl);
		Assert.Equal(0.5, pl.Vertices[0].Bulge, 6);
		Assert.Equal(0, pl.Vertices[0].Location.X, 6);
	}

	[Fact]
	public void Reverse_ClosedPolyline_RoundTrips()
	{
		var pl = new LwPolyline { IsClosed = true };
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)) { Bulge = 0.2 });
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)) { Bulge = 0.3 });
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)) { Bulge = 0.4 });
		ReverseCommand.Reverse(pl);
		Assert.Equal(0, pl.Vertices[0].Location.X, 6);
		Assert.Equal(-0.4, pl.Vertices[0].Bulge, 6); // 옛 닫는 조각 (10,10)→(0,0) 을 거꾸로
		ReverseCommand.Reverse(pl);
		Assert.Equal(new[] { 0.2, 0.3, 0.4 }, pl.Vertices.Select(v => Math.Round(v.Bulge, 6)));
	}

	[Fact]
	public void Align_TwoPairs_RotatesAndScales()
	{
		var doc = EditorDocument.New();
		var line = L(0, 0, 10, 0);
		doc.AddEntities(line);
		var cmd = new AlignCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { line }));
		Feed(cmd, "p:0,0", "p:5,5", "p:10,0", "p:5,25", "", "Y");
		Assert.Equal(5, line.EndPoint.X, 6);
		Assert.Equal(25, line.EndPoint.Y, 6);
	}

	[Fact]
	public void Pedit_Close_And_Join()
	{
		var doc = EditorDocument.New();
		var pl = new LwPolyline();
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)));
		var l1 = L(10, 0, 10, 10);
		var l2 = L(0, 10, 10, 10);
		doc.AddEntities(pl, l1, l2);
		var cmd = new PeditCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { pl }));
		cmd.OnText("J");
		cmd.OnSelection(new Entity[] { l2, l1 });
		cmd.OnEnter();
		Assert.Equal(4, pl.Vertices.Count);
		Assert.DoesNotContain(l1, doc.Document.Entities);
		cmd.OnText("C");
		Assert.True(pl.IsClosed);
		Assert.False(cmd.IsFinished, cmd.Prompt);
		cmd.OnText("U");
		Assert.False(pl.IsClosed, cmd.Prompt + " finished=" + cmd.IsFinished);
	}

	[Fact]
	public void DrawOrder_Back_And_Undo()
	{
		var doc = EditorDocument.New();
		var a = L(0, 0, 1, 0);
		var b = L(0, 1, 1, 1);
		var c = L(0, 2, 1, 2);
		doc.AddEntities(a, b, c);
		DrawOrderCommand.Apply(doc, new[] { c }, DrawOrderCommand.Placement.Back, null);
		Assert.Equal(new Entity[] { c, a, b }, doc.Document.Entities.ToArray());
		DrawOrderCommand.Apply(doc, new[] { c }, DrawOrderCommand.Placement.Above, b);
		Assert.Equal(new Entity[] { a, b, c }, doc.Document.Entities.ToArray());
		doc.Undo();
		Assert.Equal(new Entity[] { c, a, b }, doc.Document.Entities.ToArray());
	}

	[Fact]
	public void Erase_Undo_RestoresDrawOrder()
	{
		var doc = EditorDocument.New();
		var a = L(0, 0, 1, 0);
		var b = L(0, 1, 1, 1);
		var c = L(0, 2, 1, 2);
		doc.AddEntities(a, b, c);
		doc.Erase(b);
		doc.AddEntities(L(5, 5, 6, 6));
		doc.Undo();
		doc.Undo();
		Assert.Equal(new Entity[] { a, b, c }, doc.Document.Entities.ToArray());
	}

	[Fact]
	public void Array_Rect_Polar_Path()
	{
		var doc = EditorDocument.New();
		var item = new Circle { Center = new XYZ(10, 0, 0), Radius = 1 };
		doc.AddEntities(item);
		var sel = new HashSet<Entity> { item };

		var rect = new ArrayCommand(ArrayCommand.ArrayKind.Rect);
		rect.Start(new CommandContext(doc, sel));
		Feed(rect, "COU", "3", "2", "S", "5", "5", "X");
		Assert.Equal(6, doc.Document.Entities.OfType<Circle>().Count());

		var polar = new ArrayCommand(ArrayCommand.ArrayKind.Polar);
		polar.Start(new CommandContext(doc, sel));
		Feed(polar, "p:0,0", "I", "4", "F", "360", "ROT", "Y", "X");
		var circles = doc.Document.Entities.OfType<Circle>().ToList();
		Assert.Equal(9, circles.Count);
		Assert.Contains(circles, c => Math.Abs(c.Center.X) < 1e-6 && Math.Abs(c.Center.Y - 10) < 1e-6);

		var path = L(0, 50, 100, 50);
		doc.AddEntities(path);
		var pa = new ArrayCommand(ArrayCommand.ArrayKind.Path);
		pa.Start(new CommandContext(doc, sel) { Picker = (_, _) => path });
		Feed(pa, "p:50,50", "I", "5", "A", "N", "X");
		Assert.Equal(5, doc.Document.Entities.OfType<Circle>().Count(c => Math.Abs(c.Center.Y - 50) < 1e-6));
	}
}

