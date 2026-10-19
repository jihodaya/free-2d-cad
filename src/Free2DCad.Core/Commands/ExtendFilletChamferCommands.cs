using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 모깎기·모따기 공통: 고른 객체(선, 호, 원, 폴리선 조각)를 무한 직선/원으로 보고 계산한 뒤, 원래 객체를 제자리에서 자르거나 늘인다.
/// </summary>
internal static class CornerGeom
{
	/// <summary>고른 곡선. Seg = 폴리선 조각 번호 (아니면 -1).</summary>
	internal abstract record Curve(Entity Ent, XY Pick, int Seg);

	internal sealed record LineCurve(Entity Ent, XY Pick, int Seg, XY A, XY B) : Curve(Ent, Pick, Seg)
	{
		public XY Dir
		{
			get
			{
				double l = Geom.Dist(this.A, this.B);
				return l < 1e-12 ? new XY(1, 0) : new XY((this.B.X - this.A.X) / l, (this.B.Y - this.A.Y) / l);
			}
		}

		public double Param(XY p) => (p.X - this.A.X) * this.Dir.X + (p.Y - this.A.Y) * this.Dir.Y;

		public XY Foot(XY p) { double t = this.Param(p); return new XY(this.A.X + this.Dir.X * t, this.A.Y + this.Dir.Y * t); }

		public double Side(XY p) => this.Dir.X * (p.Y - this.A.Y) - this.Dir.Y * (p.X - this.A.X);
	}

	internal sealed record CircleCurve(Entity Ent, XY Pick, int Seg, XY C, double R) : Curve(Ent, Pick, Seg)
	{
		public double Side(XY p) => Geom.Dist(p, this.C) - this.R;
	}

	/// <summary>점 아래 객체를 곡선으로. 폴리선은 가장 가까운 조각.</summary>
	public static Curve? FromPick(Entity? e, XY p)
	{
		switch (e)
		{
			case Line l:
				return Geom.Dist(Geom.P(l.StartPoint), Geom.P(l.EndPoint)) < 1e-12 ? null : new LineCurve(l, p, -1, Geom.P(l.StartPoint), Geom.P(l.EndPoint));
			case Circle c: // 호 포함
				return new CircleCurve(c, p, -1, Geom.P(c.Center), c.Radius);
			case LwPolyline pl:
				{
					var segs = Geom.SegmentsOf(pl);
					if (segs.Count == 0) return null;
					int best = 0;
					double bestD = double.MaxValue;
					for (int i = 0; i < segs.Count; i++)
					{
						double d = Geom.Dist(segs[i].At(segs[i].Project(p)), p);
						if (d < bestD) { bestD = d; best = i; }
					}
					return segs[best] switch
					{
						Geom.LineSeg ls when ls.Length > 1e-12 => new LineCurve(pl, p, best, ls.A, ls.B),
						Geom.ArcSeg a => new CircleCurve(pl, p, best, a.C, a.R),
						_ => null,
					};
				}
		}
		return null;
	}

	/// <summary>두 곡선(무한 직선/전체 원)의 교점.</summary>
	public static IEnumerable<XY> Intersections(Curve a, Curve b)
	{
		switch (a, b)
		{
			case (LineCurve l1, LineCurve l2):
				if (Geom.LineLine(l1.A, l1.B, l2.A, l2.B, out double t, out _))
				{
					yield return new XY(l1.A.X + (l1.B.X - l1.A.X) * t, l1.A.Y + (l1.B.Y - l1.A.Y) * t);
				}
				break;
			case (LineCurve l, CircleCurve c):
				foreach (var h in Geom.LineCircle(l.A, l.B, c.C, c.R)) yield return h.Pt;
				break;
			case (CircleCurve c, LineCurve l):
				foreach (var h in Geom.LineCircle(l.A, l.B, c.C, c.R)) yield return h.Pt;
				break;
			case (CircleCurve c1, CircleCurve c2):
				foreach (var q in Geom.CircleCircle(c1.C, c1.R, c2.C, c2.R)) yield return q;
				break;
		}
	}

	/// <summary>곡선 위에서 점에 가장 가까운 점 (접점).</summary>
	public static XY Foot(Curve c, XY p)
	{
		if (c is LineCurve l) return l.Foot(p);
		var cc = (CircleCurve)c;
		double d = Geom.Dist(p, cc.C);
		if (d < 1e-12) return new XY(cc.C.X + cc.R, cc.C.Y);
		return new XY(cc.C.X + (p.X - cc.C.X) / d * cc.R, cc.C.Y + (p.Y - cc.C.Y) / d * cc.R);
	}

	private static double SideOf(Curve c, XY p) => c is LineCurve l ? l.Side(p) : ((CircleCurve)c).Side(p);

	/// <summary>모깎기 원의 중심 후보: 두 곡선을 r만큼 띄운 곡선끼리의 교점.</summary>
	private static IEnumerable<XY> Centers(Curve a, Curve b, double r)
	{
		IEnumerable<Curve> Offsets(Curve c)
		{
			if (c is LineCurve l)
			{
				var d = l.Dir;
				var n = new XY(-d.Y, d.X);
				foreach (int s in new[] { 1, -1 })
				{
					yield return l with { A = new XY(l.A.X + n.X * r * s, l.A.Y + n.Y * r * s), B = new XY(l.B.X + n.X * r * s, l.B.Y + n.Y * r * s) };
				}
			}
			else
			{
				var cc = (CircleCurve)c;
				yield return cc with { R = cc.R + r };
				if (Math.Abs(cc.R - r) > 1e-9) yield return cc with { R = Math.Abs(cc.R - r) };
			}
		}
		foreach (var oa in Offsets(a))
		{
			foreach (var ob in Offsets(b))
			{
				foreach (var p in Intersections(oa, ob)) yield return p;
			}
		}
	}

	/// <summary>
	/// 모깎기 원 (중심, 접점1, 접점2). 중심은 고른 쪽(상대 객체의 고른 점이 있는 쪽)에 있어야 하고,
	/// 그런 후보가 여럿이면 접점이 고른 점에 가장 가까운 것.
	/// </summary>
	public static (XY C, XY T1, XY T2)? FilletCircle(Curve a, Curve b, double r)
	{
		(XY, XY, XY)? best = null;
		double bestScore = double.MaxValue;
		foreach (var c in Centers(a, b, r))
		{
			var t1 = Foot(a, c);
			var t2 = Foot(b, c);
			// 원 안쪽에서 감싸는 경우: 접점은 반대편
			if (a is CircleCurve ca && Math.Abs(Geom.Dist(t1, c) - r) > 1e-6) t1 = new XY(2 * ca.C.X - t1.X, 2 * ca.C.Y - t1.Y);
			if (b is CircleCurve cb && Math.Abs(Geom.Dist(t2, c) - r) > 1e-6) t2 = new XY(2 * cb.C.X - t2.X, 2 * cb.C.Y - t2.Y);
			double score = Geom.Dist(t1, a.Pick) + Geom.Dist(t2, b.Pick);
			double sa = SideOf(a, b.Pick), sb = SideOf(b, a.Pick);
			bool okA = Math.Abs(sa) < 1e-9 || Math.Sign(SideOf(a, c)) == Math.Sign(sa);
			bool okB = Math.Abs(sb) < 1e-9 || Math.Sign(SideOf(b, c)) == Math.Sign(sb);
			if (!okA || !okB) score += 1e12;
			if (score < bestScore)
			{
				bestScore = score;
				best = (c, t1, t2);
			}
		}
		return best;
	}

	/// <summary>두 곡선의 모서리 점 (고른 점에 가장 가까운 교점).</summary>
	public static XY? Corner(Curve a, Curve b)
	{
		XY? best = null;
		double bestD = double.MaxValue;
		foreach (var p in Intersections(a, b))
		{
			double d = Geom.Dist(p, a.Pick) + Geom.Dist(p, b.Pick);
			if (d < bestD) { bestD = d; best = p; }
		}
		return best;
	}

	/// <summary>t1→t2를 잇는 짧은 쪽 호.</summary>
	public static Arc MakeArc(XY c, double r, XY t1, XY t2)
	{
		double a1 = Math.Atan2(t1.Y - c.Y, t1.X - c.X), a2 = Math.Atan2(t2.Y - c.Y, t2.X - c.X);
		if (Geom.Norm(a2 - a1) > Math.PI) (a1, a2) = (a2, a1);
		return new Arc { Center = Num.Z(c.X, c.Y), Radius = r, StartAngle = Geom.Norm(a1), EndAngle = Geom.Norm(a2) };
	}

	/// <summary>모양 되돌리기용 상태 (선·호·폴리선).</summary>
	private static Action Capture(Entity e)
	{
		switch (e)
		{
			case Line l:
				{
					var (s, en) = (l.StartPoint, l.EndPoint);
					return () => { l.StartPoint = s; l.EndPoint = en; };
				}
			case Arc a:
				{
					var (c, r, s, en) = (a.Center, a.Radius, a.StartAngle, a.EndAngle);
					return () => { a.Center = c; a.Radius = r; a.StartAngle = s; a.EndAngle = en; };
				}
			case LwPolyline pl:
				{
					var snap = PolySnapshot.Of(pl);
					return () => snap.ApplyTo(pl);
				}
		}
		return () => { };
	}

	/// <summary>제자리 편집 (되돌리기 가능).</summary>
	public static void Edit(EditorDocument doc, Entity e, string name, Action change)
	{
		var before = Capture(e);
		change();
		var after = Capture(e);
		before();
		doc.Execute(new EntityEditAction(name, e, after, before));
	}

	/// <summary>
	/// 곡선을 점 t까지 자르거나 늘인다: 고른 점이 있는 쪽을 남긴다. 원은 그대로. 폴리선은 끝 조각만.
	/// 실패하면 이유를 돌려준다.
	/// </summary>
	public static string? CanTrim(Curve c)
	{
		if (c.Ent is LwPolyline pl && c.Seg >= 0)
		{
			int n = pl.Vertices.Count;
			if (pl.IsClosed || (c.Seg != 0 && c.Seg != n - 2)) return "폴리선의 끝 조각이 아니면 다른 객체와 모깍기/모따기할 수 없습니다.";
			if (c is CircleCurve) return "폴리선 호 세그먼트를 모따기할 수 없습니다.";
		}
		return null;
	}

	public static void TrimTo(EditorDocument doc, Curve c, XY t, string name)
	{
		switch (c.Ent)
		{
			case Line l when c is LineCurve lc:
				{
					double tp = lc.Param(t), pp = lc.Param(c.Pick);
					var p = Num.Z(t.X, t.Y);
					// 고른 점이 t보다 끝점 쪽이면 시작점을 옮긴다
					Edit(doc, l, name, pp >= tp ? () => l.StartPoint = p : () => l.EndPoint = p);
					break;
				}
			case Arc a:
				{
					var cc = Geom.P(a.Center);
					double ta = Geom.Norm(Math.Atan2(t.Y - cc.Y, t.X - cc.X));
					double pa = Math.Atan2(c.Pick.Y - cc.Y, c.Pick.X - cc.X);
					bool keepEnd = Geom.Norm(pa - ta) <= Geom.Norm(a.EndAngle - ta) + 1e-9; // [ta, End]에 고른 점
					bool keepStart = Geom.Norm(pa - a.StartAngle) <= Geom.Norm(ta - a.StartAngle) + 1e-9; // [Start, ta]
					static double AD(double x, double y) => Math.Min(Geom.Norm(x - y), Geom.Norm(y - x));
					bool moveStart = keepEnd && (!keepStart || AD(ta, a.StartAngle) <= AD(ta, a.EndAngle));
					if (!keepEnd && !keepStart) moveStart = AD(ta, a.StartAngle) <= AD(ta, a.EndAngle);
					Edit(doc, a, name, moveStart ? () => a.StartAngle = ta : () => a.EndAngle = ta);
					break;
				}
			case LwPolyline pl when c.Seg >= 0:
				{
					int idx = c.Seg == 0 && pl.Vertices.Count > 2 ? 0
						: c.Seg == pl.Vertices.Count - 2 && pl.Vertices.Count > 2 ? pl.Vertices.Count - 1
						: (c is LineCurve lc2 && lc2.Param(c.Pick) >= lc2.Param(t) ? 0 : 1);
					var loc = t;
					Edit(doc, pl, name, () => pl.Vertices[idx].Location = loc);
					break;
				}
		}
	}

	/// <summary>객체의 도면층·색 등 (두 객체가 같은 도면층이면 그 속성, 아니면 현재 속성).</summary>
	public static void PropsFor(Entity made, Entity e1, Entity e2)
	{
		if (e1.Layer?.Name == e2.Layer?.Name) Geom.CopyProps(e1, made);
	}
}

/// <summary>
/// FILLET (기존 CAD와 같은 옵션): 폴리라인(P)/반지름(R)/자르기(T)/설정(S)/다중(M).
/// 선·호·원·폴리선(끝 조각, 같은 폴리선의 이웃 조각)을 모깎기. 반지름 0 또는 Shift+선택 = 모서리 맞춤. 평행선은 반원.
/// </summary>
public sealed class FilletCommand : EditorCommand
{
	private static double _radius;
	private static bool _trim = true;

	private CornerGeom.Curve? _first;
	private string? _await;
	private bool _multiple;

	public override string Name => "FILLET";

	public override string[] Aliases => new[] { "F" };

	public override IEnumerable<Entity> Highlighted => this._first != null ? new[] { this._first.Ent } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._await is null or "P";

	private const string FirstPrompt = "Fillet 첫번째 객체 선택 or [폴리라인(P)/반지름(R)/자르기(T)/설정(S)/다중(M)]:";
	private const string SecondPrompt = "두번째 객체 선택 또는 [반지름(R)]:";

	public static double Radius { get => _radius; set => _radius = value; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._first = null;
		this._await = null;
		this._multiple = false;
		this.PrintSettings();
		this.Prompt = FirstPrompt;
	}

	private void PrintSettings() => this.Ctx?.Print($"현재 설정: 모드 = {(_trim ? "자르기(T)" : "자르지않기(N)")}, 반지름 = {_radius:0.0000}:");

	private void Ready()
	{
		this._await = null;
		this.Prompt = this._first == null ? FirstPrompt : SecondPrompt;
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		switch (this._await)
		{
			case "R":
				if (Num.TryParse(t, out double r) && r >= 0)
				{
					_radius = r;
					this.Ready();
				}
				else
				{
					this.Ctx.Print("0 이상의 값이 필요합니다.");
				}
				return;
			case "T":
				if (t is "T" or "N")
				{
					_trim = t == "T";
					this.PrintSettings();
					this.Ready();
				}
				return;
		}
		switch (t)
		{
			case "R":
				this._await = "R";
				this.Prompt = $"Fillet 반지름 <{_radius:0.0000}>:";
				break;
			case "T" when this._first == null:
				this._await = "T";
				this.Prompt = _trim ? "[자르기(T)/자르지않기(N)]/<자르기(T)>:" : "[자르기(T)/자르지않기(N)]/<자르지않기(N)>:";
				break;
			case "P" when this._first == null:
				this._await = "P";
				this.Prompt = "Fillet 적용할 2D 폴리라인 선택:";
				break;
			case "M" when this._first == null:
				this._multiple = true;
				break;
			case "S" when this._first == null:
				this.PrintSettings();
				break;
		}
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this.Ready(); // 기본값 유지
			return;
		}
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		var ent = this.Ctx.PickAt(x, y);
		if (this._await == "P")
		{
			if (ent is LwPolyline pl)
			{
				int n = FilletPolyline(this.Ctx.Doc, pl, _radius);
				this.Ctx.Print($"{n} 선분 모깍기되었습니다.");
				this.Done();
			}
			else
			{
				this.Ctx.Print("2D 폴리라인을 선택하십시오.");
			}
			return;
		}
		if (this._await != null) return;
		var curve = CornerGeom.FromPick(ent, p);
		if (curve == null)
		{
			if (ent != null) this.Ctx.Print("선, 호, 원, 폴리선을 선택하십시오.");
			return;
		}
		if (this._first == null)
		{
			this._first = curve;
			this.Prompt = SecondPrompt;
			return;
		}
		double r = this.Ctx.ShiftPressed ? 0 : _radius;
		string? err = Apply(this.Ctx.Doc, this._first, curve, r, _trim);
		if (err != null)
		{
			this.Ctx.Print(err);
			return;
		}
		this.Done();
	}

	private void Done()
	{
		if (this._multiple)
		{
			this._first = null;
			this.Ready();
		}
		else
		{
			this.Finish();
		}
	}

	/// <summary>모깎기 실행. 실패하면 이유.</summary>
	internal static string? Apply(EditorDocument doc, CornerGeom.Curve a, CornerGeom.Curve b, double r, bool trim)
	{
		if (ReferenceEquals(a.Ent, b.Ent))
		{
			if (a.Ent is LwPolyline pl && a.Seg != b.Seg) return FilletPolylineVertex(doc, pl, a.Seg, b.Seg, r);
			return "같은 객체를 두 번 선택할 수 없습니다.";
		}
		if (trim && (CornerGeom.CanTrim(a) ?? CornerGeom.CanTrim(b)) is { } why) return why;

		doc.BeginTransaction();
		try
		{
			if (r < 1e-9)
			{
				if (CornerGeom.Corner(a, b) is not XY corner) return "평행한 객체는 반지름 0으로 모깎기할 수 없습니다.";
				CornerGeom.TrimTo(doc, a, corner, "모깎기");
				CornerGeom.TrimTo(doc, b, corner, "모깎기");
				return null;
			}
			if (a is CornerGeom.LineCurve la && b is CornerGeom.LineCurve lb && Math.Abs(la.Dir.X * lb.Dir.Y - la.Dir.Y * lb.Dir.X) < 1e-9)
			{
				return Parallel(doc, la, lb, trim);
			}
			if (CornerGeom.FilletCircle(a, b, r) is not { } f) return $"{r:0.0000} 반지름에 대해 적합한 모깍기가 없습니다.";
			if (trim)
			{
				CornerGeom.TrimTo(doc, a, f.T1, "모깎기");
				CornerGeom.TrimTo(doc, b, f.T2, "모깎기");
			}
			var arc = CornerGeom.MakeArc(f.C, r, f.T1, f.T2);
			CornerGeom.PropsFor(arc, a.Ent, b.Ent);
			doc.AddEntities(arc);
			return null;
		}
		finally
		{
			doc.CommitTransaction("FILLET");
		}
	}

	/// <summary>평행한 두 선: 첫 선의 고른 쪽 끝에서 반원으로 잇는다 (반지름 = 간격/2).</summary>
	private static string? Parallel(EditorDocument doc, CornerGeom.LineCurve a, CornerGeom.LineCurve b, bool trim)
	{
		bool atA = Geom.Dist(a.A, a.Pick) <= Geom.Dist(a.B, a.Pick);
		var end = atA ? a.A : a.B;
		var other = atA ? a.B : a.A;
		var foot = b.Foot(end);
		double gap = Geom.Dist(end, foot);
		if (gap < 1e-9) return "겹친 선은 모깎기할 수 없습니다.";
		var c = new XY((end.X + foot.X) / 2, (end.Y + foot.Y) / 2);
		double r = gap / 2;
		// 바깥쪽(첫 선의 끝 너머)으로 볼록한 반원
		double ox = end.X - other.X, oy = end.Y - other.Y, ol = Math.Sqrt(ox * ox + oy * oy);
		var outward = new XY(ox / ol, oy / ol);
		double a1 = Math.Atan2(end.Y - c.Y, end.X - c.X);
		double mid = a1 + Math.PI / 2;
		bool ccw = Math.Cos(mid) * outward.X + Math.Sin(mid) * outward.Y > 0;
		double s = ccw ? a1 : a1 + Math.PI;
		var arc = new Arc { Center = Num.Z(c.X, c.Y), Radius = r, StartAngle = Geom.Norm(s), EndAngle = Geom.Norm(s + Math.PI) };
		if (trim) CornerGeom.TrimTo(doc, b, foot, "모깎기");
		CornerGeom.PropsFor(arc, a.Ent, b.Ent);
		doc.AddEntities(arc);
		return null;
	}

	/// <summary>같은 폴리선의 이웃한 두 직선 조각 사이 꼭짓점을 모깎기 (호 조각 삽입).</summary>
	private static string? FilletPolylineVertex(EditorDocument doc, LwPolyline pl, int s1, int s2, double r)
	{
		int n = pl.Vertices.Count;
		int segCount = pl.IsClosed ? n : n - 1;
		int k;
		if ((s1 + 1) % segCount == s2 && (pl.IsClosed || s2 > s1)) k = (s1 + 1) % n;
		else if ((s2 + 1) % segCount == s1 && (pl.IsClosed || s1 > s2)) k = (s2 + 1) % n;
		else return "이웃한 폴리선 조각이 아닙니다.";
		var snap = PolySnapshot.Of(pl);
		if (!FilletAt(snap, k, r, out string? err)) return err;
		var before = PolySnapshot.Of(pl);
		doc.Execute(new EntityEditAction("모깎기", pl, () => snap.ApplyTo(pl), () => before.ApplyTo(pl)));
		return null;
	}

	/// <summary>폴리선 상태에서 꼭짓점 k를 반지름 r 호로 바꾼다 (두 이웃 조각이 직선일 때만).</summary>
	private static bool FilletAt(PolySnapshot s, int k, double r, out string? err)
	{
		err = null;
		var v = s.Vertices;
		int n = v.Count;
		int prev = (k - 1 + n) % n, next = (k + 1) % n;
		if (!s.Closed && (k == 0 || k == n - 1)) { err = "끝 꼭짓점은 모깎기할 수 없습니다."; return false; }
		if (Math.Abs(v[prev].Bulge) > 1e-12 || Math.Abs(v[k].Bulge) > 1e-12) { err = "폴리선 호 세그먼트는 모깍기할 수 없습니다."; return false; }
		XY P(int i) => new(v[i].Location.X, v[i].Location.Y);
		XY a = P(prev), b = P(k), c = P(next);
		double l1 = Geom.Dist(a, b), l2 = Geom.Dist(b, c);
		if (l1 < 1e-12 || l2 < 1e-12) { err = "길이가 0인 조각입니다."; return false; }
		var u1 = new XY((a.X - b.X) / l1, (a.Y - b.Y) / l1);
		var u2 = new XY((c.X - b.X) / l2, (c.Y - b.Y) / l2);
		double cos = Math.Clamp(u1.X * u2.X + u1.Y * u2.Y, -1, 1);
		double theta = Math.Acos(cos);
		if (theta < 1e-9 || Math.PI - theta < 1e-9) { err = "일직선인 꼭짓점입니다."; return false; }
		if (r < 1e-9) return true;
		double td = r / Math.Tan(theta / 2);
		if (td > l1 + 1e-9 || td > l2 + 1e-9) { err = $"{r:0.0000} 반지름에 대해 적합한 모깍기가 없습니다."; return false; }
		var t1 = new XY(b.X + u1.X * td, b.Y + u1.Y * td);
		var t2 = new XY(b.X + u2.X * td, b.Y + u2.Y * td);
		double turn = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
		double bulge = Math.Tan((Math.PI - theta) / 4) * Math.Sign(turn);
		var old = v[k];
		v[k] = new LwPolyline.Vertex(t1) { Bulge = bulge, StartWidth = old.StartWidth, EndWidth = old.EndWidth };
		v.Insert(k + 1, new LwPolyline.Vertex(t2) { Bulge = 0, StartWidth = old.StartWidth, EndWidth = old.EndWidth });
		return true;
	}

	/// <summary>폴리선의 모든 꼭짓점 모깎기 (P 옵션). 모깎기한 꼭짓점 수.</summary>
	internal static int FilletPolyline(EditorDocument doc, LwPolyline pl, double r)
	{
		var snap = PolySnapshot.Of(pl);
		int count = 0;
		int n = snap.Vertices.Count;
		int first = snap.Closed ? 0 : 1, last = snap.Closed ? n - 1 : n - 2;
		for (int k = last; k >= first; k--)
		{
			if (FilletAt(snap, k, r, out _) && r > 1e-9) count++;
		}
		if (count == 0) return 0;
		var before = PolySnapshot.Of(pl);
		doc.Execute(new EntityEditAction("모깎기", pl, () => snap.ApplyTo(pl), () => before.ApplyTo(pl)));
		return count;
	}
}

/// <summary>
/// CHAMFER (기존 CAD와 같은 옵션): 폴리라인(P)/거리(D)/각도(A)/방법(E)/자르기(T)/설정(S)/다중(M).
/// 선과 폴리선 직선 조각을 모따기.
/// </summary>
public sealed class ChamferCommand : EditorCommand
{
	private static double _dist1;
	private static double _dist2;
	private static double _length;
	private static double _angle;
	private static bool _byAngle;
	private static bool _trim = true;

	private CornerGeom.LineCurve? _first;
	private string? _await;
	private bool _multiple;

	public override string Name => "CHAMFER";

	public override string[] Aliases => new[] { "CHA" };

	public override IEnumerable<Entity> Highlighted => this._first != null ? new[] { this._first.Ent } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._await is null or "P";

	private const string FirstPrompt = "CHAMFER 첫번째 선 선택 또는 [폴리라인(P)/거리(D)/각도(A)/방법(E)/자르기(T)/설정(S)/다중(M)]:";
	private const string SecondPrompt = "두번째 선 선택:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._first = null;
		this._await = null;
		this._multiple = false;
		this.PrintSettings();
		this.Prompt = FirstPrompt;
	}

	private void PrintSettings()
	{
		string mode = _trim ? "자르기(T)" : "자르지않기(N)";
		this.Ctx?.Print(_byAngle
			? $"현재 설정: 모드 = {mode}, 길이 = {_length:0.0000}, 각도 = {_angle:0.####}"
			: $"현재 설정: 모드 = {mode}, 거리1 = {_dist1:0.0000}, 거리2 = {_dist2:0.0000}");
	}

	private void Ready()
	{
		this._await = null;
		this.Prompt = this._first == null ? FirstPrompt : SecondPrompt;
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = Num.TryParse(t, out double v);
		switch (this._await)
		{
			case "D1":
				if (num && v >= 0)
				{
					_dist1 = v;
					this._await = "D2";
					this.Prompt = $"인접 표면의 챔퍼 거리 입력 <{_dist1:0.0000}>:";
				}
				return;
			case "D2":
				if (num && v >= 0)
				{
					_dist2 = v;
					this.Ready();
				}
				return;
			case "A1":
				if (num && v >= 0)
				{
					_length = v;
					this._await = "A2";
					this.Prompt = $"첫번째 선에 상대적인 각도 <{_angle:0.####}>:";
				}
				return;
			case "A2":
				if (num)
				{
					_angle = v;
					this.Ready();
				}
				return;
			case "E":
				if (t is "A" or "D")
				{
					_byAngle = t == "A";
					this.Ctx.Print(_byAngle ? "모따기 모드가 각도 방법으로 설정됨." : "모따기 모드가 거리 방법으로 설정됨.");
					this.Ready();
				}
				return;
			case "T":
				if (t is "T" or "N")
				{
					_trim = t == "T";
					this.PrintSettings();
					this.Ready();
				}
				return;
		}
		if (this._first != null) return;
		switch (t)
		{
			case "D":
				_byAngle = false;
				this.Ctx.Print("모따기 모드가 거리 방법으로 설정됨.");
				this._await = "D1";
				this.Prompt = $"기본 표면의 챔퍼 거리 입력 <{_dist1:0.0000}>:";
				break;
			case "A":
				_byAngle = true;
				this.Ctx.Print("모따기 모드가 각도 방법으로 설정됨.");
				this._await = "A1";
				this.Prompt = $"첫번째 선의 거리 <{_length:0.0000}>:";
				break;
			case "E":
				this._await = "E";
				this.Prompt = _byAngle ? "모따기 사용:  [각도(A)/거리(D)]/<각도(A)>:" : "모따기 사용:  [각도(A)/거리(D)]/<거리(D)>:";
				break;
			case "T":
				this._await = "T";
				this.Prompt = _trim ? "[자르기(T)/자르지않기(N)]/<자르기(T)>:" : "[자르기(T)/자르지않기(N)]/<자르지않기(N)>:";
				break;
			case "P":
				this._await = "P";
				this.Prompt = "모따기할 2D 폴리선 선택:";
				break;
			case "M":
				this._multiple = true;
				break;
			case "S":
				this.PrintSettings();
				break;
		}
	}

	public override void OnEnter()
	{
		switch (this._await)
		{
			case "D1":
				this._await = "D2";
				this.Prompt = $"인접 표면의 챔퍼 거리 입력 <{_dist1:0.0000}>:";
				return;
			case "A1":
				this._await = "A2";
				this.Prompt = $"첫번째 선에 상대적인 각도 <{_angle:0.####}>:";
				return;
			case "D2":
				_dist2 = _dist1; // 기본값 = 첫 거리
				this.Ready();
				return;
			case null when this._first == null && !this._multiple:
				// 기존 CAD: 첫 선을 고르기 전 Enter는 현재 설정을 다시 보여 주고 계속 묻는다 (Esc로 끝냄)
				this.PrintSettings();
				this.Prompt = FirstPrompt;
				return;
			case null:
				this.Finish();
				return;
			default:
				this.Ready();
				return;
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var ent = this.Ctx.PickAt(x, y);
		if (this._await == "P")
		{
			if (ent is LwPolyline pl)
			{
				int n = ChamferPolyline(this.Ctx.Doc, pl);
				this.Ctx.Print($"{n}개의 선이 모따기되었습니다");
				this.Done();
			}
			else
			{
				this.Ctx.Print("2D 폴리선을 선택하십시오.");
			}
			return;
		}
		if (this._await != null) return;
		var curve = CornerGeom.FromPick(ent, new XY(x, y));
		if (curve is not CornerGeom.LineCurve lc)
		{
			this.Ctx.Print(curve is CornerGeom.CircleCurve && ent is LwPolyline ? "폴리선 호 세그먼트를 모따기할 수 없습니다." : "선택한 것을 사용할 수 없습니다.  다시 시도하십시오.");
			return;
		}
		if (this._first == null)
		{
			this._first = lc;
			this.Prompt = SecondPrompt;
			return;
		}
		var (d1, d2) = this.Ctx.ShiftPressed ? (0.0, 0.0) : Distances(this._first, lc);
		string? err = Apply(this.Ctx.Doc, this._first, lc, d1, d2, _trim);
		if (err != null)
		{
			this.Ctx.Print(err);
			return;
		}
		this.Done();
	}

	private void Done()
	{
		if (this._multiple)
		{
			this._first = null;
			this.Ready();
		}
		else
		{
			this.Finish();
		}
	}

	/// <summary>현재 방법(거리/각도)에 따른 두 거리.</summary>
	private static (double, double) Distances(CornerGeom.LineCurve a, CornerGeom.LineCurve b)
	{
		if (!_byAngle) return (_dist1, _dist2);
		double cos = Math.Abs(a.Dir.X * b.Dir.X + a.Dir.Y * b.Dir.Y);
		double theta = Math.Acos(Math.Clamp(cos, 0, 1));
		if (CornerGeom.Corner(a, b) is XY c)
		{
			// 모서리에서 두 고른 점 쪽 사이의 실제 각
			var u1 = Arm(a, c);
			var u2 = Arm(b, c);
			theta = Math.Acos(Math.Clamp(u1.X * u2.X + u1.Y * u2.Y, -1, 1));
		}
		double alpha = _angle * Math.PI / 180;
		double s = Math.Sin(theta + alpha);
		return (_length, Math.Abs(s) < 1e-12 ? _length : _length * Math.Sin(alpha) / s);
	}

	/// <summary>모서리에서 고른 점 쪽으로 향하는 방향.</summary>
	private static XY Arm(CornerGeom.LineCurve l, XY corner)
	{
		var d = l.Dir;
		double s = (l.Pick.X - corner.X) * d.X + (l.Pick.Y - corner.Y) * d.Y;
		if (Math.Abs(s) < 1e-9)
		{
			// 모서리를 고른 경우: 먼 끝점 쪽
			var far = Geom.Dist(l.A, corner) > Geom.Dist(l.B, corner) ? l.A : l.B;
			s = (far.X - corner.X) * d.X + (far.Y - corner.Y) * d.Y;
		}
		return s >= 0 ? d : new XY(-d.X, -d.Y);
	}

	internal static string? Apply(EditorDocument doc, CornerGeom.LineCurve a, CornerGeom.LineCurve b, double d1, double d2, bool trim)
	{
		if (ReferenceEquals(a.Ent, b.Ent))
		{
			if (a.Ent is LwPolyline pl && a.Seg != b.Seg) return ChamferPolylineVertex(doc, pl, a.Seg, b.Seg, d1, d2);
			return "폴리선이 아닌 것은 자신으로 모따기할 수 없습니다.";
		}
		if (trim && (CornerGeom.CanTrim(a) ?? CornerGeom.CanTrim(b)) is { } why) return why;
		if (CornerGeom.Corner(a, b) is not XY c) return "평행한 선을 모따기할 수 없습니다.";
		var u1 = Arm(a, c);
		var u2 = Arm(b, c);
		var p1 = new XY(c.X + u1.X * d1, c.Y + u1.Y * d1);
		var p2 = new XY(c.X + u2.X * d2, c.Y + u2.Y * d2);
		doc.BeginTransaction();
		if (trim)
		{
			CornerGeom.TrimTo(doc, a, p1, "모따기");
			CornerGeom.TrimTo(doc, b, p2, "모따기");
		}
		if (d1 > 1e-9 || d2 > 1e-9)
		{
			var line = new Line(Num.Z(p1.X, p1.Y), Num.Z(p2.X, p2.Y));
			CornerGeom.PropsFor(line, a.Ent, b.Ent);
			doc.AddEntities(line);
		}
		doc.CommitTransaction("CHAMFER");
		return null;
	}

	private static string? ChamferPolylineVertex(EditorDocument doc, LwPolyline pl, int s1, int s2, double d1, double d2)
	{
		int n = pl.Vertices.Count;
		int segCount = pl.IsClosed ? n : n - 1;
		int k;
		bool firstIsPrev;
		if ((s1 + 1) % segCount == s2 && (pl.IsClosed || s2 > s1)) { k = (s1 + 1) % n; firstIsPrev = true; }
		else if ((s2 + 1) % segCount == s1 && (pl.IsClosed || s1 > s2)) { k = (s2 + 1) % n; firstIsPrev = false; }
		else return "모따기할 폴리선의 정점을 결정할 수 없습니다.";
		var snap = PolySnapshot.Of(pl);
		if (!ChamferAt(snap, k, firstIsPrev ? d1 : d2, firstIsPrev ? d2 : d1, out string? err)) return err;
		var before = PolySnapshot.Of(pl);
		doc.Execute(new EntityEditAction("모따기", pl, () => snap.ApplyTo(pl), () => before.ApplyTo(pl)));
		return null;
	}

	/// <summary>꼭짓점 k를 앞 조각 쪽 dPrev, 뒤 조각 쪽 dNext 거리로 모따기.</summary>
	private static bool ChamferAt(PolySnapshot s, int k, double dPrev, double dNext, out string? err)
	{
		err = null;
		var v = s.Vertices;
		int n = v.Count;
		int prev = (k - 1 + n) % n, next = (k + 1) % n;
		if (!s.Closed && (k == 0 || k == n - 1)) { err = "모따기할 폴리선의 정점을 결정할 수 없습니다."; return false; }
		if (Math.Abs(v[prev].Bulge) > 1e-12 || Math.Abs(v[k].Bulge) > 1e-12) { err = "폴리선 호 세그먼트를 모따기할 수 없습니다."; return false; }
		XY P(int i) => new(v[i].Location.X, v[i].Location.Y);
		XY a = P(prev), b = P(k), c = P(next);
		double l1 = Geom.Dist(a, b), l2 = Geom.Dist(b, c);
		if (l1 < 1e-12 || l2 < 1e-12) { err = "길이가 0인 선을 모따기할 수 없습니다."; return false; }
		if (dPrev > l1 + 1e-9) { err = "첫번째 거리가 너무 길어 요청된 모따기를 수행할 수 없습니다."; return false; }
		if (dNext > l2 + 1e-9) { err = "두번째 거리가 너무 길어 요청된 모따기를 수행할 수 없습니다."; return false; }
		if (dPrev < 1e-9 && dNext < 1e-9) return true;
		var t1 = new XY(b.X + (a.X - b.X) / l1 * dPrev, b.Y + (a.Y - b.Y) / l1 * dPrev);
		var t2 = new XY(b.X + (c.X - b.X) / l2 * dNext, b.Y + (c.Y - b.Y) / l2 * dNext);
		var old = v[k];
		v[k] = new LwPolyline.Vertex(t1) { Bulge = 0, StartWidth = old.StartWidth, EndWidth = old.EndWidth };
		v.Insert(k + 1, new LwPolyline.Vertex(t2) { Bulge = 0, StartWidth = old.StartWidth, EndWidth = old.EndWidth });
		return true;
	}

	internal static int ChamferPolyline(EditorDocument doc, LwPolyline pl)
	{
		var snap = PolySnapshot.Of(pl);
		int count = 0;
		int n = snap.Vertices.Count;
		int first = snap.Closed ? 0 : 1, last = snap.Closed ? n - 1 : n - 2;
		for (int k = last; k >= first; k--)
		{
			double dp = _byAngle ? _length : _dist1, dn = _byAngle ? _length * Math.Tan(_angle * Math.PI / 180) : _dist2;
			if (ChamferAt(snap, k, dp, dn, out _)) count++;
		}
		if (count == 0) return 0;
		var before = PolySnapshot.Of(pl);
		doc.Execute(new EntityEditAction("모따기", pl, () => snap.ApplyTo(pl), () => before.ApplyTo(pl)));
		return count;
	}
}
