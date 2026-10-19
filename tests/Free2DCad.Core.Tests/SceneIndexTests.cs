using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Rendering;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

public class SceneIndexTests
{
	[Fact]
	public void Pick_And_Snap_And_SelectWindow_WorkCorrectly()
	{
		var doc = new CadDocument();

		// LINE: (0, 0) -> (100, 0)
		var line = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		doc.Entities.Add(line);

		// CIRCLE: Center (50, 50), Radius 20
		var circle = new Circle
		{
			Center = new XYZ(50, 50, 0),
			Radius = 20
		};
		doc.Entities.Add(circle);

		// LWPOLYLINE: (200, 0) -> (250, 50) -> (300, 0)
		var poly = new LwPolyline();
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(200, 0)));
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(250, 50)));
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(300, 0)));
		doc.Entities.Add(poly);

		// Another LINE intersecting the first line at (50, 0): (50, -50) -> (50, 50)
		var lineCross = new Line(new XYZ(50, -50, 0), new XYZ(50, 50, 0));
		doc.Entities.Add(lineCross);

		var scene = SceneBuilder.Build(doc);
		var index = new SceneIndex(scene);

		// 1. 선 근처 클릭 시 Pick이 그 선을 반환
		var pickedLine = index.Pick(20, 0.5, 2.0);
		Assert.Same(line, pickedLine);

		// 2. 빈 곳 클릭 시 null
		var pickedEmpty = index.Pick(10, 80, 2.0);
		Assert.Null(pickedEmpty);

		// 3. 윈도우 선택과 크로싱 선택의 결과가 다름 (선에 걸치기만 한 사각형)
		// 사각형 (40, -10) ~ (60, 10): line(0,0->100,0)과 lineCross(50,-50->50,50)을 가로지름
		var crossingBox = new Bounds(40, -10, 60, 10);
		var windowResult = index.SelectWindow(crossingBox, crossing: false);
		var crossingResult = index.SelectWindow(crossingBox, crossing: true);

		// 걸치기만 한 상태이므로 윈도우 선택에는 포함되지 않고, 크로싱 선택에는 포함되어야 함
		Assert.DoesNotContain(line, windowResult);
		Assert.Contains(line, crossingResult);
		Assert.NotEqual(windowResult.Count, crossingResult.Count);

		// 4. 선 끝점 근처에서 Endpoint 스냅 (0, 0 근처)
		var snapEnd = SnapEngine.Find(index, 0.5, 0.5, 5.0, SnapModes.Endpoint);
		Assert.NotNull(snapEnd);
		Assert.Equal(SnapKind.Endpoint, snapEnd.Value.Kind);
		Assert.Equal(0, snapEnd.Value.X, precision: 2);
		Assert.Equal(0, snapEnd.Value.Y, precision: 2);

		// 5. 원 중심 근처에서 Center 스냅 (50, 50 근처)
		var snapCenter = SnapEngine.Find(index, 51.0, 49.0, 5.0, SnapModes.Center);
		Assert.NotNull(snapCenter);
		Assert.Equal(SnapKind.Center, snapCenter.Value.Kind);
		Assert.Equal(50, snapCenter.Value.X, precision: 2);
		Assert.Equal(50, snapCenter.Value.Y, precision: 2);

		// 6. 두 선이 교차하는 지점에서 Intersection 스냅 (50, 0 근처)
		var snapIntersect = SnapEngine.Find(index, 50.8, 0.5, 5.0, SnapModes.Intersection);
		Assert.NotNull(snapIntersect);
		Assert.Equal(SnapKind.Intersection, snapIntersect.Value.Kind);
		Assert.Equal(50, snapIntersect.Value.X, precision: 2);
		Assert.Equal(0, snapIntersect.Value.Y, precision: 2);
	}

	[Fact]
	public void SampleDrawing_BuildIndex_And_Pick_Performance()
	{
		string samplePath = FindSamplePath("test-1.dwg");
		if (samplePath == null) return;
		CadDocument doc = DwgReader.Read(samplePath);
		var scene = SceneBuilder.Build(doc);

		// 1. SceneIndex 생성 시간 500 ms 미만 검증
		var sw = Stopwatch.StartNew();
		var index = new SceneIndex(scene);
		sw.Stop();
		Assert.True(sw.ElapsedMilliseconds < 500, $"SceneIndex build took {sw.ElapsedMilliseconds} ms (expected < 500 ms)");

		// 2. Pick 1000번 실행 시간 200 ms 미만 검증
		double minX = scene.Extents.MinX;
		double minY = scene.Extents.MinY;
		double spanX = scene.Extents.Width;
		double spanY = scene.Extents.Height;

		sw.Restart();
		for (int i = 0; i < 1000; i++)
		{
			double qx = minX + (spanX * ((i * 37) % 1000)) / 1000.0;
			double qy = minY + (spanY * ((i * 73) % 1000)) / 1000.0;
			index.Pick(qx, qy, 5.0);
		}
		sw.Stop();
		Assert.True(sw.ElapsedMilliseconds < 200, $"1000 Picks took {sw.ElapsedMilliseconds} ms (expected < 200 ms)");
	}

	private static string FindSamplePath(string fileName)
	{
		string? current = AppContext.BaseDirectory;
		while (current != null)
		{
			string candidate = Path.Combine(current, "sample", fileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
			candidate = Path.Combine(current, "..", "..", "..", "..", "sample", fileName);
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
			current = Path.GetDirectoryName(current);
		}
		return null!; // 샘플 도면(sample/)은 공개 저장소에 없음 → 해당 테스트 건너뜀
	}

	[Fact]
	public void XLine_In_Small_Drawing_Indexes_Quickly()
	{
		// 작은 도면(범위 40) + 구성선: 칸 개수 곱이 int로 넘쳐 수십억 칸을 만들던 문제 (앱이 멈추고 메모리 수십 GB)
		var doc = EditorDocument.New();
		doc.AddEntities(new ACadSharp.Entities.Line(new CSMath.XYZ(0, 0, 0), new CSMath.XYZ(40, 0, 0)),
			new ACadSharp.Entities.XLine { FirstPoint = new CSMath.XYZ(10, 0, 0), Direction = new CSMath.XYZ(0.7071, 0.7071, 0) });
		var scene = Free2DCad.Rendering.SceneBuilder.Build(doc.Document);
		var t = System.Threading.Tasks.Task.Run(() => new SceneIndex(scene));
		Assert.True(t.Wait(10000));
	}
}
