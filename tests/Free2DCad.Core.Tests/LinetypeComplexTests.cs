using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Styles;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>내장 선종류 목록, 문자·모양 복합 선종류 만들기·저장·그리기.</summary>
public class LinetypeComplexTests
{
	[Fact]
	public void Library_HasOwnLinetypes()
	{
		var names = LinetypeLibrary.All.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (string n in new[] { "CENTER", "CENTER2", "CENTERX2", "HIDDEN", "PHANTOM", "DASHDOT", "DIVIDE", "BORDER", "DOT", "INVISIBLE",
			"ISO02", "ISO15", "ISO02_0.25", "ISO10_0.7", "FENCE_CIRCLE", "RAILWAY", "PIPE_GAS", "LINE_ELEC" })
		{
			Assert.Contains(n, names);
		}
		Assert.Equal(new[] { 20.0, -2, 4, -2 }, LinetypeLibrary.Find("CENTER")!.Pattern);
		Assert.Equal(new[] { 10.0, -1, 2, -1 }, LinetypeLibrary.Find("CENTER2")!.Pattern);
		// ISO 128: 굵기 0.5면 대시 12d = 6, 공백 3d = 1.5
		Assert.Equal(new[] { 6.0, -1.5 }, LinetypeLibrary.Find("ISO02_0.5")!.Pattern);
	}

	[Fact]
	public void Complex_Load_SaveDwg_Reopen_Render()
	{
		var doc = EditorDocument.New();
		Assert.Equal(3, LinetypeLibrary.Load(doc, new[] { "PIPE_GAS", "RAILWAY", "FENCE_CIRCLE" }));
		var gas = doc.Document.LineTypes["PIPE_GAS"];
		var textSeg = gas.Segments.Single(s => s.IsText);
		Assert.Equal("GAS", textSeg.Text);
		Assert.Equal("Standard", textSeg.Style.Name, ignoreCase: true);
		var rail = doc.Document.LineTypes["RAILWAY"];
		var shapeSeg = rail.Segments.Single(s => s.IsShape);
		Assert.Equal(130, shapeSeg.ShapeNumber);
		Assert.True(shapeSeg.Style.IsShapeFile);

		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0)) { LineType = gas };
		var l2 = new Line(new XYZ(0, 20, 0), new XYZ(100, 20, 0)) { LineType = rail };
		doc.AddEntities(l1, l2);

		string path = Path.Combine(Path.GetTempPath(), "f2d_ltcomplex_" + Guid.NewGuid().ToString("N") + ".dwg");
		try
		{
			doc.Save(path);
			var re = EditorDocument.Open(path);
			var rgas = re.Document.LineTypes["PIPE_GAS"];
			Assert.Equal("GAS", rgas.Segments.Single(s => s.IsText).Text);
			var rtr = re.Document.LineTypes["RAILWAY"].Segments.Single(s => s.IsShape);
			Assert.Equal(130, rtr.ShapeNumber);
			Assert.True(rtr.Style.IsShapeFile);
			Assert.Equal("ltypeshp.shx", Path.GetFileName(rtr.Style.Filename), ignoreCase: true);

			// 그리기: RAILWAY는 8마다 세로 막대 (길이 2 × 1.5 = 3), 100 길이에 12~13개
			var scene = SceneBuilder.Build(re.Document);
			var ties = scene.Polylines.Where(p => p.Points.Length == 4 && Math.Abs(p.Points[0] - p.Points[2]) < 1e-6
				&& Math.Abs(Math.Abs(p.Points[3] - p.Points[1]) - 3) < 1e-6).ToList();
			Assert.InRange(ties.Count, 12, 13);
			// GAS 글자: 한 주기 20.75 → 100 안에 4~5개
			Assert.InRange(scene.Texts.Count, 4, 5);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Theory]
	[InlineData(new float[] { 25.4f, -12.7f, -38.1f }, new float[] { 25.4f, 50.8f }, 0f)]
	[InlineData(new float[] { 6.35f, -2.54f, -2.54f, 25.4f }, new float[] { 31.75f, 5.08f }, 25.4f)]
	[InlineData(new float[] { 3.302f, 6.604f, 3.302f }, null, 0f)]
	[InlineData(new float[] { -5f, 10f, -5f }, new float[] { 10f, 10f }, 15f)]
	[InlineData(new float[] { 0f, -5f }, new float[] { 0.1f, 5f }, 0f)]
	public void DashIntervals_MergesSameSignRuns(float[] dash, float[]? expected, float phase)
	{
		var (iv, ph) = SkiaSceneRenderer.DashIntervals(dash, 0.1f);
		if (expected == null)
		{
			Assert.Null(iv);
			return;
		}
		Assert.Equal(expected.Length, iv!.Length);
		for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], iv[i], 3);
		Assert.Equal(phase, ph, 3);
	}

	[Fact]
	public void Sample_DashPhase_ForStd()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		var doc = EditorDocument.New();
		LinetypeLibrary.Load(doc, new[] { "DASHED", "CENTER" });
		var dashed = doc.Document.LineTypes["DASHED"];
		var center = doc.Document.LineTypes["CENTER"];
		doc.AddEntities(
			new Line(new XYZ(0, 0, 0), new XYZ(50, 0, 0)) { LineType = dashed },
			new Line(new XYZ(0, -20, 0), new XYZ(100, -20, 0)) { LineType = center },
			new LwPolyline(new[] { new LwPolyline.Vertex(new XY(0, -40)), new LwPolyline.Vertex(new XY(30, -40)), new LwPolyline.Vertex(new XY(30, -70)), new LwPolyline.Vertex(new XY(70, -70)) }) { LineType = dashed },
			new LwPolyline(new[] { new LwPolyline.Vertex(new XY(100, -40)), new LwPolyline.Vertex(new XY(130, -40)), new LwPolyline.Vertex(new XY(130, -70)), new LwPolyline.Vertex(new XY(170, -70)) }) { LineType = dashed, Flags = LwPolylineFlags.Plinegen },
			new Circle { Center = new XYZ(30, -110, 0), Radius = 20, LineType = dashed },
			new Arc { Center = new XYZ(100, -110, 0), Radius = 20, StartAngle = 0, EndAngle = Math.PI, LineType = dashed });
		doc.Save(Path.Combine(dir, "dashphase.dwg"));
	}

	[Fact]
	public void Sample_ForStd()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir == null) return;
		var doc = EditorDocument.New();
		string[] names = { "PIPE_GAS", "RAILWAY", "FENCE_CIRCLE", "FENCE_SQUARE", "ZIGZAG_LINE", "INSULATION_LINE", "PIPE_HOT", "CENTER2", "PHANTOM", "ISO10_0.5" };
		LinetypeLibrary.Load(doc, names);
		double y = 0;
		foreach (string n in names)
		{
			doc.AddEntities(new Line(new XYZ(0, y, 0), new XYZ(200, y, 0)) { LineType = doc.Document.LineTypes[n] });
			y -= 40;
		}
		try
		{
			doc.Save(Path.Combine(dir, "linetypes.dwg"));
		}
		catch (IOException)
		{
			// 기존 CAD에서 열려 있으면 건너뜀
		}

		// 내장 선종류 전부 (기존 CAD 오류 검사용)
		var all = EditorDocument.New();
		LinetypeLibrary.Load(all, LinetypeLibrary.All.Select(d => d.Name));
		all.Save(Path.Combine(dir, "all_linetypes.dwg"));
	}
}
