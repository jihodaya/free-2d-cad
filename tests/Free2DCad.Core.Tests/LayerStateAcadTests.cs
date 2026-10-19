using ACadSharp;
using ACadSharp.Objects;
using ACadSharp.Tables;
using Free2DCad.Core.Styles;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>도면층 상태: 기존 CAD 형식(도면층 테이블 확장 사전의 ACAD_LAYERSTATES)으로 저장·읽기.</summary>
public class LayerStateAcadTests
{
	[Fact]
	public void SavesInAcadFormat_RoundTrips()
	{
		var doc = EditorDocument.New();
		var l1 = new Layer("L1") { Color = new Color(1) };
		doc.Document.Layers.Add(l1);
		l1.IsOn = false;
		LayerStates.Save(doc, "끈 상태", "설명");
		Assert.True(doc.Document.Layers.XDictionary!.TryGetEntry<CadDictionary>("ACAD_LAYERSTATES", out var d));
		var rec = Assert.Single(d!.OfType<XRecord>());
		Assert.Contains(rec.Entries, e => e.Code == 302);
		Assert.Contains(rec.Entries, e => e.Code == 330 && e.Value is Layer { Name: "L1" });

		foreach (var (ext, ver) in new[] { (".dwg", ACadVersion.AC1018), (".dwg", ACadVersion.AC1032), (".dxf", ACadVersion.AC1032) })
		{
			string path = Path.Combine(Path.GetTempPath(), $"f2d_lstate_{ver}{ext}");
			doc.Save(path, ver);
			var back = EditorDocument.Open(path);
			var st = Assert.Single(LayerStates.List(back.Document));
			Assert.Equal("끈 상태", st.Name);
			Assert.Equal("설명", st.Description);
			var p = st.Layers.Single(x => x.Name == "L1");
			Assert.False(p.On);
			Assert.Equal(1, p.Color.Index);
			Assert.True(st.Layers.Single(x => x.Name == "0").On);
			// 복원
			back.Document.Layers["L1"].IsOn = true;
			Assert.True(LayerStates.Restore(back, "끈 상태"));
			Assert.False(back.Document.Layers["L1"].IsOn);
			File.Delete(path);
		}
	}

	[Fact]
	public void ReadsStdLayerState()
	{
		string? path = Environment.GetEnvironmentVariable("F2D_REF_DWG");
		if (path == null || !File.Exists(path)) return;
		var doc = EditorDocument.Open(path);
		var st = LayerStates.List(doc.Document).Single(s => s.Name == "GCSTATE");
		Assert.Equal("desc1", st.Description);
		Assert.Equal(3, st.Layers.Count);
		Assert.False(st.Layers.Single(l => l.Name == "Defpoints").Plot);
		Assert.True(st.Layers.Single(l => l.Name == "0").Plot);
	}
}
