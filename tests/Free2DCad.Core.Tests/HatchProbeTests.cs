using System.Diagnostics;
using ACadSharp.Entities;
using Free2DCad.Core.Hatching;
using Free2DCad.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

/// <summary>
/// 샘플 도면(test-2: 도면 틀 안 전체가 닫힌 영역)의 여러 점에서 해치 경계 찾기.
/// 예전에는 SHX 문자 획까지 경계 후보로 넣고 교차 검사 격자가 한 칸에 몰려 첫 클릭에 수십 초가 걸리거나 실패했다.
/// </summary>
public class HatchProbeTests
{
	private readonly ITestOutputHelper _out;
	public HatchProbeTests(ITestOutputHelper o) => this._out = o;

	[Fact]
	public void Test2_EveryPointInsideFrame_FindsBoundaryQuickly()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample", "test-2.dwg");
		if (!File.Exists(path)) return;
		var doc = EditorDocument.Open(path);
		var scene = SceneBuilder.Build(doc.Document);
		var ext = scene.Extents;
		int fail = 0;
		long worst = 0;
		for (int i = 1; i < 10; i++)
		{
			for (int j = 1; j < 6; j++)
			{
				double x = ext.MinX + ext.Width * i / 10.0, y = ext.MinY + ext.Height * j / 6.0;
				var t = Stopwatch.StartNew();
				if (HatchBoundaryFinder.FindAt(scene, x, y, HatchStyleType.Outer) == null)
				{
					fail++;
					this._out.WriteLine($"fail at {x:0.#},{y:0.#}");
				}
				worst = Math.Max(worst, t.ElapsedMilliseconds);
			}
		}
		this._out.WriteLine($"fail {fail}, worst {worst} ms");
		Assert.Equal(0, fail);
		Assert.True(worst < 5000, $"경계 찾기가 너무 느림: {worst} ms");
	}
}
