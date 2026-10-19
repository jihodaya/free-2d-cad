using ACadSharp.Entities;
using Free2DCad.Rendering;

namespace Free2DCad.Core;

/// <summary>
/// 모델스페이스 엔티티에 속한 평탄화 장면 항목 모음.
/// </summary>
public sealed class EntitySceneItems : IEnumerable<ScenePolyline>
{
	public List<ScenePolyline> Polylines { get; } = new();

	public List<SceneFill> Fills { get; } = new();

	public List<SceneText> Texts { get; } = new();

	public IEnumerator<ScenePolyline> GetEnumerator() => this.Polylines.GetEnumerator();

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => this.Polylines.GetEnumerator();
}

/// <summary>
/// 인덱싱된 선분 구조체.
/// </summary>
public readonly record struct IndexedSegment(
	double X0,
	double Y0,
	double X1,
	double Y1,
	Entity? Source,
	int Id);

/// <summary>
/// 장면의 기하 항목 및 스냅 점을 균일 격자로 색인하여 고속 픽 및 영역 선택을 제공하는 공간 인덱스.
/// </summary>
public sealed class SceneIndex
{
	private readonly double _cellSize;
	private readonly Dictionary<CellKey, GridCell> _cells = new();
	private readonly Dictionary<CellKey, List<SnapPoint>> _snapGrid = new();
	private readonly Dictionary<Entity, EntitySceneItems> _entityItems = new();
	private readonly List<IndexedSegment> _segments = new();
	private int[] _segmentVisitedStamps = Array.Empty<int>();
	private int _queryStamp = 0;

	private readonly struct CellKey : IEquatable<CellKey>
	{
		public readonly int X;
		public readonly int Y;

		public CellKey(int x, int y)
		{
			this.X = x;
			this.Y = y;
		}

		public bool Equals(CellKey other) => this.X == other.X && this.Y == other.Y;

		public override bool Equals(object? obj) => obj is CellKey other && this.Equals(other);

		public override int GetHashCode() => HashCode.Combine(this.X, this.Y);
	}

	private sealed class GridCell
	{
		public List<int> SegmentIndices { get; } = new();
		public List<SceneFill> Fills { get; } = new();
		public List<SceneText> Texts { get; } = new();
	}

	public SceneIndex(Scene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		double maxDim = Math.Max(scene.Extents.Width, scene.Extents.Height);
		this._cellSize = maxDim > 1e-4 ? maxDim / 256.0 : 10.0;

		// 1. 폴리선 등록 (선분 단위)
		int segId = 0;
		foreach (ScenePolyline poly in scene.Polylines)
		{
			if (poly.Source != null)
			{
				this.GetOrCreateEntityItems(poly.Source).Polylines.Add(poly);
			}
			// 글자 획은 스냅(끝점·교차점)과 선분 고르기에서 뺀다 — 문자는 둘레 상자로 고른다
			if (poly.IsText)
			{
				continue;
			}

			double[] pts = poly.Points;
			int ptCount = pts.Length / 2;
			if (ptCount < 2)
			{
				continue;
			}

			int segCount = poly.Closed ? ptCount : ptCount - 1;
			for (int i = 0; i < segCount; i++)
			{
				int i0 = i * 2;
				int i1 = ((i + 1) % ptCount) * 2;
				var seg = new IndexedSegment(pts[i0], pts[i0 + 1], pts[i1], pts[i1 + 1], poly.Source, segId++);
				this._segments.Add(seg);
				this.RegisterSegment(seg);
			}
		}

		// 와이프아웃: 테두리가 보이면(WIPEOUTFRAME) 테두리로 고를 수 있다 (기존 CAD와 같음)
		foreach (SceneWipeout w in scene.Wipeouts)
		{
			if (!w.Frame || w.Source == null || w.Loop.Length < 4)
			{
				continue;
			}
			this.GetOrCreateEntityItems(w.Source).Polylines.Add(new ScenePolyline(w.Loop, true, w.FrameColor, null, w.Bounds, w.Source));
			int n = w.Loop.Length / 2;
			for (int i = 0; i < n; i++)
			{
				int i0 = i * 2, i1 = ((i + 1) % n) * 2;
				var seg = new IndexedSegment(w.Loop[i0], w.Loop[i0 + 1], w.Loop[i1], w.Loop[i1 + 1], w.Source, segId++);
				this._segments.Add(seg);
				this.RegisterSegment(seg);
			}
		}

		this._segmentVisitedStamps = new int[this._segments.Count];

		// 2. 채움 등록 (Bounds 단위)
		foreach (SceneFill fill in scene.Fills)
		{
			if (fill.Source != null)
			{
				this.GetOrCreateEntityItems(fill.Source).Fills.Add(fill);
			}
			this.RegisterFill(fill);
		}

		// 3. 텍스트 등록 (Bounds 단위)
		foreach (SceneText text in scene.Texts)
		{
			if (text.Source != null)
			{
				this.GetOrCreateEntityItems(text.Source).Texts.Add(text);
			}
			this.RegisterText(text);
		}

		// 4. 스냅 점 등록
		foreach (SnapPoint sp in scene.SnapPoints)
		{
			var key = new CellKey(this.CellX(sp.X), this.CellY(sp.Y));
			if (!this._snapGrid.TryGetValue(key, out var list))
			{
				list = new List<SnapPoint>();
				this._snapGrid[key] = list;
			}
			list.Add(sp);
		}
	}

	/// <summary>
	/// 클릭 좌표에서 허용오차 내의 최우선 엔티티를 선택한다.
	/// </summary>
	public Entity? Pick(double x, double y, double tol)
	{
		// 1순위: 선분까지 거리가 tol 이하인 것 중 가장 가까운 것
		double bestDistSq = tol * tol;
		Entity? bestEntity = null;

		int minCx = this.CellX(x - tol);
		int maxCx = this.CellX(x + tol);
		int minCy = this.CellY(y - tol);
		int maxCy = this.CellY(y + tol);

		int stamp = this.NextQueryStamp();

		for (int cy = minCy; cy <= maxCy; cy++)
		{
			for (int cx = minCx; cx <= maxCx; cx++)
			{
				if (!this._cells.TryGetValue(new CellKey(cx, cy), out var cell))
				{
					continue;
				}

				for (int i = 0; i < cell.SegmentIndices.Count; i++)
				{
					int segIdx = cell.SegmentIndices[i];
					if (this._segmentVisitedStamps[segIdx] == stamp)
					{
						continue;
					}
					this._segmentVisitedStamps[segIdx] = stamp;

					var seg = this._segments[segIdx];
					if (seg.Source == null)
					{
						continue;
					}

					double dSq = DistanceSqToSegment(x, y, seg.X0, seg.Y0, seg.X1, seg.Y1);
					if (dSq <= bestDistSq)
					{
						bestDistSq = dSq;
						bestEntity = seg.Source;
					}
				}
			}
		}

		if (bestEntity != null)
		{
			return bestEntity;
		}

		// 2순위: 텍스트 Bounds 안에 있는 것
		int textCx = this.CellX(x);
		int textCy = this.CellY(y);
		if (this._cells.TryGetValue(new CellKey(textCx, textCy), out var textCell))
		{
			for (int i = 0; i < textCell.Texts.Count; i++)
			{
				var text = textCell.Texts[i];
				if (text.Source == null)
				{
					continue;
				}
				if (x >= text.Bounds.MinX && x <= text.Bounds.MaxX && y >= text.Bounds.MinY && y <= text.Bounds.MaxY)
				{
					return text.Source;
				}
			}
		}

		// 3순위: 채움 영역 안(짝홀 규칙)에 있는 것
		if (this._cells.TryGetValue(new CellKey(textCx, textCy), out var fillCell))
		{
			for (int i = 0; i < fillCell.Fills.Count; i++)
			{
				var fill = fillCell.Fills[i];
				if (fill.Source == null)
				{
					continue;
				}
				if (x < fill.Bounds.MinX || x > fill.Bounds.MaxX || y < fill.Bounds.MinY || y > fill.Bounds.MaxY)
				{
					continue;
				}
				if (IsPointInFill(x, y, fill))
				{
					return fill.Source;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// 윈도우(포함) 또는 크로싱(교차) 사각형으로 엔티티 목록을 선택한다.
	/// </summary>
	public IReadOnlyList<Entity> SelectWindow(Bounds rect, bool crossing)
	{
		double minX = Math.Min(rect.MinX, rect.MaxX);
		double maxX = Math.Max(rect.MinX, rect.MaxX);
		double minY = Math.Min(rect.MinY, rect.MaxY);
		double maxY = Math.Max(rect.MinY, rect.MaxY);
		var normRect = new Bounds(minX, minY, maxX, maxY);

		int minCx = this.CellX(normRect.MinX);
		int maxCx = this.CellX(normRect.MaxX);
		int minCy = this.CellY(normRect.MinY);
		int maxCy = this.CellY(normRect.MaxY);

		var result = new HashSet<Entity>();

		if (crossing)
		{
			int stamp = this.NextQueryStamp();
			for (int cy = minCy; cy <= maxCy; cy++)
			{
				for (int cx = minCx; cx <= maxCx; cx++)
				{
					if (!this._cells.TryGetValue(new CellKey(cx, cy), out var cell))
					{
						continue;
					}

					// 선분 교차 검사
					for (int i = 0; i < cell.SegmentIndices.Count; i++)
					{
						int segIdx = cell.SegmentIndices[i];
						if (this._segmentVisitedStamps[segIdx] == stamp)
						{
							continue;
						}
						this._segmentVisitedStamps[segIdx] = stamp;

						var seg = this._segments[segIdx];
						if (seg.Source == null || result.Contains(seg.Source))
						{
							continue;
						}

						if (SegmentIntersectsRect(seg.X0, seg.Y0, seg.X1, seg.Y1, normRect))
						{
							result.Add(seg.Source);
						}
					}

					// 채움 교차 검사
					for (int i = 0; i < cell.Fills.Count; i++)
					{
						var fill = cell.Fills[i];
						if (fill.Source == null || result.Contains(fill.Source))
						{
							continue;
						}
						if (fill.Bounds.Intersects(normRect))
						{
							result.Add(fill.Source);
						}
					}

					// 텍스트 교차 검사
					for (int i = 0; i < cell.Texts.Count; i++)
					{
						var text = cell.Texts[i];
						if (text.Source == null || result.Contains(text.Source))
						{
							continue;
						}
						if (text.Bounds.Intersects(normRect))
						{
							result.Add(text.Source);
						}
					}
				}
			}
		}
		else
		{
			// 윈도우 선택: 후보 엔티티 중 모든 장면 항목이 사각형 안에 완전히 들어간 것만 선택
			var candidates = new HashSet<Entity>();
			for (int cy = minCy; cy <= maxCy; cy++)
			{
				for (int cx = minCx; cx <= maxCx; cx++)
				{
					if (!this._cells.TryGetValue(new CellKey(cx, cy), out var cell))
					{
						continue;
					}

					for (int i = 0; i < cell.SegmentIndices.Count; i++)
					{
						var src = this._segments[cell.SegmentIndices[i]].Source;
						if (src != null) candidates.Add(src);
					}
					for (int i = 0; i < cell.Fills.Count; i++)
					{
						var src = cell.Fills[i].Source;
						if (src != null) candidates.Add(src);
					}
					for (int i = 0; i < cell.Texts.Count; i++)
					{
						var src = cell.Texts[i].Source;
						if (src != null) candidates.Add(src);
					}
				}
			}

			foreach (Entity candidate in candidates)
			{
				if (!this._entityItems.TryGetValue(candidate, out var items))
				{
					continue;
				}

				bool allInside = true;
				foreach (var poly in items.Polylines)
				{
					if (!IsBoundsInside(poly.Bounds, normRect))
					{
						allInside = false;
						break;
					}
				}
				if (!allInside) continue;

				foreach (var fill in items.Fills)
				{
					if (!IsBoundsInside(fill.Bounds, normRect))
					{
						allInside = false;
						break;
					}
				}
				if (!allInside) continue;

				foreach (var text in items.Texts)
				{
					if (!IsBoundsInside(text.Bounds, normRect))
					{
						allInside = false;
						break;
					}
				}
				if (allInside && (items.Polylines.Count > 0 || items.Fills.Count > 0 || items.Texts.Count > 0))
				{
					result.Add(candidate);
				}
			}
		}

		return result.ToList();
	}

	/// <summary>
	/// 해당 엔티티에서 추출된 장면 항목 목록을 반환한다.
	/// </summary>
	public EntitySceneItems ItemsOf(Entity e)
	{
		ArgumentNullException.ThrowIfNull(e);
		if (this._entityItems.TryGetValue(e, out var items))
		{
			return items;
		}
		return new EntitySceneItems();
	}

	internal void QuerySegmentsInBounds(Bounds bounds, List<IndexedSegment> results)
	{
		int minCx = this.CellX(bounds.MinX);
		int maxCx = this.CellX(bounds.MaxX);
		int minCy = this.CellY(bounds.MinY);
		int maxCy = this.CellY(bounds.MaxY);

		int stamp = this.NextQueryStamp();
		for (int cy = minCy; cy <= maxCy; cy++)
		{
			for (int cx = minCx; cx <= maxCx; cx++)
			{
				if (!this._cells.TryGetValue(new CellKey(cx, cy), out var cell))
				{
					continue;
				}

				for (int i = 0; i < cell.SegmentIndices.Count; i++)
				{
					int segIdx = cell.SegmentIndices[i];
					if (this._segmentVisitedStamps[segIdx] == stamp)
					{
						continue;
					}
					this._segmentVisitedStamps[segIdx] = stamp;
					results.Add(this._segments[segIdx]);
				}
			}
		}
	}

	internal void QuerySnapPointsInBounds(Bounds bounds, List<SnapPoint> results)
	{
		int minCx = this.CellX(bounds.MinX);
		int maxCx = this.CellX(bounds.MaxX);
		int minCy = this.CellY(bounds.MinY);
		int maxCy = this.CellY(bounds.MaxY);

		for (int cy = minCy; cy <= maxCy; cy++)
		{
			for (int cx = minCx; cx <= maxCx; cx++)
			{
				if (this._snapGrid.TryGetValue(new CellKey(cx, cy), out var list))
				{
					for (int i = 0; i < list.Count; i++)
					{
						var sp = list[i];
						if (sp.X >= bounds.MinX && sp.X <= bounds.MaxX && sp.Y >= bounds.MinY && sp.Y <= bounds.MaxY)
						{
							results.Add(sp);
						}
					}
				}
			}
		}
	}

	private int NextQueryStamp()
	{
		if (this._queryStamp == int.MaxValue)
		{
			Array.Clear(this._segmentVisitedStamps);
			this._queryStamp = 0;
		}
		return ++this._queryStamp;
	}

	private int CellX(double x) => (int)Math.Floor(x / this._cellSize);

	private int CellY(double y) => (int)Math.Floor(y / this._cellSize);

	private EntitySceneItems GetOrCreateEntityItems(Entity e)
	{
		if (!this._entityItems.TryGetValue(e, out var items))
		{
			items = new EntitySceneItems();
			this._entityItems[e] = items;
		}
		return items;
	}

	private GridCell GetOrCreateCell(int cx, int cy)
	{
		var key = new CellKey(cx, cy);
		if (!this._cells.TryGetValue(key, out var cell))
		{
			cell = new GridCell();
			this._cells[key] = cell;
		}
		return cell;
	}

	private void RegisterSegment(IndexedSegment seg)
	{
		int minCx = this.CellX(Math.Min(seg.X0, seg.X1));
		int maxCx = this.CellX(Math.Max(seg.X0, seg.X1));
		int minCy = this.CellY(Math.Min(seg.Y0, seg.Y1));
		int maxCy = this.CellY(Math.Max(seg.Y0, seg.Y1));

		// long: 긴 선(구성선·광선)이면 int 곱이 넘쳐 음수가 되어 수십억 칸을 만들던 문제 (메모리 수십 GB)
		if ((long)(maxCx - minCx + 1) * (maxCy - minCy + 1) <= 25)
		{
			for (int cy = minCy; cy <= maxCy; cy++)
			{
				for (int cx = minCx; cx <= maxCx; cx++)
				{
					this.GetOrCreateCell(cx, cy).SegmentIndices.Add(seg.Id);
				}
			}
			return;
		}

		// 긴 선분의 경우 DDA 직선 격자 순회
		int x = this.CellX(seg.X0);
		int y = this.CellY(seg.Y0);
		int endX = this.CellX(seg.X1);
		int endY = this.CellY(seg.Y1);

		int stepX = Math.Sign(endX - x);
		int stepY = Math.Sign(endY - y);
		int dx = Math.Abs(endX - x);
		int dy = Math.Abs(endY - y);
		int err = dx - dy;

		this.GetOrCreateCell(x, y).SegmentIndices.Add(seg.Id);

		while (x != endX || y != endY)
		{
			int e2 = 2 * err;
			if (e2 > -dy)
			{
				err -= dy;
				x += stepX;
				this.GetOrCreateCell(x, y).SegmentIndices.Add(seg.Id);
			}
			if (e2 < dx)
			{
				err += dx;
				y += stepY;
				this.GetOrCreateCell(x, y).SegmentIndices.Add(seg.Id);
			}
		}
	}

	private void RegisterFill(SceneFill fill)
	{
		int minCx = this.CellX(fill.Bounds.MinX);
		int maxCx = this.CellX(fill.Bounds.MaxX);
		int minCy = this.CellY(fill.Bounds.MinY);
		int maxCy = this.CellY(fill.Bounds.MaxY);

		for (int cy = minCy; cy <= maxCy; cy++)
		{
			for (int cx = minCx; cx <= maxCx; cx++)
			{
				this.GetOrCreateCell(cx, cy).Fills.Add(fill);
			}
		}
	}

	private void RegisterText(SceneText text)
	{
		int minCx = this.CellX(text.Bounds.MinX);
		int maxCx = this.CellX(text.Bounds.MaxX);
		int minCy = this.CellY(text.Bounds.MinY);
		int maxCy = this.CellY(text.Bounds.MaxY);

		for (int cy = minCy; cy <= maxCy; cy++)
		{
			for (int cx = minCx; cx <= maxCx; cx++)
			{
				this.GetOrCreateCell(cx, cy).Texts.Add(text);
			}
		}
	}

	private static bool IsBoundsInside(Bounds inner, Bounds outer)
	{
		return inner.MinX >= outer.MinX && inner.MaxX <= outer.MaxX &&
		       inner.MinY >= outer.MinY && inner.MaxY <= outer.MaxY;
	}

	private static double DistanceSqToSegment(double px, double py, double x0, double y0, double x1, double y1)
	{
		double dx = x1 - x0;
		double dy = y1 - y0;
		double lenSq = dx * dx + dy * dy;
		double closestX, closestY;
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

	private static bool SegmentIntersectsRect(double x0, double y0, double x1, double y1, Bounds rect)
	{
		if (x0 >= rect.MinX && x0 <= rect.MaxX && y0 >= rect.MinY && y0 <= rect.MaxY) return true;
		if (x1 >= rect.MinX && x1 <= rect.MaxX && y1 >= rect.MinY && y1 <= rect.MaxY) return true;

		double minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
		double minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
		if (maxX < rect.MinX || minX > rect.MaxX || maxY < rect.MinY || minY > rect.MaxY)
		{
			return false;
		}

		if (SegmentsIntersect(x0, y0, x1, y1, rect.MinX, rect.MinY, rect.MaxX, rect.MinY)) return true;
		if (SegmentsIntersect(x0, y0, x1, y1, rect.MinX, rect.MaxY, rect.MaxX, rect.MaxY)) return true;
		if (SegmentsIntersect(x0, y0, x1, y1, rect.MinX, rect.MinY, rect.MinX, rect.MaxY)) return true;
		if (SegmentsIntersect(x0, y0, x1, y1, rect.MaxX, rect.MinY, rect.MaxX, rect.MaxY)) return true;

		return false;
	}

	private static bool SegmentsIntersect(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
	{
		double d1x = x2 - x1, d1y = y2 - y1;
		double d2x = x4 - x3, d2y = y4 - y3;
		double denom = d1x * d2y - d1y * d2x;
		if (Math.Abs(denom) < 1e-12)
		{
			return false;
		}

		double dx = x3 - x1;
		double dy = y3 - y1;
		double uA = (dx * d2y - dy * d2x) / denom;
		double uB = (dx * d1y - dy * d1x) / denom;
		return uA >= 0.0 && uA <= 1.0 && uB >= 0.0 && uB <= 1.0;
	}

	private static bool IsPointInFill(double x, double y, SceneFill fill)
	{
		bool inside = false;
		for (int l = 0; l < fill.Loops.Count; l++)
		{
			double[] loop = fill.Loops[l];
			int n = loop.Length / 2;
			if (n < 3) continue;

			for (int i = 0, j = n - 1; i < n; j = i++)
			{
				double xi = loop[i * 2], yi = loop[i * 2 + 1];
				double xj = loop[j * 2], yj = loop[j * 2 + 1];

				if (((yi > y) != (yj > y)) &&
				    (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
				{
					inside = !inside;
				}
			}
		}
		return inside;
	}
}
