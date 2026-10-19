using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 자르기·연장 계산 (TRIM/EXTEND 공용). 선·호·원·폴리선(호 조각 포함)을 지원한다.
/// </summary>
public static class TrimExtend
{
	/// <summary>
	/// 클릭한 쪽을 경계 사이에서 잘라 낸다. 반환: (지울 객체, 새 객체들). 자를 수 없으면 null.
	/// 경계와 만나지 않으면 객체 전체가 클릭 구간이므로 지운다(기존 CAD 빠른 모드와 같음).
	/// </summary>
	public static (Entity Remove, List<Entity> Add)? Trim(Entity target, IEnumerable<Entity> edges, XY click)
	{
		if (target is not (Line or Arc or Circle or LwPolyline))
		{
			return null;
		}
		var segs = Geom.SegmentsOf(target);
		if (segs.Count == 0)
		{
			return null;
		}
		double n = segs.Count;
		var cuts = new List<double>();
		foreach (var edge in edges)
		{
			if (ReferenceEquals(edge, target)) continue;
			foreach (var p in Geom.Intersections(target, edge))
			{
				cuts.Add(Geom.ParamOf(segs, p));
			}
		}
		// 폴리선이 자기 자신과 만나는 점도 자르는 점
		if (target is LwPolyline && segs.Count > 2)
		{
			for (int i = 0; i < segs.Count; i++)
				for (int j = i + 2; j < segs.Count; j++)
				{
					if (Geom.IsClosed(target) && i == 0 && j == segs.Count - 1) continue;
					foreach (var p in Geom.Intersect(segs[i], segs[j]))
					{
						cuts.Add(Geom.ParamOf(segs, p));
					}
				}
		}
		double pc = Geom.ParamOf(segs, click);
		bool closed = Geom.IsClosed(target);
		const double e = 1e-7;

		if (!closed)
		{
			var inner = cuts.Where(c => c > e && c < n - e).Distinct().OrderBy(c => c).ToList();
			double lower = inner.Where(c => c < pc).DefaultIfEmpty(0).Max();
			double upper = inner.Where(c => c > pc).DefaultIfEmpty(n).Min();
			var add = new List<Entity>();
			if (lower > e && Geom.ToEntity(Geom.SubRange(segs, 0, lower), target) is { } first) add.Add(first);
			if (upper < n - e && Geom.ToEntity(Geom.SubRange(segs, upper, n), target) is { } second) add.Add(second);
			return (target, add);
		}

		// 닫힌 객체: 자르는 점이 둘 이상이어야 한다. 클릭 구간을 빼고 나머지(한 덩어리)를 남긴다
		var cyc = cuts.Select(c => ((c % n) + n) % n).OrderBy(c => c).ToList();
		var uniq = new List<double>();
		foreach (double c in cyc)
		{
			if (uniq.Count == 0 || c - uniq[^1] > e) uniq.Add(c);
		}
		if (uniq.Count > 1 && n - uniq[^1] + uniq[0] < e) uniq.RemoveAt(uniq.Count - 1);
		if (uniq.Count < 2)
		{
			return uniq.Count == 0 ? (target, new List<Entity>()) : null;
		}
		double lo = uniq.Where(c => c < pc).DefaultIfEmpty(uniq[^1] - n).Max();
		double hi = uniq.Where(c => c > pc).DefaultIfEmpty(uniq[0] + n).Min();
		// 남길 구간: hi → lo + n (한 바퀴 돌아서)
		var keep = new List<Geom.Seg>();
		double from = hi, to = lo + n;
		if (from >= n) { from -= n; to -= n; }
		if (to <= n)
		{
			keep.AddRange(Geom.SubRange(segs, from, to));
		}
		else
		{
			keep.AddRange(Geom.SubRange(segs, from, n));
			keep.AddRange(Geom.SubRange(segs, 0, to - n));
		}
		var kept = Geom.ToEntity(keep, target is Circle ? new Arc() { Layer = target.Layer } : target);
		if (kept != null) Geom.CopyProps(target, kept);
		return (target, kept != null ? new List<Entity> { kept } : new List<Entity>());
	}

	/// <summary>클릭한 쪽 끝을 가장 가까운 경계까지 늘인다. 반환: 새 객체 (바꿀 수 없으면 null).</summary>
	public static Entity? Extend(Entity target, IEnumerable<Entity> edges, XY click)
	{
		var boundary = edges.Where(b => !ReferenceEquals(b, target)).SelectMany(Geom.SegmentsOf).ToList();
		if (boundary.Count == 0) return null;
		switch (target)
		{
			case Line l:
				{
					var a = Geom.P(l.StartPoint);
					var b = Geom.P(l.EndPoint);
					bool atEnd = Geom.Dist(click, b) <= Geom.Dist(click, a);
					var (far, near) = atEnd ? (a, b) : (b, a);
					double best = double.MaxValue;
					foreach (var s in boundary)
						foreach (double t in Geom.RayHits(far, near, s))
							if (t > 1 + 1e-7 && t < best) best = t;
					if (best == double.MaxValue) return null;
					var np = new XY(far.X + (near.X - far.X) * best, far.Y + (near.Y - far.Y) * best);
					var nl = (Line)l.Clone();
					if (atEnd) nl.EndPoint = new XYZ(np.X, np.Y, l.EndPoint.Z);
					else nl.StartPoint = new XYZ(np.X, np.Y, l.StartPoint.Z);
					return nl;
				}
			case Arc arc:
				{
					var c = Geom.P(arc.Center);
					var ps = new XY(c.X + arc.Radius * Math.Cos(arc.StartAngle), c.Y + arc.Radius * Math.Sin(arc.StartAngle));
					var pe = new XY(c.X + arc.Radius * Math.Cos(arc.EndAngle), c.Y + arc.Radius * Math.Sin(arc.EndAngle));
					bool atEnd = Geom.Dist(click, pe) <= Geom.Dist(click, ps);
					double sweep = Geom.Norm(arc.EndAngle - arc.StartAngle);
					double best = double.MaxValue;
					foreach (var s in boundary)
						foreach (double ang in Geom.CircleHits(c, arc.Radius, s))
						{
							// 끝 쪽: 끝각에서 반시계로, 시작 쪽: 시작각에서 시계로 얼마나 더 가는지
							double ext = atEnd ? Geom.Norm(ang - arc.EndAngle) : Geom.Norm(arc.StartAngle - ang);
							if (ext > 1e-7 && ext < 2 * Math.PI - sweep - 1e-7 && ext < best) best = ext;
						}
					if (best == double.MaxValue) return null;
					var na = (Arc)arc.Clone();
					if (atEnd) na.EndAngle = Geom.Norm(arc.EndAngle + best);
					else na.StartAngle = Geom.Norm(arc.StartAngle - best);
					return na;
				}
			case LwPolyline pl when !pl.IsClosed && pl.Vertices.Count >= 2:
				{
					var v = pl.Vertices;
					int nv = v.Count;
					var first = new XY(v[0].Location.X, v[0].Location.Y);
					var last = new XY(v[nv - 1].Location.X, v[nv - 1].Location.Y);
					bool atEnd = Geom.Dist(click, last) <= Geom.Dist(click, first);
					int i0 = atEnd ? nv - 2 : 1, i1 = atEnd ? nv - 1 : 0; // i0 → i1 이 늘일 끝 조각
					var a = new XY(v[i0].Location.X, v[i0].Location.Y);
					var b = new XY(v[i1].Location.X, v[i1].Location.Y);
					double bulge = atEnd ? v[i0].Bulge : -v[0].Bulge;
					if (Math.Abs(bulge) > 1e-12)
					{
						// 호 조각 끝 늘이기: 같은 원 위에서
						var seg = (Geom.ArcSeg)Geom.PolySeg(a, b, bulge);
						double endAng = seg.Start + seg.Sweep;
						double best = double.MaxValue;
						foreach (var s in boundary)
							foreach (double ang in Geom.CircleHits(seg.C, seg.R, s))
							{
								double ext = seg.Sweep >= 0 ? Geom.Norm(ang - endAng) : Geom.Norm(endAng - ang);
								if (ext > 1e-7 && ext < 2 * Math.PI - Math.Abs(seg.Sweep) - 1e-7 && ext < best) best = ext;
							}
						if (best == double.MaxValue) return null;
						double newSweep = seg.Sweep + Math.Sign(seg.Sweep) * best;
						var np = new XY(seg.C.X + seg.R * Math.Cos(seg.Start + newSweep), seg.C.Y + seg.R * Math.Sin(seg.Start + newSweep));
						var npl = (LwPolyline)pl.Clone();
						npl.Vertices[i1] = new LwPolyline.Vertex(np) { Bulge = npl.Vertices[i1].Bulge };
						double nb = Math.Tan(newSweep / 4);
						if (atEnd) npl.Vertices[i0] = new LwPolyline.Vertex(npl.Vertices[i0].Location) { Bulge = nb };
						else npl.Vertices[0] = new LwPolyline.Vertex(np) { Bulge = -nb };
						return npl;
					}
					else
					{
						double best = double.MaxValue;
						foreach (var s in boundary)
							foreach (double t in Geom.RayHits(a, b, s))
								if (t > 1 + 1e-7 && t < best) best = t;
						if (best == double.MaxValue) return null;
						var np = new XY(a.X + (b.X - a.X) * best, a.Y + (b.Y - a.Y) * best);
						var npl = (LwPolyline)pl.Clone();
						npl.Vertices[i1] = new LwPolyline.Vertex(np) { Bulge = npl.Vertices[i1].Bulge };
						return npl;
					}
				}
		}
		return null;
	}
}

/// <summary>
/// TRIM·EXTEND 공통 (기존 CAD와 같은 문구·옵션): 경계 선택 (Enter = 모든 객체) →
/// "[모서리모드(E)/울타리(F)/투영(P)/취소(U)]/&lt; 자르기 할 객체 선택 또는 shift를 눌러 연장할 객체 선택&gt;:".
/// 모서리모드(E): "[Extend/No extend] &lt;No extend&gt;:" (Extend면 경계를 늘린 선·원으로 봄),
/// 울타리(F): "울타리의 첫번째 점:" → "다음 울타리 점:" … Enter → 울타리와 만나는 객체를 모두 자름(연장).
/// </summary>
public abstract class TrimExtendCommandBase : EditorCommand
{
	private static bool _edgeExtend;

	private int _step; // 0 경계, 1 고르기, 2 모서리 모드, 3 울타리, 4 투영
	private readonly List<Entity> _edges = new();
	private readonly List<XY> _fence = new();
	private bool _all;

	/// <summary>true = TRIM (클릭 = 자르기, Shift = 연장), false = EXTEND.</summary>
	protected abstract bool IsTrim { get; }

	public override IEnumerable<Entity> Highlighted => this._all ? Array.Empty<Entity>() : this._edges.ToArray();

	public override bool PickingObject => !this.IsFinished && this._step is 0 or 1;

	public override bool WantsSelection => this._step == 0;

	private string EdgePrompt => this.IsTrim ? "자르기 위한 절단 객체 선택 <모두 선택하려면 ENTER>:" : "연장할 경계 객체 선택 <모두 선택하려면 ENTER>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._edges.Clear();
		this._all = false;
		if (ctx.Selection.Count > 0)
		{
			this._edges.AddRange(ctx.Selection);
			this.ToPick();
			return;
		}
		this._step = 0;
		this.Prompt = this.EdgePrompt;
	}

	private void ToPick()
	{
		this._step = 1;
		this.Prompt = this.IsTrim
			? "[모서리모드(E)/울타리(F)/투영(P)/취소(U)]/< 자르기 할 객체 선택 또는 shift를 눌러 연장할 객체 선택>:"
			: "[모서리모드(E)/울타리(F)/투영(P)/취소(U)]/< 연장 할 객체 선택 또는 shift를 눌러 자르기할 객체 선택>:";
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._step != 0) return;
		foreach (var e in entities)
		{
			if (!this._edges.Contains(e)) this._edges.Add(e);
		}
		this.Ctx?.Print($"세트 내의 객체: {this._edges.Count}");
		this.Prompt = this.EdgePrompt;
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 0:
				this._all = this._edges.Count == 0;
				this.ToPick();
				return;
			case 2:
			case 4:
				this.ToPick();
				return;
			case 3:
				this.RunFence();
				this.ToPick();
				return;
		}
		this.Finish();
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		switch (this._step)
		{
			case 1 when t == "U":
				this.Ctx.Doc.UndoLastStep();
				return;
			case 1 when t == "E":
				this._step = 2;
				this.Prompt = $"[Extend/No extend] <{(_edgeExtend ? "Extend" : "No extend")}>:";
				return;
			case 1 when t == "F":
				this._fence.Clear();
				this._step = 3;
				this.Prompt = "울타리의 첫번째 점:";
				return;
			case 1 when t == "P":
				this._step = 4;
				this.Prompt = "투영(P): [없음(N)/UCS(U)/뷰(V)] <UCS>:";
				return;
			case 2:
				if (t.StartsWith("E")) _edgeExtend = true;
				else if (t.StartsWith("N")) _edgeExtend = false;
				this.ToPick();
				return;
			case 4:
				this.ToPick(); // 2D 도면에서는 투영 방식이 결과에 영향 없음
				return;
		}
	}

	/// <summary>경계 목록. 모서리 모드가 Extend면 선은 길게 늘인 선, 호는 온전한 원으로.</summary>
	private List<Entity> Edges()
	{
		var list = this._all && this.Ctx != null ? this.Ctx.Doc.CurrentSpace.Entities.ToList() : this._edges.ToList();
		if (!_edgeExtend)
		{
			return list;
		}
		const double far = 1e7;
		return list.Select<Entity, Entity>(e => e switch
		{
			Line l when Geom.Dist(Geom.P(l.StartPoint), Geom.P(l.EndPoint)) > 1e-12 => ExtendLine(l, far),
			Arc a => new Circle { Center = a.Center, Radius = a.Radius },
			_ => e,
		}).ToList();
	}

	private static Line ExtendLine(Line l, double far)
	{
		double dx = l.EndPoint.X - l.StartPoint.X, dy = l.EndPoint.Y - l.StartPoint.Y;
		double len = Math.Sqrt(dx * dx + dy * dy);
		dx = dx / len * far;
		dy = dy / len * far;
		return new Line(new XYZ(l.StartPoint.X - dx, l.StartPoint.Y - dy, 0), new XYZ(l.EndPoint.X + dx, l.EndPoint.Y + dy, 0));
	}

	private bool Apply(Entity picked, XY click, bool trim, bool quiet)
	{
		var doc = this.Ctx!.Doc;
		if (trim)
		{
			var result = TrimExtend.Trim(picked, this.Edges(), click);
			if (result == null)
			{
				if (!quiet) this.Ctx.Print("이 객체는 자를 수 없습니다.");
				return false;
			}
			var (remove, add) = result.Value;
			doc.Replace(new[] { remove }, add, "자르기");
			int idx = this._edges.IndexOf(remove);
			if (idx >= 0)
			{
				this._edges.RemoveAt(idx);
				this._edges.AddRange(add);
			}
			return true;
		}
		if (TrimExtend.Extend(picked, this.Edges(), click) is { } ext)
		{
			doc.Replace(new[] { picked }, new[] { ext }, "연장");
			int idx = this._edges.IndexOf(picked);
			if (idx >= 0) this._edges[idx] = ext;
			return true;
		}
		if (!quiet) this.Ctx.Print("객체가 경계와 만나지 않습니다.");
		return false;
	}

	/// <summary>울타리와 만나는 객체마다 만난 점을 클릭한 것처럼 자르기(연장).</summary>
	private void RunFence()
	{
		if (this.Ctx == null || this._fence.Count < 2) return;
		var fenceSegs = new List<Geom.Seg>();
		for (int i = 0; i + 1 < this._fence.Count; i++)
		{
			fenceSegs.Add(new Geom.LineSeg(this._fence[i], this._fence[i + 1]));
		}
		var targets = this.Ctx.Doc.CurrentSpace.Entities.Where(e => e is Line or Arc or Circle or LwPolyline).ToList();
		foreach (var e in targets)
		{
			XY? hit = null;
			foreach (var s in Geom.SegmentsOf(e))
			{
				foreach (var f in fenceSegs)
				{
					foreach (var p in Geom.Intersect(s, f))
					{
						hit ??= p;
					}
				}
			}
			if (hit is { } h)
			{
				this.Apply(e, h, this.IsTrim, quiet: true);
			}
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._step == 3)
		{
			this._fence.Add(new XY(x, y));
			this.Ctx.LastPoint = (x, y);
			this.Prompt = "다음 울타리 점:";
			return;
		}
		if (this._step != 1) return;
		var picked = this.Ctx.PickAt(x, y);
		if (picked == null) return;
		bool trim = this.IsTrim != this.Ctx.ShiftPressed;
		this.Apply(picked, new XY(x, y), trim, quiet: false);
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 3 || this._fence.Count == 0) yield break;
		for (int i = 0; i + 1 < this._fence.Count; i++)
		{
			yield return new Line(new XYZ(this._fence[i].X, this._fence[i].Y, 0), new XYZ(this._fence[i + 1].X, this._fence[i + 1].Y, 0));
		}
		var last = this._fence[^1];
		yield return new Line(new XYZ(last.X, last.Y, 0), new XYZ(cursorX, cursorY, 0));
	}
}

/// <summary>TRIM: 클릭 = 자르기, Shift+클릭 = 연장.</summary>
public sealed class TrimCommand : TrimExtendCommandBase
{
	public override string Name => "TRIM";

	public override string[] Aliases => new[] { "TR" };

	protected override bool IsTrim => true;
}

/// <summary>EXTEND: 클릭 = 연장, Shift+클릭 = 자르기.</summary>
public sealed class ExtendCommand : TrimExtendCommandBase
{
	public override string Name => "EXTEND";

	public override string[] Aliases => new[] { "EX" };

	protected override bool IsTrim => false;
}
