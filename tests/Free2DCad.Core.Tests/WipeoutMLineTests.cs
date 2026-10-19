using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using SkiaSharp;
using Xunit;

namespace Free2DCad.Core.Tests;

public class WipeoutMLineTests
{
	[Fact]
	public void Wipeout_MasksEarlierOnly_AndMLineBuilds()
	{
		var doc = EditorDocument.New();
		var before = new Line(new XYZ(0, 50, 0), new XYZ(200, 50, 0)); // 가려져야 함
		doc.AddEntities(before);
		var wo = WipeoutCommand.Create(new List<XY> { new(50, 20), new(150, 20), new(150, 80), new(50, 80) });
		doc.AddEntities(wo);
		var after = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0)); // 보여야 함
		doc.AddEntities(after);
		var ml = MLineCommand.Build(new List<XY> { new(0, 150), new(100, 150), new(150, 200) }, false, ACadSharp.Objects.MLineStyle.Default, 20, ACadSharp.Entities.MLineJustification.Top)!;
		doc.AddEntities(ml);
		Assert.Equal(3, ml.Vertices.Count);
		// 위 맞춤: 첫 요소(+0.5) 거리 0, 둘째(−0.5) 거리 −20 (직선 구간)
		Assert.Equal(0, ml.Vertices[0].Segments[0].Parameters[0], 6);
		Assert.Equal(-20, ml.Vertices[0].Segments[1].Parameters[0], 6);

		var scene = SceneBuilder.Build(doc.Document);
		Assert.Single(scene.Wipeouts);
		var w = scene.Wipeouts[0];
		Assert.Equal(50, w.Bounds.MinX, 6);
		Assert.Equal(80, w.Bounds.MaxY, 6);

		using var renderer = new SkiaSceneRenderer(scene);
		var view = ViewTransform.Fit(scene.Extents, 400, 400);
		using var bmp = new SKBitmap(400, 400);
		using var canvas = new SKCanvas(bmp);
		renderer.Render(canvas, view);
		var (sx, sy) = view.ToScreen(120, 50); // 가려진 선 위
		var (ax, ay) = view.ToScreen(100, 60); // 나중 선 위
		Assert.True(bmp.GetPixel((int)sx, (int)sy).Red < 40, "earlier line should be masked");
		Assert.True(bmp.GetPixel((int)Math.Round(ax), (int)ay).Red > 100 || bmp.GetPixel((int)Math.Round(ax) - 1, (int)ay).Red > 100 || bmp.GetPixel((int)Math.Round(ax) + 1, (int)ay).Red > 100, "later line should be visible");

		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		if (dir != null)
		{
			doc.Save(Path.Combine(dir, "audit_wipeout_mline_2004.dwg"), ACadSharp.ACadVersion.AC1018);
			doc.Save(Path.Combine(dir, "audit_wipeout_mline_2018.dxf"), ACadSharp.ACadVersion.AC1032);
		}
		string? png = Environment.GetEnvironmentVariable("F2D_PNG_OUT");
		if (png != null)
		{
			using var img = SKImage.FromBitmap(bmp);
			File.WriteAllBytes(Path.Combine(png, "wipeout.png"), img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
		}
	}
}
