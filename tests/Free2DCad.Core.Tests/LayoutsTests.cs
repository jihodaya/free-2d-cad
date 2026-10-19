using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Commands;
using Xunit;

namespace Free2DCad.Core.Tests;

public class LayoutsTests
{
	[Fact]
	public void AddLayout_CreatesA3LayoutWithViewports_AndUndoRedoWorks()
	{
		var doc = EditorDocument.New();
		// 모형 공간에 선 추가하여 바운딩 박스 형성
		doc.AddEntities(new Line(new XYZ(0, 0, 0), new XYZ(1000, 500, 0)));

		var layout = doc.AddLayout("배치1");

		Assert.NotNull(layout);
		Assert.Equal("배치1", layout.Name);
		Assert.Equal(420, layout.PaperWidth);
		Assert.Equal(297, layout.PaperHeight);
		Assert.Equal(PlotPaperUnits.Millimeters, layout.PaperUnits);
		Assert.Equal(0, layout.MinLimits.X);
		Assert.Equal(0, layout.MinLimits.Y);
		Assert.Equal(420, layout.MaxLimits.X);
		Assert.Equal(297, layout.MaxLimits.Y);
		Assert.True(doc.Document.Layouts.ContainsKey("배치1"));

		// 뷰포트 확인: PaperViewport (Id 1) + Model Viewport (Id 2)
		var viewports = layout.AssociatedBlock.Entities.OfType<Viewport>().ToList();
		Assert.True(viewports.Count >= 2);

		var paperVp = viewports[0];
		Assert.Equal(1, paperVp.Id);
		Assert.True(paperVp.RepresentsPaper);

		var modelVp = viewports[1];
		Assert.Equal(2, modelVp.Id);
		Assert.False(modelVp.RepresentsPaper);
		Assert.True(modelVp.Width > 0);
		Assert.True(modelVp.Height > 0);
		Assert.True(modelVp.ViewHeight > 0);

		// Undo 확인
		doc.Undo();
		Assert.False(doc.Document.Layouts.ContainsKey("배치1"));
		Assert.False(doc.Document.BlockRecords.Contains(layout.AssociatedBlock.Name));

		// Redo 확인
		doc.Redo();
		Assert.True(doc.Document.Layouts.ContainsKey("배치1"));
		Assert.True(doc.Document.BlockRecords.Contains(layout.AssociatedBlock.Name));
	}

	[Fact]
	public void AddLayout_SaveAC1018Dwg_AndReload_ContainsLayoutAndViewports()
	{
		var doc = EditorDocument.New();
		doc.AddEntities(new Line(new XYZ(0, 0, 0), new XYZ(200, 200, 0)));
		var layout = doc.AddLayout("도면시트1");

		string tempPath = Path.Combine(Path.GetTempPath(), $"f2d_test_layout_{Guid.NewGuid():N}.dwg");
		try
		{
			doc.Save(tempPath);
			Assert.True(File.Exists(tempPath));

			CadDocument loaded = DwgReader.Read(tempPath);
			Assert.NotNull(loaded);
			Assert.True(loaded.Layouts.ContainsKey("도면시트1"));

			var loadedLayout = loaded.Layouts["도면시트1"];
			Assert.Equal(420, loadedLayout.PaperWidth);
			Assert.Equal(297, loadedLayout.PaperHeight);

			var viewports = loadedLayout.AssociatedBlock.Entities.OfType<Viewport>().ToList();
			Assert.True(viewports.Count >= 2);
		}
		finally
		{
			if (File.Exists(tempPath))
			{
				try { File.Delete(tempPath); } catch { }
			}
			string bak = Path.ChangeExtension(tempPath, ".bak");
			if (File.Exists(bak))
			{
				try { File.Delete(bak); } catch { }
			}
		}
	}

	[Fact]
	public void RenameLayout_ChangesName_AndUndoRedoRestores()
	{
		var doc = EditorDocument.New();
		var layout = doc.AddLayout("임시배치");

		doc.RenameLayout(layout, "정식배치");
		Assert.Equal("정식배치", layout.Name);
		Assert.True(doc.Document.Layouts.ContainsKey("정식배치"));
		Assert.False(doc.Document.Layouts.ContainsKey("임시배치"));

		// Undo
		doc.Undo();
		Assert.Equal("임시배치", layout.Name);
		Assert.True(doc.Document.Layouts.ContainsKey("임시배치"));
		Assert.False(doc.Document.Layouts.ContainsKey("정식배치"));

		// Redo
		doc.Redo();
		Assert.Equal("정식배치", layout.Name);
		Assert.True(doc.Document.Layouts.ContainsKey("정식배치"));
		Assert.False(doc.Document.Layouts.ContainsKey("임시배치"));
	}

	[Fact]
	public void RemoveLayout_DeletesLayout_AndUndoRedoWorks()
	{
		var doc = EditorDocument.New();
		var lay1 = doc.AddLayout("시트1");
		var lay2 = doc.AddLayout("시트2");

		doc.RemoveLayout(lay1);
		Assert.False(doc.Document.Layouts.ContainsKey("시트1"));
		Assert.True(doc.Document.Layouts.ContainsKey("시트2"));

		// Undo
		doc.Undo();
		Assert.True(doc.Document.Layouts.ContainsKey("시트1"));

		// Redo
		doc.Redo();
		Assert.False(doc.Document.Layouts.ContainsKey("시트1"));
	}

	[Fact]
	public void RemoveLayout_Throws_WhenDeletingModelOrLastRemainingLayout()
	{
		var doc = EditorDocument.New();
		var modelLayout = doc.Document.Layouts[Layout.ModelLayoutName];

		// Model 레이아웃 삭제 시도 시 예외
		Assert.Throws<InvalidOperationException>(() => doc.RemoveLayout(modelLayout));

		// 템플릿에는 배치가 여러 개일 수 있으므로 하나만 남을 때까지 지운 뒤, 마지막 배치 삭제는 예외
		while (doc.Document.Layouts.Count(l => l.IsPaperSpace) > 1)
		{
			doc.RemoveLayout(doc.Document.Layouts.First(l => l.IsPaperSpace));
		}
		var onlyPaperLayout = doc.Document.Layouts.First(l => l.IsPaperSpace);
		Assert.Throws<InvalidOperationException>(() => doc.RemoveLayout(onlyPaperLayout));
	}

	[Fact]
	public void CurrentSpace_AddEntities_TargetsCurrentSpaceBlock()
	{
		var doc = EditorDocument.New();
		var layout = doc.AddLayout("배치A");

		// 현재 작업 공간을 배치 블록으로 변경
		doc.CurrentSpace = layout.AssociatedBlock;

		var lineInPaper = new Line(new XYZ(50, 50, 0), new XYZ(150, 50, 0));
		doc.AddEntities(lineInPaper);

		Assert.Contains(lineInPaper, layout.AssociatedBlock.Entities);
		Assert.DoesNotContain(lineInPaper, doc.Document.ModelSpace.Entities);

		// Undo로 제거 확인
		doc.Undo();
		Assert.DoesNotContain(lineInPaper, layout.AssociatedBlock.Entities);

		// Redo로 복원 확인
		doc.Redo();
		Assert.Contains(lineInPaper, layout.AssociatedBlock.Entities);
	}

	[Fact]
	public void LayoutCommand_New_Rename_Delete_SetCurrent_WorkCorrectly()
	{
		var doc = EditorDocument.New();

		// 1. LAYOUT N 명령으로 새 배치 생성
		var cmd = new LayoutCommand();
		var ctx = new CommandContext(doc);
		cmd.Start(ctx);
		cmd.OnText("N");
		cmd.OnText("상세도면");

		Assert.True(doc.Document.Layouts.ContainsKey("상세도면"));
		var layout = doc.Document.Layouts["상세도면"];

		// 2. LAYOUT S 명령으로 현재 공간 설정
		var cmdSet = new LayoutCommand();
		cmdSet.Start(ctx);
		cmdSet.OnText("S");
		cmdSet.OnText("상세도면");
		Assert.Equal(layout.AssociatedBlock, doc.CurrentSpace);

		// 3. LAYOUT R 명령으로 이름 변경
		var cmdRen = new LayoutCommand();
		cmdRen.Start(ctx);
		cmdRen.OnText("R");
		cmdRen.OnText("상세도면");
		cmdRen.OnText("수정상세도면");
		Assert.True(doc.Document.Layouts.ContainsKey("수정상세도면"));
		Assert.False(doc.Document.Layouts.ContainsKey("상세도면"));

		// 4. 모형으로 다시 공간 전환 후 삭제 테스트
		var cmdSetModel = new LayoutCommand();
		cmdSetModel.Start(ctx);
		cmdSetModel.OnText("S");
		cmdSetModel.OnText("Model");
		Assert.Equal(doc.Document.ModelSpace, doc.CurrentSpace);

		// 또 다른 배치 추가 후 삭제
		doc.AddLayout("추가배치");
		var cmdDel = new LayoutCommand();
		cmdDel.Start(ctx);
		cmdDel.OnText("D");
		cmdDel.OnText("수정상세도면");
		Assert.False(doc.Document.Layouts.ContainsKey("수정상세도면"));
	}
}
