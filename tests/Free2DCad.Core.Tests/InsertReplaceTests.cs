using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Blocks;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>속성창 블록 "이름" 바꾸기 (Insert.ReplaceBlock): 같은 도면 블록으로 교체, 저장 후에도 유지.</summary>
public class InsertReplaceTests
{
	[Fact]
	public void ReplaceBlock_SavesAndReloads()
	{
		var doc = EditorDocument.New();
		var a = new BlockRecord("BLK_A");
		a.Entities.Add(new Line(XYZ.Zero, new XYZ(10, 0, 0)));
		var b = new BlockRecord("BLK_B");
		b.Entities.Add(new Circle { Radius = 5 });
		doc.Document.BlockRecords.Add(a);
		doc.Document.BlockRecords.Add(b);
		var ins = BlockDefinition.MakeInsert(a, new XYZ(20, 20, 0));
		doc.AddEntities(ins);

		ins.ReplaceBlock(b);
		Assert.Same(b, ins.Block);
		Assert.Throws<ArgumentException>(() => ins.ReplaceBlock(new BlockRecord("OTHER_DOC")));

		string dir = Path.Combine(Path.GetTempPath(), "Free2DCad_Ins_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string path = Path.Combine(dir, "ins.dwg");
			doc.Save(path);
			var re = DwgReader.Read(path);
			Assert.Equal("BLK_B", re.Entities.OfType<Insert>().Single().Block.Name);
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}
}
