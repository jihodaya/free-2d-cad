using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Text;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>FIND: 찾기·바꾸기 (서식 유지, 옵션, 한 번에 되돌리기).</summary>
public class TextSearchTests
{
	private static (EditorDocument Doc, TextEntity T, MText M, DimensionAligned D, MultiLeader L) Sample()
	{
		var doc = EditorDocument.New();
		var t = new TextEntity { Value = "ABC 123", Height = 2.5 };
		var m = new MText { Value = @"{\Labc} DEF\Pabc", Height = 2.5, InsertPoint = new XYZ(0, 10, 0) };
		var d = new DimensionAligned(new XYZ(0, 0, 0), new XYZ(10, 0, 0)) { DefinitionPoint = new XYZ(10, 5, 0), Text = "<> ABC" };
		doc.AddEntities(t, m, d);
		var l = MLeaderBuilder.Create(doc.Document, new[] { new XY(0, 0), new XY(10, 10) }, "abc 지시선");
		doc.AddEntities(l);
		return (doc, t, m, d, l);
	}

	[Fact]
	public void Find_AllTextKinds_IgnoreCase()
	{
		var (doc, _, _, _, _) = Sample();
		var found = TextSearch.Find(doc.Document, "abc", new FindOptions());
		Assert.Equal(4, found.Count);
		Assert.Contains(found, f => f.Kind == "문자");
		Assert.Contains(found, f => f.Kind == "여러 줄 문자");
		Assert.Contains(found, f => f.Kind == "치수");
		Assert.Contains(found, f => f.Kind == "다중 지시선");
		Assert.All(found, f => Assert.Equal("모형", f.Location));

		Assert.Single(TextSearch.Find(doc.Document, "abc", new FindOptions(MatchCase: true, DimensionsAndLeaders: false)));
		Assert.Empty(TextSearch.Find(doc.Document, "AB", new FindOptions(WholeWord: true)));
		Assert.Equal(4, TextSearch.Find(doc.Document, "a?c", new FindOptions(Wildcards: true)).Count);
	}

	[Fact]
	public void ReplaceAll_KeepsFormatting_AndUndoesAtOnce()
	{
		var (doc, t, m, d, l) = Sample();
		var opts = new FindOptions();
		var found = TextSearch.Find(doc.Document, "abc", opts);
		int n = TextSearch.ReplaceAll(doc, found, "abc", "XYZ", opts);
		Assert.Equal(4, n);
		Assert.Equal("XYZ 123", t.Value);
		Assert.Equal(@"{\LXYZ} DEF\PXYZ", m.Value); // 밑줄 서식 그대로
		Assert.Equal("<> XYZ", d.Text);
		Assert.Equal("XYZ 지시선", l.ContextData.TextLabel);
		doc.Undo();
		Assert.Equal("ABC 123", t.Value);
		Assert.Equal(@"{\Labc} DEF\Pabc", m.Value);
		Assert.Equal("<> ABC", d.Text);
	}

	[Fact]
	public void IgnoresHiddenLayers()
	{
		var (doc, t, _, _, _) = Sample();
		var off = new ACadSharp.Tables.Layer("OFF");
		doc.Document.Layers.Add(off);
		off.IsOn = false;
		t.Layer = off;
		Assert.Equal(3, TextSearch.Find(doc.Document, "abc", new FindOptions()).Count);
		Assert.Equal(4, TextSearch.Find(doc.Document, "abc", new FindOptions(IgnoreHidden: false)).Count);
	}
}
