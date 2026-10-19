using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using SkiaSharp;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>각도 치수 모양 확인용 그림 (F2D_PNG_OUT 폴더가 있을 때만 PNG 저장). 기존 CAD 견본과 같은 세 경우.</summary>
public class AngularVisualTests
{
	private const double S = 0.15;

	private static Line L(double x1, double y1, double x2, double y2) => new(new XYZ(x1 * S, -y1 * S, 0), new XYZ(x2 * S, -y2 * S, 0));

	[Fact]
	public void ThreeCases_Render()
	{
		var doc = EditorDocument.New();
		var a = L(68, 108, 325, 370);
		var b = L(325, 370, 513, 133);
		var c = L(513, 133, 890, 500);
		var d = L(667, 510, 1007, 115);
		doc.AddEntities(a, b, c, d);
		void Dim(Line l1, Line l2, double ax, double ay)
		{
			var queue = new Queue<Entity>(new[] { l1, l2 });
			var ctx = new CommandContext(doc) { Picker = (_, _) => queue.Dequeue() };
			var cmd = new DimAngularCommand();
			cmd.Start(ctx);
			cmd.OnPoint(0, 0);
			cmd.OnPoint(0, 0);
			cmd.OnPoint(ax * S, -ay * S);
			Assert.True(cmd.IsFinished);
		}
		Dim(a, b, 313, 170);
		Dim(b, c, 520, 345);
		Dim(c, d, 780, 545);
		var e1 = L(1100, 500, 1400, 500);
		var e2 = L(1100, 500, 1100, 200);
		doc.AddEntities(e1, e2);
		Dim(e1, e2, 1250, 350);
		var dims = doc.Document.Entities.OfType<Dimension>().ToList();
		Assert.Equal(4, dims.Count);

		string? dir = Environment.GetEnvironmentVariable("F2D_PNG_OUT");
		if (dir == null) return;
		var scene = SceneBuilder.Build(doc.Document);
		using var renderer = new SkiaSceneRenderer(scene);
		var view = ViewTransform.Fit(scene.Extents, 1160, 580);
		using var bmp = new SKBitmap(1160, 580);
		using var canvas = new SKCanvas(bmp);
		renderer.Render(canvas, view);
		using var img = SKImage.FromBitmap(bmp);
		using var data = img.Encode(SKEncodedImageFormat.Png, 100);
		File.WriteAllBytes(Path.Combine(dir, "angular.png"), data.ToArray());
		File.WriteAllText(Path.Combine(dir, "angular.txt"), string.Join("\n", dims.Select(x => x.GetMeasurementText() + " | " + string.Join(", ", x.Block!.Entities.Select(e => e switch
		{
			Arc arc => $"Arc c=({arc.Center.X:0.##},{arc.Center.Y:0.##}) r={arc.Radius:0.##} {arc.StartAngle * 180 / Math.PI:0.#}->{arc.EndAngle * 180 / Math.PI:0.#}",
			Line ln => $"Line ({ln.StartPoint.X:0.#},{ln.StartPoint.Y:0.#})-({ln.EndPoint.X:0.#},{ln.EndPoint.Y:0.#})",
			_ => e.ObjectName,
		})) + $" | center={((DimensionAngular2Line)x).Center} arcpt={((DimensionAngular2Line)x).DimensionArc}")));
	}
}
