using ACadSharp;
using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using Xunit;
using Color = ACadSharp.Color;

namespace Free2DCad.Core.Tests;

public class MoreCommandsTests
{
	[Fact]
	public void ExtendCommand_ExtendsLineToBoundary_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		// 수직 경계선 x = 100, y: 0 ~ 100
		var boundary = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		// 연장할 수평선 x: 0 ~ 50, y = 50 (오른쪽으로 연장하면 (100, 50)에 도달)
		var target = new Line(new XYZ(0, 50, 0), new XYZ(50, 50, 0));
		doc.AddEntities(boundary, target);

		var cmd = new ExtendCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnEnter(); // 모든 객체를 경계로 선택
		cmd.OnPoint(45, 50); // 끝점 부근 클릭하여 연장 실행
		cmd.OnEnter(); // 종료

		var lines = doc.Document.Entities.OfType<Line>().ToList();
		var extended = lines.FirstOrDefault(l => Math.Abs(l.StartPoint.Y - 50) < 1e-4);
		Assert.NotNull(extended);
		Assert.Equal(100, extended.EndPoint.X, precision: 2);

		// 단일 Undo로 원래 선(50, 50) 복원
		doc.Undo();
		var restoredTarget = doc.Document.Entities.OfType<Line>().FirstOrDefault(l => Math.Abs(l.StartPoint.Y - 50) < 1e-4);
		Assert.NotNull(restoredTarget);
		Assert.Equal(50, restoredTarget.EndPoint.X, precision: 2);
	}

	[Fact]
	public void FilletCommand_WithRadius_CreatesArcAndTrims_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		// 수평선 (0,0) -> (100,0)
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		// 수직선 (100,0) -> (100,100)
		var l2 = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		doc.AddEntities(l1, l2);

		var cmd = new FilletCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnText("R");
		cmd.OnText("10"); // 반지름 10 지정
		cmd.OnPoint(50, 0); // l1 클릭
		cmd.OnPoint(100, 50); // l2 클릭

		Assert.True(cmd.IsFinished);
		var arc = Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(10, arc.Radius, precision: 2);

		// 단일 Undo로 모깎기 전 원래 선들 복원
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void ChamferCommand_CreatesChamferLine_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(100, 0, 0), new XYZ(100, 100, 0));
		doc.AddEntities(l1, l2);

		var cmd = new ChamferCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnText("D");
		cmd.OnText("15");
		cmd.OnText("15");
		cmd.OnPoint(50, 0);
		cmd.OnPoint(100, 50);

		Assert.True(cmd.IsFinished);
		// trim1, trim2, chamferLine 총 3개의 Line
		Assert.Equal(3, doc.Document.Entities.OfType<Line>().Count());

		// 단일 Undo로 원래 두 선 복원
		doc.Undo();
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void BreakCommand_SplitsLine_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var l = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		doc.AddEntities(l);

		var cmd = new BreakCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(20, 0); // 객체 선택 및 1번째 점
		cmd.OnText("F"); // 첫 번째 점 다시 지정
		cmd.OnPoint(30, 0); // 첫 번째 점: 30
		cmd.OnPoint(70, 0); // 두 번째 점: 70

		Assert.True(cmd.IsFinished);
		var lines = doc.Document.Entities.OfType<Line>().ToList();
		Assert.Equal(2, lines.Count); // (0~30), (70~100)

		doc.Undo();
		var restored = Assert.Single(doc.Document.Entities.OfType<Line>());
		Assert.Equal(100, restored.EndPoint.X, precision: 2);
	}

	[Fact]
	public void BreakCommand_BreaksCircleIntoArc_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(50, 50, 0), Radius = 20 };
		doc.AddEntities(c);

		var cmd = new BreakCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(70, 50); // (50+20, 50) -> 각도 0
		cmd.OnPoint(50, 70); // (50, 50+20) -> 각도 90

		Assert.True(cmd.IsFinished);
		Assert.Empty(doc.Document.Entities.OfType<Circle>().Where(e => e is not Arc));
		var arc = Assert.Single(doc.Document.Entities.OfType<Arc>());
		Assert.Equal(20, arc.Radius, precision: 2);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Arc>());
		Assert.Single(doc.Document.Entities.OfType<Circle>());
	}

	[Fact]
	public void JoinCommand_MergesCollinearLines_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(50, 0, 0));
		var l2 = new Line(new XYZ(50, 0, 0), new XYZ(100, 0, 0));
		doc.AddEntities(l1, l2);

		var cmd = new JoinCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnSelection(new[] { l1, l2 });
		cmd.OnEnter();

		var merged = Assert.Single(doc.Document.Entities.OfType<Line>());
		Assert.Equal(0, merged.StartPoint.X, precision: 2);
		Assert.Equal(100, merged.EndPoint.X, precision: 2);

		doc.Undo();
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void JoinCommand_JoinsLinesIntoPolyline_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(50, 0, 0));
		var l2 = new Line(new XYZ(50, 0, 0), new XYZ(50, 50, 0));
		doc.AddEntities(l1, l2);

		var cmd = new JoinCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnSelection(new[] { l1, l2 });
		cmd.OnEnter();

		Assert.Empty(doc.Document.Entities.OfType<Line>());
		var poly = Assert.Single(doc.Document.Entities.OfType<LwPolyline>());
		Assert.Equal(3, poly.Vertices.Count);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<LwPolyline>());
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void StretchCommand_MovesEnclosedVertices_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		// Line from (0, 0) to (100, 0)
		var line = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		doc.AddEntities(line);

		var cmd = new StretchCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		// 교차 창으로 (80, -10) ~ (120, 10) 선택 (끝점 (100, 0)만 포함)
		cmd.OnPoint(80, -10);
		cmd.OnPoint(120, 10);
		// 기준점 (0, 0) -> 두 번째 점 (20, 30) (dx = 20, dy = 30)
		cmd.OnPoint(0, 0);
		cmd.OnPoint(20, 30);

		var stretched = Assert.Single(doc.Document.Entities.OfType<Line>());
		Assert.Equal(0, stretched.StartPoint.X, precision: 2);
		Assert.Equal(0, stretched.StartPoint.Y, precision: 2);
		Assert.Equal(120, stretched.EndPoint.X, precision: 2);
		Assert.Equal(30, stretched.EndPoint.Y, precision: 2);

		doc.Undo();
		var restored = Assert.Single(doc.Document.Entities.OfType<Line>());
		Assert.Equal(100, restored.EndPoint.X, precision: 2);
		Assert.Equal(0, restored.EndPoint.Y, precision: 2);
	}

	[Fact]
	public void ArrayCommand_CreatesClones_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(0, 0, 0), Radius = 10 };
		doc.AddEntities(c);

		var cmd = new ArrayCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnSelection(new[] { c });
		cmd.OnEnter();
		cmd.OnText("R"); // 배열 유형 = 직사각형
		foreach (var t in new[] { "COU", "3", "2", "S", "50", "50", "X" }) cmd.OnText(t);

		// 원본 1개 + 복제본 5개 = 총 6개
		Assert.Equal(6, doc.Document.Entities.OfType<Circle>().Count());

		// 단일 Undo로 원본 1개만 남김
		doc.Undo();
		Assert.Single(doc.Document.Entities.OfType<Circle>());
	}

	[Fact]
	public void MTextCommand_CreatesMText_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var cmd = new MTextCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 50);

		// 기존 CAD처럼 두 번째 구석에서 빈 문자를 만들고 앱이 바로 화면 편집(문자 편집기 탭)을 연다
		var mt = Assert.Single(doc.Document.Entities.OfType<MText>());
		Assert.True(cmd.IsFinished);
		Assert.Same(mt, cmd.Created);
		Assert.Equal(string.Empty, mt.Value);
		Assert.Equal(100, mt.RectangleWidth, precision: 2);
		Assert.Equal(50, mt.InsertPoint.Y, precision: 6);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<MText>());
	}

	[Fact]
	public void DimRadius_And_DimDiameter_Commands_WorkCorrectly()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(50, 50, 0), Radius = 25 };
		doc.AddEntities(c);

		// DIMRADIUS
		var rCmd = new DimRadiusCommand();
		var rCtx = new CommandContext(doc);
		rCmd.Start(rCtx);
		rCmd.OnPoint(75, 50); // 원 선택
		rCmd.OnPoint(90, 60); // 치수선 위치

		var rDim = Assert.Single(doc.Document.Entities.OfType<DimensionRadius>());
		Assert.Equal(25, rDim.Measurement, precision: 2);

		// DIMDIAMETER
		var dCmd = new DimDiameterCommand();
		var dCtx = new CommandContext(doc);
		dCmd.Start(dCtx);
		dCmd.OnPoint(75, 50); // 원 선택
		dCmd.OnPoint(100, 100); // 치수선 위치

		var dDim = Assert.Single(doc.Document.Entities.OfType<DimensionDiameter>());
		Assert.Equal(50, dDim.Measurement, precision: 2);

		// Undo 검증
		doc.Undo(); // 지름 치수 취소
		Assert.Empty(doc.Document.Entities.OfType<DimensionDiameter>());
		Assert.Single(doc.Document.Entities.OfType<DimensionRadius>());

		doc.Undo(); // 반지름 치수 취소
		Assert.Empty(doc.Document.Entities.OfType<DimensionRadius>());
	}

	[Fact]
	public void DimAngularCommand_CreatesAngularDimension_AndUndoRestores()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0));
		var l2 = new Line(new XYZ(0, 0, 0), new XYZ(0, 100, 0));
		doc.AddEntities(l1, l2);

		var cmd = new DimAngularCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(50, 0); // l1 선택
		cmd.OnPoint(0, 50); // l2 선택
		cmd.OnPoint(40, 40); // 호 위치

		var dim = Assert.Single(doc.Document.Entities.OfType<DimensionAngular2Line>());
		Assert.Equal(Math.PI / 2, dim.Measurement, precision: 2);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<DimensionAngular2Line>());
	}

	[Fact]
	public void HatchCommand_CreatesSolidAndPatternHatch_AndUndoRestores()
	{
		var doc = EditorDocument.New();
		var c = new Circle { Center = new XYZ(50, 50, 0), Radius = 20 };
		doc.AddEntities(c);

		var cmd = new HatchCommand { Settings = new Free2DCad.Core.Hatching.HatchSettings { PatternName = "SOLID" } };
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(50, 50); // 내부 점 클릭
		cmd.OnEnter(); // Enter로 해치 생성 (기존 CAD 방식)

		var hatch = Assert.Single(doc.Document.Entities.OfType<Hatch>());
		Assert.True(hatch.IsSolid);

		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Hatch>());
	}

	[Fact]
	public void MatchPropCommand_CopiesProperties_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var redLayer = doc.AddLayer("붉은선", new Color(1));
		var src = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0)) { Layer = redLayer, Color = new Color(1) };
		var dst = new Line(new XYZ(0, 10, 0), new XYZ(10, 10, 0)) { Layer = doc.CurrentLayer, Color = Color.ByLayer };
		doc.AddEntities(src, dst);

		var cmd = new MatchPropCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnPoint(5, 0); // 원본 선택
		cmd.OnPoint(5, 10); // 대상 선택
		cmd.OnEnter();

		Assert.Equal("붉은선", dst.Layer.Name);
		Assert.Equal((short)1, dst.Color.Index);

		doc.Undo();
		Assert.Equal("0", dst.Layer.Name);
		Assert.True(dst.Color.IsByLayer);
	}

	[Fact]
	public void InquiryCommands_List_Id_Area_ExecuteCorrectly()
	{
		var doc = EditorDocument.New();
		var line = new Line(new XYZ(10, 20, 0), new XYZ(40, 60, 0));
		doc.AddEntities(line);

		var printed = new List<string>();
		var ctx = new CommandContext(doc, print: s => printed.Add(s));

		// ID
		var idCmd = new IdCommand();
		idCmd.Start(ctx);
		idCmd.OnPoint(15.5, 25.5);
		Assert.Contains(printed, s => s.Contains("X = 15.5000") && s.Contains("Y = 25.5000"));

		// LIST
		printed.Clear();
		var listCmd = new ListCommand();
		listCmd.Start(ctx);
		listCmd.OnSelection(new[] { line });
		listCmd.OnEnter();
		Assert.Contains(printed, s => s.Contains("LINE"));
		Assert.Contains(printed, s => s.Contains("길이:  50.0000")); // 기존 CAD LIST 모양

		// AREA
		printed.Clear();
		var areaCmd = new AreaCommand();
		areaCmd.Start(ctx);
		areaCmd.OnPoint(0, 0);
		areaCmd.OnPoint(100, 0);
		areaCmd.OnPoint(100, 50);
		areaCmd.OnPoint(0, 50);
		areaCmd.OnEnter();
		Assert.Contains(printed, s => s.Contains("면적 = 5000.0000") && s.Contains("둘레 = 300.0000"));
	}

	[Fact]
	public void LayerActions_AddRenameChangeColor_WorkWithUndoRedo()
	{
		var doc = EditorDocument.New();

		// 1. 도면층 추가
		var layer = doc.AddLayer("테스트레이어", new Color(3));
		Assert.True(doc.Document.Layers.Contains("테스트레이어"));
		Assert.Equal((short)3, layer.Color.Index);

		// 2. 도면층 색상 변경
		doc.SetLayerColor(layer, new Color(5));
		Assert.Equal((short)5, layer.Color.Index);

		// 3. 도면층 이름 변경
		doc.RenameLayer(layer, "변경된레이어");
		Assert.True(doc.Document.Layers.Contains("변경된레이어"));
		Assert.False(doc.Document.Layers.Contains("테스트레이어"));

		// Undo: 이름 복원
		doc.Undo();
		Assert.True(doc.Document.Layers.Contains("테스트레이어"));
		Assert.False(doc.Document.Layers.Contains("변경된레이어"));

		// Undo: 색상 복원
		doc.Undo();
		Assert.Equal((short)3, layer.Color.Index);

		// Undo: 레이어 추가 취소
		doc.Undo();
		Assert.False(doc.Document.Layers.Contains("테스트레이어"));

		// Redo: 레이어 다시 추가
		doc.Redo();
		Assert.True(doc.Document.Layers.Contains("테스트레이어"));
	}

	[Fact]
	public void ClipboardCommands_CopyAndPaste_WorkWithUndo()
	{
		var doc = EditorDocument.New();
		var l = new Line(new XYZ(10, 10, 0), new XYZ(60, 10, 0));
		doc.AddEntities(l);

		// COPYCLIP
		var copyCmd = new CopyClipCommand();
		var ctx = new CommandContext(doc);
		copyCmd.Start(ctx);
		copyCmd.OnSelection(new[] { l });
		copyCmd.OnEnter();

		// PASTECLIP
		var pasteCmd = new PasteClipCommand();
		pasteCmd.Start(ctx);
		pasteCmd.OnPoint(50, 50); // 삽입점 (기준점에서 dx = 40, dy = 40)

		var lines = doc.Document.Entities.OfType<Line>().ToList();
		Assert.Equal(2, lines.Count);

		doc.Undo();
		Assert.Single(doc.Document.Entities.OfType<Line>());
	}
}
