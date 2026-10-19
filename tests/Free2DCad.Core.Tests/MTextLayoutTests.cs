using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Rendering;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>MTEXT 서식 풀기, 문자 바로 편집의 서식 유지, 문단 들여쓰기·탭·폭 비율 배치.</summary>
public class MTextLayoutTests
{
	private const string Notes = @"\pi-6.4,l6.4,t6.4,150.4;{\W0.8;1.	재료 두께 1.0T\P2.	모든 치수는 참고치 임\P3.	상용부품에 대하여 제원을 만족하는 동등품 이상일 경우 대체 가능함\P4.	재질 및 대체가능 재질\P\pi-16,l22.4,t22.4,150.4;가. 재질 : EGI\L\P\l나. 대체가능재질 : \P\pi-6.4,l6.4,t6.4,150.4;5.	보호피막처리 : N/A\P6.	공차 및 특기사항 : 날카로운 모서리 제거할 것\P7.	모든 치수는 도금처리 후 도장처리 전 치수임\P8.	품번 3은 클린칭 너트를 적용하여야 하나, 버링탭 작업으로도 대체 가능함}";

	[Fact]
	public void PlainText_HidesFormatCodes()
	{
		Assert.Equal("수 정 #1", MTextTokenizer.PlainText(@"{\fDotum|b0|i0|c129|p50;수 정 #1}"));
		Assert.Equal("A\nB", MTextTokenizer.PlainText(@"A\PB"));
		Assert.Equal("Ø10", MTextTokenizer.PlainText("%%c10"));
	}

	[Theory]
	[InlineData("수 정 #2", @"{\fDotum|b0|i0|c129|p50;수 정 #2}")]
	[InlineData("수 정 #12", @"{\fDotum|b0|i0|c129|p50;수 정 #12}")]
	[InlineData("변경 수 정 #1", @"{\fDotum|b0|i0|c129|p50;변경 수 정 #1}")]
	[InlineData("수정", @"{\fDotum|b0|i0|c129|p50;수정}")]
	public void ReplacePlain_KeepsFormatting(string edited, string expected)
	{
		Assert.Equal(expected, MTextTokenizer.ReplacePlain(@"{\fDotum|b0|i0|c129|p50;수 정 #1}", edited));
	}

	[Fact]
	public void ReplacePlain_NewLineBecomesParagraph()
	{
		Assert.Equal(@"{\W0.8;A\PB\PC}", MTextTokenizer.ReplacePlain(@"{\W0.8;A\PC}", "A\nB\nC"));
	}

	[Fact]
	public void Paragraph_IndentsAndTabs_AreParsed()
	{
		var tokens = MTextTokenizer.Tokenize(Notes);
		var first = tokens.First(t => t.Kind == MTextTokenKind.Char);
		Assert.Equal(-6.4, first.Paragraph.FirstIndent, 3);
		Assert.Equal(6.4, first.Paragraph.LeftIndent, 3);
		Assert.Equal(new[] { 6.4, 150.4 }, first.Paragraph.Tabs);
		Assert.Equal(0.8, first.Format.Width, 3);
		var ga = tokens.Where((t, i) => t.Char == '가' && i + 1 < tokens.Count && tokens[i + 1].Char == '.').First();
		Assert.Equal(22.4, ga.Paragraph.LeftIndent, 3);
		// \L은 EGI 뒤에 있어 밑줄이 켜졌다가 다음 문단 \l에서 꺼진다 (보이는 밑줄 없음)
		Assert.False(tokens.First(t => t.Char == 'E').Format.Underline);
		Assert.False(tokens.First(t => t.Char == '나').Format.Underline);
	}

	[Fact]
	public void Notes_LayoutLikeStd_TenLinesNoExtraWrap()
	{
		var doc = new CadDocument();
		var style = new TextStyle("Malgun") { Filename = "malgun.ttf" };
		doc.TextStyles.Add(style);
		var mt = new MText
		{
			Value = Notes,
			Height = 3.2,
			RectangleWidth = 137,
			InsertPoint = new XYZ(0, 0, 0),
			AttachmentPoint = AttachmentPointType.TopLeft,
			LineSpacing = 1.3125,
			LineSpacingStyle = LineSpacingStyleType.Exact,
			Style = style,
		};
		doc.Entities.Add(mt);
		var scene = SceneBuilder.Build(doc);
		var pick = Assert.Single(scene.Texts);
		Assert.True(pick.PickOnly);
		// 10문단이 한 줄씩(폭 비율 0.8이라 3·8번도 줄바꿈 없음): 위 3.2 + 9줄 × 7.0 + 밑부분 ≈ 67
		double height = pick.Bounds.Height;
		Assert.InRange(height, 62, 70);
		// 글자는 도형(윤곽 채움)으로 그려진다
		Assert.NotEmpty(scene.Fills);
		// 첫 줄 "1." 은 왼쪽 끝(0), 탭 뒤 "재료"는 6.4부터
		Assert.InRange(pick.Bounds.MinX, -0.5, 0.8);
		Assert.True(pick.Bounds.MaxX <= 137.5, $"글상자 폭을 넘음: {pick.Bounds.MaxX}");
	}

	// ===== 단락 (기존 CAD 단락 대화상자가 저장한 문자열) =====

	[Fact]
	public void Paragraph_StdDialogCodes_Parsed()
	{
		var p = MTextTokenizer.Tokenize(@"\pxa0.8,b0.4,i1.2,l0.8,r1.2,qc,sm1.5,,t4;Hello").First().Paragraph;
		Assert.True(p.Relative);
		Assert.Equal(0.8, p.After, 6);
		Assert.Equal(0.4, p.Before, 6);
		Assert.Equal(1.2, p.FirstIndent, 6);
		Assert.Equal(0.8, p.LeftIndent, 6);
		Assert.Equal(1.2, p.RightIndent, 6);
		Assert.Equal('c', p.Align);
		Assert.Equal('m', p.SpacingKind);
		Assert.Equal(1.5, p.Spacing, 6);
		Assert.Equal(new[] { 4.0 }, p.Tabs);

		// t 다음 r8은 오른쪽 탭 (오른쪽 들여쓰기가 아님), D. = 소수점 탭
		var t = MTextTokenizer.Tokenize(@"\pxse3,,tc4,r8,D.10;Tabs").First().Paragraph;
		Assert.Equal(0, t.RightIndent);
		Assert.Equal(new[] { 4.0, 8, 10 }, t.Tabs);
		Assert.Equal(new[] { 'c', 'r', 'd' }, t.TabKinds);
		Assert.Equal('e', t.SpacingKind);
		Assert.Equal(3, t.Spacing, 6);
	}

	[Fact]
	public void Paragraph_Format_RoundTripsStdStrings()
	{
		foreach (string code in new[] { @"\pxa0.8,b0.4,i1.2,l0.8,r1.2,qc,sm1.5,,t4;", @"\pxse3,,tc4,r8,D.10;", @"\pxl2;", @"\pxb2;" })
		{
			var p = MTextTokenizer.Tokenize(code + "A").First().Paragraph;
			Assert.Equal(code, MTextTokenizer.FormatParagraph(p));
		}
	}

	private static double FirstInkX(string value, double height)
	{
		var doc = new CadDocument();
		var style = new TextStyle("Malgun") { Filename = "malgun.ttf" };
		doc.TextStyles.Add(style);
		doc.Entities.Add(new MText { Value = value, Height = height, RectangleWidth = 40, InsertPoint = new XYZ(0, 0, 0), AttachmentPoint = AttachmentPointType.TopLeft, Style = style });
		return SceneBuilder.Build(doc).Texts.Single().Bounds.MinX;
	}

	[Fact]
	public void Paragraph_Units_PlainIsDrawingUnits_XIsTextHeights()
	{
		// 기존 CAD 화면: \pl2; (높이 2.5) → x 2, \pxl2; → x 5, 높이 5에서 \pxl2; → x 10
		Assert.InRange(FirstInkX(@"\pl2;AAAA", 2.5), 1.9, 2.4);
		Assert.InRange(FirstInkX(@"\pxl2;AAAA", 2.5), 4.9, 5.4);
		Assert.InRange(FirstInkX(@"\pxl2;AAAA", 5), 9.9, 10.5);
	}

	[Fact]
	public void SetParagraph_ReplacesCodeAndKeepsNextParagraph()
	{
		var p = new MTextParagraph(0, 2, 0, Array.Empty<double>(), 'c') { Relative = true };
		// 둘째 문단만 바꾸면 셋째 문단은 원래(기본) 서식을 적어 둔다
		string raw = MTextTokenizer.SetParagraph(@"AAA\PBBB\PCCC", 5, 5, p, 'l', 2.5);
		Assert.Equal(@"AAA\P\pxl2,qc;BBB\P\pxi0,l0,r0,ql,t0;CCC", raw);
		Assert.Equal(2, MTextTokenizer.ParagraphAt(raw, 5).LeftIndent, 6);
		Assert.Equal(0, MTextTokenizer.ParagraphAt(raw, 9).LeftIndent, 6);
		// 이미 있던 \p 코드는 바뀐다 (도면 단위 \p도 높이 배수로)
		string again = MTextTokenizer.SetParagraph(@"\pl5;AAA", 0, 0, p with { Align = '\0' }, 'l', 2.5);
		Assert.Equal(@"\pxl2;AAA", again);
	}

	// ===== 열 (기존 CAD 칼럼: 확장 데이터 ACAD_MTEXT_COLUMN_INFO) =====

	private const string ColumnWords = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty";

	[Fact]
	public void Columns_WriteRead_RoundTripsThroughDwg()
	{
		var ed = EditorDocument.New();
		var mt = new MText { Value = ColumnWords, Height = 2.5, RectangleWidth = 20, InsertPoint = new XYZ(0, 0, 0) };
		ed.AddEntities(mt);
		MTextColumns.Write(mt, new MTextColumnInfo(1, false, 2, false, 20, 2, new double[2], 12));
		Assert.Equal(42, mt.RectangleWidth, 6); // 전체 폭 = 20 × 2 + 2
		string path = Path.Combine(Path.GetTempPath(), "f2d_cols_" + Guid.NewGuid().ToString("N") + ".dwg");
		try
		{
			ed.Save(path);
			var back = ACadSharp.IO.DwgReader.Read(path).Entities.OfType<MText>().Single();
			var info = MTextColumns.Read(back);
			Assert.NotNull(info);
			Assert.Equal(1, info!.Type);
			Assert.Equal(2, info.Count);
			Assert.Equal(20, info.Width, 6);
			Assert.Equal(2, info.Gutter, 6);
			Assert.Equal(12, info.DefinedHeight, 6);
		}
		finally
		{
			File.Delete(path);
			File.Delete(Path.ChangeExtension(path, ".bak"));
		}
		// 열 없음으로 바꾸면 확장 데이터에서 빠진다
		MTextColumns.Write(mt, null);
		Assert.Null(MTextColumns.Read(mt));
	}

	[Fact]
	public void Columns_StaticFlow_LikeStd()
	{
		// 기존 CAD 화면: 폭 20·간격 2·높이 12인 정적 2열 → 첫 열 3줄(one two / three four / five six), 나머지는 x 22부터 둘째 열
		var doc = new CadDocument();
		var style = new TextStyle("Malgun") { Filename = "malgun.ttf" };
		doc.TextStyles.Add(style);
		var mt = new MText { Value = ColumnWords, Height = 2.5, RectangleWidth = 42, InsertPoint = new XYZ(0, 0, 0), AttachmentPoint = AttachmentPointType.TopLeft, Style = style };
		doc.Entities.Add(mt);
		MTextColumns.Write(mt, new MTextColumnInfo(1, false, 2, false, 20, 2, new double[2], 12));
		var scene = SceneBuilder.Build(doc);
		var pick = Assert.Single(scene.Texts);
		Assert.InRange(pick.Bounds.MaxX, 30, 42.5); // 둘째 열까지
		// 첫 열은 높이 12 안 (3줄), 둘째 열은 나머지 모두 → 첫 열보다 훨씬 길다
		Assert.True(pick.Bounds.MinY < -40, $"둘째 열이 길게 이어져야 함: {pick.Bounds.MinY}");
	}

	// ===== 배경 마스크 (기존 CAD "배경 설정": 90 = 1, 45 = 계수, 63 = 색) =====

	[Fact]
	public void BackgroundMask_RoundTripsAndDrawsBand()
	{
		var ed = EditorDocument.New();
		var mt = new MText { Value = "Mask", Height = 2.5, RectangleWidth = 300, InsertPoint = new XYZ(-600, 250, 0), AttachmentPoint = AttachmentPointType.TopLeft };
		mt.BackgroundFillFlags = BackgroundFillFlags.UseBackgroundFillColor;
		mt.BackgroundScale = 1.2;
		mt.BackgroundColor = new ACadSharp.Color(5);
		ed.AddEntities(mt);
		string path = Path.Combine(Path.GetTempPath(), "f2d_mask_" + Guid.NewGuid().ToString("N") + ".dwg");
		try
		{
			ed.Save(path);
			var back = ACadSharp.IO.DwgReader.Read(path).Entities.OfType<MText>().Single();
			Assert.Equal(BackgroundFillFlags.UseBackgroundFillColor, back.BackgroundFillFlags);
			Assert.Equal(1.2, back.BackgroundScale, 6);
			Assert.Equal(5, back.BackgroundColor.Index);
		}
		finally
		{
			File.Delete(path);
			File.Delete(Path.ChangeExtension(path, ".bak"));
		}

		// 기존 CAD 화면: 왼쪽 -600.5, 위 250.5 (계수 1.2 → (1.2 - 1) × 2.5 = 0.5), 아래 ≈ 246.83, 폭 = 글상자 폭 + 0.5 × 2
		var scene = SceneBuilder.Build(ed.Document);
		var band = Assert.Single(scene.Wipeouts, w => w.Fill != null);
		Assert.Equal(-600.5, band.Bounds.MinX, 2);
		Assert.Equal(-299.5, band.Bounds.MaxX, 2);
		Assert.Equal(250.5, band.Bounds.MaxY, 2);
		Assert.InRange(band.Bounds.MinY, 246.6, 247.0);
		Assert.Equal(new Rgb(0, 0, 255), band.Fill);
	}

	[Fact]
	public void SplitColumns_FromR2004_AreMergedLikeStd()
	{
		// 기존 CAD가 R2004로 저장한 열 문자: 첫 문자 + ACAD_MTEXT_COLUMNS(나머지 열 문자 핸들) → 열 때 하나로 합침
		var doc = new CadDocument();
		var first = new MText { Value = "one two ", Height = 2.5, RectangleWidth = 42 };
		var second = new MText { Value = "three four", Height = 2.5, RectangleWidth = 42, InsertPoint = new XYZ(22, 0, 0) };
		doc.Entities.Add(first);
		doc.Entities.Add(second);
		MTextColumns.Write(first, new MTextColumnInfo(1, false, 2, false, 20, 2, new double[2], 5));
		var data = first.ExtendedData.Get("ACAD");
		data.Records.Insert(data.Records.Count - 4, new ACadSharp.XData.ExtendedDataString("ACAD_MTEXT_COLUMNS_BEGIN"));
		data.Records.Insert(data.Records.Count - 4, new ACadSharp.XData.ExtendedDataInteger16(47));
		data.Records.Insert(data.Records.Count - 4, new ACadSharp.XData.ExtendedDataInteger16(2));
		data.Records.Insert(data.Records.Count - 4, new ACadSharp.XData.ExtendedDataHandle(second.Handle));
		data.Records.Insert(data.Records.Count - 4, new ACadSharp.XData.ExtendedDataString("ACAD_MTEXT_COLUMNS_END"));
		Assert.True(MTextColumns.Read(first)!.Split);

		Assert.Equal(1, MTextColumns.MergeSplit(doc));
		Assert.Equal("one two three four", Assert.Single(doc.Entities.OfType<MText>()).Value);
		var info = MTextColumns.Read(first);
		Assert.NotNull(info);
		Assert.False(info!.Split);
		Assert.Equal(2, info.Count);
	}
}
