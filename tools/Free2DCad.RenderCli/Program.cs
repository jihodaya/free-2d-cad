// DWG/DXF → PNG 렌더링 (뷰어 렌더링 검증용)
// 사용법: Free2DCad.RenderCli <입력.dwg> <출력.png> [폭=3000] [--window minX minY maxX maxY]
using System.Diagnostics;
using System.Globalization;
using ACadSharp;
using ACadSharp.IO;
using Free2DCad.Rendering;
using SkiaSharp;

if (args.Length < 2)
{
	Console.Error.WriteLine("사용법: Free2DCad.RenderCli <입력.dwg|dxf> <출력.png> [폭=3000] [--window minX minY maxX maxY]");
	return 1;
}

string input = args[0];
string output = args[1];
int width = args.Length > 2 && int.TryParse(args[2], out int w) ? w : 3000;
Bounds? window = null;
int wi = Array.IndexOf(args, "--window");
if (wi >= 0 && args.Length >= wi + 5)
{
	double P(int i) => double.Parse(args[wi + i], CultureInfo.InvariantCulture);
	window = new Bounds(P(1), P(2), P(3), P(4));
}

var sw = Stopwatch.StartNew();
CadDocument doc = input.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? DxfReader.Read(input) : DwgReader.Read(input);
MTextColumns.MergeSplit(doc); // 앱에서 열 때와 같게 (나눠 저장한 열 문자 합치기)
long tRead = sw.ElapsedMilliseconds;

Scene scene = SceneBuilder.Build(doc);
long tBuild = sw.ElapsedMilliseconds - tRead;

// --pdf [mono]: 출력 파일을 A3 가로 PDF로 (용지 맞춤). --window가 있으면 그 영역만
if (args.Contains("--pdf"))
{
	var opts = new PlotOptions(window ?? scene.Extents, 420, 297,
		Colors: args.Contains("mono") ? PlotColorMode.Monochrome : PlotColorMode.Color);
	PlotRenderer.ExportPdf(output, opts, scene, null);
	Console.WriteLine($"PDF 저장: {output} (축척 1:{1 / PlotRenderer.EffectiveMmPerUnit(opts):G4}, {new FileInfo(output).Length / 1024:N0} KB)");
	return 0;
}

// --layout 이름: 배치(도면 공간)를 그린다
int li = Array.IndexOf(args, "--layout");
if (li >= 0 && args.Length > li + 1)
{
	var layout = doc.Layouts.FirstOrDefault(l => l.Name.Equals(args[li + 1], StringComparison.OrdinalIgnoreCase));
	if (layout == null)
	{
		Console.Error.WriteLine("배치 없음. 목록: " + string.Join(", ", doc.Layouts.Select(l => l.Name)));
		return 1;
	}
	var ls = LayoutScene.Build(doc, layout);
	Bounds la = ls.Extents;
	int lh = Math.Max(1, (int)Math.Round(width * la.Height / Math.Max(la.Width, 1e-9)));
	var lview = ViewTransform.Fit(la, width, lh, 0.03);
	using var modelR = new SkiaSceneRenderer(scene);
	using var lr = new LayoutRenderer(ls, modelR);
	using var lsurf = SKSurface.Create(new SKImageInfo(width, lh));
	lr.Render(lsurf.Canvas, lview);
	using (var img = lsurf.Snapshot())
	using (var d = img.Encode(SKEncodedImageFormat.Png, 90))
	using (var f = File.Create(output))
	{
		d.SaveTo(f);
	}
	Console.WriteLine($"배치 '{layout.Name}': 용지 {ls.Paper.Width:F0}x{ls.Paper.Height:F0}, 뷰포트 {ls.Viewports.Count}개, 도면 공간 선 {ls.PaperScene.Polylines.Count} → {output}");
	foreach (var vp in ls.Viewports)
	{
		Console.WriteLine($"  뷰포트 ({vp.Rect.MinX:F0},{vp.Rect.MinY:F0})~({vp.Rect.MaxX:F0},{vp.Rect.MaxY:F0}) 축척 1:{1 / vp.Scale:G4} 모형중심 ({vp.ModelCenterX:F0},{vp.ModelCenterY:F0})");
	}
	return 0;
}

Bounds area = window ?? scene.Extents;
int height = Math.Max(1, (int)Math.Round(width * area.Height / Math.Max(area.Width, 1e-9)));
height = Math.Min(height, 20000);
var view = ViewTransform.Fit(area, width, height, window == null ? 0.02 : 0);

using var renderer = new SkiaSceneRenderer(scene);
long tBatch = sw.ElapsedMilliseconds - tRead - tBuild;

using var surface = SKSurface.Create(new SKImageInfo(width, height));
var t0 = sw.ElapsedMilliseconds;
renderer.Render(surface.Canvas, view);
long tRender = sw.ElapsedMilliseconds - t0;

using (var image = surface.Snapshot())
using (var data = image.Encode(SKEncodedImageFormat.Png, 90))
using (var fs = File.Create(output))
{
	data.SaveTo(fs);
}

Console.WriteLine($"{Path.GetFileName(input)}: 선 {scene.Polylines.Count:N0}, 채움 {scene.Fills.Count:N0}, 텍스트 {scene.Texts.Count:N0}");
Console.WriteLine($"  범위 ({scene.Extents.MinX:F1}, {scene.Extents.MinY:F1}) ~ ({scene.Extents.MaxX:F1}, {scene.Extents.MaxY:F1})");
Console.WriteLine($"  읽기 {tRead} ms, 장면 구성 {tBuild} ms, 배치 {tBatch} ms, 렌더 {tRender} ms → {output} ({width}x{height})");

// --probe N: 폭이나 높이가 N보다 큰 도형과 그 원본 엔티티 출력 (범위를 넓히는 원인 추적용)
int pi = Array.IndexOf(args, "--probe");
if (pi >= 0 && args.Length > pi + 1)
{
	double limit = double.Parse(args[pi + 1], CultureInfo.InvariantCulture);
	// --probe N [minY maxY]: 세로 범위 밖으로 벗어난 항목도 함께 출력
	double outMinY = args.Length > pi + 3 ? double.Parse(args[pi + 2], CultureInfo.InvariantCulture) : double.NegativeInfinity;
	double outMaxY = args.Length > pi + 3 ? double.Parse(args[pi + 3], CultureInfo.InvariantCulture) : double.PositiveInfinity;
	var big = scene.Polylines.Select(p => (Kind: "선", p.Bounds, p.Source))
		.Concat(scene.Fills.Select(f => (Kind: "채움", f.Bounds, f.Source)))
		.Concat(scene.Texts.Select(t => (Kind: "텍스트", t.Bounds, t.Source)))
		.Where(x => x.Bounds.Width > limit || x.Bounds.Height > limit || x.Bounds.MaxY > outMaxY || x.Bounds.MinY < outMinY);
	foreach (var g in big.GroupBy(x => x.Source).Take(20))
	{
		var b = g.First().Bounds;
		Console.WriteLine($"  [probe] {g.First().Kind} x{g.Count()} ← {g.Key?.GetType().Name} #{g.Key?.Handle:X} layer={g.Key?.Layer?.Name} ({b.MinX:F0},{b.MinY:F0})~({b.MaxX:F0},{b.MaxY:F0})");
	}
}
return 0;
