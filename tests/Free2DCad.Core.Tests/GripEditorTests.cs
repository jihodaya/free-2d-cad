using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Editing;
using Xunit;

namespace Free2DCad.Core.Tests;

public class GripEditorTests
{
	[Fact]
	public void Circle_HasCenterAndFourQuadrants_RadiusGripChangesRadius()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 10 };
		doc.AddEntities(c);
		var grips = GripEditor.GetGrips(c);
		Assert.Equal(5, grips.Count);
		var right = grips.Single(g => g.Index == 1);
		doc.Execute(GripEditor.CreateEdit(c, right, 25, 0)!);
		Assert.Equal(25, c.Radius, 6);
		var center = grips.Single(g => g.Index == 0);
		doc.Execute(GripEditor.CreateEdit(c, center, 5, 5)!);
		Assert.Equal(5, c.Center.X, 6);
		doc.Undo();
		Assert.Equal(0, c.Center.X, 6);
		doc.Undo();
		Assert.Equal(10, c.Radius, 6);
	}

	[Fact]
	public void Line_EndpointStretch_MidpointMoves_WithUndo()
	{
		var doc = EditorDocument.New();
		var l = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		doc.AddEntities(l);
		var g = GripEditor.GetGrips(l);
		Assert.Equal(3, g.Count);
		doc.Execute(GripEditor.CreateEdit(l, g[2], 10, 10)!);
		Assert.Equal(new XYZ(10, 10, 0), l.EndPoint);
		doc.Execute(GripEditor.CreateEdit(l, GripEditor.GetGrips(l)[1], 105, 105)!);
		Assert.Equal(100, l.StartPoint.X, 6);
		doc.Undo();
		doc.Undo();
		Assert.Equal(new XYZ(10, 0, 0), l.EndPoint);
		Assert.False(doc.LastChange.Full);
		Assert.Contains(l, doc.LastChange.Entities);
	}

	[Fact]
	public void Arc_MidGrip_KeepsEndpoints()
	{
		var doc = EditorDocument.New();
		var a = new Arc { Center = new XYZ(0, 0, 0), Radius = 10, StartAngle = 0, EndAngle = Math.PI };
		doc.AddEntities(a);
		var mid = GripEditor.GetGrips(a).Single(g => g.Index == 2);
		doc.Execute(GripEditor.CreateEdit(a, mid, 0, 5)!);
		// 끝점 (10,0), (-10,0)을 지나고 (0,5)를 지나는 호
		Assert.Equal(10, a.Center.X + a.Radius * Math.Cos(a.StartAngle), 6);
		Assert.Equal(-10, a.Center.X + a.Radius * Math.Cos(a.EndAngle), 6);
	}

	[Fact]
	public void Polyline_VertexAndSegmentGrips()
	{
		var doc = EditorDocument.New();
		var pl = new LwPolyline { IsClosed = true };
		foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) })
		{
			pl.Vertices.Add(new LwPolyline.Vertex(new XY(x, y)));
		}
		doc.AddEntities(pl);
		var grips = GripEditor.GetGrips(pl);
		Assert.Equal(8, grips.Count); // 꼭짓점 4 + 선분 중간 4
		var seg0 = grips.Single(g => g.Index == 1000);
		doc.Execute(GripEditor.CreateEdit(pl, seg0, seg0.X, seg0.Y - 5)!);
		Assert.Equal(-5, pl.Vertices[0].Location.Y, 6);
		Assert.Equal(-5, pl.Vertices[1].Location.Y, 6);
		doc.Undo();
		Assert.Equal(0, pl.Vertices[0].Location.Y, 6);
	}

	[Fact]
	public void Dimension_TextGripMovesText_AndPreviewDoesNotChangeOriginal()
	{
		var doc = EditorDocument.New();
		var dim = new DimensionLinear { FirstPoint = new XYZ(0, 0, 0), SecondPoint = new XYZ(100, 0, 0), DefinitionPoint = new XYZ(100, 20, 0) };
		dim.UpdateBlock();
		doc.AddEntities(dim);
		var textGrip = GripEditor.GetGrips(dim).Single(g => g.Index == 3);
		var preview = GripEditor.Preview(dim, textGrip, 50, 40);
		Assert.NotNull(preview);
		Assert.NotEqual(40, dim.TextMiddlePoint.Y, 6);
		doc.Execute(GripEditor.CreateEdit(dim, textGrip, 50, 40)!);
		Assert.Equal(40, dim.TextMiddlePoint.Y, 6);
		Assert.True(dim.IsTextUserDefinedLocation);
	}

	[Fact]
	public void DimLinearCommand_SecondExtensionLineIsStraight()
	{
		var doc = EditorDocument.New();
		var cmd = new Free2DCad.Core.Commands.DimLinearCommand();
		cmd.Start(new Free2DCad.Core.Commands.CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 0);
		cmd.OnPoint(40, 30); // 치수선 위치 (가운데가 아닌 곳을 찍어도)
		var dim = Assert.Single(doc.Document.Entities.OfType<DimensionLinear>());
		Assert.Equal(100, dim.DefinitionPoint.X, 6); // 두 번째 점 바로 위
		Assert.Equal(30, dim.DefinitionPoint.Y, 6);
		Assert.Equal(100, dim.Measurement, 6);
	}

	[Fact]
	public void AllEntityTypes_EveryGrip_EditAndUndoRestores()
	{
		var doc = EditorDocument.New();
		var pl = new LwPolyline();
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)) { Bulge = 0.5 });
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)));
		var sp = new Spline { Degree = 3 };
		foreach (var (x, y) in new[] { (0.0, 0.0), (5.0, 10.0), (10.0, -5.0), (15.0, 0.0) }) sp.ControlPoints.Add(new XYZ(x, y, 0));
		foreach (var k in new[] { 0.0, 0, 0, 0, 1, 1, 1, 1 }) sp.Knots.Add(k);
		var lin = new DimensionLinear { FirstPoint = new XYZ(0, 0, 0), SecondPoint = new XYZ(50, 0, 0), DefinitionPoint = new XYZ(50, 10, 0) };
		lin.UpdateBlock();
		var ents = new Entity[]
		{
			new Line(new XYZ(0, 0, 0), new XYZ(10, 5, 0)),
			new Arc { Center = new XYZ(0, 0, 0), Radius = 5, StartAngle = 0.2, EndAngle = 2.5 },
			new Circle { Center = new XYZ(3, 3, 0), Radius = 4 },
			pl,
			new TextEntity { Value = "가", Height = 3, InsertPoint = new XYZ(1, 1, 0) },
			new MText { Value = "나", Height = 3, InsertPoint = new XYZ(2, 2, 0) },
			new Ellipse { Center = new XYZ(0, 0, 0), MajorAxisEndPoint = new XYZ(10, 0, 0), RadiusRatio = 0.5 },
			sp,
			new Solid { FirstCorner = new XYZ(0, 0, 0), SecondCorner = new XYZ(5, 0, 0), ThirdCorner = new XYZ(0, 5, 0), FourthCorner = new XYZ(5, 5, 0) },
			new Point(new XYZ(7, 7, 0)),
			lin,
		};
		doc.AddEntities(ents);
		foreach (var e in ents)
		{
			var grips = GripEditor.GetGrips(e);
			Assert.NotEmpty(grips);
			foreach (var g in grips)
			{
				var before = e.GetBoundingBox();
				Assert.NotNull(GripEditor.Preview(e, g, g.X + 1.5, g.Y + 1));
				var act = GripEditor.CreateEdit(e, g, g.X + 1.5, g.Y + 1);
				Assert.NotNull(act);
				doc.Execute(act!);
				doc.Undo();
				var after = e.GetBoundingBox();
				Assert.Equal(before.Min.X, after.Min.X, 6);
				Assert.Equal(before.Max.Y, after.Max.Y, 6);
			}
		}
	}
}
