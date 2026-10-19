using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Styles;
using Xunit;

namespace Free2DCad.Core.Tests;

public class AnnotationScaleTests
{
	[Fact]
	public void Scale_AppliesToNewDimensionsAndText()
	{
		var doc = EditorDocument.New();
		AnnotationScale.Set(doc.Document, 50);
		var dim = new DimLinearCommand();
		dim.Start(new CommandContext(doc));
		dim.OnPoint(0, 0);
		dim.OnPoint(1000, 0);
		dim.OnPoint(500, 300);
		var d = doc.Document.Entities.OfType<DimensionLinear>().Single();
		Assert.EndsWith("1-50", d.Style.Name);
		Assert.Equal(50, d.Style.ScaleFactor, 6);

		var text = new TextCommand();
		text.Start(new CommandContext(doc));
		Assert.Contains("125", AfterPoint(text));
		AnnotationScale.Set(doc.Document, 1);
		Assert.Equal("1:1", AnnotationScale.Label(AnnotationScale.Get(doc.Document)));
	}

	[Fact]
	public void Scale_PersistsInFile_AsCannoscale()
	{
		var doc = EditorDocument.New();
		AnnotationScale.Set(doc.Document, 50);
		foreach (var (ext, ver) in new[] { (".dwg", ACadSharp.ACadVersion.AC1018), (".dwg", ACadSharp.ACadVersion.AC1032), (".dxf", ACadSharp.ACadVersion.AC1032) })
		{
			string path = Path.Combine(Path.GetTempPath(), $"f2d_annoscale_{ver}{ext}");
			doc.Save(path, ver);
			var back = EditorDocument.Open(path);
			Assert.Equal(50, AnnotationScale.Get(back.Document), 6);
			File.Delete(path);
		}
		if (Environment.GetEnvironmentVariable("F2D_AUDIT_OUT") is { } dir)
		{
			doc.Save(Path.Combine(dir, "annoscale_1-50.dwg"), ACadSharp.ACadVersion.AC1032);
		}
		Assert.Equal(0.5, AnnotationScale.Parse("2:1")!.Value, 9);
		Assert.Equal(1, AnnotationScale.Get(EditorDocument.New().Document), 9);
	}

	private static string AfterPoint(EditorCommand c)
	{
		c.OnPoint(0, 0);
		return c.Prompt;
	}
}
