using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

/// <summary>정렬 치수: 두 점 순서·커서 쪽 모든 조합에서 치수선이 측정 방향과 평행하고 보조선이 같은 쪽.</summary>
public class AlignedAllSidesTests
{
	private readonly ITestOutputHelper _o;

	public AlignedAllSidesTests(ITestOutputHelper o) => this._o = o;

	[Theory]
	[InlineData(76, -164, 765, -237, 192, -122)] // 사용자 사진1 (화면 좌표의 y 반전)
	[InlineData(76, -164, 765, -237, 500, -400)]
	[InlineData(765, -237, 76, -164, 192, -122)]
	[InlineData(765, -237, 76, -164, 500, -400)]
	[InlineData(0, 0, 0, 100, 50, 50)]
	[InlineData(0, 0, 0, 100, -50, 50)]
	[InlineData(0, 100, 0, 0, 50, 50)]
	[InlineData(0, 100, 0, 0, -50, 50)]
	[InlineData(0, 0, 100, -100, 100, 0)]
	[InlineData(0, 0, 100, -100, 0, -100)]
	public void Aligned_AnyOrder_AnySide(double x1, double y1, double x2, double y2, double cx, double cy)
	{
		var doc = EditorDocument.New();
		var cmd = new DimLinearCommand(true);
		cmd.Start(new CommandContext(doc));
		// 미리보기도 같은 결과여야 한다
		cmd.OnPoint(x1, y1);
		cmd.OnPoint(x2, y2);
		var pv = cmd.Preview(cx, cy).OfType<DimensionAligned>().Single();
		Check(pv, x1, y1, x2, y2, cx, cy);
		cmd.OnPoint(cx, cy);
		var d = doc.Document.Entities.OfType<DimensionAligned>().Single();
		Check(d, x1, y1, x2, y2, cx, cy);
	}

	private void Check(DimensionAligned d, double x1, double y1, double x2, double y2, double cx, double cy)
	{
		foreach (var e in d.Block!.Entities)
		{
			this._o.WriteLine(e switch
			{
				Line l => $"Line ({l.StartPoint.X:0.##},{l.StartPoint.Y:0.##})-({l.EndPoint.X:0.##},{l.EndPoint.Y:0.##})",
				MText m => $"MText {m.Value} at ({m.InsertPoint.X:0.##},{m.InsertPoint.Y:0.##})",
				_ => e.ObjectName,
			});
		}
		var lines = d.Block.Entities.OfType<Line>().ToList();
		double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
		double ux = (x2 - x1) / len, uy = (y2 - y1) / len;
		double nx = -uy, ny = ux;
		double side = Math.Sign((cx - x1) * nx + (cy - y1) * ny);
		Assert.Single(d.Block.Entities.OfType<MText>());
		foreach (var l in lines)
		{
			double dx = l.EndPoint.X - l.StartPoint.X, dy = l.EndPoint.Y - l.StartPoint.Y;
			double ll = Math.Sqrt(dx * dx + dy * dy);
			if (ll < 1e-9) continue;
			double cross = Math.Abs((dx * uy - dy * ux) / ll);
			double dot = Math.Abs((dx * ux + dy * uy) / ll);
			Assert.True(cross < 1e-6 || dot < 1e-6, $"선이 측정 방향과 평행/수직이 아님: {l.StartPoint}-{l.EndPoint}");
			// 모든 선의 끝은 커서 쪽 (측정선 위 또는 커서 쪽)
			foreach (var p in new[] { l.StartPoint, l.EndPoint })
			{
				double s = (p.X - x1) * nx + (p.Y - y1) * ny;
				Assert.True(s * side >= -1e-6, $"반대쪽 점 {p}");
			}
		}
	}
}
