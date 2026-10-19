using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Blocks;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>블록 정의(유지·변환·삭제, 다시 정의)와 블록 삽입(기존 CAD 프롬프트, 균일 축척, 분해).</summary>
public class BlockDialogTests
{
	[Fact]
	public void Define_Modes_And_Redefine_Undo()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(10, 10, 0), Radius = 5 };
		doc.AddEntities(c);
		var br = BlockDefinition.Define(doc, "B1", "설명", new XYZ(10, 10, 0), new Entity[] { c }, BlockSourceMode.Convert);
		Assert.Single(doc.Document.Entities.OfType<Insert>());
		Assert.DoesNotContain(c, doc.Document.Entities);
		Assert.Equal(0, ((Circle)br.Entities.Single()).Center.X, 6); // 기준점이 원점
		Assert.Equal("설명", br.BlockEntity.Comments);
		doc.Undo();
		Assert.Contains(c, doc.Document.Entities);
		Assert.False(doc.Document.BlockRecords.Contains("B1"));

		var keep = BlockDefinition.Define(doc, "B2", "", XYZ.Zero, new Entity[] { c }, BlockSourceMode.Retain);
		Assert.Contains(c, doc.Document.Entities);
		Assert.Empty(doc.Document.Entities.OfType<Insert>());
		var line = new Line(XYZ.Zero, new XYZ(1, 0, 0));
		doc.AddEntities(line);
		BlockDefinition.Define(doc, "B2", "", XYZ.Zero, new Entity[] { line }, BlockSourceMode.Delete);
		Assert.IsType<Line>(keep.Entities.Single()); // 다시 정의
		Assert.DoesNotContain(line, doc.Document.Entities);
	}

	[Fact]
	public void Place_Prompts_Uniform_Explode()
	{
		var doc = EditorDocument.New();
		var br = BlockDefinition.Define(doc, "B3", "", XYZ.Zero, new Entity[] { new Line(XYZ.Zero, new XYZ(10, 0, 0)) }, BlockSourceMode.Retain);
		var ctx = new CommandContext(doc);
		var cmd = new BlockPlaceCommand(br, null, null, null, uniform: true, explode: false);
		cmd.Start(ctx);
		Assert.Equal("블록의 삽입점:", cmd.Prompt);
		cmd.OnPoint(100, 0);
		Assert.Equal("[구석(C)/XYZ]/X 축척비율 <1.000000>:", cmd.Prompt);
		cmd.OnText("2");
		Assert.Equal("블록의 회전 각도 <0.0>:", cmd.Prompt); // 균일: Y를 묻지 않음
		cmd.OnText("90");
		var ins = doc.Document.Entities.OfType<Insert>().Single();
		Assert.Equal(2, ins.YScale, 6);
		Assert.Equal(Math.PI / 2, ins.Rotation, 6);

		var ex = new BlockPlaceCommand(br, new XYZ(0, 50, 0), new XYZ(1, 1, 1), 0, uniform: false, explode: true);
		ex.Start(ctx);
		Assert.True(ex.IsFinished);
		Assert.Contains(doc.Document.Entities.OfType<Line>(), l => Math.Abs(l.StartPoint.Y - 50) < 1e-9);
	}

	[Fact]
	public void Sample_ForStd()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 5 };
		var l = new Line(new XYZ(-10, 0, 0), new XYZ(-5, 0, 0));
		doc.AddEntities(c, l);
		var br = BlockDefinition.Define(doc, "F2D_BLK", "F2D 블록 설명", XYZ.Zero, new Entity[] { c, l }, BlockSourceMode.Convert, explodable: false, uniformScale: true);
		doc.AddEntities(BlockDefinition.MakeInsert(br, new XYZ(30, 0, 0), 2, 2, 2, Math.PI / 6));
		doc.Save(Path.Combine(dir, "block_define.dwg"));
	}
}
