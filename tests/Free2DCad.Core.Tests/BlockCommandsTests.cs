using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

public class BlockCommandsTests : IDisposable
{
	private readonly string _tempDir;

	public BlockCommandsTests()
	{
		this._tempDir = Path.Combine(Path.GetTempPath(), "Free2DCad_Block_Tests_" + Guid.NewGuid().ToString("N"));
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
	public void BlockCommand_CreatesBlockRecordAndInsert_AndUndoRestoresOriginalEntities()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(10, 10, 0), new XYZ(50, 10, 0));
		var l2 = new Line(new XYZ(50, 10, 0), new XYZ(50, 40, 0));
		doc.AddEntities(l1, l2);

		var cmd = new BlockCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnText("MY_BLOCK");
		cmd.OnPoint(10, 10);
		cmd.OnSelection(new[] { l1, l2 });
		cmd.OnEnter();

		Assert.True(cmd.IsFinished);

		// 블록 레코드 생성 확인
		Assert.True(doc.Document.BlockRecords.Contains("MY_BLOCK"));
		var block = doc.Document.BlockRecords["MY_BLOCK"];
		Assert.Equal(2, block.Entities.Count);

		// 블록 내부 엔티티의 로컬 좌표 변환 확인 (기준점 (10, 10) -> (0, 0))
		var bl1 = block.Entities.OfType<Line>().FirstOrDefault(l => Math.Abs(l.StartPoint.X) < 1e-4);
		Assert.NotNull(bl1);
		Assert.Equal(0, bl1.StartPoint.X, precision: 2);
		Assert.Equal(0, bl1.StartPoint.Y, precision: 2);
		Assert.Equal(40, bl1.EndPoint.X, precision: 2);
		Assert.Equal(0, bl1.EndPoint.Y, precision: 2);

		// 원본 엔티티는 삭제되고 Insert 생성 확인
		Assert.Empty(doc.Document.Entities.OfType<Line>());
		var inserts = doc.Document.Entities.OfType<Insert>().ToList();
		var insert = Assert.Single(inserts);
		Assert.Equal("MY_BLOCK", insert.Block.Name);
		Assert.Equal(10, insert.InsertPoint.X, precision: 2);
		Assert.Equal(10, insert.InsertPoint.Y, precision: 2);

		// 단일 Undo로 원래 선 2개 복원 및 블록/Insert 제거 확인
		doc.Undo();
		Assert.Empty(doc.Document.Entities.OfType<Insert>());
		Assert.Equal(2, doc.Document.Entities.OfType<Line>().Count());
		Assert.False(doc.Document.BlockRecords.Contains("MY_BLOCK"));

		// Redo 시 다시 블록 및 Insert 복원 확인
		doc.Redo();
		Assert.True(doc.Document.BlockRecords.Contains("MY_BLOCK"));
		Assert.Single(doc.Document.Entities.OfType<Insert>());
		Assert.Empty(doc.Document.Entities.OfType<Line>());
	}

	[Fact]
	public void BlockCommand_DuplicateName_PrintsMessageAndAllowsRetry()
	{
		var doc = EditorDocument.New();
		var existingBlock = new BlockRecord("EXISTING_BLK");
		doc.Document.BlockRecords.Add(existingBlock);

		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(10, 0, 0));
		doc.AddEntities(l1);

		string printed = string.Empty;
		var cmd = new BlockCommand();
		var ctx = new CommandContext(doc, print: msg => printed = msg);
		cmd.Start(ctx);

		cmd.OnText("EXISTING_BLK");
		Assert.Contains("이미 있습니다", printed);
		Assert.False(cmd.IsFinished);

		cmd.OnText("NEW_BLK");
		cmd.OnPoint(0, 0);
		cmd.OnSelection(new[] { l1 });
		cmd.OnEnter();

		Assert.True(cmd.IsFinished);
		Assert.True(doc.Document.BlockRecords.Contains("NEW_BLK"));
	}

	[Fact]
	public void InsertCommand_InsertsAtTargetLocation_AndSavesAC1018Dwg_ReloadsWithBlockAndInserts()
	{
		var doc = EditorDocument.New();
		var l1 = new Line(new XYZ(0, 0, 0), new XYZ(20, 0, 0));
		var l2 = new Line(new XYZ(20, 0, 0), new XYZ(20, 20, 0));
		doc.AddEntities(l1, l2);

		// 1. BLOCK 명령으로 MY_TEST_BLK 생성 (Insert 1개 생성)
		var blockCmd = new BlockCommand();
		var ctx1 = new CommandContext(doc);
		blockCmd.Start(ctx1);
		blockCmd.OnText("MY_TEST_BLK");
		blockCmd.OnPoint(0, 0);
		blockCmd.OnSelection(new[] { l1, l2 });
		blockCmd.OnEnter();
		Assert.True(blockCmd.IsFinished);

		// 2. INSERT 명령으로 다른 위치(100, 200)에 축척 2, 회전 90도로 삽입
		var insertCmd = new InsertCommand();
		var ctx2 = new CommandContext(doc);
		insertCmd.Start(ctx2);
		insertCmd.OnText("MY_TEST_BLK");
		insertCmd.OnPoint(100, 200);
		insertCmd.OnText("2.0");
		insertCmd.OnText("90");
		Assert.True(insertCmd.IsFinished);

		// 현재 도면에 Insert 2개 확인
		var inserts = doc.Document.Entities.OfType<Insert>().ToList();
		Assert.Equal(2, inserts.Count);

		// 3. AC1018 DWG 파일로 저장
		string filePath = Path.Combine(this._tempDir, "block_insert_test.dwg");
		doc.Save(filePath);

		// 4. 저장된 DWG 다시 읽기 검증
		var reloaded = DwgReader.Read(filePath);
		Assert.True(reloaded.BlockRecords.Contains("MY_TEST_BLK"));
		var reloadedBlock = reloaded.BlockRecords["MY_TEST_BLK"];
		Assert.Equal(2, reloadedBlock.Entities.OfType<Line>().Count());

		var reloadedInserts = reloaded.Entities.OfType<Insert>().ToList();
		Assert.Equal(2, reloadedInserts.Count);

		var ins1 = reloadedInserts.FirstOrDefault(i => Math.Abs(i.InsertPoint.X - 0) < 1e-3);
		var ins2 = reloadedInserts.FirstOrDefault(i => Math.Abs(i.InsertPoint.X - 100) < 1e-3);
		Assert.NotNull(ins1);
		Assert.NotNull(ins2);
		Assert.Equal(200, ins2.InsertPoint.Y, precision: 2);
		Assert.Equal(2.0, ins2.XScale, precision: 2);
	}

	[Fact]
	public void InsertCommand_WithAttributeDefinitions_GeneratesAttributeEntities()
	{
		var doc = EditorDocument.New();
		var block = new BlockRecord("BLK_WITH_ATT");
		var attDef = new AttributeDefinition
		{
			Tag = "PART_NO",
			Value = "PART-001",
			Prompt = "부품 번호 입력",
			InsertPoint = new XYZ(0, 0, 0),
		};
		block.Entities.Add(attDef);
		doc.Document.BlockRecords.Add(block);

		var cmd = new InsertCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnText("BLK_WITH_ATT");
		cmd.OnPoint(50, 50);
		cmd.OnText("1.0");
		cmd.OnText("0");
		// 속성 값을 묻는다 (프롬프트 <기본값>), Enter = 기본값
		Assert.Equal("부품 번호 입력 <PART-001>:", cmd.Prompt);
		cmd.OnEnter();
		Assert.True(cmd.IsFinished);

		var insert = Assert.Single(doc.Document.Entities.OfType<Insert>());
		Assert.True(insert.HasAttributes);
		var att = Assert.Single(insert.Attributes);
		Assert.Equal("PART_NO", att.Tag);
		Assert.Equal("PART-001", att.Value);
	}
}
