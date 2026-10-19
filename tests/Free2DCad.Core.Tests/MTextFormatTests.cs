using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>문자 편집기 탭: 선택한 글자에만 서식 코드 입히기.</summary>
public class MTextFormatTests
{
	[Fact]
	public void WrapsOnlySelection()
	{
		string r = MTextTokenizer.ApplyFormat("abc", 1, 2, _ => @"\L");
		Assert.Equal(@"a{\Lb}c", r);
		Assert.Equal("abc", MTextTokenizer.PlainText(r));
		Assert.True(MTextTokenizer.FormatsOf(r, 1, 2)[0].Underline);
		Assert.False(MTextTokenizer.FormatsOf(r, 0, 1)[0].Underline);
	}

	[Fact]
	public void SplitsAtExistingGroups()
	{
		string r = MTextTokenizer.ApplyFormat(@"{\Lab}cd", 1, 3, _ => @"\W0.8;");
		Assert.Equal("abcd", MTextTokenizer.PlainText(r));
		var f = MTextTokenizer.FormatsOf(r, 0, 4);
		Assert.True(f[0].Underline && f[0].Width == 1);
		Assert.True(f[1].Underline && Math.Abs(f[1].Width - 0.8) < 1e-9);
		Assert.True(!f[2].Underline && Math.Abs(f[2].Width - 0.8) < 1e-9);
		Assert.True(!f[3].Underline && f[3].Width == 1);
		Assert.Equal(r.Count(c => c == '{'), r.Count(c => c == '}'));
	}

	[Fact]
	public void BoldToggleAcrossParagraphs()
	{
		static string Code(MTextFormat f) => $@"\f굴림|b{(f.Bold ? 0 : 1)}|i0;";
		string r = MTextTokenizer.ApplyFormat(@"ab\Pcd", 0, 5, Code);
		Assert.Equal("ab\ncd", MTextTokenizer.PlainText(r));
		Assert.All(MTextTokenizer.FormatsOf(r, 0, 5).Where((_, i) => i != 2), f => Assert.True(f.Bold));
		string back = MTextTokenizer.ApplyFormat(r, 0, 5, Code);
		Assert.All(MTextTokenizer.FormatsOf(back, 0, 5), f => Assert.False(f.Bold));
	}

	[Fact]
	public void Sample_ForStd()
	{
		string? dir = Environment.GetEnvironmentVariable("F2D_AUDIT_OUT");
		var doc = EditorDocument.New();
		string raw = "F2D 굵게 밑줄 빨강 기울기";
		raw = MTextTokenizer.ApplyFormat(raw, 4, 6, _ => @"\f맑은 고딕|b1|i0;");
		raw = MTextTokenizer.ApplyFormat(raw, 7, 9, _ => @"\L");
		raw = MTextTokenizer.ApplyFormat(raw, 10, 12, _ => @"\C1;");
		raw = MTextTokenizer.ApplyFormat(raw, 13, 16, _ => @"\Q15;");
		var mt = new ACadSharp.Entities.MText { Value = raw, Height = 5, InsertPoint = new CSMath.XYZ(0, 20, 0), RectangleWidth = 200 };
		doc.AddEntities(mt);
		string path = Path.Combine(dir ?? Path.GetTempPath(), "mtext_format.dwg");
		doc.Save(path, ACadSharp.ACadVersion.AC1032);
		var back = EditorDocument.Open(path).Document.Entities.OfType<ACadSharp.Entities.MText>().Single();
		Assert.Equal(raw, back.Value);
		Assert.Equal("F2D 굵게 밑줄 빨강 기울기", MTextTokenizer.PlainText(back.Value));
		if (dir == null) File.Delete(path);
	}
}
