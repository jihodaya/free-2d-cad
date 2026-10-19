using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Tables;

namespace Free2DCad.Core.Commands;

/// <summary>
/// TABLE 명령: 표 설정(테이블 삽입 창에서 정함)으로 표를 만든다. 삽입 위치 지정 = 왼쪽 위 한 점,
/// 창 지정 = 두 구석으로 폭(열 너비)과 높이(데이터 행 수)를 정한다. 만들면 Created에 담고 끝낸다 (앱이 제목 칸 편집을 시작).
/// </summary>
public sealed class TableCommand : EditorCommand
{
	private (double X, double Y)? _first;

	public override string Name => "TABLE";

	public override string[] Aliases => new[] { "TB" };

	/// <summary>표 설정 (없으면 기본: 5열 × 63.5, 데이터 1행).</summary>
	public TableEditor.TableSpec Spec { get; set; } = TableEditor.TableSpec.Default;

	/// <summary>true면 두 구석(창)으로 크기를 정한다.</summary>
	public bool ByWindow { get; set; }

	/// <summary>만든 표 (끝난 뒤 앱이 칸 편집을 시작하는 데 씀).</summary>
	public TableEntity? Created { get; private set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._first = null;
		this.Prompt = this.ByWindow ? "첫 번째 구석 지정:" : "삽입 위치 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null)
		{
			return;
		}
		if (!this.ByWindow)
		{
			this.Commit(new XYZ(x, y, 0), this.Spec);
			return;
		}
		if (this._first == null)
		{
			this._first = (x, y);
			this.Ctx.LastPoint = (x, y);
			this.Prompt = "다른 구석 지정:";
			return;
		}
		this.Commit(new XYZ(Math.Min(this._first.Value.X, x), Math.Max(this._first.Value.Y, y), 0), this.FitToWindow(this._first.Value, (x, y)));
	}

	public override void OnText(string text)
	{
		if (CoordinateParser.TryParse(text.Trim(), this.Ctx?.LastPoint, null, false, out var pt))
		{
			this.OnPoint(pt.X, pt.Y);
		}
	}

	/// <summary>창 크기에 맞춘 설정: 열 너비 = 폭 / 열 수, 데이터 행 수 = (높이 − 제목·헤더) / 행 높이.</summary>
	private TableEditor.TableSpec FitToWindow((double X, double Y) a, (double X, double Y) b)
	{
		var s = this.Spec;
		double w = Math.Abs(b.X - a.X), h = Math.Abs(b.Y - a.Y);
		double rowH = TableEditor.RowHeightFor(s.TextHeight, s.RowLines);
		double fixedH = (s.TitleRow ? TableEditor.TitleTextHeight(s.TextHeight) * 1.524 : 0) + (s.HeaderRow ? rowH : 0);
		int dataRows = Math.Max(1, (int)Math.Floor((h - fixedH) / rowH));
		double colW = w > 1e-6 ? w / Math.Max(s.Columns, 1) : s.ColumnWidth;
		return s with { ColumnWidth = colW, DataRows = dataRows };
	}

	private void Commit(XYZ topLeft, TableEditor.TableSpec spec)
	{
		var doc = this.Ctx!.Doc;
		var table = TableEditor.Create(doc.Document, topLeft, spec);
		doc.AddEntities(table);
		this.Created = table;
		this.Ctx.Print($"표를 만들었습니다: {table.Columns.Count}열 × {table.Rows.Count}행");
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		// 표 격자 미리보기 (선만)
		var s = this.Spec;
		double x0, y0;
		TableEditor.TableSpec spec;
		if (this.ByWindow)
		{
			if (this._first == null)
			{
				yield break;
			}
			spec = this.FitToWindow(this._first.Value, (cursorX, cursorY));
			x0 = Math.Min(this._first.Value.X, cursorX);
			y0 = Math.Max(this._first.Value.Y, cursorY);
		}
		else
		{
			spec = s;
			x0 = cursorX;
			y0 = cursorY;
		}
		double width = spec.ColumnWidth * spec.Columns;
		var heights = new List<double>();
		if (spec.TitleRow) heights.Add(TableEditor.TitleTextHeight(spec.TextHeight) * 1.524);
		double rowH = TableEditor.RowHeightFor(spec.TextHeight, spec.RowLines);
		if (spec.HeaderRow) heights.Add(rowH);
		for (int i = 0; i < Math.Max(spec.DataRows, 1); i++) heights.Add(rowH);
		double y = y0;
		yield return new Line(new XYZ(x0, y, 0), new XYZ(x0 + width, y, 0));
		for (int r = 0; r < heights.Count; r++)
		{
			y -= heights[r];
			yield return new Line(new XYZ(x0, y, 0), new XYZ(x0 + width, y, 0));
		}
		double top = spec.TitleRow ? y0 - heights[0] : y0;
		for (int c = 0; c <= spec.Columns; c++)
		{
			double x = x0 + c * spec.ColumnWidth;
			bool edge = c == 0 || c == spec.Columns;
			yield return new Line(new XYZ(x, edge ? y0 : top, 0), new XYZ(x, y, 0));
		}
	}
}

/// <summary>TABLEEXPORT 명령: 표를 골라 CSV로 (파일 저장은 앱이 한다 — Csv에 결과).</summary>
public sealed class TableExportCommand : EditorCommand
{
	public override string Name => "TABLEEXPORT";

	/// <summary>CSV 결과 (끝난 뒤 앱이 저장 창을 띄운다).</summary>
	public string? Csv { get; private set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var picked = ctx.Selection.OfType<TableEntity>().FirstOrDefault();
		if (picked != null)
		{
			this.Csv = TableEditor.ToCsv(picked);
			this.Finish();
			return;
		}
		this.Prompt = "표 선택:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var doc = this.Ctx.Doc.Document;
		// 표를 클릭: 표 안의 점이면 그 표
		var table = doc.Entities.OfType<TableEntity>().FirstOrDefault(t => TableEditor.HitCell(t, x, y) != null);
		if (table != null)
		{
			this.OnSelection(new[] { table });
		}
		else
		{
			this.Ctx.Print("표가 아닙니다. 표 안을 클릭하십시오.");
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		var table = entities.OfType<TableEntity>().FirstOrDefault();
		if (table == null)
		{
			this.Ctx?.Print("표가 아닙니다. 표를 선택하십시오.");
			return;
		}
		this.Csv = TableEditor.ToCsv(table);
		this.Finish();
	}
}
