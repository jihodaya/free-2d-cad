using System.Text;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Tools;

/// <summary>
/// 표 추출 결과.
/// </summary>
public sealed class TableResult
{
	public string[][] Rows { get; }

	public TableResult(string[][] rows)
	{
		this.Rows = rows;
	}
}

/// <summary>
/// 도면 영역 내의 선과 텍스트를 분석하여 격자 표 데이터를 추출하는 도구.
/// </summary>
public static class TableExtractor
{
	private sealed record HorizSeg(double Y, double XMin, double XMax);
	private sealed record VertSeg(double X, double YMin, double YMax);

	public static TableResult Extract(Scene scene, Bounds region)
	{
		ArgumentNullException.ThrowIfNull(scene);

		double minX = Math.Min(region.MinX, region.MaxX);
		double maxX = Math.Max(region.MinX, region.MaxX);
		double minY = Math.Min(region.MinY, region.MaxY);
		double maxY = Math.Max(region.MinY, region.MaxY);
		var normRegion = new Bounds(minX, minY, maxX, maxY);

		// 1. 영역 안의 텍스트 수집 및 높이 중앙값 계산
		var textsInRegion = new List<SceneText>();
		foreach (var text in scene.Texts)
		{
			double tx = (text.Bounds.MinX + text.Bounds.MaxX) / 2.0;
			double ty = (text.Bounds.MinY + text.Bounds.MaxY) / 2.0;
			if (tx >= normRegion.MinX - 1.0 && tx <= normRegion.MaxX + 1.0 &&
			    ty >= normRegion.MinY - 1.0 && ty <= normRegion.MaxY + 1.0)
			{
				textsInRegion.Add(text);
			}
		}

		var heights = textsInRegion.Select(t => t.Height).OrderBy(h => h).ToList();
		double medianHeight = heights.Count > 0 ? heights[heights.Count / 2] : 2.5;
		double tol = medianHeight * 0.3;
		if (tol < 0.1) tol = 0.5;

		// 2. 영역 안의 폴리선 선분 중 수평선(|dy| < 폭의 0.5%)과 수직선 수집
		var horizSegs = new List<HorizSeg>();
		var vertSegs = new List<VertSeg>();

		foreach (var poly in scene.Polylines)
		{
			double[] pts = poly.Points;
			int count = pts.Length / 2;
			if (count < 2) continue;

			int segCount = poly.Closed ? count : count - 1;
			for (int i = 0; i < segCount; i++)
			{
				double x0 = pts[i * 2], y0 = pts[i * 2 + 1];
				int nextIdx = (i + 1) % count;
				double x1 = pts[nextIdx * 2], y1 = pts[nextIdx * 2 + 1];

				double sMinX = Math.Min(x0, x1), sMaxX = Math.Max(x0, x1);
				double sMinY = Math.Min(y0, y1), sMaxY = Math.Max(y0, y1);

				// 영역과 겹치는지 검사
				if (sMaxX < normRegion.MinX - tol || sMinX > normRegion.MaxX + tol ||
				    sMaxY < normRegion.MinY - tol || sMinY > normRegion.MaxY + tol)
				{
					continue;
				}

				double dx = Math.Abs(x1 - x0);
				double dy = Math.Abs(y1 - y0);
				double len = Math.Sqrt(dx * dx + dy * dy);
				if (len < 1e-4) continue;

				if (dy <= Math.Max(dx * 0.005, 0.02) && dx > dy)
				{
					// 수평선 (|dy| < 폭의 0.5%)
					double y = (y0 + y1) / 2.0;
					if (y >= normRegion.MinY - tol && y <= normRegion.MaxY + tol)
					{
						horizSegs.Add(new HorizSeg(y, Math.Max(normRegion.MinX, sMinX), Math.Min(normRegion.MaxX, sMaxX)));
					}
				}
				else if (dx <= Math.Max(dy * 0.005, 0.02) && dy > dx)
				{
					// 수직선
					double x = (x0 + x1) / 2.0;
					if (x >= normRegion.MinX - tol && x <= normRegion.MaxX + tol)
					{
						vertSegs.Add(new VertSeg(x, Math.Max(normRegion.MinY, sMinY), Math.Min(normRegion.MaxY, sMaxY)));
					}
				}
			}
		}

		// 3. 수평선의 y값, 수직선의 x값을 각각 허용 오차로 묶어 격자선 목록 작성
		var yLines = ClusterCoordinates(horizSegs.Select(s => s.Y), tol);
		var xLines = ClusterCoordinates(vertSegs.Select(s => s.X), tol);

		if (yLines.Count < 2 || xLines.Count < 2)
		{
			return new TableResult(Array.Empty<string[]>());
		}

		// Y는 위(큰 값)에서 아래(작은 값) 순서로 정렬 (행 번호 증가)
		yLines.Sort((a, b) => b.CompareTo(a));
		// X는 왼쪽(작은 값)에서 오른쪽(큰 값) 순서로 정렬 (열 번호 증가)
		xLines.Sort();

		int rowCount = yLines.Count - 1;
		int colCount = xLines.Count - 1;

		// 4. 셀별 텍스트 배정 (거리 최소화 기준)
		var cellTexts = new List<SceneText>[rowCount, colCount];
		for (int r = 0; r < rowCount; r++)
		{
			for (int c = 0; c < colCount; c++)
			{
				cellTexts[r, c] = new List<SceneText>();
			}
		}

		foreach (var text in textsInRegion)
		{
			double tx = (text.Bounds.MinX + text.Bounds.MaxX) / 2.0;
			double ty = (text.Bounds.MinY + text.Bounds.MaxY) / 2.0;

			int bestR = 0;
			double bestRDist = double.MaxValue;
			for (int r = 0; r < rowCount; r++)
			{
				double top = yLines[r];
				double bottom = yLines[r + 1];
				double dist = 0;
				if (ty > top) dist = ty - top;
				else if (ty < bottom) dist = bottom - ty;

				if (dist < bestRDist)
				{
					bestRDist = dist;
					bestR = r;
				}
			}

			int bestC = 0;
			double bestCDist = double.MaxValue;
			for (int c = 0; c < colCount; c++)
			{
				double left = xLines[c];
				double right = xLines[c + 1];
				double dist = 0;
				if (tx < left) dist = left - tx;
				else if (tx > right) dist = tx - right;

				if (dist < bestCDist)
				{
					bestCDist = dist;
					bestC = c;
				}
			}

			cellTexts[bestR, bestC].Add(text);
		}

		// 5. 선이 끊겨 있어 병합된 셀(수직선이 그 행 구간에 없는 경우) 판단
		var mergedWithPrev = new bool[rowCount, colCount];
		for (int r = 0; r < rowCount; r++)
		{
			double rTop = yLines[r];
			double rBottom = yLines[r + 1];
			double rMid = (rTop + rBottom) / 2.0;

			for (int c = 1; c < colCount; c++)
			{
				double boundX = xLines[c];
				bool hasVertLine = vertSegs.Any(v =>
					Math.Abs(v.X - boundX) <= tol &&
					v.YMin <= rMid + tol &&
					v.YMax >= rMid - tol);

				if (!hasVertLine)
				{
					mergedWithPrev[r, c] = true;
				}
			}
		}

		// 병합된 셀의 값은 병합 범위의 첫 셀에만 둔다
		for (int r = 0; r < rowCount; r++)
		{
			int c = 0;
			while (c < colCount)
			{
				int startCol = c;
				while (c + 1 < colCount && mergedWithPrev[r, c + 1])
				{
					c++;
				}
				int endCol = c;

				if (endCol > startCol)
				{
					for (int k = startCol + 1; k <= endCol; k++)
					{
						cellTexts[r, startCol].AddRange(cellTexts[r, k]);
						cellTexts[r, k].Clear();
					}
				}
				c++;
			}
		}

		// 6. 셀 내 텍스트 정렬(위→아래, 왼쪽→오른쪽) 및 공백 결합
		var rawGrid = new string[rowCount][];
		for (int r = 0; r < rowCount; r++)
		{
			rawGrid[r] = new string[colCount];
			for (int c = 0; c < colCount; c++)
			{
				var list = cellTexts[r, c];
				if (list.Count == 0)
				{
					rawGrid[r][c] = string.Empty;
				}
				else
				{
					var sorted = list.OrderByDescending(t => (t.Bounds.MinY + t.Bounds.MaxY) / 2.0)
						.ThenBy(t => (t.Bounds.MinX + t.Bounds.MaxX) / 2.0);

					var parts = new List<string>();
					foreach (var t in sorted)
					{
						string combined = string.Join(" ", t.Lines).Trim();
						if (!string.IsNullOrEmpty(combined))
						{
							parts.Add(combined);
						}
					}
					rawGrid[r][c] = string.Join(" ", parts);
				}
			}
		}

		// 7. 텍스트가 하나도 없는 행과 열 제거
		var activeRowIndices = new List<int>();
		for (int r = 0; r < rowCount; r++)
		{
			if (rawGrid[r].Any(val => !string.IsNullOrWhiteSpace(val)))
			{
				activeRowIndices.Add(r);
			}
		}

		var activeColIndices = new List<int>();
		for (int c = 0; c < colCount; c++)
		{
			bool colHasText = false;
			foreach (int r in activeRowIndices)
			{
				if (!string.IsNullOrWhiteSpace(rawGrid[r][c]))
				{
					colHasText = true;
					break;
				}
			}
			if (colHasText)
			{
				activeColIndices.Add(c);
			}
		}

		var finalRows = new string[activeRowIndices.Count][];
		for (int i = 0; i < activeRowIndices.Count; i++)
		{
			int r = activeRowIndices[i];
			finalRows[i] = new string[activeColIndices.Count];
			for (int j = 0; j < activeColIndices.Count; j++)
			{
				int c = activeColIndices[j];
				finalRows[i][j] = rawGrid[r][c];
			}
		}

		return new TableResult(finalRows);
	}

	/// <summary>
	/// RFC 4180 규격에 맞춰 표 결과를 CSV 문자열로 변환한다.
	/// </summary>
	public static string ToCsv(TableResult table)
	{
		ArgumentNullException.ThrowIfNull(table);

		var sb = new StringBuilder();
		for (int r = 0; r < table.Rows.Length; r++)
		{
			var row = table.Rows[r];
			for (int c = 0; c < row.Length; c++)
			{
				if (c > 0) sb.Append(',');
				string val = row[c] ?? string.Empty;
				if (val.Contains('"') || val.Contains(',') || val.Contains('\n') || val.Contains('\r'))
				{
					sb.Append('"').Append(val.Replace("\"", "\"\"")).Append('"');
				}
				else
				{
					sb.Append(val);
				}
			}
			sb.Append("\r\n");
		}
		return sb.ToString();
	}

	/// <summary>
	/// UTF-8 BOM 인코딩을 적용하여 파일로 저장한다.
	/// </summary>
	public static void SaveCsv(TableResult table, string filePath)
	{
		string csv = ToCsv(table);
		File.WriteAllText(filePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	}

	private static List<double> ClusterCoordinates(IEnumerable<double> coords, double tol)
	{
		var sorted = coords.OrderBy(c => c).ToList();
		var result = new List<double>();
		if (sorted.Count == 0) return result;

		var currentGroup = new List<double> { sorted[0] };
		for (int i = 1; i < sorted.Count; i++)
		{
			if (sorted[i] - currentGroup[0] <= tol)
			{
				currentGroup.Add(sorted[i]);
			}
			else
			{
				result.Add(currentGroup.Average());
				currentGroup.Clear();
				currentGroup.Add(sorted[i]);
			}
		}
		if (currentGroup.Count > 0)
		{
			result.Add(currentGroup.Average());
		}

		return result;
	}
}
