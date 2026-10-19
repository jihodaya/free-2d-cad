using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>RECTANG: 기존 CAD와 같은 문구·옵션 (모따기·모깎기·너비·치수·면적·회전·정사각형).</summary>
[Collection("RectangStatics")]
public class RectangStdTests
{
	private static (EditorDocument Doc, RectangCommand Cmd) Start(EditorDocument? existing = null)
	{
		var doc = existing ?? EditorDocument.New();
		var cmd = new RectangCommand();
		cmd.Start(new CommandContext(doc));
		return (doc, cmd);
	}

	private static LwPolyline Last(EditorDocument doc) => doc.Document.Entities.OfType<LwPolyline>().Last();

	[Fact]
	public void Options_Fillet_Width_Dimensions_Area_Square_Rotated()
	{
		var (doc, cmd) = Start();
		Assert.Equal("[모따기(C)/높이(E)/모깎기(F)/회전(R)/정사각형(S)/두께(T)/너비(W)]/<사각형의 첫번째 모서리 선택>:", cmd.Prompt);
		cmd.OnText("F");
		Assert.Equal("모든 사각형에 사용할 모깎기 거리(F) <0.0000>:", cmd.Prompt);
		cmd.OnText("2");
		cmd.OnText("W");
		Assert.Equal("모든 사각형에 사용할 너비:", cmd.Prompt);
		cmd.OnText("0.5");
		cmd.OnPoint(0, 0);
		Assert.Equal("다른 구석 지점 지정 또는 [면적(A)/치수(D)/회전(R)]:", cmd.Prompt);
		cmd.OnPoint(10, 5);
		var pl = Last(doc);
		Assert.Equal(8, pl.Vertices.Count); // 모깎기 → 구석마다 두 점 + 호
		Assert.Equal(0.5, pl.ConstantWidth, 6);
		Assert.Equal(Math.Tan(Math.PI / 8), Math.Abs(pl.Vertices[0].Bulge), 6);

		// 다음 명령에도 모깎기 유지 → 끄기
		(doc, cmd) = Start(doc);
		cmd.OnText("F");
		Assert.Equal("모든 사각형에 사용할 모깎기 거리(F) <2.0000>:", cmd.Prompt);
		cmd.OnText("0");
		cmd.OnText("W");
		cmd.OnText("0");
		cmd.OnPoint(0, 0);
		cmd.OnText("D");
		Assert.StartsWith("직사각형 길이 입력 <", cmd.Prompt);
		cmd.OnText("20");
		Assert.StartsWith("직사각형 너비 입력 <", cmd.Prompt);
		cmd.OnText("10");
		Assert.Equal("다른 구석 지점 지정 또는 [면적(A)/치수(D)/회전(R)]:", cmd.Prompt);
		cmd.OnPoint(-1, -1); // 방향만: 왼쪽 아래로
		pl = Last(doc);
		Assert.Equal(4, pl.Vertices.Count);
		Assert.Equal(-20, pl.Vertices[2].Location.X, 6);
		Assert.Equal(-10, pl.Vertices[2].Location.Y, 6);

		(doc, cmd) = Start();
		cmd.OnPoint(0, 0);
		cmd.OnText("A");
		Assert.StartsWith("현재 단위로 직사각형의 면적 입력 <", cmd.Prompt);
		cmd.OnText("50");
		Assert.Equal("[길이(L)/너비(W)]를 기반으로 직사각형 치수 계산 <길이>:", cmd.Prompt);
		cmd.OnText("L");
		cmd.OnText("5");
		Assert.Equal("면 선택:", cmd.Prompt);
		cmd.OnPoint(1, 1);
		Assert.Equal(10, Last(doc).Vertices[2].Location.Y, 6);

		(doc, cmd) = Start();
		cmd.OnText("S");
		Assert.Equal("[모따기(C)/높이(E)/모깎기(F)/회전(R)/두께(T)/너비(W)]/<사각형의 첫번째 모서리 선택>:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("사각형의 두번째 구석:", cmd.Prompt);
		cmd.OnPoint(4, 0);
		Assert.Equal(4, Last(doc).Vertices[2].Location.Y, 6);

		(doc, cmd) = Start();
		cmd.OnText("R");
		Assert.Equal("[모따기(C)/높이(E)/모깎기(F)/정사각형(S)/두께(T)/너비(W)]/<사각형의 첫번째 모서리 선택>:", cmd.Prompt);
		cmd.OnPoint(0, 0);
		Assert.Equal("직사각형의 다른 구석:", cmd.Prompt);
		cmd.OnPoint(0, 10);
		Assert.StartsWith("직사각형의 회전각도(R) <", cmd.Prompt);
		cmd.OnText("90"); // 90° 돌린 축: 대각선 (0,10) → 변 길이 10, 너비 0... 대각선이 축 위면 납작 → 만들지 않음
		cmd.OnText("45");
		Assert.True(cmd.IsFinished || Last(doc).Vertices.Count == 4);
	}
}
