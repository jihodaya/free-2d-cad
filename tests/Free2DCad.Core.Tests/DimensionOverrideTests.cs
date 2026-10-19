using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Styles;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>속성창 치수 재정의 (DSTYLE): 블록 다시 만들기, 되돌리기, DWG 저장·읽기.</summary>
public class DimensionOverrideTests
{
	private static (EditorDocument Doc, DimensionLinear Dim) Make()
	{
		var doc = EditorDocument.New();
		var dim = new DimensionLinear { FirstPoint = new XYZ(0, 0, 0), SecondPoint = new XYZ(100, 0, 0), DefinitionPoint = new XYZ(100, 20, 0) };
		doc.AddEntities(dim);
		dim.UpdateBlock();
		return (doc, dim);
	}

	private static IEnumerable<Entity> BlockEntities(Dimension dim) => dim.Block.Entities;

	[Fact]
	public void Colors_And_TextHeight_Apply_To_Block_And_Undo()
	{
		var (doc, dim) = Make();
		var dims = new[] { dim };
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.DimLineColor, (short)1, "치수선 색상"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextColor, (short)5, "텍스트 색상"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextHeight, 7.0, "텍스트 높이"));

		Assert.True(dim.HasStyleOverride);
		var st = dim.GetActiveDimensionStyle();
		Assert.Equal(1, st.DimensionLineColor.Index);
		Assert.Equal(5, st.TextColor.Index);
		Assert.Equal(7.0, st.TextHeight, 6);
		Assert.Contains(BlockEntities(dim).OfType<Line>(), l => l.Color.Index == 1);
		var text = BlockEntities(dim).OfType<MText>().Single();
		Assert.Equal(5, text.Color.Index);
		Assert.Equal(7.0, text.Height, 6);
		// 스타일 자체는 그대로
		Assert.NotEqual(1, dim.Style.DimensionLineColor.Index);

		doc.Undo();
		doc.Undo();
		doc.Undo();
		Assert.False(dim.HasStyleOverride);
		Assert.DoesNotContain(BlockEntities(dim).OfType<Line>(), l => l.Color.Index == 1);
	}

	[Fact]
	public void Same_As_Style_Removes_Override()
	{
		var (doc, dim) = Make();
		var dims = new[] { dim };
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextHeight, 9.0, "h"));
		Assert.True(DimensionOverrides.IsOverridden(dim, DimensionOverrides.TextHeight));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextHeight, dim.Style.TextHeight, "h"));
		Assert.False(DimensionOverrides.IsOverridden(dim, DimensionOverrides.TextHeight));
		Assert.False(dim.ExtendedData.TryGet(AppId.DefaultName, out _));
	}

	[Fact]
	public void Arrow_And_Suppress_DimLine()
	{
		var (doc, dim) = Make();
		var dims = new[] { dim };
		var tick = DimensionOverrides.GetOrCreateArrow(doc.Document, "_ArchTick")!;
		doc.Execute(DimensionOverrides.SetAction(dims, new Dictionary<int, object?>
		{
			[DimensionOverrides.SeparateArrows] = true,
			[DimensionOverrides.Arrow1] = tick.Handle,
			[DimensionOverrides.Arrow2] = 0UL,
		}, "화살촉 1"));
		var st = dim.GetActiveDimensionStyle();
		Assert.Same(tick, Dimension.FirstArrowBlock(st));
		Assert.Null(Dimension.SecondArrowBlock(st));
		Assert.Single(BlockEntities(dim).OfType<Insert>(), i => i.Block == tick);
		Assert.Single(BlockEntities(dim).OfType<Solid>());

		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.SuppressDimLine2, true, "치수선 2"));
		Assert.Empty(BlockEntities(dim).OfType<Solid>()); // 둘째 화살촉도 사라짐
	}

	[Fact]
	public void Aligned_Second_Arrow_At_Second_End()
	{
		var dim = new DimensionAligned(new XYZ(0, 0, 0), new XYZ(100, 0, 0)) { DefinitionPoint = new XYZ(100, 20, 0) };
		dim.UpdateBlock();
		var solids = dim.Block.Entities.OfType<Solid>().ToList();
		Assert.Equal(2, solids.Count);
		Assert.Contains(solids, s => Math.Abs(s.FirstCorner.X - 0) < 1e-6);
		Assert.Contains(solids, s => Math.Abs(s.FirstCorner.X - 100) < 1e-6);
	}

	[Fact]
	public void Overrides_RoundTrip_Dwg()
	{
		var (doc, dim) = Make();
		var dims = new[] { dim };
		var style = doc.Document.TextStyles.First();
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.DimLineColor, (short)1, "c"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextColor, (short)4, "c"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextHeight, 6.5, "h"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.TextInsideExt, true, "tix"));
		doc.Execute(DimensionOverrides.SetAction(dims, DimensionOverrides.Postfix, "<> mm", "post"));

		string dir = Path.Combine(Path.GetTempPath(), "Free2DCad_DimOv_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		try
		{
			string path = Path.Combine(dir, "ov.dwg");
			doc.Save(path);
			var re = DwgReader.Read(path);
			var d2 = re.Entities.OfType<DimensionLinear>().Single();
			Assert.True(d2.HasStyleOverride);
			var st = d2.GetActiveDimensionStyle();
			Assert.Equal(1, st.DimensionLineColor.Index);
			Assert.Equal(4, st.TextColor.Index);
			Assert.Equal(6.5, st.TextHeight, 6);
			Assert.True(st.TextInsideExtensions);
			Assert.Equal("<> mm", st.PostFix);
			Assert.Equal((short)1, Convert.ToInt16(DimensionOverrides.Effective(d2, DimensionOverrides.DimLineColor)));
		}
		finally
		{
			try { Directory.Delete(dir, true); } catch { }
		}
	}
}
