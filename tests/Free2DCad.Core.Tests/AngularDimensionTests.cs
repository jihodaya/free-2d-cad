using ACadSharp.Entities;
using CSMath;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>각도 치수: 호를 놓는 쪽의 각도를 재고, 블록(호·화살표·각도 문자)을 만든다.</summary>
public class AngularDimensionTests
{
	private static DimensionAngular2Line TwoLine(double ax, double ay)
	{
		// x축 선(0,0)-(20,0)과 45° 선(0,0)-(20,20)
		var d = new DimensionAngular2Line
		{
			FirstPoint = new XYZ(0, 0, 0),
			SecondPoint = new XYZ(20, 0, 0),
			AngleVertex = new XYZ(0, 0, 0),
			DefinitionPoint = new XYZ(20, 20, 0),
			DimensionArc = new XYZ(ax, ay, 0),
		};
		d.UpdateBlock();
		return d;
	}

	[Theory]
	[InlineData(10, 3, 45)]     // 두 선 사이
	[InlineData(-10, -3, 45)]   // 맞은편 꼭지각
	[InlineData(-3, 10, 135)]   // 이웃 각
	[InlineData(3, -10, 135)]
	public void TwoLine_MeasuresQuadrantOfArcPoint(double ax, double ay, double expectedDeg)
	{
		var d = TwoLine(ax, ay);
		Assert.Equal(expectedDeg, d.Measurement * 180 / Math.PI, 3);
		var ents = d.Block!.Entities.ToList();
		var arc = Assert.Single(ents.OfType<Arc>());
		Assert.Equal(Math.Sqrt(ax * ax + ay * ay), arc.Radius, 6);
		// 호의 각도 폭 = 측정값
		double sweep = arc.EndAngle - arc.StartAngle;
		if (sweep < 0) sweep += 2 * Math.PI;
		Assert.Equal(expectedDeg, sweep * 180 / Math.PI, 3);
		Assert.Equal(2, ents.Count(e => e is Solid or Insert));
		var text = Assert.Single(ents.OfType<MText>());
		Assert.Contains(((int)expectedDeg).ToString(), text.Value);
	}

	[Fact]
	public void TwoLine_ArcBeyondLines_DrawsExtensionLines()
	{
		var d = TwoLine(30, 9);
		Assert.True(d.Block!.Entities.OfType<Line>().Count() >= 2);
	}

	[Fact]
	public void ThreePoint_FromVertex()
	{
		var d = new DimensionAngular3Pt
		{
			AngleVertex = new XYZ(0, 0, 0),
			FirstPoint = new XYZ(10, 0, 0),
			SecondPoint = new XYZ(0, 10, 0),
			DefinitionPoint = new XYZ(5, 5, 0),
		};
		d.UpdateBlock();
		Assert.Equal(90, d.Measurement * 180 / Math.PI, 3);
		Assert.Single(d.Block!.Entities.OfType<Arc>());
		Assert.Single(d.Block.Entities.OfType<MText>());
	}
}
