using ACadSharp.Tables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Free2DCad.App;

/// <summary>
/// 선종류 견본 (기존 CAD 속성창·리본처럼 이름 앞에 실제 선 모양). 패턴이 없으면 실선.
/// 패턴은 견본 폭에 2주기쯤 들어가게 줄이고, 점(0)은 짧은 점, 문자·모양 요소 자리는 작은 표시로 그린다.
/// </summary>
public sealed class LinetypeSwatch : Control
{
	private readonly double[]? _pattern;
	private readonly bool[]? _marks;
	private readonly IBrush _brush;

	public LinetypeSwatch(LineType? lt, IBrush brush, double width = 48, double height = 12)
		: this(PatternOf(lt), MarksOf(lt), brush, width, height)
	{
	}

	public LinetypeSwatch(double[]? pattern, bool[]? marks, IBrush brush, double width = 48, double height = 12)
	{
		this._pattern = pattern != null && pattern.Length > 0 && pattern.Sum(Math.Abs) > 0 ? pattern : null;
		this._marks = marks;
		this._brush = brush;
		this.Width = width;
		this.Height = height;
		this.VerticalAlignment = VerticalAlignment.Center;
	}

	/// <summary>선종류의 구간 길이 (실선·ByLayer·ByBlock이면 null).</summary>
	public static double[]? PatternOf(LineType? lt)
	{
		if (lt == null || lt.Segments.Count() < 1)
		{
			return null;
		}
		return lt.Segments.Select(s => s.Length).ToArray();
	}

	private static bool[]? MarksOf(LineType? lt) =>
		lt == null ? null : lt.Segments.Select(s => s.IsShape || s.IsText).ToArray();

	public override void Render(DrawingContext dc)
	{
		base.Render(dc);
		double w = this.Bounds.Width;
		double cy = Math.Round(this.Bounds.Height / 2) + 0.5;
		var pen = new Pen(this._brush, 1);
		if (this._pattern == null)
		{
			dc.DrawLine(pen, new Point(0, cy), new Point(w, cy));
			return;
		}
		double period = this._pattern.Sum(Math.Abs);
		double scale = w / (period * 2);
		// 너무 촘촘하거나 성기지 않게 (구간 최소 1.5px)
		double minSeg = this._pattern.Where(v => v != 0).Select(Math.Abs).DefaultIfEmpty(period).Min();
		scale = Math.Max(scale, 1.5 / Math.Max(minSeg, 1e-9));
		if (period * scale > w)
		{
			scale = w / period;
		}
		double x = 0;
		int guard = 0;
		while (x < w && guard++ < 400)
		{
			for (int i = 0; i < this._pattern.Length && x < w; i++)
			{
				double len = Math.Abs(this._pattern[i]) * scale;
				if (this._pattern[i] > 0)
				{
					dc.DrawLine(pen, new Point(x, cy), new Point(Math.Min(x + Math.Max(len, 1), w), cy));
				}
				else if (this._pattern[i] == 0)
				{
					dc.FillRectangle(this._brush, new Rect(x, cy - 0.5, 1, 1));
					len = Math.Max(len, 1);
				}
				x += len;
				if (this._marks != null && i < this._marks.Length && this._marks[i] && x < w)
				{
					// 문자·모양 자리: 작은 네모
					dc.DrawRectangle(null, pen, new Rect(x - 2, cy - 2.5, 4, 5));
				}
			}
			if (period * scale <= 0)
			{
				break;
			}
		}
	}
}
