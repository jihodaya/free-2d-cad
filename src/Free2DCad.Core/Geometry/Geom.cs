using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Geometry;

/// <summary>
/// 2D 곡선 계산: 객체를 선분·호 조각으로 나누고 교점·매개변수·길이를 구한다 (자르기·연장·끊기·길이 조정 공용).
/// 호 조각은 중심·반지름·시작각·부호 있는 회전각(반시계 +)으로 나타낸다.
/// </summary>
public static class Geom
{
	public const double Eps = 1e-9;

	public abstract record Seg
	{
		public abstract double Length { get; }

		/// <summary>조각 위 0..1 위치의 점.</summary>
		public abstract XY At(double u);

		/// <summary>점에 가장 가까운 조각 위 위치(0..1, 범위로 자름).</summary>
		public abstract double Project(XY p);
	}

	public sealed record LineSeg(XY A, XY B) : Seg
	{
		public override double Length => Dist(this.A, this.B);

		public override XY At(double u) => new(this.A.X + (this.B.X - this.A.X) * u, this.A.Y + (this.B.Y - this.A.Y) * u);

		public override double Project(XY p)
		{
			double dx = this.B.X - this.A.X, dy = this.B.Y - this.A.Y, l2 = dx * dx + dy * dy;
			return l2 < Eps ? 0 : Math.Clamp(((p.X - this.A.X) * dx + (p.Y - this.A.Y) * dy) / l2, 0, 1);
		}
	}

	/// <summary>호 조각: Sweep 부호 = 방향 (반시계 +).</summary>
	public sealed record ArcSeg(XY C, double R, double Start, double Sweep) : Seg
	{
		public override double Length => Math.Abs(this.Sweep) * this.R;

		public override XY At(double u)
		{
			double a = this.Start + this.Sweep * u;
			return new XY(this.C.X + this.R * Math.Cos(a), this.C.Y + this.R * Math.Sin(a));
		}

		/// <summary>각도 a가 조각 위 어디(0..1)인지. 밖이면 NaN.</summary>
		public double UOfAngle(double a)
		{
			double d = Norm(this.Sweep >= 0 ? a - this.Start : this.Start - a);
			double sw = Math.Abs(this.Sweep);
			if (d <= sw + 1e-9) return sw < Eps ? 0 : d / sw;
			if (Math.Abs(d - 2 * Math.PI) < 1e-9) return 0;
			return double.NaN;
		}

		public override double Project(XY p)
		{
			double u = this.UOfAngle(Math.Atan2(p.Y - this.C.Y, p.X - this.C.X));
			if (!double.IsNaN(u)) return u;
			return Dist(p, this.At(0)) <= Dist(p, this.At(1)) ? 0 : 1;
		}
	}

	public static double Dist(XY a, XY b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

	public static double Norm(double a)
	{
		a %= 2 * Math.PI;
		return a < 0 ? a + 2 * Math.PI : a;
	}

	public static XY P(XYZ p) => new(p.X, p.Y);

	/// <summary>폴리선 꼭짓점 사이 조각 (볼록 값으로 호).</summary>
	public static Seg PolySeg(XY a, XY b, double bulge)
	{
		if (Math.Abs(bulge) < 1e-12)
		{
			return new LineSeg(a, b);
		}
		double sweep = 4 * Math.Atan(bulge);
		double chord = Dist(a, b);
		if (chord < Eps) return new LineSeg(a, b);
		double r = chord / (2 * Math.Sin(Math.Abs(sweep) / 2));
		// 현의 가운데에서 중심까지 (볼록 부호에 따라 왼쪽/오른쪽)
		double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2;
		double h = r * Math.Cos(sweep / 2) * Math.Sign(bulge);
		double nx = -(b.Y - a.Y) / chord, ny = (b.X - a.X) / chord;
		var c = new XY(mx + nx * h, my + ny * h);
		double start = Math.Atan2(a.Y - c.Y, a.X - c.X);
		return new ArcSeg(c, r, start, sweep);
	}

	/// <summary>객체를 조각 목록으로. 모르는 종류면 빈 목록.</summary>
	public static List<Seg> SegmentsOf(Entity e)
	{
		var list = new List<Seg>();
		switch (e)
		{
			case Line l:
				list.Add(new LineSeg(P(l.StartPoint), P(l.EndPoint)));
				break;
			case Arc a:
				{
					double sweep = Norm(a.EndAngle - a.StartAngle);
					if (sweep < Eps) sweep = 2 * Math.PI;
					list.Add(new ArcSeg(P(a.Center), a.Radius, a.StartAngle, sweep));
					break;
				}
			case Circle c:
				list.Add(new ArcSeg(P(c.Center), c.Radius, 0, 2 * Math.PI));
				break;
			case LwPolyline pl:
				{
					var v = pl.Vertices;
					int n = v.Count;
					int count = pl.IsClosed ? n : n - 1;
					for (int i = 0; i < count; i++)
					{
						var a = v[i];
						var b = v[(i + 1) % n];
						list.Add(PolySeg(new XY(a.Location.X, a.Location.Y), new XY(b.Location.X, b.Location.Y), a.Bulge));
					}
					break;
				}
			case Ellipse el:
				{
					// 타원은 잘게 나눈 선분으로 (교점 계산용)
					var major = el.MajorAxisEndPoint;
					double ra = Math.Sqrt(major.X * major.X + major.Y * major.Y), rb = ra * el.RadiusRatio, rot = Math.Atan2(major.Y, major.X);
					double t0 = el.StartParameter, t1 = el.EndParameter;
					if (t1 <= t0) t1 += 2 * Math.PI;
					int segs = Math.Max(16, (int)((t1 - t0) / (2 * Math.PI) * 96));
					XY Pt(double t) => new(el.Center.X + ra * Math.Cos(t) * Math.Cos(rot) - rb * Math.Sin(t) * Math.Sin(rot),
						el.Center.Y + ra * Math.Cos(t) * Math.Sin(rot) + rb * Math.Sin(t) * Math.Cos(rot));
					for (int i = 0; i < segs; i++)
					{
						list.Add(new LineSeg(Pt(t0 + (t1 - t0) * i / segs), Pt(t0 + (t1 - t0) * (i + 1) / segs)));
					}
					break;
				}
			case Spline sp:
				{
					try
					{
						var pts = sp.PolygonalVertexes(Math.Max(sp.ControlPoints.Count * 8, 32));
						for (int i = 0; i + 1 < pts.Count; i++)
						{
							list.Add(new LineSeg(P(pts[i]), P(pts[i + 1])));
						}
					}
					catch (Exception)
					{
						// 계산할 수 없는 곡선은 건너뜀
					}
					break;
				}
		}
		return list;
	}

	/// <summary>두 조각의 교점 (조각 범위 안).</summary>
	public static IEnumerable<XY> Intersect(Seg a, Seg b)
	{
		switch (a, b)
		{
			case (LineSeg l1, LineSeg l2):
				if (LineLine(l1.A, l1.B, l2.A, l2.B, out double t, out double u) && t >= -1e-9 && t <= 1 + 1e-9 && u >= -1e-9 && u <= 1 + 1e-9)
				{
					yield return l1.At(t);
				}
				break;
			case (LineSeg l, ArcSeg c):
				foreach (var (pt, t2) in LineCircle(l.A, l.B, c.C, c.R))
				{
					if (t2 >= -1e-9 && t2 <= 1 + 1e-9 && !double.IsNaN(c.UOfAngle(Math.Atan2(pt.Y - c.C.Y, pt.X - c.C.X))))
					{
						yield return pt;
					}
				}
				break;
			case (ArcSeg c, LineSeg l):
				foreach (var pt in Intersect(l, c)) yield return pt;
				break;
			case (ArcSeg c1, ArcSeg c2):
				foreach (var pt in CircleCircle(c1.C, c1.R, c2.C, c2.R))
				{
					if (!double.IsNaN(c1.UOfAngle(Math.Atan2(pt.Y - c1.C.Y, pt.X - c1.C.X))) && !double.IsNaN(c2.UOfAngle(Math.Atan2(pt.Y - c2.C.Y, pt.X - c2.C.X))))
					{
						yield return pt;
					}
				}
				break;
		}
	}

	/// <summary>두 객체의 교점들.</summary>
	public static List<XY> Intersections(Entity a, Entity b)
	{
		var result = new List<XY>();
		var sa = SegmentsOf(a);
		var sb = SegmentsOf(b);
		foreach (var x in sa)
		{
			foreach (var y in sb)
			{
				foreach (var p in Intersect(x, y))
				{
					if (!result.Any(q => Dist(q, p) < 1e-7)) result.Add(p);
				}
			}
		}
		return result;
	}

	/// <summary>무한 직선(점 a, b를 지남)과 조각의 교점: 직선 위 매개변수 t (a=0, b=1).</summary>
	public static IEnumerable<double> RayHits(XY a, XY b, Seg s)
	{
		switch (s)
		{
			case LineSeg l:
				if (LineLine(a, b, l.A, l.B, out double t, out double u) && u >= -1e-9 && u <= 1 + 1e-9)
				{
					yield return t;
				}
				break;
			case ArcSeg c:
				foreach (var (pt, t2) in LineCircle(a, b, c.C, c.R))
				{
					if (!double.IsNaN(c.UOfAngle(Math.Atan2(pt.Y - c.C.Y, pt.X - c.C.X))))
					{
						yield return t2;
					}
				}
				break;
		}
	}

	/// <summary>원(중심 c, 반지름 r)과 조각의 교점: 원 위 각도.</summary>
	public static IEnumerable<double> CircleHits(XY c, double r, Seg s)
	{
		var full = new ArcSeg(c, r, 0, 2 * Math.PI);
		foreach (var p in Intersect(full, s))
		{
			yield return Math.Atan2(p.Y - c.Y, p.X - c.X);
		}
	}

	public static bool LineLine(XY a, XY b, XY c, XY d, out double t, out double u)
	{
		double rx = b.X - a.X, ry = b.Y - a.Y, sx = d.X - c.X, sy = d.Y - c.Y;
		double den = rx * sy - ry * sx;
		t = u = 0;
		if (Math.Abs(den) < 1e-12 * (Math.Abs(rx) + Math.Abs(ry) + 1) * (Math.Abs(sx) + Math.Abs(sy) + 1))
		{
			return false;
		}
		double qx = c.X - a.X, qy = c.Y - a.Y;
		t = (qx * sy - qy * sx) / den;
		u = (qx * ry - qy * rx) / den;
		return true;
	}

	/// <summary>직선(a→b)과 원의 교점: (점, 직선 매개변수 t).</summary>
	public static IEnumerable<(XY Pt, double T)> LineCircle(XY a, XY b, XY c, double r)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y;
		double fx = a.X - c.X, fy = a.Y - c.Y;
		double A = dx * dx + dy * dy, B = 2 * (fx * dx + fy * dy), C = fx * fx + fy * fy - r * r;
		if (A < 1e-18) yield break;
		double disc = B * B - 4 * A * C;
		if (disc < -1e-12 * A) yield break;
		if (disc < 0) disc = 0;
		double sq = Math.Sqrt(disc);
		double t1 = (-B - sq) / (2 * A), t2 = (-B + sq) / (2 * A);
		yield return (new XY(a.X + dx * t1, a.Y + dy * t1), t1);
		if (sq > 1e-12) yield return (new XY(a.X + dx * t2, a.Y + dy * t2), t2);
	}

	public static IEnumerable<XY> CircleCircle(XY c1, double r1, XY c2, double r2)
	{
		double d = Dist(c1, c2);
		if (d < Eps || d > r1 + r2 + 1e-9 || d < Math.Abs(r1 - r2) - 1e-9) yield break;
		double a = (r1 * r1 - r2 * r2 + d * d) / (2 * d);
		double h2 = r1 * r1 - a * a;
		double h = h2 > 0 ? Math.Sqrt(h2) : 0;
		double mx = c1.X + a * (c2.X - c1.X) / d, my = c1.Y + a * (c2.Y - c1.Y) / d;
		double ox = -h * (c2.Y - c1.Y) / d, oy = h * (c2.X - c1.X) / d;
		yield return new XY(mx + ox, my + oy);
		if (h > 1e-12) yield return new XY(mx - ox, my - oy);
	}

	// ===== 객체 위 위치(매개변수) =====

	/// <summary>객체 위 점의 위치: 조각 번호 + 조각 안 0..1 (조각 목록 순서). 원은 각도/2π.</summary>
	public static double ParamOf(List<Seg> segs, XY p)
	{
		int best = 0;
		double bestD = double.MaxValue, bestU = 0;
		for (int i = 0; i < segs.Count; i++)
		{
			double u = segs[i].Project(p);
			double d = Dist(segs[i].At(u), p);
			if (d < bestD - 1e-12)
			{
				bestD = d;
				best = i;
				bestU = u;
			}
		}
		return best + bestU;
	}

	public static XY PointAt(List<Seg> segs, double param)
	{
		int i = Math.Clamp((int)Math.Floor(param), 0, segs.Count - 1);
		return segs[i].At(Math.Clamp(param - i, 0, 1));
	}

	/// <summary>조각 목록의 [p0, p1] 부분을 잘라 낸 조각들 (p0 &lt; p1).</summary>
	public static List<Seg> SubRange(List<Seg> segs, double p0, double p1)
	{
		var result = new List<Seg>();
		for (int i = 0; i < segs.Count; i++)
		{
			double a = Math.Max(p0, i), b = Math.Min(p1, i + 1);
			if (b - a < 1e-12) continue;
			result.Add(Part(segs[i], a - i, b - i));
		}
		return result;
	}

	public static Seg Part(Seg s, double u0, double u1) => s switch
	{
		LineSeg l => new LineSeg(l.At(u0), l.At(u1)),
		ArcSeg c => new ArcSeg(c.C, c.R, c.Start + c.Sweep * u0, c.Sweep * (u1 - u0)),
		_ => s,
	};

	/// <summary>조각들을 객체로: 선 하나 → Line, 호 하나 → Arc, 여러 개 → LwPolyline (볼록 값으로 호).</summary>
	public static Entity? ToEntity(List<Seg> segs, Entity propsFrom)
	{
		if (segs.Count == 0) return null;
		Entity result;
		if (segs.Count == 1 && segs[0] is LineSeg l && propsFrom is not LwPolyline)
		{
			result = new Line(new XYZ(l.A.X, l.A.Y, 0), new XYZ(l.B.X, l.B.Y, 0));
		}
		else if (segs.Count == 1 && segs[0] is ArcSeg c && propsFrom is not LwPolyline)
		{
			double s = c.Sweep >= 0 ? c.Start : c.Start + c.Sweep;
			result = new Arc { Center = new XYZ(c.C.X, c.C.Y, 0), Radius = c.R, StartAngle = Norm(s), EndAngle = Norm(s + Math.Abs(c.Sweep)) };
		}
		else
		{
			var pl = new LwPolyline();
			foreach (var sg in segs)
			{
				var a = sg.At(0);
				double bulge = sg is ArcSeg arc ? Math.Tan(arc.Sweep / 4) : 0;
				pl.Vertices.Add(new LwPolyline.Vertex(a) { Bulge = bulge });
			}
			var end = segs[^1].At(1);
			pl.Vertices.Add(new LwPolyline.Vertex(end));
			if (propsFrom is LwPolyline src)
			{
				pl.ConstantWidth = src.ConstantWidth;
				pl.Elevation = src.Elevation;
			}
			result = pl;
		}
		CopyProps(propsFrom, result);
		return result;
	}

	/// <summary>도면층·색·선종류·선가중치·선종류 축척 복사.</summary>
	public static void CopyProps(Entity from, Entity to)
	{
		to.Layer = from.Layer;
		to.Color = from.Color;
		to.LineType = from.LineType;
		to.LineWeight = from.LineWeight;
		to.LineTypeScale = from.LineTypeScale;
		to.Transparency = from.Transparency;
	}

	/// <summary>닫힌 객체(원, 닫힌 폴리선)인지.</summary>
	public static bool IsClosed(Entity e) => e is Circle and not Arc || e is LwPolyline { IsClosed: true } || (e is Ellipse el && Math.Abs(Norm(el.EndParameter - el.StartParameter)) < 1e-9);

	/// <summary>전체 길이.</summary>
	public static double Length(List<Seg> segs) => segs.Sum(s => s.Length);
}
