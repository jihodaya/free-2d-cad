using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>
/// 선형 치수 배치 (기존 CAD test-2.dwg와 기존 CAD DIMLINEAR로 만든 치수 블록과 대조한 값):
/// 문자는 치수선 위로 간격 + 상자 높이/2, 수직 치수의 수평 문자는 선 위 가운데(선을 끊음), 좁으면 화살촉을 밖으로.
/// </summary>
public class DimLayoutTests
{
	private static DimensionStyle KrStyle() => new("KR Test")
	{
		TextVerticalAlignment = DimensionTextVerticalAlignment.Above,
		TextInsideHorizontal = true,
		TextOutsideHorizontal = true,
		DimensionLineGap = 1,
		ArrowSize = 3.5,
		TextHeight = 3.5,
		ExtensionLineExtension = 1,
		ExtensionLineOffset = 1,
		DecimalPlaces = 1,
	};

	private static DimensionLinear Make(EditorDocument doc, XYZ p1, XYZ p2, XYZ def, double rot)
	{
		// 새 도면의 기본 치수 스타일 ISO-25 KR (문자 스타일 Malgun = malgun.ttf, 기존 CAD와 같은 글꼴로 재야 값이 맞는다)
		if (!doc.Document.DimensionStyles.TryGetValue("ISO-25 KR", out var st))
		{
			st = KrStyle();
			doc.Document.DimensionStyles.Add(st);
		}
		var dim = new DimensionLinear { FirstPoint = p1, SecondPoint = p2, DefinitionPoint = def, Rotation = rot };
		doc.AddEntities(dim);
		dim.Style = st;
		dim.UpdateBlock();
		return dim;
	}

	private static List<Line> DimLines(Dimension d, bool vertical) =>
		d.Block.Entities.OfType<Line>().Where(l => vertical ? Math.Abs(l.StartPoint.X - l.EndPoint.X) < 1e-9 : Math.Abs(l.StartPoint.Y - l.EndPoint.Y) < 1e-9).ToList();

	[Fact]
	public void Horizontal_TextAboveLine_LineBetweenArrowBases()
	{
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(0, 0, 0), new XYZ(31.5, 0, 0), new XYZ(31.5, 20, 0), 0);
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(15.75, text.InsertPoint.X, 6);
		Assert.Equal(20 + 2.81825, text.InsertPoint.Y, 2); // 기존 CAD DIMLINEAR "31.5": 2.81825 (간격 + 글자 모양 높이/2)
		var line = DimLines(dim, vertical: false).Single(l => Math.Abs(l.StartPoint.Y - 20) < 1e-9);
		Assert.Equal(3.5, Math.Min(line.StartPoint.X, line.EndPoint.X), 6);
		Assert.Equal(28.0, Math.Max(line.StartPoint.X, line.EndPoint.X), 6);
		Assert.Equal(2, dim.Block.Entities.OfType<Solid>().Count());
	}

	[Fact]
	public void Vertical_HorizontalText_CentredAndLineBroken()
	{
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(0, 31.5, 0), new XYZ(0, 0, 0), new XYZ(12, 0, 0), Math.PI / 2);
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(12, text.InsertPoint.X, 6); // 선 위 가운데
		Assert.Equal(15.75, text.InsertPoint.Y, 6);
		var lines = DimLines(dim, vertical: true).Where(l => Math.Abs(l.StartPoint.X - 12) < 1e-9).ToList();
		Assert.Equal(2, lines.Count); // 문자 자리에서 끊김
		double gapLow = lines.Max(l => Math.Min(l.StartPoint.Y, l.EndPoint.Y));
		double gapHigh = lines.Min(l => Math.Max(l.StartPoint.Y, l.EndPoint.Y));
		Assert.Equal(5.6365, gapLow - gapHigh, 2); // CAD DIMLINEAR "31.5": 18.56825 − 12.93175
	}

	[Fact]
	public void Narrow_ArrowsOutsideWithStubs()
	{
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(10, 0, 0), new XYZ(10, 5, 0), new XYZ(0, 5, 0), Math.PI / 2);
		var solids = dim.Block.Entities.OfType<Solid>().ToList();
		Assert.Equal(2, solids.Count);
		// 화살촉 끝은 보조선(y=0, y=5), 몸통은 바깥쪽 (y<0, y>5)
		Assert.Contains(solids, s => Math.Abs(s.FirstCorner.Y) < 1e-9 && s.SecondCorner.Y < -3);
		Assert.Contains(solids, s => Math.Abs(s.FirstCorner.Y - 5) < 1e-9 && s.SecondCorner.Y > 8);
		var lines = DimLines(dim, vertical: true).Where(l => Math.Abs(l.StartPoint.X) < 1e-9).ToList();
		Assert.Contains(lines, l => Math.Abs(Math.Min(l.StartPoint.Y, l.EndPoint.Y) + 7) < 1e-6 && Math.Abs(Math.Max(l.StartPoint.Y, l.EndPoint.Y) + 3.5) < 1e-6);
		Assert.Contains(lines, l => Math.Abs(Math.Min(l.StartPoint.Y, l.EndPoint.Y) - 8.5) < 1e-6 && Math.Abs(Math.Max(l.StartPoint.Y, l.EndPoint.Y) - 12) < 1e-6);
		// 보조선 사이에는 치수선이 없음 (DIMTOFL 꺼짐)
		Assert.DoesNotContain(lines, l => Math.Min(l.StartPoint.Y, l.EndPoint.Y) >= -1e-9 && Math.Max(l.StartPoint.Y, l.EndPoint.Y) <= 5 + 1e-9);
	}

	[Fact]
	public void UserText_StoredOnLine_DrawnAbove()
	{
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(0, 0, 0), new XYZ(5, 0, 0), new XYZ(5, -4, 0), 0);
		Free2DCad.Core.Editing.DimensionGeometry.MoveText(dim, new XYZ(-10, -4, 0));
		dim.UpdateBlock();
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(-4, dim.TextMiddlePoint.Y, 6);
		Assert.Equal(-4 + 2.80775, text.InsertPoint.Y, 2); // CAD "5.0": 2.80775
		// 치수선이 문자 밑까지 늘어남
		var lines = DimLines(dim, vertical: false).Where(l => Math.Abs(l.StartPoint.Y + 4) < 1e-9).ToList();
		Assert.True(lines.Min(l => Math.Min(l.StartPoint.X, l.EndPoint.X)) < -10);
	}

	[Fact]
	public void NarrowVertical_TextOnLanding()
	{
		// 기존 CAD DIMLINEAR 100,40 → 100,45 → 88,45: 짧은 선 48.5→52, 받침선 (88,52)→(94.83,52), 문자 (91.91, 54.81)
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(100, 40, 0), new XYZ(100, 45, 0), new XYZ(88, 45, 0), Math.PI / 2);
		var lines = dim.Block.Entities.OfType<Line>().ToList();
		Assert.Contains(lines, l => Math.Abs(l.StartPoint.X - 88) < 1e-6 && Math.Abs(l.StartPoint.Y - 52) < 1e-6 && Math.Abs(l.EndPoint.Y - 52) < 1e-6 && Math.Abs(l.EndPoint.X - 94.8275) < 0.05);
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(91.91375, text.InsertPoint.X, 1);
		Assert.Equal(54.80775, text.InsertPoint.Y, 2);
	}

	[Fact]
	public void Radius_OutsideText_LeaderAndLanding()
	{
		// 기존 CAD test-2.dwg "6-R3.0": 지시선 (377.151,308.301)→(374.817,310.909), 받침선 → 360.217, 문자 (367.017, 313.717)
		var doc = EditorDocument.New();
		var st = doc.Document.DimensionStyles["ISO-25 KR"];
		var dim = new DimensionRadius
		{
			DefinitionPoint = new XYZ(381.48674331902190, 303.45834566188387, 0),
			AngleVertex = new XYZ(379.48583541622133, 305.6936012545425, 0),
			TextMiddlePoint = new XYZ(367.01677857055665, 313.71745171349914, 0),
			Text = "6-<>",
		};
		doc.AddEntities(dim);
		dim.Style = st;
		dim.UpdateBlock();
		var lines = dim.Block.Entities.OfType<Line>().ToList();
		Assert.Contains(lines, l => Math.Abs(l.EndPoint.X - 374.817) < 0.01 && Math.Abs(l.EndPoint.Y - 310.909) < 0.01 && Math.Abs(l.StartPoint.X - 377.151) < 0.01);
		Assert.Contains(lines, l => Math.Abs(l.StartPoint.Y - 310.909) < 0.01 && Math.Abs(l.EndPoint.Y - 310.909) < 0.01 && Math.Abs(l.EndPoint.X - 360.217) < 0.05);
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal("6-R3.0", text.Value);
		Assert.Equal(367.017, text.InsertPoint.X, 1);
		Assert.Equal(313.717, text.InsertPoint.Y, 2);
	}

	[Fact]
	public void DimRegen_RebuildsAndUndoes()
	{
		var doc = EditorDocument.New();
		var dim = Make(doc, new XYZ(0, 0, 0), new XYZ(31.5, 0, 0), new XYZ(31.5, 20, 0), 0);
		// 예전 모양 흉내: 블록을 비움
		foreach (var e in dim.Block.Entities.ToList()) dim.Block.Entities.Remove(e);
		var cmd = Free2DCad.Core.Commands.CommandRegistry.Create("DIMREGEN")!;
		var lines = new List<string>();
		cmd.Start(new Free2DCad.Core.Commands.CommandContext(doc, print: lines.Add));
		Assert.Contains(lines, l => l.Contains("치수 1개"));
		Assert.Single(dim.Block.Entities.OfType<MText>());
		doc.Undo();
		Assert.Empty(dim.Block.Entities);
		doc.Redo();
		Assert.Single(dim.Block.Entities.OfType<MText>());
	}

	// ===== 공차·한계·대체 단위·기본 (기존 CAD가 다시 그린 "ISO-25 KR" 30 길이 치수 블록과 대조) =====

	private static DimensionLinear MakeStyled(Action<DimensionStyle> setup)
	{
		var doc = EditorDocument.New();
		if (!doc.Document.DimensionStyles.TryGetValue("ISO-25 KR", out var st))
		{
			st = KrStyle();
			doc.Document.DimensionStyles.Add(st);
		}
		setup(st);
		return Make(doc, new XYZ(0, 0, 0), new XYZ(30, 0, 0), new XYZ(30, 10, 0), 0);
	}

	private static string TextOf(Dimension d) => d.Block.Entities.OfType<MText>().Single().Value;

	[Fact]
	public void Tolerance_Deviation_Stacked()
	{
		var dim = MakeStyled(st => { st.GenerateTolerances = true; st.PlusTolerance = 0.1; st.MinusTolerance = 0.2; st.ToleranceDecimalPlaces = 1; });
		Assert.Equal(@"\A1;30.0\S+0.1^-0.2;", TextOf(dim));
		Assert.Equal(10 + 5.239, dim.Block.Entities.OfType<MText>().Single().InsertPoint.Y, 0); // 쌓인 문자 높이만큼 위로
	}

	[Fact]
	public void Tolerance_Symmetric_PlusMinus()
	{
		var dim = MakeStyled(st => { st.GenerateTolerances = true; st.PlusTolerance = 0.1; st.MinusTolerance = 0.1; st.ToleranceDecimalPlaces = 1; });
		Assert.Equal("30.0%%p0.1", TextOf(dim));
	}

	[Fact]
	public void Limits_UpperOverLower()
	{
		var dim = MakeStyled(st => { st.LimitsGeneration = true; st.PlusTolerance = 0.1; st.MinusTolerance = 0.2; });
		Assert.Equal(@"\A1;\S30.1^29.8;", TextOf(dim));
	}

	[Fact]
	public void AlternateUnits_Bracketed_TextOutsidePastArrow()
	{
		var dim = MakeStyled(st => { st.AlternateUnitDimensioning = true; st.AlternateUnitDecimalPlaces = 2; st.AlternateUnitScaleFactor = 25.4; st.AlternateDimensioningSuffix = "mm"; });
		Assert.Equal("30.0 [762.00mm]", TextOf(dim));
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(52.5145, text.InsertPoint.X, 0); // 둘째 보조선 + 화살촉 + 간격부터 (화살촉이 안이어도)
		double far = DimLines(dim, vertical: false).Max(l => Math.Max(l.StartPoint.X, l.EndPoint.X));
		Assert.Equal(71.529, far, 0); // 치수선을 문자 끝 + 간격까지
	}

	[Fact]
	public void Basic_BoxAroundText()
	{
		var dim = MakeStyled(st => st.DimensionLineGap = -1);
		var text = dim.Block.Entities.OfType<MText>().Single();
		Assert.Equal(10 + 3.808, text.InsertPoint.Y, 1); // 간격 두 배 + 높이/2
		var box = dim.Block.Entities.OfType<Line>().Where(l => l.StartPoint.Y > 10.5 || l.EndPoint.Y > 10.5).Where(l => l.StartPoint.X > 5 && l.StartPoint.X < 25).ToList();
		Assert.Equal(4, box.Count);
		Assert.Equal(11.0, box.Min(l => Math.Min(l.StartPoint.Y, l.EndPoint.Y)), 1);
		Assert.Equal(16.6155, box.Max(l => Math.Max(l.StartPoint.Y, l.EndPoint.Y)), 1);
	}
}
