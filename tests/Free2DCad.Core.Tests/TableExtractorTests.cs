using ACadSharp;
using ACadSharp.IO;
using Free2DCad.Core.Tools;
using Free2DCad.Rendering;
using System.IO;
using Xunit;

namespace Free2DCad.Core.Tests;

public class TableExtractorTests
{
	[Fact]
	public void Extract_PinmapTable_From_Sample1_WorksCorrectly()
	{
		string samplePath = FindSamplePath("test-1.dwg");
		if (samplePath == null) return;
		CadDocument doc = DwgReader.Read(samplePath);
		var scene = SceneBuilder.Build(doc);

		// 첫 번째 시트의 핀맵 표 영역: (89, 37) ~ (163, 76)
		var region = new Bounds(88.5, 36.5, 163.5, 76.5);
		var result = TableExtractor.Extract(scene, region);
		if (result.Rows.Length != 9)
		{
			result = TableExtractor.Extract(scene, new Bounds(89, 37, 163, 76));
		}

		// 1. 행 수는 헤더 포함 9행
		Assert.Equal(9, result.Rows.Length);

		// 2. 첫 행에 "Part number", "Pin", "Color", "두께" 포함
		string headerRow = string.Join(" ", result.Rows[0]);
		Assert.Contains("Part number", headerRow, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Pin", headerRow, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Color", headerRow, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("두께", headerRow);

		// 3. "RED", "BLUE", "GREEN", "BROWN", "WHITE", "YELLOW", "GRAY", "SHIELD"가 순서대로 한 열에 있음
		int colorColIdx = -1;
		for (int c = 0; c < result.Rows[0].Length; c++)
		{
			if (result.Rows[0][c].Contains("Color", StringComparison.OrdinalIgnoreCase) ||
			    (result.Rows.Length > 1 && result.Rows[1][c].Contains("RED", StringComparison.OrdinalIgnoreCase)))
			{
				colorColIdx = c;
				break;
			}
		}

		Assert.True(colorColIdx >= 0, "Color 열을 찾을 수 없습니다.");

		string[] expectedColors = new[]
		{
			"RED", "BLUE", "GREEN", "BROWN", "WHITE", "YELLOW", "GRAY", "SHIELD"
		};

		for (int r = 0; r < expectedColors.Length; r++)
		{
			string cellValue = result.Rows[r + 1][colorColIdx];
			Assert.Contains(expectedColors[r], cellValue, StringComparison.OrdinalIgnoreCase);
		}

		// 4. ToCsv 결과에 한글이 그대로 들어 있음
		string csv = TableExtractor.ToCsv(result);
		Assert.Contains("두께", csv);
		Assert.Contains("RED", csv);
		Assert.Contains("SHIELD", csv);
		Assert.Contains("\r\n", csv);
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
