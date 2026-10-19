using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>도면층 명령 (LAYISO/LAYUNISO/LAYOFF/LAYDEL/LAYERP/LAYMCH).</summary>
public class LayerCommandsTests
{
	private static (EditorDocument Doc, Line A, Line B) Sample()
	{
		var doc = EditorDocument.New();
		var la = doc.AddLayer("A");
		var lb = doc.AddLayer("B");
		var a = new Line(XYZ.Zero, new XYZ(1, 0, 0)) { Layer = la };
		var b = new Line(XYZ.Zero, new XYZ(0, 1, 0)) { Layer = lb };
		doc.AddEntities(a, b);
		return (doc, a, b);
	}

	[Fact]
	public void Isolate_Then_Unisolate()
	{
		var (doc, a, _) = Sample();
		var iso = new LayerPickCommand(LayerPickCommand.Op.Isolate);
		iso.Start(new CommandContext(doc, new HashSet<Entity> { a }));
		Assert.True(doc.Document.Layers["A"].IsOn);
		Assert.False(doc.Document.Layers["B"].IsOn);
		Assert.Equal("A", doc.CurrentLayer.Name);
		new LayerAllCommand(LayerAllCommand.Op.Unisolate).Start(new CommandContext(doc));
		Assert.True(doc.Document.Layers["B"].IsOn);
	}

	[Fact]
	public void Off_Undo_And_Previous()
	{
		var (doc, _, b) = Sample();
		var off = new LayerPickCommand(LayerPickCommand.Op.Off);
		off.Start(new CommandContext(doc) { Picker = (_, _) => b });
		off.OnPoint(0, 0.5);
		off.OnEnter();
		Assert.False(doc.Document.Layers["B"].IsOn);
		new LayerAllCommand(LayerAllCommand.Op.Previous).Start(new CommandContext(doc));
		Assert.True(doc.Document.Layers["B"].IsOn);
		doc.Undo(); // LAYERP 되돌리기
		Assert.False(doc.Document.Layers["B"].IsOn);
	}

	[Fact]
	public void Delete_RemovesLayerAndEntities_Undoable()
	{
		var (doc, _, b) = Sample();
		var del = new LayerPickCommand(LayerPickCommand.Op.Delete);
		del.Start(new CommandContext(doc));
		del.OnText("N");
		del.OnText("B");
		del.OnText("Y");
		Assert.False(doc.Document.Layers.Contains("B"));
		Assert.DoesNotContain(b, doc.Document.Entities);
		doc.Undo();
		Assert.True(doc.Document.Layers.Contains("B"));
		Assert.Contains(b, doc.Document.Entities);
	}

	[Fact]
	public void Match_ByName()
	{
		var (doc, a, _) = Sample();
		var m = new LayerPickCommand(LayerPickCommand.Op.Match);
		m.Start(new CommandContext(doc, new HashSet<Entity> { a }));
		m.OnText("N");
		m.OnText("B");
		Assert.Equal("B", a.Layer.Name);
	}
}

public class LayerStateTests
{
	[Fact]
	public void SaveRestore_PersistsInFile()
	{
		var doc = EditorDocument.New();
		var a = doc.AddLayer("A");
		Free2DCad.Core.Styles.LayerStates.Save(doc, "작업1");
		a.IsOn = false;
		a.Color = new ACadSharp.Color(5);
		var path = Path.Combine(Path.GetTempPath(), $"ls_{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			var re = EditorDocument.Open(path);
			var states = Free2DCad.Core.Styles.LayerStates.List(re.Document);
			Assert.Contains(states, s => s.Name == "작업1");
			Assert.True(Free2DCad.Core.Styles.LayerStates.Restore(re, "작업1"));
			var ra = re.Document.Layers["A"];
			Assert.True(ra.IsOn);
			Assert.NotEqual((short)5, ra.Color.Index);
			re.Undo();
			Assert.False(re.Document.Layers["A"].IsOn);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
