using System;
using System.IO;
using ACadSharp;
using ACadSharp.IO;
using Free2DCad.Rendering;
using SkiaSharp;
using Xunit;

namespace Free2DCad.Core.Tests;

/// <summary>
/// 작업 13: 플롯(인쇄) 렌더러 검증 단위 테스트.
/// </summary>
public sealed class PlotTests
{
	[Fact]
	public void ExportPdf_Test1_FirstSheet_CreatesValidPdfUnder2MB()
	{
		string samplePath = FindSamplePath("test-1.dwg");
		if (samplePath == null) return;
		CadDocument doc = DwgReader.Read(samplePath);
		var scene = SceneBuilder.Build(doc);

		// test-1 첫 시트 영역 (8,8)~(221,160) A3 가로(420x297 mm)
		var area = new Bounds(8, 8, 221, 160);
		var options = new PlotOptions(
			Area: area,
			PaperWidthMm: 420.0,
			PaperHeightMm: 297.0,
			MarginMm: 5,
			FitToPaper: true,
			Center: true,
			Colors: PlotColorMode.Color,
			LineWidthMm: 0.18);

		string tempPdfPath = Path.Combine(Path.GetTempPath(), $"plot-test1-{Guid.NewGuid():N}.pdf");
		try
		{
			PlotRenderer.ExportPdf(tempPdfPath, options, scene, null);

			// 1. 파일이 정상적으로 생성되었는지 확인
			Assert.True(File.Exists(tempPdfPath), "PDF 파일이 생성되지 않았습니다.");

			// 2. 파일 크기가 0보다 크고 2 MB 미만인지 확인
			var fileInfo = new FileInfo(tempPdfPath);
			Assert.True(fileInfo.Length > 0, "PDF 파일 내용이 비어 있습니다.");
			Assert.True(fileInfo.Length < 2 * 1024 * 1024, $"PDF 파일 크기({fileInfo.Length / 1024.0:F1} KB)가 2 MB를 초과합니다.");
		}
		finally
		{
			if (File.Exists(tempPdfPath))
			{
				try
				{
					File.Delete(tempPdfPath);
				}
				catch { }
			}
		}
	}

	[Fact]
	public void RenderBitmap_Monochrome_HasOnlyGrayscalePixels()
	{
		string samplePath = FindSamplePath("test-1.dwg");
		if (samplePath == null) return;
		CadDocument doc = DwgReader.Read(samplePath);
		var scene = SceneBuilder.Build(doc);

		// 흑백 모드 20 dpi 비트맵
		var area = new Bounds(8, 8, 221, 160);
		var options = new PlotOptions(
			Area: area,
			PaperWidthMm: 420.0,
			PaperHeightMm: 297.0,
			MarginMm: 5,
			FitToPaper: true,
			Center: true,
			Colors: PlotColorMode.Monochrome,
			LineWidthMm: 0.18);

		using var bmp = PlotRenderer.RenderBitmap(options, 20, scene, null);

		Assert.NotNull(bmp);
		Assert.True(bmp.Width > 0 && bmp.Height > 0);

		// 모든 픽셀에서 R == G == B 확인 (흑백 및 안티앨리어싱 회색만 허용, 유색 픽셀 불허)
		for (int y = 0; y < bmp.Height; y++)
		{
			for (int x = 0; x < bmp.Width; x++)
			{
				SKColor pixel = bmp.GetPixel(x, y);
				Assert.True(
					pixel.Red == pixel.Green && pixel.Green == pixel.Blue,
					$"흑백 모드에서 유색 픽셀 발견 ({x}, {y}): R={pixel.Red}, G={pixel.Green}, B={pixel.Blue}");
			}
		}
	}

	[Fact]
	public void EffectiveMmPerUnit_CalculatesCorrectScale()
	{
		// 100 x 50 영역, 여백 5mm, 가로 420 x 297 A3 용지 (인쇄 가능 폭 410, 높이 287)
		var area = new Bounds(0, 0, 100, 50);
		var fitOptions = new PlotOptions(
			Area: area,
			PaperWidthMm: 420.0,
			PaperHeightMm: 297.0,
			MarginMm: 5,
			FitToPaper: true);

		double eff = PlotRenderer.EffectiveMmPerUnit(fitOptions);
		// 폭 비율: 410 / 100 = 4.1, 높이 비율: 287 / 50 = 5.74 -> 더 작은 4.1 선택
		Assert.Equal(4.1, eff, 4);

		var customOptions = new PlotOptions(
			Area: area,
			PaperWidthMm: 420.0,
			PaperHeightMm: 297.0,
			FitToPaper: false,
			MmPerUnit: 2.5);

		Assert.Equal(2.5, PlotRenderer.EffectiveMmPerUnit(customOptions), 4);
	}

	private static string FindSamplePath(string fileName)
	{
		string? current = AppContext.BaseDirectory;
		while (current != null)
		{
			string candidate = Path.Combine(current, "sample", fileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
			candidate = Path.Combine(current, "..", "..", "..", "..", "sample", fileName);
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
			current = Path.GetDirectoryName(current);
		}
		return null!; // 샘플 도면(sample/)은 공개 저장소에 없음 → 해당 테스트 건너뜀
	}
}
