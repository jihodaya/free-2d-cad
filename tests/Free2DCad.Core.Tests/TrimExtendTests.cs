using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>자르기·연장: 선·호·원·폴리선.</summary>
public class TrimExtendTests
{
	private static Line L(double x1, double y1, double x2, double y2) => new(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0));

	[Fact]
	public void Trim_Line_RemovesClickedSide()
	{
		var target = L(0, 0, 10, 0);
		var edge = L(4, -5, 4, 5);
		var (remove, add) = TrimExtend.Trim(target, new[] { edge }, new XY(8, 0))!.Value;
		Assert.Same(target, remove);
		var kept = Assert.IsType<Line>(Assert.Single(add));
		Assert.Equal(0, kept.StartPoint.X, 6);
		Assert.Equal(4, kept.EndPoint.X, 6);
	}

	[Fact]
	public void Trim_Line_BetweenTwoEdges_LeavesTwoPieces()
	{
		var target = L(0, 0, 10, 0);
		var (_, add) = TrimExtend.Trim(target, new[] { L(3, -1, 3, 1), L(7, -1, 7, 1) }, new XY(5, 0))!.Value;
		Assert.Equal(2, add.Count);
	}

	[Fact]
	public void Trim_Circle_BecomesArc()
	{
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 5 };
		var edge = L(-10, 0, 10, 0); // 원을 위아래로 나눔
		var (_, add) = TrimExtend.Trim(c, new[] { edge }, new XY(0, 5))!.Value;
		var arc = Assert.IsType<Arc>(Assert.Single(add));
		// 위쪽을 잘라 냈으므로 아래 반원(π → 2π)이 남는다
		Assert.Equal(Math.PI, arc.StartAngle, 6);
		Assert.Equal(0, arc.EndAngle % (2 * Math.PI), 6);
	}

	[Fact]
	public void Trim_Arc()
	{
		var arc = new Arc { Center = new XYZ(0, 0, 0), Radius = 5, StartAngle = 0, EndAngle = Math.PI };
		var edge = L(0, 0, 0, 10); // 90°에서 자름
		var (_, add) = TrimExtend.Trim(arc, new[] { edge }, new XY(-4, 3))!.Value;
		var kept = Assert.IsType<Arc>(Assert.Single(add));
		Assert.Equal(0, kept.StartAngle, 6);
		Assert.Equal(Math.PI / 2, kept.EndAngle, 6);
	}

	[Fact]
	public void Trim_ClosedPolyline_KeepsRemainder()
	{
		var pl = new LwPolyline { IsClosed = true };
		foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) })
			pl.Vertices.Add(new LwPolyline.Vertex(new XY(x, y)));
		var edge = L(5, -5, 5, 15);
		var (_, add) = TrimExtend.Trim(pl, new[] { edge }, new XY(10, 5))!.Value;
		var kept = Assert.IsType<LwPolyline>(Assert.Single(add));
		Assert.False(kept.IsClosed);
		Assert.All(kept.Vertices, v => Assert.True(v.Location.X <= 5 + 1e-6));
	}

	[Fact]
	public void Extend_Line_ToBoundary()
	{
		var target = L(0, 0, 5, 0);
		var ext = Assert.IsType<Line>(TrimExtend.Extend(target, new[] { L(9, -5, 9, 5) }, new XY(4, 0)));
		Assert.Equal(9, ext.EndPoint.X, 6);
		Assert.Equal(0, ext.StartPoint.X, 6);
	}

	[Fact]
	public void Extend_Arc_ToLine()
	{
		var arc = new Arc { Center = new XYZ(0, 0, 0), Radius = 5, StartAngle = 0, EndAngle = Math.PI / 4 };
		var ext = Assert.IsType<Arc>(TrimExtend.Extend(arc, new[] { L(0, 0, 0, 10) }, new XY(3.5, 3.5)));
		Assert.Equal(Math.PI / 2, ext.EndAngle, 6);
	}

	[Fact]
	public void Extend_PolylineEnd()
	{
		var pl = new LwPolyline();
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(5, 0)));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(5, 5)));
		var ext = Assert.IsType<LwPolyline>(TrimExtend.Extend(pl, new[] { L(-5, 9, 15, 9) }, new XY(5, 4)));
		Assert.Equal(9, ext.Vertices[2].Location.Y, 6);
	}
}
