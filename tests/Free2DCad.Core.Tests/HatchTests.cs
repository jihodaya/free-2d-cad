using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Hatching;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

public class HatchTests
{
	private static double Area(double[] loop)
	{
		double a = 0;
		int n = loop.Length / 2;
		for (int i = 0, j = n - 1; i < n; j = i++)
		{
			a += loop[2 * j] * loop[2 * i + 1] - loop[2 * i] * loop[2 * j + 1];
		}
		return Math.Abs(a / 2);
	}

	private static EditorDocument RectOfLines(double w = 100, double h = 50)
	{
		var doc = EditorDocument.New();
		// 선 4개로 된 직사각형 (한 변은 조금 삐져나오게 해서 교차점 분할도 확인)
		doc.AddEntities(
			new Line(new XYZ(-10, 0, 0), new XYZ(w, 0, 0)),
			new Line(new XYZ(w, 0, 0), new XYZ(w, h, 0)),
			new Line(new XYZ(w, h, 0), new XYZ(0, h, 0)),
			new Line(new XYZ(0, h + 10, 0), new XYZ(0, -10, 0)));
		return doc;
	}

	[Fact]
	public void PatternLibrary_LoadsStandardPatterns()
	{
		Assert.True(HatchPatternLibrary.All.Count >= 30);
		Assert.Single(HatchPatternLibrary.Get("ANSI31")!.Lines);
		Assert.Equal(2, HatchPatternLibrary.Get("ANSI37")!.Lines.Count);
		Assert.Empty(HatchPatternLibrary.Get("SOLID")!.Lines);
		Assert.Contains(HatchPatternLibrary.ByCategory("ANSI"), p => p.Name == "ANSI38");
		Assert.Contains(HatchPatternLibrary.ByCategory("ISO"), p => p.Name == "ISO02");
		Assert.NotEmpty(HatchPatternLibrary.PreviewSegments("ANSI31", 20, 20, 1));
	}

	[Fact]
	public void BoundaryFinder_FindsRegionFormedBySeparateLines()
	{
		var doc = RectOfLines();
		var scene = SceneBuilder.Build(doc.Document);
		var loops = HatchBoundaryFinder.FindAt(scene, 30, 20, HatchStyleType.Outer);
		Assert.NotNull(loops);
		Assert.Single(loops!);
		Assert.Equal(5000, Area(loops![0]), 3);
	}

	[Fact]
	public void BoundaryFinder_ReturnsNullOutside()
	{
		var doc = RectOfLines();
		var scene = SceneBuilder.Build(doc.Document);
		Assert.Null(HatchBoundaryFinder.FindAt(scene, 300, 20, HatchStyleType.Outer));
	}

	[Fact]
	public void BoundaryFinder_IslandStyles()
	{
		var doc = RectOfLines(200, 200);
		doc.AddEntities(
			new Circle { Center = new XYZ(100, 100, 0), Radius = 50 },
			new Circle { Center = new XYZ(100, 100, 0), Radius = 20 });
		var scene = SceneBuilder.Build(doc.Document);

		Assert.Equal(3, HatchBoundaryFinder.FindAt(scene, 10, 10, HatchStyleType.Normal)!.Count);
		Assert.Equal(2, HatchBoundaryFinder.FindAt(scene, 10, 10, HatchStyleType.Outer)!.Count);
		Assert.Single(HatchBoundaryFinder.FindAt(scene, 10, 10, HatchStyleType.Ignore)!);

		// 원과 원 사이(고리)를 클릭하면 바깥 원이 경계, 안쪽 원이 섬
		var ring = HatchBoundaryFinder.FindAt(scene, 100 + 35, 100, HatchStyleType.Outer)!;
		Assert.Equal(2, ring.Count);
		Assert.InRange(Area(ring[0]), Math.PI * 50 * 50 * 0.99, Math.PI * 50 * 50 * 1.01); // 원은 다각형으로 근사
	}

	[Fact]
	public void HatchCommand_PicksPointsThenEnter_CreatesPatternHatch_AndUndo()
	{
		var doc = RectOfLines();
		var cmd = new HatchCommand { Settings = new HatchSettings { PatternName = "ANSI31", Scale = 2, AngleDegrees = 0 } };
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(30, 20);
		Assert.Empty(doc.Document.Entities.OfType<Hatch>()); // Enter 전에는 만들지 않음
		cmd.OnEnter();

		var hatch = Assert.Single(doc.Document.Entities.OfType<Hatch>());
		Assert.False(hatch.IsSolid);
		Assert.Equal("ANSI31", hatch.Pattern.Name);
		Assert.Equal(2, hatch.PatternScale, 6);
		// 간격 3 x 2 = 6, 45도 → 약 (100+50)/(6.35/sin45)... 선이 여러 개 생겨야 함
		Assert.True(hatch.ExplodePattern().Count() > 10);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Hatch>());
	}

	[Fact]
	public void HatchCommand_ObjectSelection_Associative()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 10 };
		doc.AddEntities(c);
		var cmd = new HatchCommand { StartWithObjects = true, Settings = new HatchSettings { PatternName = "ANSI37" } };
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(10, 0); // 원 위를 클릭
		cmd.OnEnter();
		var hatch = Assert.Single(doc.Document.Entities.OfType<Hatch>());
		Assert.True(hatch.IsAssociative);
	}

	[Fact]
	public void Hatch_SavesAndReloads()
	{
		var doc = RectOfLines();
		var hatches = HatchCommand.CreateHatches(new HatchSettings { PatternName = "ANSI32", AngleDegrees = 30 },
			new[] { HatchBoundaryFinder.FindAt(SceneBuilder.Build(doc.Document), 30, 20, HatchStyleType.Outer)! },
			Array.Empty<Entity>());
		doc.AddEntities(hatches);
		string path = Path.Combine(Path.GetTempPath(), $"f2d-hatch-{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			CadDocument loaded = DwgReader.Read(path);
			var h = Assert.Single(loaded.Entities.OfType<Hatch>());
			Assert.Equal("ANSI32", h.Pattern.Name);
			Assert.Equal(2, h.Pattern.Lines.Count);
			Assert.Single(h.Paths);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void PatternLibrary_RegistersPatternsFromOpenedDrawing()
	{
		// 라이브러리에 없는 패턴 이름으로 해치를 만들어 저장 → 다시 열면 '사용자' 탭에 등록
		var doc = RectOfLines();
		var hatch = HatchCommand.CreateHatches(new HatchSettings { PatternName = "ANSI31", Scale = 2, AngleDegrees = 30 },
			new[] { HatchBoundaryFinder.FindAt(SceneBuilder.Build(doc.Document), 30, 20, HatchStyleType.Outer)! },
			Array.Empty<Entity>())[0];
		string name = "TESTPAT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
		hatch.Pattern.Name = name;
		doc.AddEntities(hatch);
		string path = Path.Combine(Path.GetTempPath(), $"f2d-pat-{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			EditorDocument.Open(path);
			var info = Assert.Single(HatchPatternLibrary.ByCategory("사용자"), p => p.Name == name);
			var basePat = HatchPatternLibrary.Get(name)!;
			var line = Assert.Single(basePat.Lines);
			Assert.Equal(Math.PI / 4, line.Angle, 3);          // 30도 회전을 되돌려 45도
			Assert.Equal(3, Math.Abs(line.LineOffset), 3); // 축척 2를 되돌려 3
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void PatternLibrary_HasManyPatterns()
	{
		Assert.True(HatchPatternLibrary.All.Count >= 45);
		foreach (var p in HatchPatternLibrary.All.Where(p => p.Name != "SOLID" && p.Category != "사용자"))
		{
			Assert.NotEmpty(HatchPatternLibrary.Get(p.Name)!.Lines);
			Assert.True(HatchPatternLibrary.PreviewSegments(p.Name, 76, 44, 0).Count > 0, p.Name); // 견본이 비지 않음 (점 패턴 포함)
		}
	}
}
