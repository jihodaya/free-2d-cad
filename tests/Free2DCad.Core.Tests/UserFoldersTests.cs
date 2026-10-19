using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core.Styles;
using Free2DCad.Core.Tools;
using Xunit;

namespace Free2DCad.Core.Tests;

public class UserFoldersTests
{
	private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample", name);

	[Fact]
	public void BlockLibrary_ImportsDrawingAsBlock_InsertSavesAndReloads()
	{
		string src = Sample("test-2.dwg");
		if (!File.Exists(src)) return;
		string lib = Path.Combine(Path.GetTempPath(), $"부품A-{Guid.NewGuid():N}.dwg");
		File.Copy(src, lib);
		string outPath = Path.Combine(Path.GetTempPath(), $"f2d-blk-{Guid.NewGuid():N}.dwg");
		try
		{
			var doc = EditorDocument.New();
			var r = BlockLibrary.Import(doc, lib);
			Assert.False(r.AlreadyExisted);
			Assert.True(r.Imported > 100);
			doc.AddEntities(new Insert(r.Block) { InsertPoint = new XYZ(1000, 0, 0) });
			doc.Save(outPath);
			CadDocument loaded = DwgReader.Read(outPath);
			var ins = Assert.Single(loaded.Entities.OfType<Insert>());
			Assert.True(ins.Block.Entities.Count() >= r.Imported);
			// 두 번째 가져오기는 기존 블록 재사용
			Assert.True(BlockLibrary.Import(doc, lib).AlreadyExisted);
		}
		finally
		{
			File.Delete(lib);
			if (File.Exists(outPath)) File.Delete(outPath);
		}
	}

	[Fact]
	public void LinParser_ReadsSimpleAndComplex()
	{
		string lin = "*MYDASH,내 파선 __ __\nA,5,-2.5\n*GAS,가스선 --GAS--\nA,12.7,-5.08,[\"GAS\",STANDARD,S=2.54,X=-2.54,Y=-1.27],-10\n*ODD,모양\nA,5,[FOO,other.shx],-2\n*DOTS2,점\nA,0,-3\n";
		var defs = LinetypeLibrary.ParseLin(lin).ToList();
		Assert.Equal(3, defs.Count); // 알 수 없는 모양(other.shx의 FOO)이 든 선종류는 건너뜀
		Assert.Equal("MYDASH", defs[0].Name);
		Assert.Equal(new[] { 5.0, -2.5 }, defs[0].Pattern);
		Assert.Equal("GAS", defs[1].Name);
		Assert.Equal(new[] { 12.7, -5.08, -10 }, defs[1].Pattern);
		var el = Assert.Single(defs[1].Elements);
		Assert.Equal((1, true, "GAS", 2.54, -2.54, -1.27), (el.Segment, el.IsText, el.Value, el.Scale, el.X, el.Y));
		Assert.Equal("DOTS2", defs[2].Name);
	}

	[Fact]
	public void NewFromTemplate_KeepsLayersAndHasNoPath()
	{
		string src = Sample("test-2.dwg");
		if (!File.Exists(src)) return;
		var doc = EditorDocument.NewFromTemplate(src);
		Assert.Null(doc.FilePath);
		Assert.True(doc.Document.Layers.Count() > 5);
	}
}
