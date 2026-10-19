using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using Free2DCad.Core.Tables;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>표 만들기·저장(2004)·다시 읽기, 칸 문자, 행·열 추가/삭제, CSV.</summary>
public class TableTests
{
	private static (EditorDocument Doc, TableEntity Table) NewTable()
	{
		var doc = EditorDocument.New();
		var t = TableEditor.Create(doc.Document, new XYZ(10, 20, 0), TableEditor.TableSpec.Default);
		doc.Document.Entities.Add(t);
		return (doc, t);
	}

	[Fact]
	public void Create_LikeStd_TitleMergedAcrossColumns()
	{
		var (_, t) = NewTable();
		Assert.Equal(3, t.Rows.Count); // 제목 + 헤더 + 데이터 1
		Assert.Equal(5, t.Columns.Count);
		Assert.Equal(5, t.Rows[0].Cells[0].BorderWidth);
		Assert.All(t.Rows[0].Cells.Skip(1), c => Assert.Equal(1, c.MergedValue));
		Assert.Equal(9, t.Rows[1].Height, 3);
		Assert.True(t.Block!.IsAnonymous);
		// 격자: 가로 4줄 + 세로 (양끝 2 + 안쪽 4)
		Assert.Equal(10, t.Block.Entities.OfType<Line>().Count());
	}

	[Fact]
	public void SaveAs2004_ReadBack_KeepsTableAndCellText()
	{
		var (doc, t) = NewTable();
		TableEditor.SetCellText(doc.Document, t, 0, 0, "부품 목록");
		TableEditor.SetCellText(doc.Document, t, 1, 2, "품명");
		TableEditor.SetCellText(doc.Document, t, 2, 4, "SUS304");
		string path = Path.Combine(Path.GetTempPath(), "free2dcad-formats", "table-new.dwg");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		doc.Save(path);

		var back = DwgReader.Read(path).Entities.OfType<TableEntity>().Single();
		Assert.Equal(3, back.Rows.Count);
		Assert.Equal(5, back.Columns.Count);
		Assert.Equal("부품 목록", TableEditor.GetCellText(back, 0, 0));
		Assert.Equal("품명", TableEditor.GetCellText(back, 1, 2));
		Assert.Equal("SUS304", TableEditor.GetCellText(back, 2, 4));
		Assert.Contains(back.Block!.Entities.OfType<MText>(), m => m.Value == "SUS304");
	}

	[Fact]
	public void HitCell_And_NextCell_SkipMergedTitleCells()
	{
		var (_, t) = NewTable();
		// 제목 행 가운데(합친 칸)는 주인 칸 (0,0)
		Assert.Equal((0, 0), TableEditor.HitCell(t, 10 + 150, 20 - 3));
		Assert.Equal((1, 1), TableEditor.HitCell(t, 10 + 63.5 + 5, 20 - 9.144 - 2));
		Assert.Null(TableEditor.HitCell(t, 0, 0));
		Assert.Equal((1, 0), TableEditor.NextCell(t, 0, 0, backward: false));
		Assert.Equal((0, 0), TableEditor.NextCell(t, 1, 0, backward: true));
	}

	[Fact]
	public void InsertDeleteRowsColumns_KeepTitleSpan()
	{
		var (doc, t) = NewTable();
		TableEditor.InsertRow(doc.Document, t, t.Rows.Count);
		Assert.Equal(4, t.Rows.Count);
		TableEditor.InsertColumn(doc.Document, t, t.Columns.Count);
		Assert.Equal(6, t.Columns.Count);
		Assert.Equal(6, t.Rows[0].Cells[0].BorderWidth);
		TableEditor.DeleteColumn(doc.Document, t, 5);
		TableEditor.DeleteRow(doc.Document, t, 3);
		Assert.Equal(5, t.Columns.Count);
		Assert.Equal(3, t.Rows.Count);
		Assert.Equal(5, t.Rows[0].Cells[0].BorderWidth);
	}

	[Fact]
	public void Snapshot_Restore_And_Csv()
	{
		var (doc, t) = NewTable();
		var snap = new TableEditor.Snapshot(t);
		TableEditor.SetCellText(doc.Document, t, 2, 0, "a,b");
		TableEditor.InsertRow(doc.Document, t, 3);
		snap.Restore(t);
		Assert.Equal(3, t.Rows.Count);
		Assert.Equal(string.Empty, TableEditor.GetCellText(t, 2, 0));
		TableEditor.SetCellText(doc.Document, t, 2, 0, "a,b");
		Assert.StartsWith("\"a,b\"", TableEditor.ToCsv(t).Split('\n')[2]);
	}
}
