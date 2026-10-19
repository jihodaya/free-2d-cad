using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>TOLERANCE: 기존 CAD와 같은 공차 문자열·틀 모양·저장.</summary>
public class ToleranceTests
{
	private const string StdText = @"{\Fgdt;j}%%v{\Fgdt;n}0.1{\Fgdt;m}%%v%%vA%%vB%%v";

	[Fact]
	public void Row_BuildsSameStringAsStd()
	{
		string row = GdtText.Row('j', (true, "0.1", 'm'), (false, "", '\0'), ("A", '\0'), ("B", '\0'), ("", '\0'));
		Assert.Equal(StdText, row);
		var rows = GdtText.Parse(row);
		var f = Assert.Single(rows);
		Assert.Equal(6, f.Count);
		Assert.Equal('j', f[0][0].Symbol);
		Assert.Equal(new[] { 'n', '\0', 'm' }, f[1].Select(i => i.Symbol));
		Assert.Equal("0.1", f[1][1].Text);
		Assert.Empty(f[2]);
	}

	[Fact]
	public void Command_PlacesTolerance_FrameLikeStd()
	{
		var doc = EditorDocument.New();
		var cmd = new ToleranceCommand(StdText);
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(300, 0);
		var tol = doc.Document.Entities.OfType<Tolerance>().Single();
		Assert.Equal(StdText, tol.Text);

		var scene = SceneBuilder.BuildEntities(doc.Document, new[] { tol });
		double h = tol.Style.TextHeight * (tol.Style.ScaleFactor > 0 ? tol.Style.ScaleFactor : 1);
		// 틀: 왼쪽 가운데가 삽입점, 높이 2h, 기호 칸 폭 2.5h
		var frames = scene.Polylines.Where(p => p.Closed && p.Points.Length == 8).ToList();
		Assert.Equal(4, frames.Count); // 기호, 공차1, 데이텀 A, B
		var first = frames.OrderBy(p => p.Points.Where((_, i) => i % 2 == 0).Min()).First();
		var xs = first.Points.Where((_, i) => i % 2 == 0).ToList();
		var ys = first.Points.Where((_, i) => i % 2 == 1).ToList();
		Assert.Equal(300, xs.Min(), 6);
		Assert.Equal(2.5 * h, xs.Max() - xs.Min(), 6);
		Assert.Equal(h, ys.Max(), 6);
		Assert.Equal(-h, ys.Min(), 6);

		foreach (var (ext, ver) in new[] { (".dwg", ACadSharp.ACadVersion.AC1018), (".dwg", ACadSharp.ACadVersion.AC1032), (".dxf", ACadSharp.ACadVersion.AC1032) })
		{
			string dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT") ?? Path.GetTempPath();
			string path = Path.Combine(dir, $"tolerance_{ver}{ext}");
			doc.Save(path, ver);
			var back = EditorDocument.Open(path).Document.Entities.OfType<Tolerance>().Single();
			Assert.Equal(StdText, back.Text);
			Assert.Equal(300, back.InsertionPoint.X, 6);
			if (Environment.GetEnvironmentVariable("F2D_AUDIT_OUT") == null) File.Delete(path);
		}
	}
}
