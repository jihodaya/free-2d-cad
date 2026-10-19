using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Editing;
using Free2DCad.Core.Tables;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>표 그립으로 크기 조절 (기존 CAD: 열 경계 = 그 열 너비, 오른쪽 위 = 전체 너비, 왼쪽 아래 = 전체 높이, 오른쪽 아래 = 둘 다).</summary>
public class TableGripTests
{
	private static (EditorDocument Doc, TableEntity Table) Make()
	{
		var doc = EditorDocument.New();
		var t = TableEditor.Create(doc.Document, new XYZ(0, 100, 0), new TableEditor.TableSpec(3, 20, 2, 1, 2.5, true, true));
		doc.AddEntities(t);
		return (doc, t);
	}

	[Fact]
	public void ColumnGrip_ChangesThatColumn_AndUndo()
	{
		var (doc, t) = Make();
		var grip = GripEditor.GetGrips(t).Single(g => g.Index == 100); // 첫 열 오른쪽 경계 (x = 20)
		Assert.Equal(20, grip.X, 6);
		doc.Execute(GripEditor.CreateEdit(t, grip, 35, grip.Y)!);
		Assert.Equal(35, t.Columns[0].Width, 6);
		Assert.Equal(20, t.Columns[1].Width, 6);
		Assert.Contains(t.Block.Entities.OfType<Line>(), l => Math.Abs(l.StartPoint.X - 35) < 1e-6 && Math.Abs(l.EndPoint.X - 35) < 1e-6);
		doc.Undo();
		Assert.Equal(20, t.Columns[0].Width, 6);
	}

	[Fact]
	public void CornerGrips_ScaleWidthAndHeight()
	{
		var (doc, t) = Make();
		double h = t.Rows.Sum(r => r.Height);
		var tr = GripEditor.GetGrips(t).Single(g => g.Index == 200);
		doc.Execute(GripEditor.CreateEdit(t, tr, 90, tr.Y)!); // 60 → 90
		Assert.Equal(90, t.Columns.Sum(c => c.Width), 6);
		Assert.Equal(30, t.Columns[2].Width, 6);
		var br = GripEditor.GetGrips(t).Single(g => g.Index == 202);
		doc.Execute(GripEditor.CreateEdit(t, br, 120, 100 - 2 * h)!);
		Assert.Equal(120, t.Columns.Sum(c => c.Width), 6);
		Assert.Equal(2 * h, t.Rows.Sum(r => r.Height), 6);
		var preview = GripEditor.Preview(t, GripEditor.GetGrips(t).Single(g => g.Index == 201), 0, 100 - h);
		Assert.NotNull(preview);
		Assert.Equal(2 * h, t.Rows.Sum(r => r.Height), 6); // 미리보기는 원본을 바꾸지 않음
	}
}
