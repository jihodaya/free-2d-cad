using ACadSharp.Entities;
using ACadSharp.Objects;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>새 도면 템플릿 = A3 (기존 CAD 새 도면과 같음: LIMMIN 0,0 / LIMMAX 420,297).</summary>
public class TemplateA3Tests
{
	[Fact]
	public void NewDocument_IsA3()
	{
		var d = EditorDocument.New().Document;
		Assert.Equal(0, d.Header.ModelSpaceLimitsMin.X, 6);
		Assert.Equal(0, d.Header.ModelSpaceLimitsMin.Y, 6);
		Assert.Equal(420, d.Header.ModelSpaceLimitsMax.X, 6);
		Assert.Equal(297, d.Header.ModelSpaceLimitsMax.Y, 6);
		foreach (var layout in d.Layouts.Where(l => !l.Name.Equals(Layout.ModelLayoutName, StringComparison.OrdinalIgnoreCase)))
		{
			Assert.Equal("ISO_A3_(420.00_x_297.00_MM)", layout.PaperSize);
			Assert.Equal(PlotPaperUnits.Millimeters, layout.PaperUnits);
			// 배치 뷰포트가 모형의 A3 영역을 보여 줌 (대상점 0 — 원본의 대상점이 남으면 기존 CAD에서 빈 뷰포트)
			foreach (var vp in layout.AssociatedBlock.Entities.OfType<Viewport>())
			{
				Assert.Equal(210, vp.ViewCenter.X, 6);
				Assert.Equal(148.5, vp.ViewCenter.Y, 6);
				Assert.Equal(0, vp.ViewTarget.X, 6);
			}
		}
	}
}
