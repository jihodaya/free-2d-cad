using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

public class CommandsTests : IDisposable
{
	private readonly string _tempDir;

	public CommandsTests()
	{
		this._tempDir = Path.Combine(Path.GetTempPath(), "Free2DCad_Cmd_Tests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this._tempDir);
	}

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(this._tempDir))
			{
				Directory.Delete(this._tempDir, recursive: true);
			}
		}
		catch
		{
		}
	}

	[Fact]
	public void CoordinateParser_AllFormats_WorkCorrectly()
	{
		// 1. 절대 좌표: "15.5,25.5"
		Assert.True(CoordinateParser.TryParse("15.5,25.5", null, null, false, out var p1));
		Assert.Equal(15.5, p1.X, precision: 4);
		Assert.Equal(25.5, p1.Y, precision: 4);

		// 2. 상대 직교 좌표: "@10,-5" with base (20, 30)
		Assert.True(CoordinateParser.TryParse("@10,-5", (20, 30), null, false, out var p2));
		Assert.Equal(30, p2.X, precision: 4);
		Assert.Equal(25, p2.Y, precision: 4);

		// 3. 상대 극좌표: "@100<90" with base (10, 20)
		Assert.True(CoordinateParser.TryParse("@100<90", (10, 20), null, false, out var p3));
		Assert.Equal(10, p3.X, precision: 3);
		Assert.Equal(120, p3.Y, precision: 3);

		// 4. 거리 숫자만 입력: "50" with lastPoint (10, 10), cursor (20, 10)
		Assert.True(CoordinateParser.TryParse("50", (10, 10), (20, 10), false, out var p4));
		Assert.Equal(60, p4.X, precision: 4);
		Assert.Equal(10, p4.Y, precision: 4);

		// 5. Ortho 적용된 거리 숫자: "50" with lastPoint (0, 0), cursor (10, 2)
		Assert.True(CoordinateParser.TryParse("50", (0, 0), (10, 2), true, out var p5));
		Assert.Equal(50, p5.X, precision: 4);
		Assert.Equal(0, p5.Y, precision: 4);
	}

	[Fact]
	public void LineCommand_CreatesLine_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var cmd = new LineCommand();
		var ctx = new CommandContext(doc);

		cmd.Start(ctx);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 50);
		cmd.OnEnter();

		Assert.True(cmd.IsFinished);
		var line = Assert.Single(doc.Document.Entities.OfType<Line>());
		Assert.Equal(0, line.StartPoint.X, precision: 3);
		Assert.Equal(0, line.StartPoint.Y, precision: 3);
		Assert.Equal(100, line.EndPoint.X, precision: 3);
		Assert.Equal(50, line.EndPoint.Y, precision: 3);

		// 명령 한 번 = Undo 한 번
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Line>());
	}

	[Fact]
	public void RectangCommand_CreatesClosedPolyline()
	{
		var doc = EditorDocument.New();
		var cmd = new RectangCommand();
		var ctx = new CommandContext(doc);

		cmd.Start(ctx);
		cmd.OnPoint(10, 20);
		cmd.OnPoint(30, 40);

		Assert.True(cmd.IsFinished);
		var poly = Assert.Single(doc.Document.Entities.OfType<LwPolyline>());
		Assert.True(poly.IsClosed);
		Assert.Equal(4, poly.Vertices.Count);
		Assert.Equal(10, poly.Vertices[0].Location.X, precision: 3);
		Assert.Equal(20, poly.Vertices[0].Location.Y, precision: 3);
		Assert.Equal(30, poly.Vertices[2].Location.X, precision: 3);
		Assert.Equal(40, poly.Vertices[2].Location.Y, precision: 3);
	}

	[Fact]
	public void CircleCommand_CreatesCircle_WithRadiusAndDiameter()
	{
		var doc = EditorDocument.New();
		var cmd = new CircleCommand();
		var ctx = new CommandContext(doc);

		cmd.Start(ctx);
		cmd.OnPoint(50, 50);
		cmd.OnText("D");
		cmd.OnText("40");

		Assert.True(cmd.IsFinished);
		var circle = Assert.Single(doc.Document.Entities.OfType<Circle>());
		Assert.Equal(50, circle.Center.X, precision: 3);
		Assert.Equal(50, circle.Center.Y, precision: 3);
		Assert.Equal(20, circle.Radius, precision: 3); // 지름 40 -> 반지름 20
	}

	[Fact]
	public void TextCommand_CreatesTextEntity()
	{
		var doc = EditorDocument.New();
		var cmd = new TextCommand();
		var ctx = new CommandContext(doc);

		cmd.Start(ctx);
		Assert.Equal("텍스트의 시작점을 지정 or [정렬(J)/Style]:", cmd.Prompt);
		cmd.OnPoint(15, 25);
		Assert.StartsWith("문자의 높이 <", cmd.Prompt);
		cmd.OnText("3.5");
		Assert.Equal("문자의 회전 각도 <0>:", cmd.Prompt);
		cmd.OnText("0");

		// 기존 CAD처럼 빈 문자를 만들고 그 자리에서 입력 (앱이 Created로 편집을 연다)
		Assert.True(cmd.IsFinished);
		var text = Assert.Single(doc.Document.Entities.OfType<TextEntity>());
		Assert.Same(text, cmd.Created);
		Assert.Equal(string.Empty, text.Value);
		var next = TextCommand.NextLine(text);
		Assert.Equal(25 - 3.5 * 5 / 3, next.InsertPoint.Y, precision: 6);

		// 정렬: 중간 중심
		var cmd2 = new TextCommand();
		cmd2.Start(ctx);
		cmd2.OnText("J");
		Assert.StartsWith("옵션을 입력하세요 [왼쪽(L)/중앙(C)/오른쪽(R)/Align(A)/중간(M)/맞춤(F)/", cmd2.Prompt);
		cmd2.OnText("MC");
		Assert.Equal("문자의 중간 중심 점:", cmd2.Prompt);
		cmd2.OnPoint(0, 0);
		cmd2.OnEnter();
		cmd2.OnEnter();
		Assert.Equal(TextVerticalAlignmentType.Middle, cmd2.Created!.VerticalAlignment);
		Assert.Equal(3.5, text.Height, precision: 3);
		Assert.Equal(15, text.InsertPoint.X, precision: 3);
		Assert.Equal(25, text.InsertPoint.Y, precision: 3);
	}

	[Fact]
	public void Move_And_Rotate_And_Scale_WorkAndUndo()
	{
		var doc = EditorDocument.New();
		var line = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		doc.AddEntities(line);

		// 1. Move
		var moveCmd = new MoveCommand();
		var ctx = new CommandContext(doc, new HashSet<Entity> { line });
		moveCmd.Start(ctx);
		moveCmd.OnPoint(0, 0);
		moveCmd.OnPoint(5, 5);

		Assert.Equal(5, line.StartPoint.X, precision: 3);
		Assert.Equal(5, line.StartPoint.Y, precision: 3);
		Assert.Equal(15, line.EndPoint.X, precision: 3);
		Assert.Equal(5, line.EndPoint.Y, precision: 3);

		doc.Undo();
		Assert.Equal(0, line.StartPoint.X, precision: 3);
		Assert.Equal(10, line.EndPoint.X, precision: 3);

		// 2. Rotate
		var rotCmd = new RotateCommand();
		ctx = new CommandContext(doc, new HashSet<Entity> { line });
		rotCmd.Start(ctx);
		rotCmd.OnPoint(0, 0);
		rotCmd.OnText("90");

		Assert.Equal(0, line.StartPoint.X, precision: 3);
		Assert.Equal(0, line.StartPoint.Y, precision: 3);
		Assert.Equal(0, line.EndPoint.X, precision: 3);
		Assert.Equal(10, line.EndPoint.Y, precision: 3);

		doc.Undo();
		Assert.Equal(10, line.EndPoint.X, precision: 3);

		// 3. Scale
		var scaleCmd = new ScaleCommand();
		ctx = new CommandContext(doc, new HashSet<Entity> { line });
		scaleCmd.Start(ctx);
		scaleCmd.OnPoint(0, 0);
		scaleCmd.OnText("2");

		Assert.Equal(20, line.EndPoint.X, precision: 3);
		doc.Undo();
		Assert.Equal(10, line.EndPoint.X, precision: 3);
	}

	[Fact]
	public void Explode_LwPolyline_DecomposesToLines_AndSingleUndoRestores()
	{
		var doc = EditorDocument.New();
		var poly = new LwPolyline { IsClosed = true };
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(0, 0)));
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(10, 0)));
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(10, 10)));
		poly.Vertices.Add(new LwPolyline.Vertex(new XY(0, 10)));
		doc.AddEntities(poly);

		var cmd = new ExplodeCommand();
		var ctx = new CommandContext(doc, new HashSet<Entity> { poly });
		cmd.Start(ctx);

		Assert.Empty(doc.Document.Entities.OfType<LwPolyline>());
		Assert.Equal(4, doc.Document.Entities.OfType<Line>().Count());

		doc.Undo();
		Assert.Single(doc.Document.Entities.OfType<LwPolyline>());
		Assert.Empty(doc.Document.Entities.OfType<Line>());
	}

	[Fact]
	public void DistCommand_CalculatesCorrectDistance()
	{
		var doc = EditorDocument.New();
		string output = string.Empty;
		var cmd = new DistCommand();
		var ctx = new CommandContext(doc, print: msg => output = msg);

		cmd.Start(ctx);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(30, 40);

		Assert.True(cmd.IsFinished);
		Assert.Contains("50.0000", output); // 3-4-5 직각삼각형 빗변 = 50
	}

	[Fact]
	public void DimLinear_CreatesDimension_SavesAC1018_ReloadsWithBlockAndMeasurement()
	{
		var doc = EditorDocument.New();
		var cmd = new DimLinearCommand();
		var ctx = new CommandContext(doc);

		cmd.Start(ctx);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(100, 0);
		cmd.OnPoint(50, 20); // 치수선 위치

		Assert.True(cmd.IsFinished);
		var dim = Assert.Single(doc.Document.Entities.OfType<DimensionLinear>());
		Assert.NotNull(dim.Block);
		Assert.True(dim.Block.Entities.Count > 0);
		Assert.Equal(100, dim.Measurement, precision: 2);

		// 파일 저장 및 재로드 검증
		string path = Path.Combine(this._tempDir, "dim_test.dwg");
		doc.Save(path);

		CadDocument reloaded = DwgReader.Read(path);
		var reloadedDim = Assert.Single(reloaded.Entities.OfType<DimensionLinear>());
		Assert.NotNull(reloadedDim.Block);
		Assert.True(reloadedDim.Block.Entities.Count > 0);
		Assert.Equal(100, reloadedDim.Measurement, precision: 2);
	}
}
