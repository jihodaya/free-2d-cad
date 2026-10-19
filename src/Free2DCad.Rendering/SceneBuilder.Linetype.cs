using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Rendering;

/// <summary>
/// 복합 선종류 (문자·모양이 들어간 선: PIPE_GAS, RAILWAY, FENCE_CIRCLE 등) 그리기.
/// 대시는 일반 선종류처럼 그리고, 요소는 붙은 구간의 끝에서 선 방향 X, 왼쪽 Y만큼 옮긴 곳에 놓는다.
/// 회전은 선 방향 기준(절대 각도 플래그면 고정), 문자 높이 = S × 스타일 고정 높이(0이면 S).
/// </summary>
public sealed partial class SceneBuilder
{
	/// <summary>Dash()가 찾은 복합 선종류. 바로 다음 AddPolyline이 쓰고 비운다.</summary>
	private (LineType Lt, double Scale)? _complexLt;

	/// <summary>한 도형에 놓을 요소 수가 이보다 많으면(화면에서 알아볼 수 없을 만큼 촘촘) 대시만 그린다.</summary>
	private const int MaxLinetypeElements = 4000;

	/// <summary>
	/// ltypeshp.shx 모양 (F2D가 직접 정의, 단위 크기). 130 TRACK1 세로 막대, 131 ZIG 꺾쇠, 132 BOX 사각,
	/// 133 CIRC1 원(왼쪽 끝이 원점), 134 BAT 단열재 고리.
	/// </summary>
	private static readonly Dictionary<int, List<double[]>> LinetypeShapes = BuildLinetypeShapes();

	private static Dictionary<int, List<double[]>> BuildLinetypeShapes()
	{
		static double[] Arc(double cx, double cy, double r, double a0, double a1, int n, params double[] prefix)
		{
			var pts = new List<double>(prefix);
			for (int i = 0; i <= n; i++)
			{
				double a = a0 + (a1 - a0) * i / n;
				pts.Add(cx + r * Math.Cos(a));
				pts.Add(cy + r * Math.Sin(a));
			}
			return pts.ToArray();
		}
		var bat = Arc(1, -2, 2, Math.PI, 2 * Math.PI, 16, 0, 0).Concat(new[] { 2.0, 0 }).ToArray();
		return new Dictionary<int, List<double[]>>
		{
			[130] = new() { new[] { 0, 1.0, 0, -1 } },
			[131] = new() { new[] { 0, 0, 1.0, -1, 2, 0 } },
			[132] = new() { new[] { 0, 0, 0, -1.0, 2, -1, 2, 1, 0, 1, 0, 0 } },
			[133] = new() { Arc(1, 0, 1, Math.PI, 3 * Math.PI, 36) },
			[134] = new() { bat },
		};
	}

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ShxFont?> ShapeFiles = new(StringComparer.OrdinalIgnoreCase);

	private static bool IsComplex(LineType lt) => lt.Segments.Any(s => s.IsShape || s.IsText);

	/// <summary>선(도면 좌표 꼭짓점)을 따라 복합 선종류의 문자·모양을 놓는다. unit = 패턴 길이 → 도면 길이 배율.</summary>
	private void AddLinetypeElements(double[] pts, bool closed, LineType lt, double unit, double dashOffset, Rgb color)
	{
		var segs = lt.Segments.ToList();
		double period = segs.Sum(s => Math.Abs(s.Length)) * unit;
		if (period <= 1e-12)
		{
			return;
		}
		// 꼭짓점별 누적 길이
		var path = new List<(double X, double Y)>();
		for (int i = 0; i < pts.Length; i += 2)
		{
			path.Add((pts[i], pts[i + 1]));
		}
		if (closed && path.Count > 2)
		{
			path.Add(path[0]);
		}
		var cum = new double[path.Count];
		for (int i = 1; i < path.Count; i++)
		{
			cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].Y - path[i - 1].Y, 2));
		}
		double total = cum[^1];
		int perPeriod = segs.Count(s => s.IsShape || s.IsText);
		if (total <= 0 || total / period * perPeriod > MaxLinetypeElements)
		{
			return;
		}

		// 패턴 안에서 각 구간 끝의 위치
		var ends = new double[segs.Count];
		double acc = 0;
		for (int i = 0; i < segs.Count; i++)
		{
			acc += Math.Abs(segs[i].Length) * unit;
			ends[i] = acc;
		}

		int k = 0;
		double first = -dashOffset;
		while (first > 0)
		{
			first -= period; // 시작 앞쪽의 잘린 주기에도 요소가 있을 수 있음
		}
		double tol = 1e-6 * Math.Max(1, total);
		for (double start = first; start < total; start += period)
		{
			for (int i = 0; i < segs.Count; i++)
			{
				var s = segs[i];
				if (!s.IsShape && !s.IsText)
				{
					continue;
				}
				double d = start + ends[i];
				if (d > total + tol)
				{
					break;
				}
				if (d < -tol)
				{
					continue;
				}
				// 경로 위 점과 방향
				while (k < cum.Length - 2 && cum[k + 1] < d)
				{
					k++;
				}
				int j = Math.Min(k, cum.Length - 2);
				double segLen = cum[j + 1] - cum[j];
				double t = segLen > 0 ? (d - cum[j]) / segLen : 0;
				double dx = path[j + 1].X - path[j].X, dy = path[j + 1].Y - path[j].Y;
				double ang = Math.Atan2(dy, dx);
				double px = path[j].X + dx * t, py = path[j].Y + dy * t;
				double cos = Math.Cos(ang), sin = Math.Sin(ang);
				double ox = s.Offset.X * unit, oy = s.Offset.Y * unit;
				double ex = px + ox * cos - oy * sin, ey = py + ox * sin + oy * cos;
				double rot = s.Flags.HasFlag(LineTypeShapeFlags.RotationIsAbsolute) ? s.Rotation : ang + s.Rotation;
				double size = (s.Scale != 0 ? s.Scale : 1) * unit;

				// 선 끝을 넘어가는 요소는 그리지 않고 그 자리를 실선으로 잇는다 (기존 CAD와 같음)
				bool Inside(IEnumerable<(double X, double Y)> points)
				{
					double lo = double.MaxValue, hi = double.MinValue;
					foreach (var q in points)
					{
						double u = d + (q.X - px) * cos + (q.Y - py) * sin;
						lo = Math.Min(lo, u);
						hi = Math.Max(hi, u);
					}
					if (lo >= -tol && hi <= total + tol)
					{
						return true;
					}
					double a = Math.Max(0, lo), b = Math.Min(total, hi);
					if (b > a)
					{
						this.AddPolyline(SubPath(path, cum, a, b), false, color, null, Affine2.Identity);
					}
					return false;
				}
				if (s.IsText)
				{
					this.AddLinetypeText(s, ex, ey, rot, size, color, Inside);
				}
				else
				{
					this.AddLinetypeShape(s, ex, ey, rot, size, color, Inside);
				}
			}
		}
	}

	/// <summary>경로에서 길이 a~b 부분의 꼭짓점.</summary>
	private static double[] SubPath(List<(double X, double Y)> path, double[] cum, double a, double b)
	{
		var list = new List<double>();
		(double X, double Y) At(double d)
		{
			int i = Array.FindLastIndex(cum, c => c <= d);
			i = Math.Clamp(i, 0, cum.Length - 2);
			double len = cum[i + 1] - cum[i];
			double t = len > 0 ? (d - cum[i]) / len : 0;
			return (path[i].X + (path[i + 1].X - path[i].X) * t, path[i].Y + (path[i + 1].Y - path[i].Y) * t);
		}
		var p0 = At(a);
		list.Add(p0.X);
		list.Add(p0.Y);
		for (int i = 1; i < cum.Length - 1; i++)
		{
			if (cum[i] > a && cum[i] < b)
			{
				list.Add(path[i].X);
				list.Add(path[i].Y);
			}
		}
		var p1 = At(b);
		list.Add(p1.X);
		list.Add(p1.Y);
		return list.ToArray();
	}

	private void AddLinetypeText(LineType.Segment s, double x, double y, double rot, double size, Rgb color, Func<IEnumerable<(double X, double Y)>, bool> inside)
	{
		if (string.IsNullOrEmpty(s.Text))
		{
			return;
		}
		var style = s.Style;
		double h = style != null && !style.IsShapeFile && style.Height > 0 ? style.Height * size : size;
		double wf = style?.Width > 0 ? style.Width : 1.0;
		var sf = this.FontsOf(style);
		double w = s.Text.Sum(ch =>
		{
			var g = this.GlyphOf(ch, sf, MTextFormat.Default, h);
			return AdvanceOf(g.Shx, g.Ttf, h);
		}) * wf;
		var box = Affine2.Rotation(rot).Then(Affine2.Translation(x, y));
		if (!inside(new[] { box.Apply(0, 0), box.Apply(w, 0), box.Apply(0, h), box.Apply(w, h) }))
		{
			return;
		}
		this.AddTextBlock(MTextTokenizer.Tokenize(s.Text, isMText: false), new XYZ(x, y, 0), h, rot, wf, style?.ObliqueAngle ?? 0, 0, 0,
			TextVAlign.Baseline, false, 1.0, false, style, color, Affine2.Identity);
	}

	private void AddLinetypeShape(LineType.Segment s, double x, double y, double rot, double size, Rgb color, Func<IEnumerable<(double X, double Y)>, bool> inside)
	{
		List<double[]>? strokes = null;
		string file = Path.GetFileName(s.Style?.Filename ?? string.Empty);
		if (file.Length == 0 || file.Equals("ltypeshp.shx", StringComparison.OrdinalIgnoreCase) || file.Equals("ltypeshp", StringComparison.OrdinalIgnoreCase))
		{
			LinetypeShapes.TryGetValue(s.ShapeNumber, out strokes);
		}
		if (strokes == null && file.Length > 0)
		{
			if (!ShapeFiles.TryGetValue(file, out var shx))
			{
				ShapeFiles[file] = shx = UserFonts.LoadShx(file);
			}
			strokes = shx?.GetGlyph(s.ShapeNumber)?.Strokes;
		}
		if (strokes == null)
		{
			return;
		}
		var xf = Affine2.Scale(size, size).Then(Affine2.Rotation(rot)).Then(Affine2.Translation(x, y));
		if (!inside(strokes.SelectMany(st => Enumerable.Range(0, st.Length / 2).Select(m => xf.Apply(st[2 * m], st[2 * m + 1])))))
		{
			return;
		}
		foreach (double[] stroke in strokes)
		{
			this.AddPolyline(stroke, false, color, null, xf);
		}
	}
}
