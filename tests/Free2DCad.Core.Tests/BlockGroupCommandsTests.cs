using ACadSharp.Entities;
using ACadSharp.Header;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>속성 정의·삽입 값·동기화·편집, 그룹, ATTDISP.</summary>
public class BlockGroupCommandsTests
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
	public void AttDef_Block_Insert_AsksValues_Then_AttEdit_And_Sync()
	{
		var doc = EditorDocument.New();
		var attdef = new AttDefCommand();
		attdef.Start(new CommandContext(doc));
		Feed(attdef, "NO", "번호", "1", "3", "p:0,0");
		var def = doc.Document.Entities.OfType<AttributeDefinition>().Single();
		Assert.Equal("NO", def.Tag);

		var line = new Line(XYZ.Zero, new XYZ(10, 0, 0));
		doc.AddEntities(line);
		var block = new BlockCommand();
		block.Start(new CommandContext(doc));
		Feed(block, "TAGBLK", "p:0,0");
		block.OnSelection(new Entity[] { def, line });
		Feed(block, "");
		Assert.True(doc.Document.BlockRecords.Contains("TAGBLK"));

		var ins = new InsertCommand();
		ins.Start(new CommandContext(doc));
		Feed(ins, "TAGBLK", "p:100,0", "", "", "A-7");
		var insert = doc.Document.Entities.OfType<Insert>().Last();
		Assert.Equal("A-7", insert.Attributes.Single().Value);

		var edit = new AttEditCommand();
		edit.Start(new CommandContext(doc, new HashSet<Entity> { insert }));
		Feed(edit, "B-9");
		Assert.Equal("B-9", insert.Attributes.Single().Value);
		doc.Undo();
		Assert.Equal("A-7", insert.Attributes.Single().Value);

		var sync = new AttSyncCommand();
		sync.Start(new CommandContext(doc, new HashSet<Entity> { insert }));
		Assert.Equal("A-7", insert.Attributes.Single().Value);
	}

	[Fact]
	public void Group_Ungroup_And_SaveDropsErased()
	{
		var doc = EditorDocument.New();
		var a = new Line(XYZ.Zero, new XYZ(1, 0, 0));
		var b = new Line(XYZ.Zero, new XYZ(0, 1, 0));
		doc.AddEntities(a, b);
		var g = new GroupCommand(false);
		g.Start(new CommandContext(doc, new HashSet<Entity> { a, b }));
		Feed(g, "벽체");
		Assert.Equal(2, doc.Document.Groups["벽체"].Entities.Count());

		var q = new GroupCommand(true);
		q.Start(new CommandContext(doc, new HashSet<Entity> { a }));
		var q2 = new GroupCommand(true);
		q2.Start(new CommandContext(doc, new HashSet<Entity> { b }));
		Assert.Equal(2, doc.Document.Groups.Count(x => x.IsUnnamed));
		Assert.Equal(2, doc.Document.Groups.Where(x => x.IsUnnamed).Select(x => x.Name).Distinct().Count());

		var ug = new UngroupCommand();
		ug.Start(new CommandContext(doc, new HashSet<Entity> { a }));
		Assert.Empty(doc.Document.Groups.Where(x => x.Entities.Contains(a)));
		doc.Undo();
		Assert.Contains(doc.Document.Groups, x => x.Name == "벽체");

		doc.Erase(b);
		var path = Path.Combine(Path.GetTempPath(), $"grp_{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			Assert.DoesNotContain(b, doc.Document.Groups["벽체"].Entities);
			var re = EditorDocument.Open(path);
			Assert.Single(re.Document.Groups["벽체"].Entities);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void AttDisp_SetsHeader()
	{
		var doc = EditorDocument.New();
		var c = new AttDispCommand();
		c.Start(new CommandContext(doc));
		c.OnText("OFF");
		Assert.Equal(AttributeVisibilityMode.None, doc.Document.Header.AttributeVisibility);
		doc.Undo();
		Assert.Equal(AttributeVisibilityMode.Normal, doc.Document.Header.AttributeVisibility);
	}
}
