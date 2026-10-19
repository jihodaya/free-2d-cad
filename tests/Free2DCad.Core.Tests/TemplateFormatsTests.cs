using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>템플릿: 회사에서 쓰던 2000·2004·2007·2010 DWG와 DXF 모두 새 도면 템플릿으로 열린다 (경로는 비움).</summary>
public class TemplateFormatsTests
{
	[Theory]
	[InlineData(ACadVersion.AC1015, ".dwg")]
	[InlineData(ACadVersion.AC1018, ".dwg")]
	[InlineData(ACadVersion.AC1021, ".dwg")]
	[InlineData(ACadVersion.AC1024, ".dwg")]
	[InlineData(ACadVersion.AC1018, ".dxf")]
	[InlineData(ACadVersion.AC1018, ".dwt")]
	public void Template_OpensAsNewDocument(ACadVersion version, string ext)
	{
		var src = EditorDocument.New();
		src.Document.Layers.Add(new Layer("회사_도곽"));
		src.AddEntities(new Line(new XYZ(0, 0, 0), new XYZ(420, 297, 0)));
		string path = Path.Combine(Path.GetTempPath(), $"tpl_{Guid.NewGuid():N}{ext}");
		try
		{
			src.Save(path, version);
			var doc = EditorDocument.NewFromTemplate(path);
			Assert.Null(doc.FilePath);
			Assert.True(doc.Document.Layers.Contains("회사_도곽"));
			Assert.Single(doc.Document.Entities.OfType<Line>());
		}
		finally
		{
			File.Delete(path);
		}
	}
}

/// <summary>LINE을 Esc로 끝내도 그린 선은 남는다 (기존 CAD와 같음).</summary>
public class LineEscapeTests
{
	[Fact]
	public void Escape_KeepsDrawnSegments()
	{
		var doc = EditorDocument.New();
		var cmd = new Free2DCad.Core.Commands.LineCommand();
		cmd.Start(new Free2DCad.Core.Commands.CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 0);
		cmd.OnPoint(10, 10);
		cmd.OnCancel();
		Assert.True(cmd.IsFinished);
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Line>());
	}
}
