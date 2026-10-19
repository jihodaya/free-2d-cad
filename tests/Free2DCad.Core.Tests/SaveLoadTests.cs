using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

public class SaveLoadTests : IDisposable
{
	private readonly string _tempDir;

	public SaveLoadTests()
	{
		this._tempDir = Path.Combine(Path.GetTempPath(), "Free2DCad_Core_Tests_" + Guid.NewGuid().ToString("N"));
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
			// 정리 실패는 무시
		}
	}

	[Fact]
	public void Save_KoreanText_And_Line_As_AC1018_And_ReloadWithDwgReader()
	{
		var ed = EditorDocument.New();
		string dwgPath = Path.Combine(this._tempDir, "korean_test.dwg");

		var text = new TextEntity
		{
			Value = "한글 테스트 %%c10",
			InsertPoint = new XYZ(10, 20, 0),
			Height = 2.5
		};

		var line = new Line(new XYZ(0, 0, 0), new XYZ(100, 50, 0));

		ed.AddEntities(text, line);

		// Save to DWG
		ed.Save(dwgPath);

		Assert.False(ed.IsDirty);
		Assert.Equal(dwgPath, ed.FilePath);
		Assert.True(File.Exists(dwgPath));

		// Read back with DwgReader
		CadDocument reloadedDoc = DwgReader.Read(dwgPath);

		Assert.Equal(ACadVersion.AC1018, reloadedDoc.Header.Version);
		Assert.Equal("ANSI_949", reloadedDoc.Header.CodePage);

		// Verify TEXT entity
		var reloadedText = reloadedDoc.Entities.OfType<TextEntity>().FirstOrDefault();
		Assert.NotNull(reloadedText);
		Assert.Equal("한글 테스트 %%c10", reloadedText.Value);
		Assert.Equal(10, reloadedText.InsertPoint.X, precision: 3);
		Assert.Equal(20, reloadedText.InsertPoint.Y, precision: 3);

		// Verify LINE entity
		var reloadedLine = reloadedDoc.Entities.OfType<Line>().FirstOrDefault();
		Assert.NotNull(reloadedLine);
		Assert.Equal(0, reloadedLine.StartPoint.X, precision: 3);
		Assert.Equal(0, reloadedLine.StartPoint.Y, precision: 3);
		Assert.Equal(100, reloadedLine.EndPoint.X, precision: 3);
		Assert.Equal(50, reloadedLine.EndPoint.Y, precision: 3);
	}

	[Fact]
	public void Save_CharOutsideCodePage_WrittenAsUnicodeEscape()
	{
		// 코드 페이지(ANSI_949)에 없는 지름 기호 ∅(U+2205)는 기존 CAD처럼 \U+2205로 (예전: '?'로 깨짐)
		var ed = EditorDocument.New();
		string dwgPath = Path.Combine(this._tempDir, "unicode_escape.dwg");
		ed.AddEntities(new MText { Value = "∅10 한글", InsertPoint = new XYZ(0, 0, 0), Height = 2.5 },
			new TextEntity { Value = "∅5", InsertPoint = new XYZ(0, 10, 0), Height = 2.5 });
		ed.Save(dwgPath);

		CadDocument reloaded = DwgReader.Read(dwgPath);
		Assert.Equal(@"\U+220510 한글", reloaded.Entities.OfType<MText>().Single().Value);
		string text = reloaded.Entities.OfType<TextEntity>().Single().Value;
		Assert.Equal(@"\U+22055", text);
		Assert.Equal("∅5", Free2DCad.Rendering.MTextTokenizer.PlainText(text, isMText: false));
	}

	[Fact]
	public void Save_CreatesBakFile_WhenFileAlreadyExists()
	{
		var ed = EditorDocument.New();
		string dwgPath = Path.Combine(this._tempDir, "backup_test.dwg");
		string bakPath = Path.Combine(this._tempDir, "backup_test.bak");

		var line1 = new Line(new XYZ(0, 0, 0), new XYZ(10, 10, 0));
		ed.AddEntities(line1);

		// First save: creates dwg, no bak yet
		ed.Save(dwgPath);
		Assert.True(File.Exists(dwgPath));
		Assert.False(File.Exists(bakPath));

		// Modify document
		var line2 = new Line(new XYZ(10, 10, 0), new XYZ(20, 20, 0));
		ed.AddEntities(line2);

		// Second save: should create .bak backup of the previous file
		ed.Save();
		Assert.True(File.Exists(bakPath));

		// Verify backup file can be read and contains only 1 entity
		CadDocument bakDoc = DwgReader.Read(bakPath);
		Assert.Single(bakDoc.Entities.OfType<Line>());

		// Verify current file contains 2 entities
		CadDocument currentDoc = DwgReader.Read(dwgPath);
		Assert.Equal(2, currentDoc.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void Save_ConvertsCodePage_FromAnsi1252_ToAnsi949_WhenKoreanPresent()
	{
		var ed = EditorDocument.New();
		ed.Document.Header.CodePage = "ANSI_1252";

		string dwgPath = Path.Combine(this._tempDir, "codepage_test.dwg");

		var text = new TextEntity
		{
			Value = "도면 한글 텍스트",
			InsertPoint = new XYZ(0, 0, 0)
		};
		ed.AddEntities(text);

		ed.Save(dwgPath);

		CadDocument reloadedDoc = DwgReader.Read(dwgPath);
		Assert.Equal("ANSI_949", reloadedDoc.Header.CodePage);
	}

	[Fact]
	public void Open_OpensExistingDwgFile()
	{
		string dwgPath = Path.Combine(this._tempDir, "open_test.dwg");

		var doc = new CadDocument();
		doc.Header.Version = ACadVersion.AC1018;
		doc.Header.CodePage = "ANSI_949";
		doc.Entities.Add(new Line(new XYZ(1, 2, 0), new XYZ(3, 4, 0)));
		DwgWriter.Write(dwgPath, doc);

		var ed = EditorDocument.Open(dwgPath);
		Assert.NotNull(ed);
		Assert.Equal(dwgPath, ed.FilePath);
		Assert.False(ed.IsDirty);
		Assert.Single(ed.Document.Entities);
	}
}
