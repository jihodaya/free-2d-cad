using System.Diagnostics;
using ACadSharp.Entities;
using ACadSharp.IO;
using Free2DCad.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

public class Test3PerfTests
{
	private readonly ITestOutputHelper _out;
	public Test3PerfTests(ITestOutputHelper o) => this._out = o;

	[Fact]
	public void Perf_Test3()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample", "test-3.dwg");
		if (!File.Exists(path)) return;
		var sw = Stopwatch.StartNew();
		var doc = EditorDocument.Open(path);
		long tRead = sw.ElapsedMilliseconds; sw.Restart();
		var scene = SceneBuilder.Build(doc.Document);
		long tScene = sw.ElapsedMilliseconds; sw.Restart();
		var index = new SceneIndex(scene);
		long tIndex = sw.ElapsedMilliseconds; sw.Restart();
		using var r = new SkiaSceneRenderer(scene);
		long tRenderer = sw.ElapsedMilliseconds; sw.Restart();
		var ext = scene.Extents;
		var rnd = new Random(1);
		double tol = ext.Width / 1500 * 12;
		for (int i = 0; i < 200; i++)
		{
			SnapEngine.Find(index, ext.MinX + rnd.NextDouble() * ext.Width, ext.MinY + rnd.NextDouble() * ext.Height, tol);
		}
		long tSnap = sw.ElapsedMilliseconds; sw.Restart();
		for (int i = 0; i < 200; i++)
		{
			index.Pick(ext.MinX + rnd.NextDouble() * ext.Width, ext.MinY + rnd.NextDouble() * ext.Height, tol);
		}
		long tPick = sw.ElapsedMilliseconds;
		this._out.WriteLine($"read={tRead} scene={tScene} index={tIndex} renderer={tRenderer} snap200={tSnap} pick200={tPick} snapPts={scene.SnapPoints.Count} polylines={scene.Polylines.Count} pts={scene.Polylines.Sum(p => p.Points.Length / 2)}");
		// 회귀: 스플라인이 원점(0,0)으로 튀는 선이 없어야 함
		Assert.DoesNotContain(scene.Polylines, p => p.Source is Spline && p.Bounds.Width > 3000);
	}
}
