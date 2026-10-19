using ACadSharp.Entities;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Hatching;

/// <summary>
/// '추가: 내부 점 선택' — 클릭한 점을 둘러싼 닫힌 영역을 도면의 선·호·폴리선 등에서 찾는다.
/// 선 여러 개가 만나서 이루는 영역도 찾는다 (교차점에서 선을 나눠 평면 그래프를 만들고 면을 따라감).
/// 결과: Loops[0] = 바깥 경계, 나머지 = 고립영역(섬). 좌표는 x0,y0,x1,y1,...
/// </summary>
public static class HatchBoundaryFinder
{
	private readonly record struct Seg(double X1, double Y1, double X2, double Y2);

	/// <summary>
	/// 경계 찾기. window를 주면 그 범위(현재 화면)에 걸친 객체만 경계 후보로 쓴다 (기존 CAD '경계 세트: 현재 뷰포트').
	/// 찾지 못하면 null.
	/// </summary>
	// 같은 장면·범위로 여러 번 클릭할 때(한 번의 HATCH 명령) 그래프를 다시 만들지 않는다
	private static readonly object CacheGate = new();
	private static (Scene Scene, Bounds? Window, PlanarGraph Graph, double Eps)? _cache;

	public static List<double[]>? FindAt(Scene scene, double px, double py, HatchStyleType style, Bounds? window = null)
	{
		PlanarGraph graph;
		double eps;
		lock (CacheGate)
		{
			if (_cache is { } c && ReferenceEquals(c.Scene, scene) && Nullable.Equals(c.Window, window))
			{
				graph = c.Graph;
				eps = c.Eps;
			}
			else
			{
				var segs = CollectSegments(scene, window);
				if (segs.Count == 0)
				{
					return null;
				}

				// 좌표 크기에 맞춘 허용 오차
				var ext = window ?? scene.Extents;
				double diag = Math.Max(Math.Sqrt(ext.Width * ext.Width + ext.Height * ext.Height), 1e-6);
				eps = diag * 1e-7;

				graph = new PlanarGraph(eps);
				graph.Build(segs);
				graph.PruneDangling();
				graph.SortAdjacency();
				graph.ComputeComponents();
				_cache = (scene, window, graph, eps);
			}
		}

		// 수평 반직선에 꼭짓점이 딱 걸리지 않도록 아주 조금 비켜서 쏜다.
		// 반직선이 만나는 변을 가까운 순으로 한 번에 모아 차례로 본다 (섬·다른 도형이 많아도 한 번의 훑기)
		double ry = py + eps * 3.71;
		var excluded = new HashSet<int>();
		List<(double X, double Y)>? outer = null;
		int outerComp = -1;
		foreach (int he in graph.RayHits(px, ry))
		{
			int comp = graph.ComponentOfHalfEdge(he);
			if (excluded.Contains(comp))
			{
				continue;
			}
			var loop = graph.WalkFace(he);
			if (loop == null)
			{
				excluded.Add(comp);
				continue;
			}
			double area = SignedArea(loop);
			if (area > 0 && PointInPolygon(px, py, loop))
			{
				outer = loop;
				outerComp = comp;
				break;
			}
			// 바깥쪽을 돈 경우: 이 연결 요소는 점 밖의 섬 → 무시하고 더 멀리
			excluded.Add(comp);
		}
		if (outer == null)
		{
			return null; // 둘러싼 경계 없음
		}

		var loops = new List<double[]> { Flatten(outer) };
		if (style == HatchStyleType.Ignore)
		{
			return loops;
		}

		// 고립영역: 바깥 경계 안에 있고 바깥 경계와 연결되지 않은 연결 요소의 외곽선
		var islands = new List<List<(double X, double Y)>>();
		foreach (int comp in graph.Components())
		{
			if (comp == outerComp)
			{
				continue;
			}
			var (vx, vy) = graph.AnyVertexOf(comp);
			if (!PointInPolygon(vx, vy, outer))
			{
				continue;
			}
			var hull = graph.OuterBoundaryOf(comp, eps);
			if (hull != null && hull.Count >= 3)
			{
				islands.Add(hull);
			}
		}

		// 섬 안에 있는 점은 해치하지 않는다: 클릭한 점을 품은 섬이 있으면 그 섬은 바깥 경계가 되어야 했으므로 제외
		islands.RemoveAll(isl => PointInPolygon(px, py, isl));

		if (style == HatchStyleType.Outer)
		{
			// 외부: 다른 섬 안에 들어 있지 않은 가장 바깥 섬만
			islands = islands
				.Where(a => !islands.Any(b => !ReferenceEquals(a, b) && PointInPolygon(a[0].X, a[0].Y, b) && Math.Abs(SignedArea(b)) > Math.Abs(SignedArea(a))))
				.ToList();
		}
		loops.AddRange(islands.Select(Flatten));
		return loops;
	}

	private static List<Seg> CollectSegments(Scene scene, Bounds? window)
	{
		var segs = new List<Seg>();
		foreach (var pl in scene.Polylines)
		{
			// 해치 무늬·치수·문자 획은 경계로 쓰지 않는다 (SHX 글자 획이 수만 개라 느려지고 경계를 가렸다)
			if (pl.IsText || pl.Source is Hatch or Dimension or TextEntity or MText)
			{
				continue;
			}
			if (window is { } w && !w.Intersects(pl.Bounds))
			{
				continue;
			}
			double[] p = pl.Points;
			int n = p.Length / 2;
			for (int i = 0; i + 1 < n; i++)
			{
				AddSeg(segs, p[2 * i], p[2 * i + 1], p[2 * i + 2], p[2 * i + 3]);
			}
			if (pl.Closed && n > 2)
			{
				AddSeg(segs, p[2 * n - 2], p[2 * n - 1], p[0], p[1]);
			}
		}
		return segs;
	}

	private static void AddSeg(List<Seg> segs, double x1, double y1, double x2, double y2)
	{
		if (x1 != x2 || y1 != y2)
		{
			segs.Add(new Seg(x1, y1, x2, y2));
		}
	}

	private static double[] Flatten(List<(double X, double Y)> loop)
	{
		var r = new double[loop.Count * 2];
		for (int i = 0; i < loop.Count; i++)
		{
			r[2 * i] = loop[i].X;
			r[2 * i + 1] = loop[i].Y;
		}
		return r;
	}

	internal static double SignedArea(List<(double X, double Y)> poly)
	{
		double a = 0;
		for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
		{
			a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
		}
		return a / 2;
	}

	internal static bool PointInPolygon(double px, double py, List<(double X, double Y)> poly)
	{
		bool inside = false;
		for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
		{
			var (xi, yi) = poly[i];
			var (xj, yj) = poly[j];
			if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi)
			{
				inside = !inside;
			}
		}
		return inside;
	}

	/// <summary>교차점에서 나눈 선분들로 만든 평면 그래프 (반변 구조).</summary>
	private sealed class PlanarGraph
	{
		private readonly double _eps;
		private readonly Dictionary<(long, long), int> _vertexKey = new();
		private readonly List<(double X, double Y)> _v = new();

		// 반변: 2k와 2k+1이 서로 짝(twin)
		private readonly List<int> _from = new();
		private readonly List<int> _to = new();
		private readonly List<bool> _alive = new();
		private List<int>[] _out = Array.Empty<List<int>>();
		private int[] _comp = Array.Empty<int>();

		public PlanarGraph(double eps) => this._eps = eps;

		public void Build(List<Seg> segs)
		{
			var cuts = new List<double>[segs.Count];
			for (int i = 0; i < segs.Count; i++)
			{
				cuts[i] = new List<double> { 0.0, 1.0 };
			}

			// 격자로 가까운 선분끼리만 교차 검사.
			// 칸 크기는 선분 길이(중앙값)에 맞춘다: 개수로 정하면 작은 호 조각이 몰린 칸에 수천 개가 들어가 수십 초씩 걸렸다
			double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
			var lengths = new double[segs.Count];
			for (int i = 0; i < segs.Count; i++)
			{
				var s = segs[i];
				minX = Math.Min(minX, Math.Min(s.X1, s.X2));
				minY = Math.Min(minY, Math.Min(s.Y1, s.Y2));
				maxX = Math.Max(maxX, Math.Max(s.X1, s.X2));
				maxY = Math.Max(maxY, Math.Max(s.Y1, s.Y2));
				lengths[i] = Math.Max(Math.Abs(s.X2 - s.X1), Math.Abs(s.Y2 - s.Y1));
			}
			Array.Sort(lengths);
			double cell = Math.Max(lengths[lengths.Length / 2] * 2, this._eps * 10);
			// 칸 수가 너무 많아지지 않게 (긴 선이 칸 수만큼 등록되므로)
			double span = Math.Max(maxX - minX, maxY - minY);
			cell = Math.Max(cell, span / 2000);

			var boxes = new (int X0, int Y0, int X1, int Y1)[segs.Count];
			var cells = new Dictionary<long, List<int>>();
			for (int i = 0; i < segs.Count; i++)
			{
				var s = segs[i];
				int cx0 = (int)Math.Floor((Math.Min(s.X1, s.X2) - minX - this._eps) / cell);
				int cx1 = (int)Math.Floor((Math.Max(s.X1, s.X2) - minX + this._eps) / cell);
				int cy0 = (int)Math.Floor((Math.Min(s.Y1, s.Y2) - minY - this._eps) / cell);
				int cy1 = (int)Math.Floor((Math.Max(s.Y1, s.Y2) - minY + this._eps) / cell);
				boxes[i] = (cx0, cy0, cx1, cy1);
				for (int cx = cx0; cx <= cx1; cx++)
				{
					for (int cy = cy0; cy <= cy1; cy++)
					{
						long key = ((long)cx << 32) | (uint)cy;
						if (!cells.TryGetValue(key, out var list))
						{
							cells[key] = list = new List<int>();
						}
						list.Add(i);
					}
				}
			}

			foreach (var (key, list) in cells)
			{
				int kx = (int)(key >> 32), ky = (int)(key & 0xFFFFFFFF);
				for (int a = 0; a < list.Count; a++)
				{
					int i = list[a];
					var bi = boxes[i];
					for (int b = a + 1; b < list.Count; b++)
					{
						int j = list[b];
						var bj = boxes[j];
						// 두 선분이 같이 걸친 칸 중 왼쪽 아래 칸에서만 검사 (중복 검사 방지, 해시 집합 없이)
						if (Math.Max(bi.X0, bj.X0) != kx || Math.Max(bi.Y0, bj.Y0) != ky)
						{
							continue;
						}
						this.Intersect(segs[i], segs[j], cuts[i], cuts[j]);
					}
				}
			}

			// 나눈 조각을 변으로 추가 (같은 변 중복 제거)
			var seen = new HashSet<(int, int)>();
			for (int i = 0; i < segs.Count; i++)
			{
				var s = segs[i];
				var ts = cuts[i];
				ts.Sort();
				int prev = -1;
				double prevT = -1;
				foreach (double t in ts)
				{
					if (prev >= 0 && t - prevT < 1e-12)
					{
						continue;
					}
					int v = this.Vertex(s.X1 + (s.X2 - s.X1) * t, s.Y1 + (s.Y2 - s.Y1) * t);
					if (prev >= 0 && v != prev)
					{
						var k = prev < v ? (prev, v) : (v, prev);
						if (seen.Add(k))
						{
							this._from.Add(prev); this._to.Add(v); this._alive.Add(true);
							this._from.Add(v); this._to.Add(prev); this._alive.Add(true);
						}
					}
					prev = v;
					prevT = t;
				}
			}
		}

		private void Intersect(Seg p, Seg q, List<double> tp, List<double> tq)
		{
			double e = this._eps;
			if (Math.Max(p.X1, p.X2) < Math.Min(q.X1, q.X2) - e || Math.Max(q.X1, q.X2) < Math.Min(p.X1, p.X2) - e
				|| Math.Max(p.Y1, p.Y2) < Math.Min(q.Y1, q.Y2) - e || Math.Max(q.Y1, q.Y2) < Math.Min(p.Y1, p.Y2) - e)
			{
				return;
			}
			double rx = p.X2 - p.X1, ry = p.Y2 - p.Y1;
			double sx = q.X2 - q.X1, sy = q.Y2 - q.Y1;
			double denom = rx * sy - ry * sx;
			double lp = Math.Sqrt(rx * rx + ry * ry), lq = Math.Sqrt(sx * sx + sy * sy);
			double ep = this._eps / Math.Max(lp, 1e-300), eq = this._eps / Math.Max(lq, 1e-300);
			double qpx = q.X1 - p.X1, qpy = q.Y1 - p.Y1;

			if (Math.Abs(denom) > 1e-12 * lp * lq)
			{
				double t = (qpx * sy - qpy * sx) / denom;
				double u = (qpx * ry - qpy * rx) / denom;
				if (t >= -ep && t <= 1 + ep && u >= -eq && u <= 1 + eq)
				{
					tp.Add(Math.Clamp(t, 0, 1));
					tq.Add(Math.Clamp(u, 0, 1));
				}
				return;
			}

			// 평행: 같은 직선 위에서 겹치면 서로의 끝점에서 나눈다
			double dist = Math.Abs(qpx * ry - qpy * rx) / Math.Max(lp, 1e-300);
			if (dist > this._eps)
			{
				return;
			}
			double rr = rx * rx + ry * ry, ss = sx * sx + sy * sy;
			double t0 = (qpx * rx + qpy * ry) / rr;
			double t1 = ((q.X2 - p.X1) * rx + (q.Y2 - p.Y1) * ry) / rr;
			if (t0 > ep && t0 < 1 - ep) tp.Add(t0);
			if (t1 > ep && t1 < 1 - ep) tp.Add(t1);
			double u0 = ((p.X1 - q.X1) * sx + (p.Y1 - q.Y1) * sy) / ss;
			double u1 = ((p.X2 - q.X1) * sx + (p.Y2 - q.Y1) * sy) / ss;
			if (u0 > eq && u0 < 1 - eq) tq.Add(u0);
			if (u1 > eq && u1 < 1 - eq) tq.Add(u1);
		}

		private int Vertex(double x, double y)
		{
			var key = ((long)Math.Round(x / this._eps), (long)Math.Round(y / this._eps));
			if (!this._vertexKey.TryGetValue(key, out int id))
			{
				id = this._v.Count;
				this._v.Add((x, y));
				this._vertexKey[key] = id;
			}
			return id;
		}

		/// <summary>한쪽 끝이 매달린 변(막다른 선)을 반복해서 지운다. 면을 따라갈 때 가시가 생기지 않도록.</summary>
		public void PruneDangling()
		{
			var degree = new int[this._v.Count];
			for (int e = 0; e < this._from.Count; e += 2)
			{
				degree[this._from[e]]++;
				degree[this._to[e]]++;
			}
			var adj = new List<int>[this._v.Count];
			for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
			for (int e = 0; e < this._from.Count; e += 2)
			{
				adj[this._from[e]].Add(e);
				adj[this._to[e]].Add(e);
			}
			var stack = new Stack<int>(Enumerable.Range(0, this._v.Count).Where(v => degree[v] == 1));
			while (stack.Count > 0)
			{
				int v = stack.Pop();
				if (degree[v] != 1) continue;
				foreach (int e in adj[v])
				{
					if (!this._alive[e]) continue;
					this._alive[e] = false;
					this._alive[e + 1] = false;
					int other = this._from[e] == v ? this._to[e] : this._from[e];
					degree[v]--;
					degree[other]--;
					if (degree[other] == 1) stack.Push(other);
					break;
				}
			}
		}

		public void SortAdjacency()
		{
			this._out = new List<int>[this._v.Count];
			for (int i = 0; i < this._out.Length; i++) this._out[i] = new List<int>();
			for (int e = 0; e < this._from.Count; e++)
			{
				if (this._alive[e]) this._out[this._from[e]].Add(e);
			}
			foreach (var list in this._out)
			{
				list.Sort((a, b) => this.Angle(a).CompareTo(this.Angle(b)));
			}
		}

		private double Angle(int he)
		{
			var (x1, y1) = this._v[this._from[he]];
			var (x2, y2) = this._v[this._to[he]];
			return Math.Atan2(y2 - y1, x2 - x1);
		}

		public void ComputeComponents()
		{
			this._comp = Enumerable.Repeat(-1, this._v.Count).ToArray();
			int c = 0;
			for (int s = 0; s < this._v.Count; s++)
			{
				if (this._comp[s] >= 0 || this._out[s].Count == 0) continue;
				var stack = new Stack<int>();
				stack.Push(s);
				this._comp[s] = c;
				while (stack.Count > 0)
				{
					int v = stack.Pop();
					foreach (int he in this._out[v])
					{
						int w = this._to[he];
						if (this._comp[w] < 0)
						{
							this._comp[w] = c;
							stack.Push(w);
						}
					}
				}
				c++;
			}
		}

		public int ComponentOfHalfEdge(int he) => this._comp[this._from[he]];

		public IEnumerable<int> Components() => this._comp.Where(c => c >= 0).Distinct();

		public (double X, double Y) AnyVertexOf(int comp) => this._v[Array.IndexOf(this._comp, comp)];

		/// <summary>
		/// 점에서 +X 방향 반직선이 처음 만나는 변. 점이 왼쪽에 오도록 방향을 정해 반변 번호를 돌려준다.
		/// </summary>
		public int RayHit(double px, double py, HashSet<int> excluded, int onlyComponent)
		{
			int best = -1;
			double bestX = double.MaxValue;
			for (int e = 0; e < this._from.Count; e += 2)
			{
				if (!this._alive[e]) continue;
				int c = this._comp[this._from[e]];
				if (excluded.Contains(c) || (onlyComponent >= 0 && c != onlyComponent)) continue;
				var (x1, y1) = this._v[this._from[e]];
				var (x2, y2) = this._v[this._to[e]];
				if ((y1 > py) == (y2 > py)) continue;
				double x = x1 + (py - y1) * (x2 - x1) / (y2 - y1);
				if (x > px && x < bestX)
				{
					bestX = x;
					best = e;
				}
			}
			if (best < 0) return -1;
			var (ax, ay) = this._v[this._from[best]];
			var (bx, by) = this._v[this._to[best]];
			double cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
			return cross > 0 ? best : best ^ 1;
		}

		/// <summary>
		/// 점에서 +X 방향 반직선이 만나는 변들을 가까운 순으로. 점이 왼쪽에 오도록 방향을 정한 반변 번호.
		/// </summary>
		public IEnumerable<int> RayHits(double px, double py)
		{
			var hits = new List<(double X, int He)>();
			for (int e = 0; e < this._from.Count; e += 2)
			{
				if (!this._alive[e]) continue;
				var (x1, y1) = this._v[this._from[e]];
				var (x2, y2) = this._v[this._to[e]];
				if ((y1 > py) == (y2 > py)) continue;
				double x = x1 + (py - y1) * (x2 - x1) / (y2 - y1);
				if (x <= px) continue;
				double cross = (x2 - x1) * (py - y1) - (y2 - y1) * (px - x1);
				hits.Add((x, cross > 0 ? e : e ^ 1));
			}
			hits.Sort((a, b) => a.X.CompareTo(b.X));
			return hits.Select(h => h.He);
		}

		/// <summary>반변의 왼쪽 면을 한 바퀴 따라간다.</summary>
		public List<(double X, double Y)>? WalkFace(int start)
		{
			var pts = new List<(double X, double Y)>();
			int he = start;
			int limit = this._from.Count + 2;
			for (int i = 0; i < limit; i++)
			{
				pts.Add(this._v[this._from[he]]);
				int v = this._to[he];
				var outs = this._out[v];
				int idx = outs.IndexOf(he ^ 1);
				if (idx < 0) return null;
				he = outs[(idx - 1 + outs.Count) % outs.Count];
				if (he == start) return pts;
			}
			return null;
		}

		/// <summary>연결 요소의 바깥 윤곽: 가장 왼쪽 꼭짓점 바로 왼쪽에서 반직선을 쏴서 바깥 면을 따라간다.</summary>
		public List<(double X, double Y)>? OuterBoundaryOf(int comp, double eps)
		{
			int minV = -1;
			for (int v = 0; v < this._v.Count; v++)
			{
				if (this._comp[v] != comp || this._out[v].Count == 0) continue;
				if (minV < 0 || this._v[v].X < this._v[minV].X) minV = v;
			}
			if (minV < 0) return null;
			var (x, y) = this._v[minV];
			foreach (double dy in new[] { eps * 5.3, -eps * 5.3, eps * 50.7, -eps * 50.7 })
			{
				int he = this.RayHit(x - eps * 100, y + dy, new HashSet<int>(), comp);
				if (he < 0) continue;
				var loop = this.WalkFace(he);
				if (loop != null && loop.Count >= 3)
				{
					return loop;
				}
			}
			return null;
		}
	}
}
