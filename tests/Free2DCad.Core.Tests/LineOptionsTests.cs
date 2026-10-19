using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>LINE: 기존 CAD와 같은 문구·옵션 (각도 A, 길이 L, 따라가기 F, 닫기 C, 취소 U, Enter = 마지막 점).</summary>
public class LineOptionsTests
{
	private static (EditorDocument Doc, LineCommand Cmd, CommandContext Ctx) Start()
	{
		var doc = EditorDocument.New();
		var cmd = new LineCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		return (doc, cmd, ctx);
	}

	[Fact]
	public void Prompts_And_AngleLengthFollow()
	{
		var (doc, cmd, _) = Start();
		Assert.Equal("첫번째 점 지정 or Enter키로 마지막 점 지정 [따라가기(F)]:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("[각도(A)/길이(L)]/<끝점>:", cmd.Prompt);
		cmd.OnText("A");
		Assert.Equal("선의 각도:", cmd.Prompt);
		cmd.OnText("90");
		Assert.Equal("선 길이:", cmd.Prompt);
		cmd.OnText("10");
		Assert.Equal("[각도(A)/길이(L)/따라가기(F)/취소(U)]/<끝점>:", cmd.Prompt);
		cmd.OnText("L");
		Assert.Equal("선 길이:", cmd.Prompt);
		cmd.OnText("5");
		Assert.Equal("선의 각도:", cmd.Prompt);
		cmd.OnText("0");
		Assert.Equal("[각도(A)/길이(L)/따라가기(F)/닫기(C)/취소(U)]/<다음점 지정>:", cmd.Prompt);
		cmd.OnText("F");
		cmd.OnText("3"); // 앞 선(0°)을 따라 3
		var lines = doc.Document.Entities.OfType<Line>().ToList();
		Assert.Equal(3, lines.Count);
		Assert.Equal(10, lines[0].EndPoint.Y, 6);
		Assert.Equal(5, lines[1].EndPoint.X, 6);
		Assert.Equal(8, lines[2].EndPoint.X, 6);
		Assert.Equal(10, lines[2].EndPoint.Y, 6);
		cmd.OnText("U");
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
		cmd.OnText("C");
		Assert.True(cmd.IsFinished);
		Assert.Equal(3, doc.Document.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void Enter_StartsAtLastPoint_And_FirstFollow()
	{
		var (doc, cmd, ctx) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 0);
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);

		var cmd2 = new LineCommand();
		cmd2.Start(ctx);
		cmd2.OnText("F"); // 마지막 점(10,0)에서 앞 선 방향으로
		Assert.Equal("선 길이:", cmd2.Prompt);
		cmd2.OnText("4");
		Assert.Equal(14, doc.Document.Entities.OfType<Line>().Last().EndPoint.X, 6);
		cmd2.OnEnter();

		var cmd3 = new LineCommand();
		cmd3.Start(ctx);
		cmd3.OnEnter(); // Enter = 마지막 점(14,0)
		cmd3.OnPoint(14, 5);
		var last = doc.Document.Entities.OfType<Line>().Last();
		Assert.Equal(14, last.StartPoint.X, 6);
		Assert.Equal(5, last.EndPoint.Y, 6);
	}
}
