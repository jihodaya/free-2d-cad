using ACadSharp.Entities;
using ACadSharp.Tables;

namespace Free2DCad.Rendering;

/// <summary>
/// 기하 공차(TOLERANCE) 그리기 (기존 CAD와 같은 모양): 삽입점 = 첫 줄 틀의 왼쪽 가운데,
/// 줄 높이 = 문자 높이 × 2, 기호 칸 폭 = 문자 높이 × 2.5, 칸 안 여백·항목 사이 = 문자 높이 / 2, 기호 지름 = 문자 높이.
/// </summary>
public sealed partial class SceneBuilder
{
	private void AddTolerance(Tolerance tol, Rgb color, Context ctx)
	{
		var style = tol.Style;
		double scale = style?.ScaleFactor > 0 ? style.ScaleFactor : 1.0;
		double h = (style?.TextHeight > 0 ? style.TextHeight : 2.5) * scale;
		double gap = h / 2;
		double rowH = h * 2;
		var dir = tol.Direction;
		double ang = Math.Abs(dir.X) + Math.Abs(dir.Y) > 1e-12 ? Math.Atan2(dir.Y, dir.X) : 0;
		var xf = Affine2.Rotation(ang).Then(Affine2.Translation(tol.InsertionPoint.X, tol.InsertionPoint.Y)).Then(ctx.Xf);
		TextStyle? textStyle = style?.Style;
		var sf = this.FontsOf(textStyle);
		double Measure(string s) => s.Sum(ch => AdvanceOf(this.GlyphOf(ch, sf, MTextFormat.Default, h).Shx, this.GlyphOf(ch, sf, MTextFormat.Default, h).Ttf, h));

		// 기존 CAD처럼 틀·기호는 치수선 색(DIMCLRD), 글자는 치수 문자 색(DIMCLRT). ByLayer·ByBlock이면 객체 색
		Rgb StyleColor(ACadSharp.Color c) => c.IsByLayer || c.IsByBlock ? color : c.Index == 7 ? Rgb.White : new Rgb(c.R, c.G, c.B);
		var frameColor = style != null ? StyleColor(style.DimensionLineColor) : color;
		var textColor = style != null ? StyleColor(style.TextColor) : color;
		var rows = GdtText.Parse(tol.Text);
		double top = rowH / 2;
		foreach (var fields in rows)
		{
			if (fields.All(f => f.Count == 0))
			{
				continue;
			}
			double bottom = top - rowH, mid = top - rowH / 2;
			double x = 0;
			for (int fi = 0; fi < fields.Count; fi++)
			{
				var items = fields[fi];
				if (items.Count == 0)
				{
					continue;
				}
				double content = items.Sum(it => it.IsSymbol ? h : Measure(it.Text)) + gap * (items.Count - 1);
				bool symbolBox = fi == 0 && items.Count == 1 && items[0].IsSymbol;
				double w = symbolBox ? h * 2.5 : content + gap * 2;
				this.AddPolyline(new[] { x, top, x + w, top, x + w, bottom, x, bottom }, true, frameColor, null, xf);
				double cx = x + (w - content) / 2;
				foreach (var it in items)
				{
					if (it.IsSymbol)
					{
						this.AddGdtSymbol(it.Symbol, cx + h / 2, mid, h, textColor, xf);
						cx += h + gap;
					}
					else
					{
						this.AddTextBlock(MTextTokenizer.Tokenize(it.Text, false), new CSMath.XYZ(cx, mid - h / 2, 0), h, 0, 1, 0, 0, 0,
							TextVAlign.Baseline, false, 1, false, textStyle, textColor, xf);
						cx += Measure(it.Text) + gap;
					}
				}
				x += w;
			}
			top = bottom;
		}
	}

	/// <summary>GDT 기호를 선으로 그린다 (중심 cx·cy, 크기 s = 문자 높이).</summary>
	private void AddGdtSymbol(char code, double cx, double cy, double s, Rgb color, Affine2 xf)
	{
		double r = s / 2;
		void L(params double[] p) => this.AddPolyline(p, false, color, null, xf);
		void C(double x0, double y0, double rad, double a0 = 0, double a1 = Math.PI * 2, bool closed = true)
		{
			const int n = 36;
			int m = Math.Max(4, (int)Math.Ceiling(n * Math.Abs(a1 - a0) / (Math.PI * 2)));
			var pts = new double[(m + 1) * 2];
			for (int i = 0; i <= m; i++)
			{
				double a = a0 + (a1 - a0) * i / m;
				pts[i * 2] = x0 + rad * Math.Cos(a);
				pts[i * 2 + 1] = y0 + rad * Math.Sin(a);
			}
			this.AddPolyline(pts, closed && Math.Abs(a1 - a0 - Math.PI * 2) < 1e-9, color, null, xf);
		}
		void Arrow(double x0, double y0, double x1, double y1)
		{
			L(x0, y0, x1, y1);
			double a = Math.Atan2(y1 - y0, x1 - x0), al = s * 0.3;
			L(x1 - al * Math.Cos(a - 0.4), y1 - al * Math.Sin(a - 0.4), x1, y1, x1 - al * Math.Cos(a + 0.4), y1 - al * Math.Sin(a + 0.4));
		}
		void Letter(string t)
		{
			C(cx, cy, r);
			double th = s * 0.6;
			this.AddTextBlock(MTextTokenizer.Tokenize(t, false), new CSMath.XYZ(cx, cy - th / 2, 0), th, 0, 1, 0, 0, 0.5,
				TextVAlign.Baseline, false, 1, false, null, color, xf);
		}
		switch (code)
		{
			case 'a': L(cx - r, cy - r, cx + r, cy - r); L(cx - r, cy - r, cx + r, cy + r * 0.6); break; // 경사도
			case 'b': L(cx - r, cy - r, cx + r, cy - r); L(cx, cy - r, cx, cy + r); break; // 직각도
			case 'c': this.AddPolyline(new[] { cx - r, cy - r * 0.5, cx + r * 0.5, cy - r * 0.5, cx + r, cy + r * 0.5, cx - r * 0.5, cy + r * 0.5 }, true, color, null, xf); break; // 평면도
			case 'd': C(cx, cy - r * 0.5, r, 0, Math.PI, false); L(cx - r, cy - r * 0.5, cx + r, cy - r * 0.5); break; // 면의 윤곽도
			case 'e': C(cx, cy, r); break; // 진원도
			case 'f': L(cx - r * 0.6, cy - r, cx, cy + r); L(cx, cy - r, cx + r * 0.6, cy + r); break; // 평행도
			case 'g': C(cx, cy, r * 0.6); L(cx - r, cy - r, cx + r * 0.2, cy + r); L(cx - r * 0.2, cy - r, cx + r, cy + r); break; // 원통도
			case 'h': Arrow(cx - r, cy - r, cx + r, cy + r); break; // 원주 흔들림
			case 'i': L(cx - r, cy, cx + r, cy); L(cx - r * 0.6, cy + r * 0.5, cx + r * 0.6, cy + r * 0.5); L(cx - r * 0.6, cy - r * 0.5, cx + r * 0.6, cy - r * 0.5); break; // 대칭도
			case 'j': C(cx, cy, r); L(cx - s * 0.75, cy, cx + s * 0.75, cy); L(cx, cy - s * 0.75, cx, cy + s * 0.75); break; // 위치도
			case 'k': C(cx, cy - r * 0.5, r, 0, Math.PI, false); break; // 선의 윤곽도
			case 'l': Letter("L"); break;
			case 'm': Letter("M"); break;
			case 's': Letter("S"); break;
			case 'p': Letter("P"); break;
			case 'n': C(cx, cy, r); L(cx - s * 0.43, cy - s * 0.75, cx + s * 0.43, cy + s * 0.75); break; // 지름
			case 'r': C(cx, cy, r); C(cx, cy, r * 0.5); break; // 동심도
			case 't': Arrow(cx - r, cy - r, cx - r * 0.1, cy + r); Arrow(cx, cy - r, cx + r * 0.9, cy + r); L(cx - r, cy - r, cx + r * 0.2, cy - r); break; // 온 흔들림
			case 'u': L(cx - r, cy, cx + r, cy); break; // 진직도
		}
	}
}
