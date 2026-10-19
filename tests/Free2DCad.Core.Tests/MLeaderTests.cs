using ACadSharp;
using ACadSharp.Entities;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>MLEADER: 기존 CAD와 같은 MULTILEADER 객체 (연결점·착륙선·문자 위치·정렬).</summary>
public class MLeaderTests
{
	private static MultiLeader Make(EditorDocument doc, double ax, double ay, double cx, double cy, string text)
	{
		var cmd = new MLeaderCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(ax, ay);
		cmd.OnPoint(cx, cy);
		Assert.True(cmd.IsFinished); // 두 번째 점에서 바로 만들고 화면 편집으로
		var ml = doc.Document.Entities.OfType<MultiLeader>().Last();
		Assert.Same(ml, cmd.Created);
		MLeaderBuilder.SetText(ml, text);
		return ml;
	}

	[Fact]
	public void RightAndLeft_LikeStd()
	{
		var doc = EditorDocument.New();
		// 기존 CAD 견본과 같은 점 (Standard: 문자 4, 착륙 8, 간격 2)
		var r = Make(doc, 19.85680851063833, 108.9060182370821, 141.4011246200608, 199.6038601823708, "ABC");
		var cd = r.ContextData;
		Assert.Equal("ABC", cd.TextLabel);
		Assert.Equal(141.4011246200608, cd.LeaderRoots[0].ConnectionPoint.X, 6);
		Assert.Equal(1, cd.LeaderRoots[0].Direction.X);
		Assert.Equal(149.4011246200608, cd.ContentBasePoint.X, 6);
		Assert.Equal(151.4011246200608, cd.TextLocation.X, 6);
		Assert.Equal(TextAlignmentType.Left, cd.TextAlignment);
		Assert.Single(cd.LeaderRoots[0].Lines[0].Points);

		var l = Make(doc, 323.7175987841945, 78.51993920972643, 232.5593617021277, 139.2920972644377, "DEF");
		Assert.Equal(-1, l.ContextData.LeaderRoots[0].Direction.X);
		Assert.Equal(222.5593617021277, l.ContextData.TextLocation.X, 6);
		Assert.Equal(141.2920972644377, l.ContextData.TextLocation.Y, 6);
		Assert.Equal(TextAlignmentType.Right, l.ContextData.TextAlignment);
		Assert.Equal(TextAttachmentPointType.Right, l.ContextData.TextAttachmentPoint);

		// 문자 없이 끝내면 앱이 바로 편집하도록 Created
		var cmd = new MLeaderCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnPoint(0, 0);
		cmd.OnPoint(10, 10);
		Assert.NotNull(cmd.Created);

		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		foreach (var (name, ver) in new[] { ("mleader_2018.dwg", ACadVersion.AC1032), ("mleader_2018.dxf", ACadVersion.AC1032), ("mleader_2013.dwg", ACadVersion.AC1027) })
		{
			string path = Path.Combine(dir ?? Path.GetTempPath(), name);
			doc.Save(path, ver);
			var back = EditorDocument.Open(path).Document.Entities.OfType<MultiLeader>().ToList();
			Assert.Equal(3, back.Count);
			Assert.Contains(back, m => m.ContextData.TextLabel == "DEF" && m.ContextData.TextAlignment == TextAlignmentType.Right);
			if (dir == null) File.Delete(path);
		}
	}

	[Fact]
	public void LandingFirst_Option()
	{
		var doc = EditorDocument.New();
		var cmd = new MLeaderCommand();
		cmd.Start(new CommandContext(doc));
		cmd.OnText("L");
		cmd.OnPoint(100, 100); // 연결점
		cmd.OnPoint(0, 0);     // 화살촉
		var ml = doc.Document.Entities.OfType<MultiLeader>().Single();
		Assert.Equal(100, ml.ContextData.LeaderRoots[0].ConnectionPoint.X, 6);
		Assert.Equal(0, ml.ContextData.LeaderRoots[0].Lines[0].Points[0].X, 6);
	}
}
