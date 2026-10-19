using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>LEADER: 기존 CAD와 같은 프롬프트·옵션과 저장 구조 (찍은 점 그대로, 문자 = 끝점 ±DIMGAP, +DIMGAP, 아래 맞춤).</summary>
public class ClassicLeaderTests
{
	private static (EditorDocument Doc, ClassicLeaderCommand Cmd) Start()
	{
		var doc = EditorDocument.New();
		var cmd = new ClassicLeaderCommand();
		cmd.Start(new CommandContext(doc));
		return (doc, cmd);
	}

	private static double Gap(EditorDocument doc)
	{
		var st = doc.Document.DimensionStyles[doc.Document.Header.CurrentDimensionStyleName];
		return Math.Abs(st.DimensionLineGap) * (st.ScaleFactor > 0 ? st.ScaleFactor : 1);
	}

	[Fact]
	public void Prompts_MatchStd()
	{
		var (_, cmd) = Start();
		Assert.Equal("지시선의 시작:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("다음 점:", cmd.Prompt);
		cmd.OnPoint(10, 10);
		Assert.Equal("지점까지: [형식(F)/취소(U)]/<주석>:", cmd.Prompt);
		cmd.OnText("F");
		Assert.Equal("[화살촉(A)/없음(N)/스플라인(S)/직선(ST)]/<종료(E)>:", cmd.Prompt);
		cmd.OnEnter();
		cmd.OnEnter();
		Assert.Equal("주석 문자의 첫 줄/<옵션>:", cmd.Prompt);
		Assert.True(cmd.SpaceIsText);
		cmd.OnEnter();
		Assert.Equal("치수 문자 옵션: [블록(B)/복사(C)/없음(N)/공차(T)]/<Mtext>:", cmd.Prompt);
		Assert.False(cmd.SpaceIsText);
	}

	[Fact]
	public void TypedText_RightGoing_BottomLeftAtGap_NoHookVertex()
	{
		var (doc, cmd) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnPoint(20, 30);
		cmd.OnEnter();
		cmd.OnText("ABC DEF");
		Assert.Equal("주석 문자의 다음 줄:", cmd.Prompt);
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var mt = Assert.IsType<MText>(leader.AssociatedAnnotation);
		double g = Gap(doc);
		Assert.Equal("ABC DEF", mt.Value);
		Assert.Equal(2, leader.Vertices.Count);
		Assert.Equal(AttachmentPointType.BottomLeft, mt.AttachmentPoint);
		Assert.Equal(20 + g, mt.InsertPoint.X, 9);
		Assert.Equal(30 + g, mt.InsertPoint.Y, 9);
		Assert.Equal(HookLineDirection.Opposite, leader.HookLineDirection);
		Assert.Equal(LeaderCreationType.CreatedWithTextAnnotation, leader.CreationType);
		Assert.Equal(0, leader.TextHeight);
		Assert.Null(cmd.Created);
	}

	[Fact]
	public void LeftGoing_MultiLine_BottomRight_Same()
	{
		var (doc, cmd) = Start();
		cmd.OnPoint(100, 0);
		cmd.OnPoint(60, 40);
		cmd.OnEnter();
		cmd.OnText("LINE1");
		cmd.OnText("LINE2");
		cmd.OnEnter();
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var mt = Assert.IsType<MText>(leader.AssociatedAnnotation);
		Assert.Equal("LINE1\\PLINE2", mt.Value);
		Assert.Equal(AttachmentPointType.BottomRight, mt.AttachmentPoint);
		Assert.Equal(60 - Gap(doc), mt.InsertPoint.X, 9);
		Assert.Equal(HookLineDirection.Same, leader.HookLineDirection);
	}

	[Fact]
	public void Mtext_Default_CreatesEmptyForInplaceEdit()
	{
		var (doc, cmd) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);
		Assert.NotNull(cmd.Created);
		Assert.Same(cmd.Created, doc.Document.Entities.OfType<Leader>().Single().AssociatedAnnotation);
	}

	[Fact]
	public void None_Format_Undo()
	{
		var (doc, cmd) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnPoint(20, 10);
		cmd.OnText("U");
		Assert.Equal("지점까지: [형식(F)/취소(U)]/<주석>:", cmd.Prompt);
		cmd.OnText("F");
		cmd.OnText("N");
		cmd.OnText("F");
		cmd.OnText("S");
		cmd.OnPoint(30, 5);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnText("N");
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		Assert.Equal(3, leader.Vertices.Count);
		Assert.Equal(new XYZ(30, 5, 0), leader.Vertices[2]);
		Assert.False(leader.ArrowHeadEnabled);
		Assert.Equal(LeaderPathType.Spline, leader.PathType);
		Assert.Equal(LeaderCreationType.CreatedWithoutAnnotation, leader.CreationType);
		Assert.Null(leader.AssociatedAnnotation);
		Assert.Empty(doc.Document.Entities.OfType<MText>());
	}

	[Fact]
	public void Tolerance_FromDialog_AtEndPoint()
	{
		var (doc, cmd) = Start();
		bool asked = false;
		cmd.ToleranceRequested += _ => asked = true;
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnText("T");
		Assert.True(asked);
		Assert.False(cmd.IsFinished);
		cmd.SetTolerance("{\\Fgdt;j}%%v0.05%%v%%v%%v%%v");
		Assert.True(cmd.IsFinished);
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var tol = Assert.IsType<Tolerance>(leader.AssociatedAnnotation);
		Assert.Equal(new XYZ(10, 10, 0), tol.InsertionPoint);
		Assert.Equal(LeaderCreationType.CreatedWithToleranceAnnotation, leader.CreationType);
	}

	[Fact]
	public void Copy_MText_ToEnd()
	{
		var (doc, cmd) = Start();
		var src = new MText { Value = "SRC", Height = 2.5, InsertPoint = new XYZ(200, 200, 0) };
		doc.AddEntities(src);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnText("C");
		Assert.True(cmd.WantsSelection);
		Assert.Equal("도면요소 선택:", cmd.Prompt);
		cmd.OnSelection(new Entity[] { src });
		Assert.True(cmd.IsFinished);
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var copy = Assert.IsType<MText>(leader.AssociatedAnnotation);
		Assert.NotSame(src, copy);
		Assert.Equal("SRC", copy.Value);
		Assert.Equal(10 + Gap(doc), copy.InsertPoint.X, 9);
		Assert.Equal(new XYZ(200, 200, 0), src.InsertPoint);
	}

	[Fact]
	public void Block_InsertAtPoint_Linked()
	{
		var (doc, cmd) = Start();
		var br = new ACadSharp.Tables.BlockRecord("MARK");
		br.Entities.Add(new Circle { Radius = 1 });
		doc.Document.BlockRecords.Add(br);
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnText("B");
		cmd.OnText("MARK");
		Assert.Equal("블록의 삽입점:", cmd.Prompt);
		cmd.OnPoint(10, 10);
		cmd.OnText("2");
		cmd.OnEnter();
		cmd.OnEnter();
		var leader = doc.Document.Entities.OfType<Leader>().Single();
		var ins = Assert.IsType<Insert>(leader.AssociatedAnnotation);
		Assert.Equal("MARK", ins.Block.Name);
		Assert.Equal(2, ins.XScale);
		Assert.Equal(2, ins.YScale);
		Assert.Equal(LeaderCreationType.CreatedWithBlockReferenceAnnotation, leader.CreationType);
	}

	[Fact]
	public void SavedLeader_RoundTrips()
	{
		var (doc, cmd) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnPoint(20, 30);
		cmd.OnEnter();
		cmd.OnText("ABC");
		cmd.OnEnter();
		string path = Path.Combine(Path.GetTempPath(), $"leader_{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(path);
			var back = ACadSharp.IO.DwgReader.Read(path);
			var leader = back.Entities.OfType<Leader>().Single();
			Assert.Equal("ABC", Assert.IsType<MText>(leader.AssociatedAnnotation).Value);
			Assert.Equal(HookLineDirection.Opposite, leader.HookLineDirection);
		}
		finally
		{
			File.Delete(path);
		}
	}
}

public class ClassicLeaderRenderTests
{
	[Fact]
	public void SplineLeader_DrawsCurve()
	{
		var doc = EditorDocument.New();
		var cmd = new ClassicLeaderCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		cmd.OnText("F");
		cmd.OnText("S");
		cmd.OnPoint(30, 12);
		cmd.OnEnter();
		cmd.OnEnter();
		cmd.OnText("N");
		var scene = Free2DCad.Rendering.SceneBuilder.Build(doc.Document);
		Assert.Contains(scene.Polylines, p => p.Points.Length > 12);
	}
}
