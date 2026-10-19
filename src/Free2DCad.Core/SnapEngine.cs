using ACadSharp.Entities;
using Free2DCad.Rendering;

namespace Free2DCad.Core;

/// <summary>
/// 객체 스냅 모드 플래그.
/// </summary>
[Flags]
public enum SnapModes
{
	None = 0,
	Endpoint = 1 << 0,
	Midpoint = 1 << 1,
	Center = 1 << 2,
	Quadrant = 1 << 3,
	Insertion = 1 << 4,
	Node = 1 << 5,
	Intersection = 1 << 6,
	Nearest = 1 << 7,
	Default = Endpoint | Midpoint | Center | Quadrant | Insertion | Node | Intersection
}

/// <summary>
/// 객체 스냅 계산 엔진.
/// </summary>
public static class SnapEngine
{
	/// <summary>
	/// 주어진 위치 주변의 가장 적합한 객체 스냅 점을 찾는다.
	/// </summary>
	public static SnapPoint? Find(SceneIndex index, double x, double y, double tol, SnapModes modes = SnapModes.Default)
	{
		ArgumentNullException.ThrowIfNull(index);

		if (modes == SnapModes.None || tol <= 0)
		{
			return null;
		}

		double bestDistSq = tol * tol;
		SnapPoint? bestSnap = null;

		var searchBounds = new Bounds(x - tol, y - tol, x + tol, y + tol);

		// 1. 등록된 스냅 점(Endpoint, Midpoint, Center, Quadrant, Insertion, Node) 검사
		var snapPoints = new List<SnapPoint>();
		index.QuerySnapPointsInBounds(searchBounds, snapPoints);

		for (int i = 0; i < snapPoints.Count; i++)
		{
			var sp = snapPoints[i];
			if (!MatchesMode(sp.Kind, modes))
			{
				continue;
			}

			double dx = sp.X - x;
			double dy = sp.Y - y;
			double dSq = dx * dx + dy * dy;
			if (dSq <= bestDistSq)
			{
				bestDistSq = dSq;
				bestSnap = sp;
			}
		}

		// 2. 교차점(Intersection) 스냅 계산
		List<IndexedSegment>? segments = null;
		if (modes.HasFlag(SnapModes.Intersection))
		{
			segments = new List<IndexedSegment>();
			index.QuerySegmentsInBounds(searchBounds, segments);

			if (segments.Count > 200)
			{
				segments.Sort((a, b) =>
				{
					double da = DistanceSqToSegment(x, y, a.X0, a.Y0, a.X1, a.Y1);
					double db = DistanceSqToSegment(x, y, b.X0, b.Y0, b.X1, b.Y1);
					return da.CompareTo(db);
				});
				segments.RemoveRange(200, segments.Count - 200);
			}

			for (int i = 0; i < segments.Count; i++)
			{
				var segA = segments[i];
				for (int j = i + 1; j < segments.Count; j++)
				{
					var segB = segments[j];
					if (TryGetSegmentIntersection(
						segA.X0, segA.Y0, segA.X1, segA.Y1,
						segB.X0, segB.Y0, segB.X1, segB.Y1,
						out double ix, out double iy))
					{
						double dx = ix - x;
						double dy = iy - y;
						double dSq = dx * dx + dy * dy;
						if (dSq <= bestDistSq)
						{
							bestDistSq = dSq;
							bestSnap = new SnapPoint(ix, iy, SnapKind.Intersection, segA.Source ?? segB.Source);
						}
					}
				}
			}
		}

		// 3. 근처점(Nearest) 스냅: 다른 스냅이 하나도 없을 때만 적용
		if (bestSnap == null && modes.HasFlag(SnapModes.Nearest))
		{
			if (segments == null)
			{
				segments = new List<IndexedSegment>();
				index.QuerySegmentsInBounds(searchBounds, segments);
			}

			for (int i = 0; i < segments.Count; i++)
			{
				var seg = segments[i];
				double dSq = DistanceSqToSegment(x, y, seg.X0, seg.Y0, seg.X1, seg.Y1, out double nx, out double ny);
				if (dSq <= bestDistSq)
				{
					bestDistSq = dSq;
					bestSnap = new SnapPoint(nx, ny, SnapKind.Nearest, seg.Source);
				}
			}
		}

		return bestSnap;
	}

	private static bool MatchesMode(SnapKind kind, SnapModes modes) => kind switch
	{
		SnapKind.Endpoint => (modes & SnapModes.Endpoint) != 0,
		SnapKind.Midpoint => (modes & SnapModes.Midpoint) != 0,
		SnapKind.Center => (modes & SnapModes.Center) != 0,
		SnapKind.Quadrant => (modes & SnapModes.Quadrant) != 0,
		SnapKind.Insertion => (modes & SnapModes.Insertion) != 0,
		SnapKind.Node => (modes & SnapModes.Node) != 0,
		SnapKind.Intersection => (modes & SnapModes.Intersection) != 0,
		SnapKind.Nearest => (modes & SnapModes.Nearest) != 0,
		_ => false
	};

	private static double DistanceSqToSegment(double px, double py, double x0, double y0, double x1, double y1)
	{
		return DistanceSqToSegment(px, py, x0, y0, x1, y1, out _, out _);
	}

	private static double DistanceSqToSegment(double px, double py, double x0, double y0, double x1, double y1, out double closestX, out double closestY)
	{
		double dx = x1 - x0;
		double dy = y1 - y0;
		double lenSq = dx * dx + dy * dy;
		if (lenSq < 1e-12)
		{
			closestX = x0;
			closestY = y0;
		}
		else
		{
			double t = Math.Clamp(((px - x0) * dx + (py - y0) * dy) / lenSq, 0.0, 1.0);
			closestX = x0 + t * dx;
			closestY = y0 + t * dy;
		}

		double diffX = px - closestX;
		double diffY = py - closestY;
		return diffX * diffX + diffY * diffY;
	}

	private static bool TryGetSegmentIntersection(
		double x1, double y1, double x2, double y2,
		double x3, double y3, double x4, double y4,
		out double ix, out double iy)
	{
		ix = 0;
		iy = 0;
		double d1x = x2 - x1;
		double d1y = y2 - y1;
		double d2x = x4 - x3;
		double d2y = y4 - y3;

		double denom = d1x * d2y - d1y * d2x;
		if (Math.Abs(denom) < 1e-12)
		{
			return false;
		}

		double dx = x3 - x1;
		double dy = y3 - y1;
		double t1 = (dx * d2y - dy * d2x) / denom;
		double t2 = (dx * d1y - dy * d1x) / denom;

		const double eps = 1e-6;
		if (t1 >= -eps && t1 <= 1.0 + eps && t2 >= -eps && t2 <= 1.0 + eps)
		{
			ix = x1 + Math.Clamp(t1, 0.0, 1.0) * d1x;
			iy = y1 + Math.Clamp(t1, 0.0, 1.0) * d1y;
			return true;
		}

		return false;
	}
}
