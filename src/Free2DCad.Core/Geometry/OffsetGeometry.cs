using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Geometry;

/// <summary>
/// 간격띄우기(OFFSET) 도형 계산: 선·원·호·폴리선(선·호 조각). 폴리선은 조각마다 평행하게 옮긴 뒤
/// 이웃 조각을 서로 만나는 곳까지 늘이거나 자른다 (OFFSETGAPTYPE=0, 기존 CAD 기본).
/// </summary>
public static class OffsetGeometry
{
	/// <summary>side 쪽으로 dist만큼 띄운 새 객체. 만들 수 없으면(반지름이 0 이하 등) null.</summary>
	public static Entity? Offset(Entity e, double dist, XY side)
	{
		Entity? result = e switch
		{
			Line l => OffsetLine(l, dist, side),
			Arc a => OffsetArc(a, dist, side),
			Circle c => OffsetCircle(c, dist, side),
			LwPolyline pl => OffsetPolyline(pl, dist, side),
			_ => null,
		};
		if (result != null)
		{
			Geom.CopyProps(e, result);
		}
		return result;
	}

	/// <summary>점에서 객체까지 거리 (통과점 간격띄우기용).</summary>
	public static double DistanceTo(Entity e, XY p)
	{
		var segs = Geom.SegmentsOf(e);
		if (segs.Count == 0)
		{
			return 0;
		}
		return segs.Min(s => Geom.Dist(p, s.At(s.Project(p))));
	}

	private static Line? OffsetLine(Line l, double d, XY side)
	{
		var a = Geom.P(l.StartPoint);
		var b = Geom.P(l.EndPoint);
		double len = Geom.Dist(a, b);
		if (len < Geom.Eps) return null;
		double nx = -(b.Y - a.Y) / len, ny = (b.X - a.X) / len;
		double s = (side.X - a.X) * nx + (side.Y - a.Y) * ny >= 0 ? 1 : -1;
		return new Line(new XYZ(a.X + nx * d * s, a.Y + ny * d * s, l.StartPoint.Z), new XYZ(b.X + nx * d * s, b.Y + ny * d * s, l.EndPoint.Z));
	}

	private static double NewRadius(XYZ center, double r, double d, XY side)
	{
		double ds = Geom.Dist(Geom.P(center), side);
		return ds >= r ? r + d : r - d;
	}

	private static Circle? OffsetCircle(Circle c, double d, XY side)
	{
		double r = NewRadius(c.Center, c.Radius, d, side);
		return r > Geom.Eps ? new Circle { Center = c.Center, Radius = r, Normal = c.Normal, Thickness = c.Thickness } : null;
	}

	private static Arc? OffsetArc(Arc a, double d, XY side)
	{
		double r = NewRadius(a.Center, a.Radius, d, side);
		return r > Geom.Eps ? new Arc { Center = a.Center, Radius = r, StartAngle = a.StartAngle, EndAngle = a.EndAngle, Normal = a.Normal, Thickness = a.Thickness } : null;
	}

	/// <summary>조각의 진행 방향 왼쪽으로 d만큼 옮긴 조각 (호가 사라지면 null).</summary>
	private static Geom.Seg? Shift(Geom.Seg s, double d)
	{
		switch (s)
		{
			case Geom.LineSeg l:
				{
					double len = Geom.Dist(l.A, l.B);
					if (len < Geom.Eps) return null;
					double nx = -(l.B.Y - l.A.Y) / len * d, ny = (l.B.X - l.A.X) / len * d;
					return new Geom.LineSeg(new XY(l.A.X + nx, l.A.Y + ny), new XY(l.B.X + nx, l.B.Y + ny));
				}
			case Geom.ArcSeg c:
				{
					// 반시계 호의 왼쪽은 중심 쪽
					double r = c.Sweep >= 0 ? c.R - d : c.R + d;
					return r > Geom.Eps ? c with { R = r } : null;
				}
		}
		return null;
	}

	/// <summary>두 조각의 받침(무한 직선·온전한 원)이 만나는 점 중 기준점에 가까운 것.</summary>
	private static XY? Meet(Geom.Seg a, Geom.Seg b, XY near)
	{
		IEnumerable<XY> pts = (a, b) switch
		{
			(Geom.LineSeg l1, Geom.LineSeg l2) => Geom.LineLine(l1.A, l1.B, l2.A, l2.B, out double t, out _) ? new[] { l1.At(t) } : Array.Empty<XY>(),
			(Geom.LineSeg l, Geom.ArcSeg c) => Geom.LineCircle(l.A, l.B, c.C, c.R).Select(x => x.Pt),
			(Geom.ArcSeg c, Geom.LineSeg l) => Geom.LineCircle(l.A, l.B, c.C, c.R).Select(x => x.Pt),
			(Geom.ArcSeg c1, Geom.ArcSeg c2) => Geom.CircleCircle(c1.C, c1.R, c2.C, c2.R),
			_ => Array.Empty<XY>(),
		};
		XY? best = null;
		double bestD = double.MaxValue;
		foreach (var p in pts)
		{
			double dd = Geom.Dist(p, near);
			if (dd < bestD)
			{
				bestD = dd;
				best = p;
			}
		}
		return best;
	}

	/// <summary>받침은 그대로 두고 시작·끝점을 바꾼 조각.</summary>
	private static Geom.Seg Rebuild(Geom.Seg s, XY start, XY end)
	{
		if (s is Geom.ArcSeg c)
		{
			double a0 = Math.Atan2(start.Y - c.C.Y, start.X - c.C.X);
			double a1 = Math.Atan2(end.Y - c.C.Y, end.X - c.C.X);
			double sweep = c.Sweep >= 0 ? Geom.Norm(a1 - a0) : -Geom.Norm(a0 - a1);
			if (Math.Abs(sweep) < 1e-12 && Math.Abs(c.Sweep) > Math.PI) sweep = c.Sweep >= 0 ? 2 * Math.PI : -2 * Math.PI;
			return new Geom.ArcSeg(c.C, c.R, a0, sweep);
		}
		return new Geom.LineSeg(start, end);
	}

	private static LwPolyline? OffsetPolyline(LwPolyline pl, double d, XY side)
	{
		var segs = Geom.SegmentsOf(pl);
		if (segs.Count == 0) return null;
		bool closed = pl.IsClosed;

		// 고른 쪽: 가장 가까운 조각의 진행 방향 왼쪽인지
		int nearest = 0;
		double nd = double.MaxValue;
		for (int i = 0; i < segs.Count; i++)
		{
			double dd = Geom.Dist(side, segs[i].At(segs[i].Project(side)));
			if (dd < nd)
			{
				nd = dd;
				nearest = i;
			}
		}
		var ns = segs[nearest];
		double u = ns.Project(side);
		var p0 = ns.At(Math.Max(0, u - 1e-4));
		var p1 = ns.At(Math.Min(1, u + 1e-4));
		var foot = ns.At(u);
		double cross = (p1.X - p0.X) * (side.Y - foot.Y) - (p1.Y - p0.Y) * (side.X - foot.X);
		double sd = cross >= 0 ? d : -d;

		var shifted = new List<(Geom.Seg Seg, XY Start, XY End, XY Orig0, XY Orig1)>();
		foreach (var s in segs)
		{
			var o = Shift(s, sd);
			if (o != null)
			{
				shifted.Add((o, o.At(0), o.At(1), s.At(0), s.At(1)));
			}
		}
		if (shifted.Count == 0) return null;

		// 이웃 조각 잇기
		int n = shifted.Count;
		int joins = closed ? n : n - 1;
		var gaps = new Dictionary<int, Geom.Seg>();
		for (int i = 0; i < joins; i++)
		{
			int j = (i + 1) % n;
			var a = shifted[i];
			var b = shifted[j];
			if (Geom.Dist(a.End, b.Start) < 1e-9) continue;
			var meet = Meet(a.Seg, b.Seg, a.Orig1);
			if (meet is { } m && Geom.Dist(m, a.Orig1) < Math.Abs(d) * 1e3 + 1)
			{
				shifted[i] = a with { End = m };
				shifted[j] = shifted[j] with { Start = m };
			}
			else
			{
				gaps[i] = new Geom.LineSeg(a.End, b.Start);
			}
		}

		var outSegs = new List<Geom.Seg>();
		for (int i = 0; i < n; i++)
		{
			var s = shifted[i];
			outSegs.Add(Rebuild(s.Seg, s.Start, s.End));
			if (gaps.TryGetValue(i, out var g)) outSegs.Add(g);
		}

		var result = new LwPolyline { IsClosed = closed, ConstantWidth = pl.ConstantWidth, Elevation = pl.Elevation, Normal = pl.Normal };
		foreach (var sg in outSegs)
		{
			double bulge = sg is Geom.ArcSeg arc ? Math.Tan(arc.Sweep / 4) : 0;
			result.Vertices.Add(new LwPolyline.Vertex(sg.At(0)) { Bulge = bulge });
		}
		if (!closed)
		{
			result.Vertices.Add(new LwPolyline.Vertex(outSegs[^1].At(1)));
		}
		return result;
	}
}
