using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>주석 명령 (선형 옵션, 기준선/연속, 세로좌표, 호 길이, 중심 표시, 지시선, 문자 변환, 경계).</summary>
public class AnnotationCommandsTests
{
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
	public void DimLinear_SelectObject_And_VerticalOption()
	{
		var doc = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(30, 40, 0));
		doc.AddEntities(line);
		var cmd = new DimLinearCommand();
		cmd.Start(new CommandContext(doc) { Picker = (_, _) => line });
		Feed(cmd, "", "p:15,20", "V", "p:50,20");
		var dim = doc.Document.Entities.OfType<DimensionLinear>().Single();
		Assert.Equal(40, dim.Measurement, 6);
	}

	[Fact]
	public void DimLinear_TextOverride()
	{
		var doc = EditorDocument.New();
		var cmd = new DimLinearCommand();
		cmd.Start(new CommandContext(doc));
		Feed(cmd, "p:0,0", "p:10,0", "T", "길이 <>", "p:5,5");
		Assert.Equal("길이 <>", doc.Document.Entities.OfType<DimensionLinear>().Single().Text);
	}

	[Fact]
	public void DimBaseline_And_Continue()
	{
		var doc = EditorDocument.New();
		var lin = new DimLinearCommand();
		lin.Start(new CommandContext(doc));
		Feed(lin, "p:0,0", "p:10,0", "p:5,5");

		var cont = new DimChainCommand(true);
		cont.Start(new CommandContext(doc));
		Feed(cont, "p:25,0", "p:40,0", "", "");
		var dims = doc.Document.Entities.OfType<DimensionLinear>().ToList();
		Assert.Equal(3, dims.Count);
		Assert.Equal(15, dims[1].Measurement, 6);
		Assert.Equal(10, dims[1].FirstPoint.X, 6);
		Assert.Equal(25, dims[2].FirstPoint.X, 6);
		Assert.Equal(dims[0].DefinitionPoint.Y, dims[2].DefinitionPoint.Y, 6);

		var bas = new DimChainCommand(false);
		bas.Start(new CommandContext(doc));
		Feed(bas, "p:60,0", "", "");
		var b = doc.Document.Entities.OfType<DimensionLinear>().Last();
		Assert.Equal(25, b.FirstPoint.X, 6); // 마지막 치수(25→40)의 첫 원점이 기준
		Assert.True(b.DefinitionPoint.Y > dims[2].DefinitionPoint.Y);
	}

	[Fact]
	public void DimOrdinate_MeasuresX()
	{
		var doc = EditorDocument.New();
		var cmd = new DimOrdinateCommand();
		cmd.Start(new CommandContext(doc));
		Feed(cmd, "p:12,3", "p:12,20");
		var d = doc.Document.Entities.OfType<DimensionOrdinate>().Single();
		Assert.True(d.IsOrdinateTypeX);
		Assert.Equal(12, d.Measurement, 6);
	}

	[Fact]
	public void DimArc_LengthAndText()
	{
		var doc = EditorDocument.New();
		var arc = new Arc { Center = XYZ.Zero, Radius = 10, StartAngle = 0, EndAngle = 1.5 * Math.PI };
		doc.AddEntities(arc);
		var cmd = new DimArcCommand();
		cmd.Start(new CommandContext(doc) { Picker = (_, _) => arc });
		Feed(cmd, "p:0,10", "p:-10,10");
		var d = doc.Document.Entities.OfType<DimensionArc>().Single();
		Assert.Equal(15 * Math.PI, d.Measurement, 6);
		Assert.StartsWith("⌒", d.GetMeasurementText());
		Assert.NotEmpty(d.Block!.Entities.OfType<Arc>());
	}

	[Fact]
	public void DimCenter_Marks()
	{
		var lines = DimCenterCommand.Mark(new Circle { Center = XYZ.Zero, Radius = 10 }, -2);
		Assert.Equal(6, lines.Count);
	}

	[Fact]
	public void Leader_WithText_LinksAnnotation()
	{
		var doc = EditorDocument.New();
		var cmd = new LeaderCommand("QLEADER");
		cmd.Start(new CommandContext(doc));
		Feed(cmd, "p:0,0", "p:10,10", "", "첫 줄", "둘째 줄", "");
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var mt = Assert.IsType<MText>(leader.AssociatedAnnotation);
		Assert.Equal("첫 줄\\P둘째 줄", mt.Value);
		Assert.Equal(3, leader.Vertices.Count); // 연결선(hook) 추가
	}

	[Fact]
	public void Txt2Mtxt_CombinesTopToBottom()
	{
		var doc = EditorDocument.New();
		var a = new TextEntity { Value = "아래", Height = 2, InsertPoint = new XYZ(0, 0, 0) };
		var b = new TextEntity { Value = "위", Height = 2, InsertPoint = new XYZ(0, 5, 0) };
		doc.AddEntities(a, b);
		var cmd = new Txt2MtxtCommand();
		cmd.Start(new CommandContext(doc, new HashSet<Entity> { a, b }));
		var mt = doc.Document.Entities.OfType<MText>().Single();
		Assert.Equal("위\\P아래", mt.Value);
		Assert.Empty(doc.Document.Entities.OfType<TextEntity>());
	}

	[Fact]
	public void Boundary_FromLines()
	{
		var doc = EditorDocument.New();
		doc.AddEntities(
			new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)),
			new Line(new XYZ(10, 0, 0), new XYZ(10, 10, 0)),
			new Line(new XYZ(10, 10, 0), new XYZ(0, 10, 0)),
			new Line(new XYZ(0, 10, 0), new XYZ(0, 0, 0)));
		var cmd = new BoundaryCommand();
		cmd.Start(new CommandContext(doc));
		Feed(cmd, "p:5,5", "");
		var pl = doc.Document.Entities.OfType<LwPolyline>().Single();
		Assert.True(pl.IsClosed);
		Assert.Equal(4, pl.Vertices.Count);
	}
}
