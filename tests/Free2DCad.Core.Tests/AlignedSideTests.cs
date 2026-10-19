using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Free2DCad.Core.Tests;

public class AlignedSideTests
{
	private readonly ITestOutputHelper _o;

	public AlignedSideTests(ITestOutputHelper o) => this._o = o;

	[Theory]
	[InlineData(30, 40)]   // 왼쪽 위 (선의 왼쪽)
	[InlineData(70, 0)]    // 오른쪽 아래 (선의 오른쪽)
	public void Aligned_BothSides(double x, double y)
	{
		var doc = EditorDocument.New();
		var cmd = new DimLinearCommand(true);
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 40);
		cmd.OnPoint(x, y);
		var d = doc.Document.Entities.OfType<DimensionAligned>().Single();
		foreach (var e in d.Block!.Entities)
		{
			this._o.WriteLine(e switch
			{
				Line l => $"Line ({l.StartPoint.X:0.##},{l.StartPoint.Y:0.##})-({l.EndPoint.X:0.##},{l.EndPoint.Y:0.##})",
				MText m => $"MText {m.Value} at ({m.InsertPoint.X:0.##},{m.InsertPoint.Y:0.##})",
				_ => e.ObjectName,
			});
		}
		this._o.WriteLine($"def={d.DefinitionPoint} meas={d.Measurement}");
		// 치수선은 측정 방향과 평행해야 한다
		var dimLine = d.Block.Entities.OfType<Line>().OrderByDescending(l => Math.Sqrt(Math.Pow(l.EndPoint.X - l.StartPoint.X, 2) + Math.Pow(l.EndPoint.Y - l.StartPoint.Y, 2))).First();
		double ang = Math.Atan2(dimLine.EndPoint.Y - dimLine.StartPoint.Y, dimLine.EndPoint.X - dimLine.StartPoint.X);
		double want = Math.Atan2(40, 100);
		Assert.True(Math.Abs(Math.Sin(ang - want)) < 1e-6, $"dim line angle {ang * 180 / Math.PI:0.##}");
	}
}

public class LinearSideTests
{
	[Theory]
	[InlineData(50, 80, 0)]
	[InlineData(50, -40, 0)]
	[InlineData(-30, 20, 90)]
	[InlineData(140, 20, 90)]
	[InlineData(50, 90, 30)]
	[InlineData(50, -60, 30)]
	public void Linear_DimLineParallel(double x, double y, double rotDeg)
	{
		var doc = EditorDocument.New();
		var cmd = new DimLinearCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 40);
		if (rotDeg == 90) cmd.OnText("V");
		else if (rotDeg == 0) cmd.OnText("H");
		else { cmd.OnText("R"); cmd.OnText(rotDeg.ToString()); }
		cmd.OnPoint(x, y);
		var d = doc.Document.Entities.OfType<DimensionLinear>().Single();
		var lines = d.Block!.Entities.OfType<Line>().ToList();
		double want = rotDeg * Math.PI / 180;
		// 치수선(가장 긴 선 중 방향이 맞는 것)이 있어야 하고, 두 보조선 끝이 치수선 위에 있어야 한다
		Assert.Contains(lines, l => Math.Abs(Math.Sin(Math.Atan2(l.EndPoint.Y - l.StartPoint.Y, l.EndPoint.X - l.StartPoint.X) - want)) < 1e-6
			&& Math.Sqrt(Math.Pow(l.EndPoint.X - l.StartPoint.X, 2) + Math.Pow(l.EndPoint.Y - l.StartPoint.Y, 2)) > 10);
		// 치수선이 클릭한 점을 지난다
		var p = d.DefinitionPoint;
		double nx = -Math.Sin(want), ny = Math.Cos(want);
		Assert.Equal((x - p.X) * nx + (y - p.Y) * ny, 0, 6);
	}
}
