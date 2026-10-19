using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Core.Tables;

/// <summary>
/// 표(ACAD_TABLE) 만들기·고치기. 표는 블록 참조의 한 종류로, 보이는 모양(격자선 + 칸 문자)은 익명 블록(*T…)에 들어 있다.
/// 기존 CAD가 만든 표와 같은 구조로 만든다: 제목 행은 첫 칸이 모든 열을 합친 칸(나머지 칸은 '합쳐짐'),
/// 칸 여백 1.5, 선·문자 색은 블록별(ByBlock). 칸 문자만 바꿀 때는 그 칸의 문자만 고쳐 원래 배치를 지킨다.
/// </summary>
public static class TableEditor
{
	public const double DefaultMargin = 1.5;

	/// <summary>새 표 설정 (기존 CAD '테이블 삽입' 창과 같은 항목 + 문자 높이).</summary>
	public sealed record TableSpec(int Columns, double ColumnWidth, int DataRows, int RowLines, double TextHeight, bool TitleRow, bool HeaderRow)
	{
		public static TableSpec Default => new(5, 63.5, 1, 1, 4.5, true, true);
	}

	/// <summary>칸 종류별 문자 높이: 제목은 데이터의 4/3배 (기존 CAD Standard: 6 / 4.5).</summary>
	public static double TitleTextHeight(double textHeight) => textHeight * 4.0 / 3.0;

	/// <summary>행 높이: 문자 높이 × 4/3 × 줄 수 + 위아래 여백 (기존 CAD: 4.5 → 9).</summary>
	public static double RowHeightFor(double textHeight, int lines, double margin = DefaultMargin) =>
		textHeight * 4.0 / 3.0 * Math.Max(lines, 1) + 2 * margin;

	/// <summary>새 표를 만든다 (문서에 넣지는 않음 — 호출한 쪽이 추가 동작으로 넣는다). 익명 블록은 문서에 등록한다.</summary>
	public static TableEntity Create(CadDocument doc, XYZ insertPoint, TableSpec spec)
	{
		int cols = Math.Max(spec.Columns, 1);
		int rows = (spec.TitleRow ? 1 : 0) + (spec.HeaderRow ? 1 : 0) + Math.Max(spec.DataRows, 1);
		var block = new BlockRecord(NextAnonymousName(doc));
		block.IsAnonymous = true;
		doc.BlockRecords.Add(block);

		var table = new TableEntity(block)
		{
			InsertPoint = insertPoint,
			HorizontalDirection = XYZ.AxisX,
			ValueFlag = 0x16, // 블록 있음 + 아래로 펼침 (기존 CAD 값)
		};
		if (doc.TableStyles.TryGet("Standard", out var style))
		{
			table.Style = style;
		}
		for (int c = 0; c < cols; c++)
		{
			table.Columns.Add(new TableEntity.Column { Width = spec.ColumnWidth });
		}
		for (int r = 0; r < rows; r++)
		{
			bool title = spec.TitleRow && r == 0;
			double h = title ? TitleTextHeight(spec.TextHeight) * 1.524 : RowHeightFor(spec.TextHeight, spec.RowLines);
			var row = new TableEntity.Row { Height = h };
			for (int c = 0; c < cols; c++)
			{
				row.Cells.Add(NewCell(title && c == 0 ? cols : 1, title && c > 0));
			}
			table.Rows.Add(row);
		}
		Regenerate(doc, table, spec.TextHeight);
		return table;
	}

	private static TableEntity.Cell NewCell(int spanWidth, bool merged)
	{
		var cell = new TableEntity.Cell
		{
			Type = TableEntity.CellType.Text,
			BorderWidth = spanWidth,
			BorderHeight = 1,
			MergedValue = merged ? (short)1 : (short)0,
		};
		var content = new TableEntity.CellContent { ContentType = TableEntity.TableCellContentType.Value };
		content.CadValue.SetValue(string.Empty, CadValueType.String);
		cell.Contents.Add(content);
		return cell;
	}

	private static string NextAnonymousName(CadDocument doc)
	{
		int max = 0;
		foreach (var br in doc.BlockRecords)
		{
			if (br.Name.StartsWith("*T", StringComparison.OrdinalIgnoreCase) && int.TryParse(br.Name[2..], out int n))
			{
				max = Math.Max(max, n);
			}
		}
		return $"*T{max + 1}";
	}

	// ===== 칸 조회 =====

	public static string GetCellText(TableEntity table, int row, int col)
	{
		if (row < 0 || row >= table.Rows.Count || col < 0 || col >= table.Columns.Count || col >= table.Rows[row].Cells.Count)
		{
			return string.Empty;
		}
		return table.Rows[row].Cells[col].Content?.CadValue?.Value?.ToString() ?? string.Empty;
	}

	/// <summary>칸이 다른 칸에 합쳐져 가려졌는지.</summary>
	public static bool IsCovered(TableEntity table, int row, int col) =>
		col < table.Rows[row].Cells.Count && table.Rows[row].Cells[col].MergedValue != 0;

	/// <summary>칸의 표 안 좌표 범위 (표 삽입점 기준, 오른쪽 +X, 아래쪽 −Y). 합친 칸은 합친 범위.</summary>
	public static (double X0, double Y0, double X1, double Y1) CellRect(TableEntity table, int row, int col)
	{
		double x0 = 0;
		for (int c = 0; c < col; c++) x0 += table.Columns[c].Width;
		double y0 = 0;
		for (int r = 0; r < row; r++) y0 -= table.Rows[r].Height;
		var cell = table.Rows[row].Cells[col];
		int sw = Math.Clamp(cell.BorderWidth <= 0 ? 1 : cell.BorderWidth, 1, table.Columns.Count - col);
		int sh = Math.Clamp(cell.BorderHeight <= 0 ? 1 : cell.BorderHeight, 1, table.Rows.Count - row);
		double x1 = x0;
		for (int c = col; c < col + sw; c++) x1 += table.Columns[c].Width;
		double y1 = y0;
		for (int r = row; r < row + sh; r++) y1 -= table.Rows[r].Height;
		return (x0, y0, x1, y1);
	}

	/// <summary>도면 좌표 → 표 안 좌표 (삽입점·회전·축척 반영).</summary>
	public static (double X, double Y) ToTableLocal(TableEntity table, double wx, double wy)
	{
		double dx = wx - table.InsertPoint.X, dy = wy - table.InsertPoint.Y;
		double rot = table.Rotation;
		double cos = Math.Cos(-rot), sin = Math.Sin(-rot);
		double lx = dx * cos - dy * sin, ly = dx * sin + dy * cos;
		double sx = table.XScale == 0 ? 1 : table.XScale, sy = table.YScale == 0 ? 1 : table.YScale;
		return (lx / sx, ly / sy);
	}

	/// <summary>도면 점이 들어 있는 칸 (합친 칸은 그 주인 칸). 표 밖이면 null.</summary>
	public static (int Row, int Col)? HitCell(TableEntity table, double wx, double wy)
	{
		var (lx, ly) = ToTableLocal(table, wx, wy);
		for (int r = 0; r < table.Rows.Count; r++)
		{
			for (int c = 0; c < table.Columns.Count; c++)
			{
				if (c >= table.Rows[r].Cells.Count || IsCovered(table, r, c))
				{
					continue;
				}
				var (x0, y0, x1, y1) = CellRect(table, r, c);
				if (lx >= x0 && lx <= x1 && ly <= y0 && ly >= y1)
				{
					return (r, c);
				}
			}
		}
		return null;
	}

	/// <summary>다음(또는 이전) 편집 칸 — 가려진 칸은 건너뜀. 끝이면 null.</summary>
	public static (int Row, int Col)? NextCell(TableEntity table, int row, int col, bool backward)
	{
		int n = table.Rows.Count * table.Columns.Count;
		int i = row * table.Columns.Count + col;
		for (int step = 0; step < n; step++)
		{
			i += backward ? -1 : 1;
			if (i < 0 || i >= n) return null;
			int r = i / table.Columns.Count, c = i % table.Columns.Count;
			if (!IsCovered(table, r, c)) return (r, c);
		}
		return null;
	}

	// ===== 블록(보이는 모양) =====

	/// <summary>칸 문자의 높이: 블록에 이미 있는 그 칸 문자 높이 → 없으면 행 종류(제목/그 외)로.</summary>
	private static double TextHeightOf(TableEntity table, int row, double dataHeight)
	{
		bool title = row == 0 && table.Rows[0].Cells.Count > 0 && table.Rows[0].Cells[0].BorderWidth >= table.Columns.Count && table.Columns.Count > 1;
		return title ? TitleTextHeight(dataHeight) : dataHeight;
	}

	/// <summary>표에서 데이터 문자 높이를 추정 (블록의 제목이 아닌 문자 중 가장 흔한 높이, 없으면 4.5).</summary>
	public static double GuessTextHeight(TableEntity table)
	{
		var heights = table.Block?.Entities.OfType<MText>().Select(m => Math.Round(m.Height, 3)).ToList() ?? new List<double>();
		if (heights.Count == 0) return 4.5;
		var min = heights.Min();
		return min > 0 ? min : 4.5;
	}

	/// <summary>
	/// 블록을 표 구조(행·열·칸 문자)로 다시 만든다: 칸 테두리(합친 칸 안쪽 선은 없음) + 칸마다 가운데 맞춤 여러 줄 문자.
	/// </summary>
	public static void Regenerate(CadDocument doc, TableEntity table, double? dataTextHeight = null)
	{
		var block = table.Block;
		if (block == null)
		{
			return;
		}
		double th = dataTextHeight ?? GuessTextHeight(table);
		var textStyle = doc.TextStyles.TryGetValue("Standard", out var ts) ? ts : null;
		foreach (var e in block.Entities.ToList())
		{
			block.Entities.Remove(e);
		}

		// 칸 테두리 선분 모으기 → 같은 줄의 이어진 선분은 하나로
		var horizontal = new Dictionary<double, List<(double A, double B)>>();
		var vertical = new Dictionary<double, List<(double A, double B)>>();
		void AddH(double y, double a, double b) => (horizontal.TryGetValue(Math.Round(y, 6), out var l) ? l : horizontal[Math.Round(y, 6)] = new()).Add((Math.Min(a, b), Math.Max(a, b)));
		void AddV(double x, double a, double b) => (vertical.TryGetValue(Math.Round(x, 6), out var l) ? l : vertical[Math.Round(x, 6)] = new()).Add((Math.Min(a, b), Math.Max(a, b)));

		var texts = new List<MText>();
		for (int r = 0; r < table.Rows.Count; r++)
		{
			for (int c = 0; c < table.Columns.Count; c++)
			{
				if (c >= table.Rows[r].Cells.Count || IsCovered(table, r, c))
				{
					continue;
				}
				var (x0, y0, x1, y1) = CellRect(table, r, c);
				AddH(y0, x0, x1);
				AddH(y1, x0, x1);
				AddV(x0, y1, y0);
				AddV(x1, y1, y0);
				string text = GetCellText(table, r, c);
				if (!string.IsNullOrEmpty(text))
				{
					texts.Add(CellText(text, x0, y0, x1, y1, TextHeightOf(table, r, th), textStyle));
				}
			}
		}
		foreach (var m in texts)
		{
			block.Entities.Add(m);
		}
		foreach (var (y, segs) in horizontal)
		{
			foreach (var (a, b) in MergeSegments(segs))
			{
				block.Entities.Add(ByBlock(new Line(new XYZ(a, y, 0), new XYZ(b, y, 0))));
			}
		}
		foreach (var (x, segs) in vertical)
		{
			foreach (var (a, b) in MergeSegments(segs))
			{
				block.Entities.Add(ByBlock(new Line(new XYZ(x, b, 0), new XYZ(x, a, 0))));
			}
		}
	}

	private static Line ByBlock(Line l)
	{
		l.Color = Color.ByBlock;
		return l;
	}

	private static MText CellText(string text, double x0, double y0, double x1, double y1, double height, TextStyle? style)
	{
		var m = new MText
		{
			Value = ACadSharpEscape(text),
			Height = height,
			InsertPoint = new XYZ((x0 + x1) / 2, (y0 + y1) / 2, 0),
			AttachmentPoint = AttachmentPointType.MiddleCenter,
			RectangleWidth = Math.Max(Math.Abs(x1 - x0) - 2 * DefaultMargin, height),
			Color = Color.ByBlock,
		};
		if (style != null)
		{
			m.Style = style;
		}
		return m;
	}

	/// <summary>칸 문자(보이는 글) → MTEXT 값 (줄 바꿈 = \P, 역슬래시·중괄호 이스케이프).</summary>
	private static string ACadSharpEscape(string text) =>
		text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\n").Replace("\n", "\\P");

	private static IEnumerable<(double A, double B)> MergeSegments(List<(double A, double B)> segs)
	{
		var sorted = segs.OrderBy(s => s.A).ToList();
		double a = sorted[0].A, b = sorted[0].B;
		for (int i = 1; i < sorted.Count; i++)
		{
			if (sorted[i].A <= b + 1e-9)
			{
				b = Math.Max(b, sorted[i].B);
			}
			else
			{
				yield return (a, b);
				(a, b) = sorted[i];
			}
		}
		yield return (a, b);
	}

	/// <summary>
	/// 칸 문자만 바꾼다: 칸 값 + 블록에서 그 칸 자리에 있는 문자(없으면 새로). 다른 칸의 배치·모양은 그대로 둔다.
	/// </summary>
	public static void SetCellText(CadDocument doc, TableEntity table, int row, int col, string text)
	{
		var cell = table.Rows[row].Cells[col];
		if (cell.Contents.Count == 0)
		{
			cell.Contents.Add(new TableEntity.CellContent { ContentType = TableEntity.TableCellContentType.Value });
		}
		cell.Content.CadValue.SetValue(text, CadValueType.String);
		var block = table.Block;
		if (block == null)
		{
			return;
		}
		var (x0, y0, x1, y1) = CellRect(table, row, col);
		var existing = block.Entities.OfType<MText>().FirstOrDefault(m =>
			m.InsertPoint.X > x0 && m.InsertPoint.X < x1 && m.InsertPoint.Y < y0 && m.InsertPoint.Y > y1);
		if (string.IsNullOrEmpty(text))
		{
			if (existing != null) block.Entities.Remove(existing);
			return;
		}
		if (existing != null)
		{
			existing.Value = ACadSharpEscape(text);
			return;
		}
		var style = doc.TextStyles.TryGetValue("Standard", out var ts) ? ts : null;
		block.Entities.Add(CellText(text, x0, y0, x1, y1, TextHeightOf(table, row, GuessTextHeight(table)), style));
	}

	/// <summary>칸 문자를 블록에서 찾는다 (바로 편집 커서 위치용). 없으면 null.</summary>
	public static MText? CellMText(TableEntity table, int row, int col)
	{
		var (x0, y0, x1, y1) = CellRect(table, row, col);
		return table.Block?.Entities.OfType<MText>().FirstOrDefault(m =>
			m.InsertPoint.X > x0 && m.InsertPoint.X < x1 && m.InsertPoint.Y < y0 && m.InsertPoint.Y > y1);
	}

	// ===== 행·열 추가/삭제 =====

	public static void InsertRow(CadDocument doc, TableEntity table, int index)
	{
		index = Math.Clamp(index, 0, table.Rows.Count);
		double th = GuessTextHeight(table);
		var template = table.Rows.Count > 0 ? table.Rows[Math.Min(Math.Max(index - 1, 0), table.Rows.Count - 1)] : null;
		bool templateIsTitle = template != null && table.Rows.IndexOf(template) == 0 && template.Cells.Count > 0 && template.Cells[0].BorderWidth >= table.Columns.Count && table.Columns.Count > 1;
		var row = new TableEntity.Row { Height = template != null && !templateIsTitle ? template.Height : RowHeightFor(th, 1) };
		for (int c = 0; c < table.Columns.Count; c++)
		{
			row.Cells.Add(NewCell(1, false));
		}
		table.Rows.Insert(index, row);
		Regenerate(doc, table, th);
	}

	public static void DeleteRow(CadDocument doc, TableEntity table, int index)
	{
		if (table.Rows.Count <= 1 || index < 0 || index >= table.Rows.Count) return;
		double th = GuessTextHeight(table);
		table.Rows.RemoveAt(index);
		Regenerate(doc, table, th);
	}

	public static void InsertColumn(CadDocument doc, TableEntity table, int index)
	{
		index = Math.Clamp(index, 0, table.Columns.Count);
		double th = GuessTextHeight(table);
		double w = table.Columns.Count > 0 ? table.Columns[Math.Min(Math.Max(index - 1, 0), table.Columns.Count - 1)].Width : 63.5;
		table.Columns.Insert(index, new TableEntity.Column { Width = w });
		for (int r = 0; r < table.Rows.Count; r++)
		{
			var cells = table.Rows[r].Cells;
			bool titleSpan = r == 0 && cells.Count > 0 && cells[0].BorderWidth >= table.Columns.Count - 1 && table.Columns.Count - 1 > 1;
			cells.Insert(Math.Min(index, cells.Count), NewCell(1, titleSpan && index > 0));
			if (titleSpan)
			{
				cells[0].BorderWidth = table.Columns.Count; // 제목 칸은 새 열까지 넓힌다
			}
		}
		Regenerate(doc, table, th);
	}

	public static void DeleteColumn(CadDocument doc, TableEntity table, int index)
	{
		if (table.Columns.Count <= 1 || index < 0 || index >= table.Columns.Count) return;
		double th = GuessTextHeight(table);
		table.Columns.RemoveAt(index);
		for (int r = 0; r < table.Rows.Count; r++)
		{
			var cells = table.Rows[r].Cells;
			if (index < cells.Count)
			{
				var removed = cells[index];
				cells.RemoveAt(index);
				// 제목(합친) 칸의 주인이 지워지면 다음 칸이 주인
				if (removed.MergedValue == 0 && removed.BorderWidth > 1 && index < cells.Count)
				{
					cells[index].MergedValue = 0;
					cells[index].BorderWidth = removed.BorderWidth - 1;
					cells[index].Contents.Clear();
					cells[index].Contents.AddRange(removed.Contents);
				}
			}
			if (cells.Count > 0 && cells[0].BorderWidth > table.Columns.Count)
			{
				cells[0].BorderWidth = table.Columns.Count;
			}
		}
		Regenerate(doc, table, th);
	}

	/// <summary>모든 열 너비 / 모든 행 높이 바꾸기 (제목 행은 행 높이 바꾸기에서 제외).</summary>
	public static void SetUniformColumnWidth(CadDocument doc, TableEntity table, double width)
	{
		if (width <= 0) return;
		double th = GuessTextHeight(table);
		foreach (var c in table.Columns) c.Width = width;
		Regenerate(doc, table, th);
	}

	public static void SetUniformRowHeight(CadDocument doc, TableEntity table, double height)
	{
		if (height <= 0) return;
		double th = GuessTextHeight(table);
		bool hasTitle = table.Rows.Count > 0 && table.Rows[0].Cells.Count > 0 && table.Rows[0].Cells[0].BorderWidth >= table.Columns.Count && table.Columns.Count > 1;
		for (int r = hasTitle ? 1 : 0; r < table.Rows.Count; r++) table.Rows[r].Height = height;
		Regenerate(doc, table, th);
	}

	// ===== 되돌리기용 상태 =====

	/// <summary>표의 구조·칸 문자·블록 모양 전체를 저장해 두었다가 되돌린다.</summary>
	public sealed class Snapshot
	{
		private readonly List<double> _widths;
		private readonly List<double> _heights;
		private readonly List<List<(string Text, int Bw, int Bh, short Merged)>> _cells;
		private readonly List<Entity> _blockEntities;

		public Snapshot(TableEntity table)
		{
			this._widths = table.Columns.Select(c => c.Width).ToList();
			this._heights = table.Rows.Select(r => r.Height).ToList();
			this._cells = table.Rows.Select(r => r.Cells.Select(c => (c.Content?.CadValue?.Value?.ToString() ?? string.Empty, c.BorderWidth, c.BorderHeight, c.MergedValue)).ToList()).ToList();
			this._blockEntities = table.Block?.Entities.Select(e => (Entity)e.Clone()).ToList() ?? new List<Entity>();
		}

		public void Restore(TableEntity table)
		{
			table.Columns.Clear();
			foreach (double w in this._widths) table.Columns.Add(new TableEntity.Column { Width = w });
			table.Rows.Clear();
			for (int r = 0; r < this._heights.Count; r++)
			{
				var row = new TableEntity.Row { Height = this._heights[r] };
				foreach (var (text, bw, bh, merged) in this._cells[r])
				{
					var cell = NewCell(bw, merged != 0);
					cell.BorderHeight = bh;
					cell.Content.CadValue.SetValue(text, CadValueType.String);
					row.Cells.Add(cell);
				}
				table.Rows.Add(row);
			}
			if (table.Block != null)
			{
				foreach (var e in table.Block.Entities.ToList()) table.Block.Entities.Remove(e);
				foreach (var e in this._blockEntities) table.Block.Entities.Add((Entity)e.Clone());
			}
		}
	}

	/// <summary>칸 문자를 CSV로 (합쳐 가려진 칸은 빈칸). 쉼표·따옴표·줄 바꿈이 있으면 따옴표로 감싼다.</summary>
	public static string ToCsv(TableEntity table)
	{
		var sb = new System.Text.StringBuilder();
		for (int r = 0; r < table.Rows.Count; r++)
		{
			var fields = new List<string>();
			for (int c = 0; c < table.Columns.Count; c++)
			{
				string t = IsCovered(table, r, c) ? string.Empty : GetCellText(table, r, c);
				fields.Add(t.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t);
			}
			sb.AppendLine(string.Join(",", fields));
		}
		return sb.ToString();
	}
}
