using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>QDIM: 선택한 도형의 끝점으로 연속·기준선·엇갈림·세로좌표·반지름 치수를 한 번에.</summary>
public class QDimTests
{
	private static (EditorDocument Doc, List<Entity> Ents) Steps()
	{
		var doc = EditorDocument.New();
		var ents = new List<Entity>
		{
			new Line(new XYZ(50, 50, 0), new XYZ(150, 50, 0)),
			new Line(new XYZ(150, 50, 0), new XYZ(150, 100, 0)),
			new Line(new XYZ(150, 100, 0), new XYZ(250, 100, 0)),
		};
		doc.AddEntities(ents.ToArray());
		return (doc, ents);
	}

	private static List<Dimension> Run(EditorDocument doc, List<Entity> ents, params string[] input)
	{
		var cmd = new QDimCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity>(ents)));
		foreach (string s in input)
		{
			if (s.StartsWith("p:"))
			{
				var xy = s[2..].Split(',');
				cmd.OnPoint(double.Parse(xy[0]), double.Parse(xy[1]));
			}
			else cmd.OnText(s);
		}
		Assert.True(cmd.IsFinished);
		return doc.Document.Entities.OfType<Dimension>().ToList();
	}

	[Fact]
	public void Continuous_Horizontal_BelowGeometry()
	{
		var (doc, ents) = Steps();
		var dims = Run(doc, ents, "C", "p:150,20");
		Assert.Equal(2, dims.Count);
		Assert.All(dims, d => Assert.Equal(100, d.Measurement, 6));
		Assert.All(dims.OfType<DimensionLinear>(), d => Assert.Equal(0, d.Rotation, 9));
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Dimension>()); // 한 번에 되돌리기
	}

	[Fact]
	public void Continuous_Vertical_RightOfGeometry()
	{
		var (doc, ents) = Steps();
		var dims = Run(doc, ents, "C", "p:300,75");
		var d = Assert.Single(dims);
		Assert.Equal(50, d.Measurement, 6);
		Assert.Equal(Math.PI / 2, ((DimensionLinear)d).Rotation, 9);
	}

	[Fact]
	public void Baseline_And_Staggered_And_Ordinate()
	{
		var (doc, ents) = Steps();
		var b = Run(doc, ents, "B", "p:150,20");
		Assert.Equal(new[] { 100.0, 200.0 }, b.Select(d => Math.Round(d.Measurement, 6)).OrderBy(v => v));

		(doc, ents) = Steps();
		var s = Run(doc, ents, "S", "p:150,20");
		Assert.Single(s); // 점 3개(50,150,250) → 바깥 짝 하나
		Assert.Equal(200, s[0].Measurement, 6);

		(doc, ents) = Steps();
		var o = Run(doc, ents, "O", "p:150,20");
		Assert.Equal(3, o.Count);
		Assert.All(o, d => Assert.IsType<DimensionOrdinate>(d));
		Run(doc, new List<Entity> { ents[0] }, "C", "p:0,0"); // 방식은 다시 연속으로
	}

	[Fact]
	public void Radius_ForCircles()
	{
		var doc = EditorDocument.New();
		var c1 = new Circle { Center = new XYZ(0, 0, 0), Radius = 10 };
		var c2 = new Circle { Center = new XYZ(50, 0, 0), Radius = 5 };
		doc.AddEntities(c1, c2);
		var dims = Run(doc, new List<Entity> { c1, c2 }, "R", "p:30,30");
		Assert.Equal(2, dims.Count);
		Assert.Contains(dims, d => Math.Abs(d.Measurement - 10) < 1e-6);
		Assert.Contains(dims, d => Math.Abs(d.Measurement - 5) < 1e-6);
		Run(doc, new List<Entity> { c1 }, "C", "p:0,30");
	}
}
