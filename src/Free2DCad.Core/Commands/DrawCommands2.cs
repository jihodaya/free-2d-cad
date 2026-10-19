using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>점 입력 명령 공통: 숫자만 입력하면 OnText로, 좌표는 OnPoint로 온다.</summary>
internal static class Num
{
	public static bool TryParse(string text, out double v) =>
		double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);

	public static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

	public static XYZ Z(double x, double y) => new(x, y, 0);

	public static double Deg(double rad) => rad * 180 / Math.PI;

	public static double Rad(double deg) => deg * Math.PI / 180;
}

/// <summary>
/// CIRCLE (기존 CAD와 같은 옵션): 중심점 + 반지름/지름(D), 3점(3P), 2점(2P), 접선 접선 반지름(TTR), 접선 접선 접선(TTT).
/// 리본의 원▾ 목록은 Mode로 바로 그 방식을 시작한다.
/// </summary>
public sealed class CircleCommand : EditorCommand
{
	public enum CircleMode { CenterRadius, CenterDiameter, TwoPoint, ThreePoint, TanTanRadius, TanTanTan }

	private CircleMode _mode;
	private int _step;
	private (double X, double Y) _center;
	private readonly List<(double X, double Y)> _pts = new();
	private readonly List<(Entity Ent, (double X, double Y) Pick)> _tans = new();

	public override string Name => "CIRCLE";
	public override IEnumerable<Entity> Highlighted => this._tans.Select(t => t.Ent).ToArray();

	public override bool PickingObject => !this.IsFinished && (this._convertArc || (this._mode is CircleMode.TanTanRadius or CircleMode.TanTanTan && this._step < 2));

	public override string[] Aliases => new[] { "C" };

	/// <summary>시작할 방식 (리본 목록용). 기본은 중심-반지름.</summary>
	public CircleMode StartMode { get; set; } = CircleMode.CenterRadius;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._tans.Clear();
		this._step = 0;
		this._convertArc = false;
		this.SetMode(this.StartMode);
	}

	public override void OnEnter()
	{
		// 반지름 단계에서 Enter = 직전 반지름 (<기본값>)
		if (this._mode is CircleMode.CenterRadius or CircleMode.CenterDiameter && this._step == 1)
		{
			this.Add(new XY(this._center.X, this._center.Y), LastRadius);
		}
		else
		{
			this.Finish();
		}
	}

	private void SetMode(CircleMode mode)
	{
		this._mode = mode;
		this._step = 0;
		this._pts.Clear();
		this._tans.Clear();
		this.Prompt = mode switch
		{
			CircleMode.TwoPoint => "원 지름의 첫 번째 끝점 지정:",
			CircleMode.ThreePoint => "원 위의 첫 번째 점 지정:",
			CircleMode.TanTanRadius => "원의 첫 번째 접점에 대한 객체 위의 점 지정:",
			CircleMode.TanTanTan => "원 위의 첫 번째 점 지정: _tan 대상",
			_ => "[2점(2P)/3점(3P)/반지름접선(RTT)/접선(TTT)/호를원으로변환(A)]/<원의 중심>:",
		};
	}

	private void Add(XY c, double r)
	{
		if (r <= 1e-9 || double.IsNaN(r)) return;
		LastRadius = r;
		this.Ctx!.Doc.AddEntities(new Circle { Center = Num.Z(c.X, c.Y), Radius = r });
		this.Ctx.LastPoint = (c.X, c.Y);
		this.Finish();
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._step == 0 && this._mode == CircleMode.CenterRadius)
		{
			switch (t)
			{
				case "3P": this.SetMode(CircleMode.ThreePoint); return;
				case "2P": this.SetMode(CircleMode.TwoPoint); return;
				case "T": case "TTR": case "RTT": this.SetMode(CircleMode.TanTanRadius); return;
				case "TTT": this.SetMode(CircleMode.TanTanTan); return;
				case "A":
					this._convertArc = true;
					this.Prompt = "원으로 변환할 호 선택:";
					return;
			}
		}
		if (this._mode is CircleMode.CenterRadius or CircleMode.CenterDiameter && this._step == 1)
		{
			if (t == "D")
			{
				this._mode = CircleMode.CenterDiameter;
				this.Prompt = $"원의 지름 <{LastRadius * 2:0.####}>:";
				return;
			}
			if (t == "R")
			{
				this._mode = CircleMode.CenterRadius;
				this.Prompt = $"원의 반지름 <{LastRadius:0.####}>:";
				return;
			}
			if (Num.TryParse(t, out double v) && v > 0)
			{
				this.Add(new XY(this._center.X, this._center.Y), this._mode == CircleMode.CenterDiameter ? v / 2 : v);
			}
			return;
		}
		if (this._mode == CircleMode.TanTanRadius && this._step == 2 && Num.TryParse(t, out double rr) && rr > 0)
		{
			this.FinishTtr(rr);
		}
	}

	private bool _convertArc;
	private static double LastRadius = 10;

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._convertArc)
		{
			if (this.Ctx.PickAt(x, y) is Arc arc)
			{
				var circle = new Circle { Center = arc.Center, Radius = arc.Radius };
				Geom.CopyProps(arc, circle);
				this.Ctx.Doc.Replace(new[] { arc }, new[] { circle }, "호를 원으로");
				this.Finish();
			}
			else
			{
				this.Ctx.Print("호를 선택하십시오.");
			}
			return;
		}
		switch (this._mode)
		{
			case CircleMode.CenterRadius:
			case CircleMode.CenterDiameter:
				if (this._step == 0)
				{
					this._center = (x, y);
					this.Ctx.LastPoint = (x, y);
					this._step = 1;
					this.Prompt = this._mode == CircleMode.CenterDiameter ? $"원의 지름 <{LastRadius * 2:0.####}>:" : $"[지름(D)]/<반지름(R)> <{LastRadius:0.####}>:";
				}
				else
				{
					double r = Num.Dist(this._center, (x, y));
					this.Add(new XY(this._center.X, this._center.Y), this._mode == CircleMode.CenterDiameter ? r / 2 : r);
				}
				break;
			case CircleMode.TwoPoint:
				this._pts.Add((x, y));
				this.Ctx.LastPoint = (x, y);
				if (this._pts.Count == 1)
				{
					this.Prompt = "원 지름의 두 번째 끝점 지정:";
				}
				else
				{
					var (a, b) = (this._pts[0], this._pts[1]);
					this.Add(new XY((a.X + b.X) / 2, (a.Y + b.Y) / 2), Num.Dist(a, b) / 2);
				}
				break;
			case CircleMode.ThreePoint:
				this._pts.Add((x, y));
				this.Ctx.LastPoint = (x, y);
				if (this._pts.Count == 1) this.Prompt = "원 위의 두 번째 점 지정:";
				else if (this._pts.Count == 2) this.Prompt = "원 위의 세 번째 점 지정:";
				else if (Circumcircle(this._pts[0], this._pts[1], this._pts[2]) is { } cc)
				{
					this.Add(cc.C, cc.R);
				}
				else
				{
					this.Ctx.Print("세 점이 한 직선 위에 있습니다.");
					this._pts.RemoveAt(2);
				}
				break;
			case CircleMode.TanTanRadius:
			case CircleMode.TanTanTan:
				{
					var picked = this.Ctx.PickAt(x, y);
					if (picked is not (Line or Circle or LwPolyline))
					{
						this.Ctx.Print("선, 원, 호를 선택하십시오.");
						return;
					}
					this._tans.Add((picked, (x, y)));
					if (this._mode == CircleMode.TanTanRadius)
					{
						if (this._tans.Count == 1)
						{
							this.Prompt = "원의 두 번째 접점에 대한 객체 위의 점 지정:";
						}
						else
						{
							this._step = 2;
							this.Prompt = "원의 반지름 지정:";
						}
					}
					else
					{
						if (this._tans.Count == 1) this.Prompt = "원 위의 두 번째 점 지정: _tan 대상";
						else if (this._tans.Count == 2) this.Prompt = "원 위의 세 번째 점 지정: _tan 대상";
						else this.FinishTtt();
					}
					break;
				}
		}
	}

	public static (XY C, double R)? Circumcircle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
	{
		double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
		if (Math.Abs(d) < 1e-12) return null;
		double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
		double ux = (a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d;
		double uy = (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d;
		return (new XY(ux, uy), Math.Sqrt((a.X - ux) * (a.X - ux) + (a.Y - uy) * (a.Y - uy)));
	}

	private abstract record Locus;
	private sealed record LineLocus(XY A, XY B) : Locus;
	private sealed record CircleLocus(XY C, double R) : Locus;

	/// <summary>객체를 "중심이 있을 수 있는 곳"으로: 선 → 거리 R인 평행선 2개, 원 → 반지름 r±R인 원.</summary>
	private static IEnumerable<Locus> Offsets(Entity e, double r)
	{
		switch (e)
		{
			case Line l:
				{
					var a = Geom.P(l.StartPoint);
					var b = Geom.P(l.EndPoint);
					double len = Geom.Dist(a, b);
					if (len < 1e-12) yield break;
					double nx = -(b.Y - a.Y) / len, ny = (b.X - a.X) / len;
					foreach (int s in new[] { 1, -1 })
						yield return new LineLocus(new XY(a.X + nx * r * s, a.Y + ny * r * s), new XY(b.X + nx * r * s, b.Y + ny * r * s));
					break;
				}
			case Circle c:
				yield return new CircleLocus(Geom.P(c.Center), c.Radius + r);
				if (Math.Abs(c.Radius - r) > 1e-9) yield return new CircleLocus(Geom.P(c.Center), Math.Abs(c.Radius - r));
				break;
		}
	}

	private static IEnumerable<XY> Meet(Locus a, Locus b)
	{
		switch (a, b)
		{
			case (LineLocus l1, LineLocus l2):
				if (Geom.LineLine(l1.A, l1.B, l2.A, l2.B, out double t, out _))
					yield return new XY(l1.A.X + (l1.B.X - l1.A.X) * t, l1.A.Y + (l1.B.Y - l1.A.Y) * t);
				break;
			case (LineLocus l, CircleLocus c):
				foreach (var (p, _) in Geom.LineCircle(l.A, l.B, c.C, c.R)) yield return p;
				break;
			case (CircleLocus c, LineLocus l):
				foreach (var (p, _) in Geom.LineCircle(l.A, l.B, c.C, c.R)) yield return p;
				break;
			case (CircleLocus c1, CircleLocus c2):
				foreach (var p in Geom.CircleCircle(c1.C, c1.R, c2.C, c2.R)) yield return p;
				break;
		}
	}

	private void FinishTtr(double r)
	{
		var (e1, p1) = this._tans[0];
		var (e2, p2) = this._tans[1];
		XY? best = null;
		double bestD = double.MaxValue;
		foreach (var o1 in Offsets(AsCurve(e1), r))
			foreach (var o2 in Offsets(AsCurve(e2), r))
				foreach (var c in Meet(o1, o2))
				{
					double d = Geom.Dist(c, new XY(p1.X, p1.Y)) + Geom.Dist(c, new XY(p2.X, p2.Y));
					if (d < bestD) { bestD = d; best = c; }
				}
		if (best is { } bc) this.Add(bc, r);
		else this.Ctx?.Print("원이 없습니다 (반지름이 너무 작거나 큼).");
	}

	/// <summary>세 선에 접하는 원 (내접·방접 중 고른 점에 가장 가까운 것).</summary>
	private void FinishTtt()
	{
		var lines = this._tans.Select(t => AsCurve(t.Ent)).OfType<Line>().ToList();
		if (lines.Count < 3)
		{
			this.Ctx?.Print("접선 접선 접선은 세 선에서만 지원합니다.");
			this.Finish();
			return;
		}
		(double A, double B, double C)[] eq = lines.Select(l =>
		{
			double dx = l.EndPoint.X - l.StartPoint.X, dy = l.EndPoint.Y - l.StartPoint.Y, len = Math.Sqrt(dx * dx + dy * dy);
			double nx = -dy / len, ny = dx / len;
			return (nx, ny, nx * l.StartPoint.X + ny * l.StartPoint.Y);
		}).ToArray();
		XY? best = null;
		double bestR = 0, bestD = double.MaxValue;
		foreach (int s1 in new[] { 1, -1 })
			foreach (int s2 in new[] { 1, -1 })
				foreach (int s3 in new[] { 1, -1 })
				{
					// n·c − d = s R  → n·c − s R = d  (미지수 cx, cy, R)
					double[,] m = { { eq[0].A, eq[0].B, -s1 }, { eq[1].A, eq[1].B, -s2 }, { eq[2].A, eq[2].B, -s3 } };
					double[] rhs = { eq[0].C, eq[1].C, eq[2].C };
					if (Solve3(m, rhs) is not { } sol || sol[2] <= 1e-9) continue;
					var c = new XY(sol[0], sol[1]);
					double d = this._tans.Sum(t => Math.Abs(Geom.Dist(c, new XY(t.Pick.X, t.Pick.Y)) - sol[2]));
					if (d < bestD) { bestD = d; best = c; bestR = sol[2]; }
				}
		if (best is { } bc) this.Add(bc, bestR);
		else { this.Ctx?.Print("원이 없습니다."); this.Finish(); }
	}

	private static double[]? Solve3(double[,] m, double[] r)
	{
		double det(double[,] a) => a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
		double d = det(m);
		if (Math.Abs(d) < 1e-12) return null;
		var res = new double[3];
		for (int k = 0; k < 3; k++)
		{
			var c = (double[,])m.Clone();
			for (int i = 0; i < 3; i++) c[i, k] = r[i];
			res[k] = det(c) / d;
		}
		return res;
	}

	/// <summary>폴리선은 고른 점에 가장 가까운 조각을 선/원으로.</summary>
	private Entity AsCurve(Entity e)
	{
		if (e is LwPolyline pl)
		{
			var pick = this._tans.First(t => ReferenceEquals(t.Ent, e)).Pick;
			var segs = Geom.SegmentsOf(pl);
			var seg = segs.OrderBy(s => Geom.Dist(s.At(s.Project(new XY(pick.X, pick.Y))), new XY(pick.X, pick.Y))).First();
			return seg switch
			{
				Geom.LineSeg l => new Line(Num.Z(l.A.X, l.A.Y), Num.Z(l.B.X, l.B.Y)),
				Geom.ArcSeg a => new Circle { Center = Num.Z(a.C.X, a.C.Y), Radius = a.R },
				_ => e,
			};
		}
		return e;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		switch (this._mode)
		{
			case CircleMode.CenterRadius or CircleMode.CenterDiameter when this._step == 1:
				{
					double r = Num.Dist(this._center, (cursorX, cursorY));
					if (this._mode == CircleMode.CenterDiameter) r /= 2;
					if (r > 1e-9) yield return new Circle { Center = Num.Z(this._center.X, this._center.Y), Radius = r };
					break;
				}
			case CircleMode.TwoPoint when this._pts.Count == 1:
				{
					var a = this._pts[0];
					double r = Num.Dist(a, (cursorX, cursorY)) / 2;
					if (r > 1e-9) yield return new Circle { Center = Num.Z((a.X + cursorX) / 2, (a.Y + cursorY) / 2), Radius = r };
					break;
				}
			case CircleMode.ThreePoint when this._pts.Count == 2:
				if (Circumcircle(this._pts[0], this._pts[1], (cursorX, cursorY)) is { } cc)
					yield return new Circle { Center = Num.Z(cc.C.X, cc.C.Y), Radius = cc.R };
				break;
		}
	}
}

/// <summary>
/// ARC (기존 CAD의 호 11가지): 3점, 시작-중심-끝/각도/길이, 시작-끝-각도/방향/반지름, 중심-시작-끝/각도/길이.
/// 명령줄에서는 "호의 시작점 지정 또는 [중심(C)]:" → "두 번째 점 또는 [중심(C)/끝(E)]:" 순으로 옵션을 고른다.
/// </summary>
public sealed class ArcCommand : EditorCommand
{
	public enum ArcMode { ThreePoint, StartCenterEnd, StartCenterAngle, StartCenterLength, StartEndAngle, StartEndDirection, StartEndRadius, StartEndCenter, CenterStartEnd, CenterStartAngle, CenterStartLength }

	private ArcMode _mode;
	private double _pendingAngle, _pendingRadius;
	private (double X, double Y)? _start, _second, _center, _end;
	private string _await = "start"; // 다음에 받을 값

	public override string Name => "ARC";
	public override string[] Aliases => new[] { "A" };

	public ArcMode StartMode { get; set; } = ArcMode.ThreePoint;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._start = this._second = this._center = this._end = null;
		this._mode = this.StartMode;
		if (this._mode is ArcMode.CenterStartEnd or ArcMode.CenterStartAngle or ArcMode.CenterStartLength)
		{
			this._await = "center";
			this.Prompt = "호의 중심:";
		}
		else
		{
			this._await = "start";
			this.Prompt = "사용할 최종점/[중심(C)/따라가기(F)]/<호의 시작>:";
		}
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		switch (this._await)
		{
			case "start" when t == "C":
				this._mode = ArcMode.CenterStartEnd;
				this._await = "center";
				this.Prompt = "호의 중심:";
				return;
			case "second" when t == "C":
				this._mode = ArcMode.StartCenterEnd;
				this._await = "center";
				this.Prompt = "호의 중심:";
				return;
			case "start" when t == "F":
				this.OnEnter();
				return;
			case "second" when t == "A":
				this._await = "angleFirst";
				this.Prompt = "포함된 각도:";
				return;
			case "second" when t == "D":
				this._mode = ArcMode.StartEndDirection;
				this._await = "dirFirst";
				this.Prompt = "시작부터의 방향:";
				return;
			case "second" when t == "R":
				this._mode = ArcMode.StartEndRadius;
				this._await = "radiusFirst";
				this.Prompt = "반지름:";
				return;
			case "angleEnd" when t == "C":
				this._await = "angleCenter";
				this.Prompt = "호의 중심:";
				return;
			case "radiusEnd" when t == "I":
				this._await = "radiusAngle";
				this.Prompt = "호의 포함된 각도:";
				return;
			case "second" when t == "E":
				this._mode = ArcMode.StartEndAngle;
				this._await = "end";
				this.Prompt = "끝점:";
				return;
			case "endOrOpt" when t == "A":
				this._mode = this._mode == ArcMode.CenterStartEnd ? ArcMode.CenterStartAngle : ArcMode.StartCenterAngle;
				this._await = "angle";
				this.Prompt = "포함된 각도:";
				return;
			case "endOrOpt" when t == "L":
				this._mode = this._mode == ArcMode.CenterStartEnd ? ArcMode.CenterStartLength : ArcMode.StartCenterLength;
				this._await = "chord";
				this.Prompt = "현 길이:";
				return;
			case "seOpt" when t == "A":
				this._mode = ArcMode.StartEndAngle;
				this._await = "angle";
				this.Prompt = "포함된 각도:";
				return;
			case "seOpt" when t == "D":
				this._mode = ArcMode.StartEndDirection;
				this._await = "direction";
				this.Prompt = "시작부터의 방향:";
				return;
			case "seOpt" when t == "R":
				this._mode = ArcMode.StartEndRadius;
				this._await = "radius";
				this.Prompt = "반지름:";
				return;
		}
		if (!Num.TryParse(t, out double v)) return;
		switch (this._await)
		{
			case "angleFirst":
				this._pendingAngle = Num.Rad(v);
				this._await = "angleEnd";
				this.Prompt = "[중심(C)]/<호의 끝점>:";
				return;
			case "dirFirst":
				this._pendingAngle = Num.Rad(v);
				this._await = "dirEnd";
				this.Prompt = "호의 끝:";
				return;
			case "radiusFirst" when v > 0:
				this._pendingRadius = v;
				this._await = "radiusEnd";
				this.Prompt = "[포함된각도(I)]/<호의 끝>:";
				return;
			case "radiusAngle":
				this._pendingAngle = Num.Rad(v);
				this._await = "chordDir";
				this.Prompt = "현의 방향:";
				return;
			case "chordDir":
				this.FromRadiusAngleChord(Num.Rad(v));
				return;
			case "angle": this.FromAngle(Num.Rad(v)); break;
			case "chord": this.FromChord(v); break;
			case "radius": this.FromRadius(v); break;
			case "direction": this.FromDirection(Num.Rad(v)); break;
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = (x, y);
		switch (this._await)
		{
			case "start":
				this._start = p;
				this.Ctx.LastPoint = p;
				if (this._mode is ArcMode.StartCenterEnd or ArcMode.StartCenterAngle or ArcMode.StartCenterLength)
				{
					this._await = "center";
					this.Prompt = "호의 중심:";
				}
				else if (this._mode is ArcMode.StartEndAngle or ArcMode.StartEndDirection or ArcMode.StartEndRadius or ArcMode.StartEndCenter)
				{
					this._await = "end";
					this.Prompt = "끝점:";
				}
				else
				{
					this._await = "second";
					this.Prompt = "[각도(A)/중심(C)/방향(D)/끝(E)/반지름(R)]/<두번째 점>:";
				}
				break;
			case "second":
				this._second = p;
				this._await = "third";
				this.Prompt = "끝점:";
				break;
			case "third":
				if (CircleCommand.Circumcircle(this._start!.Value, this._second!.Value, p) is { } cc)
				{
					// 세 점을 지나는 방향(반시계/시계)대로
					double a0 = Math.Atan2(this._start.Value.Y - cc.C.Y, this._start.Value.X - cc.C.X);
					double a1 = Math.Atan2(this._second.Value.Y - cc.C.Y, this._second.Value.X - cc.C.X);
					double a2 = Math.Atan2(y - cc.C.Y, x - cc.C.X);
					bool ccw = Geom.Norm(a1 - a0) < Geom.Norm(a2 - a0);
					this.AddArc(cc.C, cc.R, ccw ? a0 : a2, ccw ? a2 : a0);
				}
				break;
			case "center":
				this._center = p;
				if (this._start == null)
				{
					this._await = "start2";
					this.Prompt = "시작:";
				}
				else
				{
					this._await = "endOrOpt";
					this.Prompt = "[각도(A)/길이(L)]/<끝점>:";
				}
				this.ApplyPresetAfterCenter();
				break;
			case "start2":
				this._start = p;
				this._await = "endOrOpt";
				this.Prompt = "[각도(A)/길이(L)]/<끝점>:";
				this.ApplyPresetAfterCenter();
				break;
			case "endOrOpt":
				{
					var c = this._center!.Value;
					double r = Num.Dist(c, this._start!.Value);
					double a0 = Math.Atan2(this._start.Value.Y - c.Y, this._start.Value.X - c.X);
					this.AddArc(new XY(c.X, c.Y), r, a0, Math.Atan2(y - c.Y, x - c.X));
					break;
				}
			case "end":
				this._end = p;
				this._await = "seOpt";
				this.Prompt = this._mode == ArcMode.StartEndCenter ? "호의 중심:" : "[각도(A)/방향(D)/반지름(R)]/<중심점>:";
				if (this._mode == ArcMode.StartEndAngle && this.StartMode == ArcMode.StartEndAngle) { this._await = "angle"; this.Prompt = "포함된 각도:"; }
				else if (this._mode == ArcMode.StartEndDirection) { this._await = "direction"; this.Prompt = "시작부터의 방향:"; }
				else if (this._mode == ArcMode.StartEndRadius) { this._await = "radius"; this.Prompt = "반지름:"; }
				break;
			case "seOpt":
				{
					// 시작-끝-중심
					var c = (x, y);
					double r = Num.Dist(c, this._start!.Value);
					this.AddArc(new XY(x, y), r, Math.Atan2(this._start.Value.Y - y, this._start.Value.X - x), Math.Atan2(this._end!.Value.Y - y, this._end.Value.X - x));
					break;
				}
			case "angle":
				this.FromAngle(this.AngleFromPoint(p));
				break;
			case "angleFirst":
			case "dirFirst":
			case "radiusAngle":
			case "chordDir":
				this.OnText((Math.Atan2(y - this._start!.Value.Y, x - this._start.Value.X) * 180 / Math.PI).ToString(System.Globalization.CultureInfo.InvariantCulture));
				break;
			case "radiusFirst":
				this.OnText(Num.Dist(this._start!.Value, p).ToString(System.Globalization.CultureInfo.InvariantCulture));
				break;
			case "angleEnd":
				this._end = p;
				this.FromAngle(this._pendingAngle);
				break;
			case "angleCenter":
				{
					var s0 = this._start!.Value;
					double r = Num.Dist(p, s0);
					double a0 = Math.Atan2(s0.Y - y, s0.X - x);
					if (this._pendingAngle >= 0) this.AddArc(new XY(x, y), r, a0, a0 + this._pendingAngle);
					else this.AddArc(new XY(x, y), r, a0 + this._pendingAngle, a0);
					break;
				}
			case "dirEnd":
				this._end = p;
				this.FromDirection(this._pendingAngle);
				break;
			case "radiusEnd":
				this._end = p;
				this.FromRadius(this._pendingRadius);
				break;
			case "chord":
				this.FromChord(Num.Dist(this._start!.Value, p));
				break;
			case "radius":
				this.FromRadius(Num.Dist(this._end!.Value, p));
				break;
			case "direction":
				this.FromDirection(Math.Atan2(y - this._start!.Value.Y, x - this._start.Value.X));
				break;
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx == null) return;
		if (this._await == "start" && this.Ctx.LastPoint is { } lp)
		{
			// Enter·따라가기: 마지막 점에서, 마지막 선·호의 끝 방향에 접하게
			this._start = lp;
			double? dir = null;
			var last = this.Ctx.Doc.CurrentSpace.Entities.LastOrDefault(e => e is Line or Arc);
			if (last is Line l)
			{
				dir = Math.Atan2(l.EndPoint.Y - l.StartPoint.Y, l.EndPoint.X - l.StartPoint.X);
			}
			else if (last is Arc a)
			{
				dir = a.EndAngle + Math.PI / 2;
			}
			if (dir is { } d)
			{
				this._mode = ArcMode.StartEndDirection;
				this._pendingAngle = d;
				this._await = "dirEnd";
				this.Prompt = "끝점:";
			}
			else
			{
				this._await = "second";
				this.Prompt = "[각도(A)/중심(C)/방향(D)/끝(E)/반지름(R)]/<두번째 점>:";
			}
			return;
		}
		base.OnEnter();
	}

	/// <summary>시작-반지름-사이각-현의 방향.</summary>
	private void FromRadiusAngleChord(double chordDir)
	{
		if (this._start is not { } s) return;
		double inc = this._pendingAngle;
		double chord = 2 * this._pendingRadius * Math.Sin(Math.Abs(inc) / 2);
		this._end = (s.X + chord * Math.Cos(chordDir), s.Y + chord * Math.Sin(chordDir));
		this.FromAngle(inc);
	}

	/// <summary>리본에서 고른 방식이면 중심 다음 단계를 그 방식으로.</summary>
	private void ApplyPresetAfterCenter()
	{
		if (this._await != "endOrOpt") return;
		switch (this.StartMode)
		{
			case ArcMode.StartCenterAngle or ArcMode.CenterStartAngle:
				this._await = "angle"; this.Prompt = "포함된 각도:"; break;
			case ArcMode.StartCenterLength or ArcMode.CenterStartLength:
				this._await = "chord"; this.Prompt = "현 길이:"; break;
		}
	}

	private double AngleFromPoint((double X, double Y) p)
	{
		var refPt = this._center ?? this._start!.Value;
		return Math.Atan2(p.Y - refPt.Y, p.X - refPt.X);
	}

	private void FromAngle(double included)
	{
		if (this._center is { } c && this._start is { } s)
		{
			double r = Num.Dist(c, s);
			double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X);
			if (included >= 0) this.AddArc(new XY(c.X, c.Y), r, a0, a0 + included);
			else this.AddArc(new XY(c.X, c.Y), r, a0 + included, a0);
		}
		else if (this._start is { } s2 && this._end is { } e)
		{
			// 시작-끝-사이각: 중심은 현의 수직이등분선 위
			double chord = Num.Dist(s2, e);
			if (Math.Abs(included) < 1e-9 || chord < 1e-12) return;
			double r = chord / (2 * Math.Sin(Math.Abs(included) / 2));
			double h = r * Math.Cos(included / 2) * Math.Sign(included);
			double mx = (s2.X + e.X) / 2, my = (s2.Y + e.Y) / 2;
			double nx = -(e.Y - s2.Y) / chord, ny = (e.X - s2.X) / chord;
			var c2 = new XY(mx + nx * h, my + ny * h);
			double a0 = Math.Atan2(s2.Y - c2.Y, s2.X - c2.X), a1 = Math.Atan2(e.Y - c2.Y, e.X - c2.X);
			if (included > 0) this.AddArc(c2, r, a0, a1);
			else this.AddArc(c2, r, a1, a0);
		}
	}

	private void FromChord(double chord)
	{
		if (this._center is not { } c || this._start is not { } s) return;
		double r = Num.Dist(c, s);
		if (chord <= 0 || chord > 2 * r) return;
		double inc = 2 * Math.Asin(chord / (2 * r));
		double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X);
		this.AddArc(new XY(c.X, c.Y), r, a0, a0 + inc);
	}

	private void FromRadius(double r)
	{
		if (this._start is not { } s || this._end is not { } e) return;
		double chord = Num.Dist(s, e);
		if (r < chord / 2) { this.Ctx?.Print("반지름이 너무 작습니다."); return; }
		double h = Math.Sqrt(r * r - chord * chord / 4);
		double mx = (s.X + e.X) / 2, my = (s.Y + e.Y) / 2;
		double nx = -(e.Y - s.Y) / chord, ny = (e.X - s.X) / chord;
		var c = new XY(mx + nx * h, my + ny * h); // 시작→끝 반시계 작은 호
		this.AddArc(c, r, Math.Atan2(s.Y - c.Y, s.X - c.X), Math.Atan2(e.Y - c.Y, e.X - c.X));
	}

	private void FromDirection(double dirAngle)
	{
		if (this._start is not { } s || this._end is not { } e) return;
		// 시작점에서 접선 방향 → 사이각 = 2 × (접선과 현 사이 각)
		double chordAng = Math.Atan2(e.Y - s.Y, e.X - s.X);
		double theta = Math.Atan2(Math.Sin(chordAng - dirAngle), Math.Cos(chordAng - dirAngle));
		this.FromAngle(2 * theta);
	}

	/// <summary>세 점을 지나는 호 (점 순서대로).</summary>
	public static Arc? CreateArcFrom3Points((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3)
	{
		if (CircleCommand.Circumcircle(p1, p2, p3) is not { } cc) return null;
		double a0 = Math.Atan2(p1.Y - cc.C.Y, p1.X - cc.C.X), a1 = Math.Atan2(p2.Y - cc.C.Y, p2.X - cc.C.X), a2 = Math.Atan2(p3.Y - cc.C.Y, p3.X - cc.C.X);
		bool ccw = Geom.Norm(a1 - a0) < Geom.Norm(a2 - a0);
		return new Arc { Center = Num.Z(cc.C.X, cc.C.Y), Radius = cc.R, StartAngle = Geom.Norm(ccw ? a0 : a2), EndAngle = Geom.Norm(ccw ? a2 : a0) };
	}

	private void AddArc(XY c, double r, double a0, double a1)
	{
		if (r <= 1e-9) return;
		this.Ctx!.Doc.AddEntities(new Arc { Center = Num.Z(c.X, c.Y), Radius = r, StartAngle = Geom.Norm(a0), EndAngle = Geom.Norm(a1) });
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._await == "third" && this._start is { } s && this._second is { } m && CircleCommand.Circumcircle(s, m, (cursorX, cursorY)) is { } cc)
		{
			double a0 = Math.Atan2(s.Y - cc.C.Y, s.X - cc.C.X), a1 = Math.Atan2(m.Y - cc.C.Y, m.X - cc.C.X), a2 = Math.Atan2(cursorY - cc.C.Y, cursorX - cc.C.X);
			bool ccw = Geom.Norm(a1 - a0) < Geom.Norm(a2 - a0);
			yield return new Arc { Center = Num.Z(cc.C.X, cc.C.Y), Radius = cc.R, StartAngle = Geom.Norm(ccw ? a0 : a2), EndAngle = Geom.Norm(ccw ? a2 : a0) };
		}
		else if (this._await == "endOrOpt" && this._center is { } c && this._start is { } st)
		{
			double r = Num.Dist(c, st);
			yield return new Arc { Center = Num.Z(c.X, c.Y), Radius = r, StartAngle = Geom.Norm(Math.Atan2(st.Y - c.Y, st.X - c.X)), EndAngle = Geom.Norm(Math.Atan2(cursorY - c.Y, cursorX - c.X)) };
		}
		else if (this._await is "center" or "start2" or "end" or "dirEnd" or "radiusEnd" or "angleEnd" or "angleCenter" && (this._start ?? this._center) is { } p0)
		{
			yield return new Line(Num.Z(p0.X, p0.Y), Num.Z(cursorX, cursorY));
		}
	}
}

/// <summary>
/// PLINE (기존 CAD와 같은 문구·옵션).
/// 시작: "마지막 점 사용하려면 ENTER/[따라가기]/&lt;폴리라인의 시작&gt;:".
/// 선: "[호그리기(A)/닫기(C)/거리(D)/따라가기(F)/반폭(H)/너비(W)/취소(U)]/&lt;다음 점&gt;:" (닫기는 두 조각부터, 취소는 한 조각부터).
/// 호: "[각도(A)/중심(CE)/닫기(CL)/방향(D)/반폭(H)/선그리기(L)/반지름(R)/두번째점(S)/너비(W)/취소(U)]/&lt;호의 끝&gt;:".
/// 폭은 시작·끝 폭을 따로 묻고(조각마다 다를 수 있음), 호는 따로 정하지 않으면 앞 조각에 접한다.
/// </summary>
public sealed class PlineCommand : EditorCommand
{
	private const string AngleAsk = "부분의 포함 각도 (0보다 크면 반시계 방향, 0은 직선, 0보다 작으면 시계 방향):";

	private readonly List<LwPolyline.Vertex> _verts = new();
	private bool _arcMode;
	private bool _follow;
	private double _startWidth, _endWidth;

	// 지금 묻는 값 (null이면 기본 프롬프트)
	private string? _await;
	private double _pendingAngle;
	private double _pendingRadius;
	private double _pendingDist;
	private XY _pendingPoint;

	public override string Name => "PLINE";
	public override string[] Aliases => new[] { "PL" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._verts.Clear();
		this._arcMode = false;
		this._follow = false;
		this._await = null;
		this._startWidth = this._endWidth = Math.Max(0, ctx.Doc.Document.Header.PolylineWidthDefault);
		this.Prompt = "마지막 점 사용하려면 ENTER/[따라가기]/<폴리라인의 시작>:";
	}

	private int Segments => Math.Max(0, this._verts.Count - 1);

	private void UpdatePrompt()
	{
		this._await = null;
		if (this._arcMode)
		{
			this.Prompt = "[각도(A)/중심(CE)/닫기(CL)/방향(D)/반폭(H)/선그리기(L)/반지름(R)/두번째점(S)/너비(W)/취소(U)]/<호의 끝>:";
			return;
		}
		string close = this.Segments >= 2 ? "닫기(C)/" : string.Empty;
		string undo = this.Segments >= 1 ? "/취소(U)" : string.Empty;
		this.Prompt = $"[호그리기(A)/{close}거리(D)/따라가기(F)/반폭(H)/너비(W){undo}]/<다음 점>:";
	}

	private void Ask(string key, string prompt)
	{
		this._await = key;
		this.Prompt = prompt;
	}

	private XY LastPt => this._verts[^1].Location;

	/// <summary>마지막 조각의 끝 방향 (호 접선용).</summary>
	private double? LastDirection()
	{
		if (this._verts.Count < 2) return null;
		var a = this._verts[^2];
		var b = this._verts[^1];
		var seg = Geom.PolySeg(a.Location, b.Location, a.Bulge);
		if (seg is Geom.ArcSeg arc)
		{
			double end = arc.Start + arc.Sweep;
			return end + (arc.Sweep >= 0 ? Math.PI / 2 : -Math.PI / 2);
		}
		return Math.Atan2(b.Location.Y - a.Location.Y, b.Location.X - a.Location.X);
	}

	/// <summary>시작 방향(dir)에 접하며 end로 가는 호의 bulge.</summary>
	private double BulgeTangent(XY end, double? dir = null)
	{
		var s = this.LastPt;
		double chord = Math.Atan2(end.Y - s.Y, end.X - s.X);
		double d = dir ?? this.LastDirection() ?? chord;
		double theta = Math.Atan2(Math.Sin(chord - d), Math.Cos(chord - d));
		return Math.Tan(theta / 2); // 사이각 = 2θ → bulge = tan(사이각/4)
	}

	private static XY Rotate(XY p, XY c, double a)
	{
		double cs = Math.Cos(a), sn = Math.Sin(a);
		double dx = p.X - c.X, dy = p.Y - c.Y;
		return new XY(c.X + dx * cs - dy * sn, c.Y + dx * sn + dy * cs);
	}

	/// <summary>조각 하나 추가 (마지막 꼭짓점에 bulge·폭을 넣고 끝점을 더함). 다음 조각의 폭은 끝 폭.</summary>
	private void AddSegment(XY end, double bulge)
	{
		var last = this._verts[^1];
		this._verts[^1] = new LwPolyline.Vertex(last.Location) { Bulge = bulge, StartWidth = this._startWidth, EndWidth = this._endWidth };
		this._verts.Add(new LwPolyline.Vertex(end) { StartWidth = this._endWidth, EndWidth = this._endWidth });
		this._startWidth = this._endWidth;
		this.Ctx!.LastPoint = (end.X, end.Y);
		this._follow = false;
		this.UpdatePrompt();
	}

	private void AddLineTo(XY end) => this.AddSegment(end, 0);

	private static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		if (this._verts.Count == 0)
		{
			this._verts.Add(new LwPolyline.Vertex(p) { StartWidth = this._startWidth, EndWidth = this._endWidth });
			this.Ctx.LastPoint = (x, y);
			this.UpdatePrompt();
			return;
		}
		var s = this.LastPt;
		double dist = Math.Sqrt((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y));
		double ang = Math.Atan2(y - s.Y, x - s.X);
		switch (this._await)
		{
			// 값을 묻는 중에 찍은 점 = 그 점까지의 거리·방향
			case "W1": case "W2": case "H1": case "H2": case "DIST": case "R": case "RR": case "CLEN":
				this.OnText(Inv(dist));
				return;
			case "DANG": case "DIR": case "CHORD": case "A": case "RA": case "CEA":
				this.OnText(Inv(ang * 180 / Math.PI));
				return;
			case "S2":
				this._pendingPoint = p;
				this.Ask("S3", "호의 끝:");
				return;
			case "S3":
				this.AddSegment(p, ThreePointBulge(s, this._pendingPoint, p));
				return;
			case "DIREND":
				this.AddSegment(p, this.BulgeTangent(p, this._pendingAngle));
				return;
			case "CE":
				this._pendingPoint = p;
				this.Ask("CEEND", "[각도(A)/길이(L)]/<호의끝(E)>:");
				return;
			case "CEEND":
				{
					var c = this._pendingPoint;
					double sweep = Math.Atan2(y - c.Y, x - c.X) - Math.Atan2(s.Y - c.Y, s.X - c.X);
					while (sweep <= 0) sweep += 2 * Math.PI;
					this.AddSegment(Rotate(s, c, sweep), Math.Tan(sweep / 4));
					return;
				}
			case "AEND":
				this.AddSegment(p, Math.Tan(this._pendingAngle / 4));
				return;
			case "AC":
				this.AddSegment(Rotate(s, p, this._pendingAngle), Math.Tan(this._pendingAngle / 4));
				return;
			case "REND":
				{
					// 반지름과 끝점: 작은 호, 방향은 앞 조각에 가까운 쪽
					double r = Math.Max(this._pendingRadius, dist / 2);
					double sweep = 2 * Math.Asin(Math.Min(1, dist / (2 * r)));
					double dir = this.LastDirection() ?? ang;
					double cross = Math.Cos(dir) * (y - s.Y) - Math.Sin(dir) * (x - s.X);
					this.AddSegment(p, Math.Tan((cross >= 0 ? sweep : -sweep) / 4));
					return;
				}
		}

		if (this.Ctx.Ortho && !this._arcMode)
		{
			var o = CoordinateParser.ApplyOrtho((s.X, s.Y), (x, y));
			p = new XY(o.X, o.Y);
		}
		if (this._arcMode)
		{
			this.AddSegment(p, this.BulgeTangent(p));
		}
		else
		{
			this.AddLineTo(p);
		}
	}

	private static double ThreePointBulge(XY a, XY m, XY b)
	{
		// 세 점을 지나는 원의 중심 → a에서 m을 거쳐 b까지의 부호 있는 사이각
		double d = 2 * (a.X * (m.Y - b.Y) + m.X * (b.Y - a.Y) + b.X * (a.Y - m.Y));
		if (Math.Abs(d) < 1e-12) return 0;
		double a2 = a.X * a.X + a.Y * a.Y, m2 = m.X * m.X + m.Y * m.Y, b2 = b.X * b.X + b.Y * b.Y;
		double cx = (a2 * (m.Y - b.Y) + m2 * (b.Y - a.Y) + b2 * (a.Y - m.Y)) / d;
		double cy = (a2 * (b.X - m.X) + m2 * (a.X - b.X) + b2 * (m.X - a.X)) / d;
		double sa = Math.Atan2(a.Y - cy, a.X - cx), sb = Math.Atan2(b.Y - cy, b.X - cx);
		bool ccw = (m.X - a.X) * (b.Y - a.Y) - (m.Y - a.Y) * (b.X - a.X) > 0;
		double sweep = sb - sa;
		if (ccw)
		{
			while (sweep <= 0) sweep += 2 * Math.PI;
		}
		else
		{
			while (sweep >= 0) sweep -= 2 * Math.PI;
		}
		return Math.Tan(sweep / 4);
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = Num.TryParse(t, out double v);

		if (this._verts.Count == 0)
		{
			if (t == "F" && this.Ctx.LastPoint is { } lp)
			{
				this.OnPoint(lp.X, lp.Y);
				this._follow = true;
			}
			return;
		}

		if (this._await != null)
		{
			this.OnValue(t, num, v);
			return;
		}

		if (num && this._follow && !this._arcMode && this.LastDirection() is { } fd)
		{
			// 따라가기: 앞 조각 방향으로 그 길이
			var s = this.LastPt;
			this.AddLineTo(new XY(s.X + v * Math.Cos(fd), s.Y + v * Math.Sin(fd)));
			return;
		}

		switch (t)
		{
			case "W":
				this.Ask("W1", $"시작 폭 <{this._startWidth:0.0000}>:");
				return;
			case "H":
				this.Ask("H1", $"시작 반폭 <{this._startWidth / 2:0.0000}>:");
				return;
			case "U" when this.Segments >= 1:
				this._verts.RemoveAt(this._verts.Count - 1);
				var lastV = this._verts[^1];
				this._verts[^1] = new LwPolyline.Vertex(lastV.Location) { StartWidth = lastV.StartWidth, EndWidth = lastV.EndWidth };
				this.Ctx.LastPoint = (lastV.Location.X, lastV.Location.Y);
				this.UpdatePrompt();
				return;
		}

		if (!this._arcMode)
		{
			switch (t)
			{
				case "A":
					this._arcMode = true;
					this.UpdatePrompt();
					return;
				case "D":
					this.Ask("DIST", "세그먼트 거리:");
					return;
				case "F" when this.Segments >= 1:
					this._follow = true;
					return;
				case "C" or "CL" when this.Segments >= 2:
					this.Close();
					return;
			}
			return;
		}

		switch (t)
		{
			case "L":
				this._arcMode = false;
				this.UpdatePrompt();
				return;
			case "A":
				this.Ask("A", AngleAsk);
				return;
			case "CE":
				this.Ask("CE", "중심점:");
				return;
			case "CL" when this.Segments >= 1:
				this.Close();
				return;
			case "D":
				this.Ask("DIR", "시작부터의 방향:");
				return;
			case "R":
				this.Ask("R", "반지름:");
				return;
			case "S":
				this.Ask("S2", "두번째 점:");
				return;
		}
	}

	/// <summary>현의 방향 기본값 = 접선 방향 + 사이각/2.</summary>
	private void AskChord()
	{
		double tangent = (this.LastDirection() ?? 0) * 180 / Math.PI;
		this._pendingDist = tangent + this._pendingAngle * 90 / Math.PI;
		this.Ask("CHORD", $"코드의 방향 <{this._pendingDist:0.####}>:");
	}

	/// <summary>묻던 값에 대한 입력 (숫자 또는 하위 옵션).</summary>
	private void OnValue(string t, bool num, double v)
	{
		var s = this.LastPt;
		double rad = v * Math.PI / 180;
		switch (this._await)
		{
			case "W1" when num && v >= 0:
				this._startWidth = v;
				this.Ask("W2", $"끝 폭 <{v:0.0000}>:");
				return;
			case "W2" when num && v >= 0:
				this._endWidth = v;
				this.UpdatePrompt();
				return;
			case "H1" when num && v >= 0:
				this._startWidth = v * 2;
				this.Ask("H2", $"끝 반폭 <{v:0.0000}>:");
				return;
			case "H2" when num && v >= 0:
				this._endWidth = v * 2;
				this.UpdatePrompt();
				return;
			case "DIST" when num:
				this._pendingDist = v;
				this.Ask("DANG", "세그먼트의 각도:");
				return;
			case "DANG" when num:
				this.AddLineTo(new XY(s.X + this._pendingDist * Math.Cos(rad), s.Y + this._pendingDist * Math.Sin(rad)));
				return;
			case "A" when num:
				this._pendingAngle = rad;
				this.Ask("AEND", "[중심(C)/반지름(R)]/<호의끝(E)>:");
				return;
			case "AEND" when t == "C":
				this.Ask("AC", "중심점:");
				return;
			case "AEND" when t == "R":
				this.Ask("RR", "반지름:");
				return;
			case "RR" when num && v > 0:
				this._pendingRadius = v;
				this.AskChord();
				return;
			case "CHORD" when num:
				{
					double sweep = this._pendingAngle;
					double c = 2 * this._pendingRadius * Math.Sin(Math.Abs(sweep) / 2);
					this.AddSegment(new XY(s.X + c * Math.Cos(rad), s.Y + c * Math.Sin(rad)), Math.Tan(sweep / 4));
					return;
				}
			case "R" when num && v > 0:
				this._pendingRadius = v;
				this.Ask("REND", "[각도(A)]/<호의 끝>:");
				return;
			case "REND" when t == "A":
				this.Ask("RA", AngleAsk);
				return;
			case "RA" when num:
				this._pendingAngle = rad;
				this.AskChord();
				return;
			case "CEEND" when t == "A":
				this.Ask("CEA", AngleAsk);
				return;
			case "CEEND" when t == "L":
				this.Ask("CLEN", "현의 길이:");
				return;
			case "CEA" when num:
				this.AddSegment(Rotate(s, this._pendingPoint, rad), Math.Tan(rad / 4));
				return;
			case "CLEN" when num:
				{
					var c = this._pendingPoint;
					double r = Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
					if (r < 1e-12 || Math.Abs(v) > 2 * r) return;
					double sweep = 2 * Math.Asin(Math.Abs(v) / (2 * r));
					if (v < 0) sweep = 2 * Math.PI - sweep;
					this.AddSegment(Rotate(s, c, sweep), Math.Tan(sweep / 4));
					return;
				}
			case "DIR" when num:
				this._pendingAngle = rad;
				this.Ask("DIREND", "호의 끝:");
				return;
		}
	}

	private void Close()
	{
		var first = this._verts[0].Location;
		var last = this._verts[^1];
		double bulge = this._arcMode ? this.BulgeTangent(first) : 0;
		this._verts[^1] = new LwPolyline.Vertex(last.Location) { Bulge = bulge, StartWidth = this._startWidth, EndWidth = this._endWidth };
		this.Create(closed: true);
		this.Finish();
	}

	public override void OnEnter()
	{
		if (this.Ctx == null) return;
		if (this._verts.Count == 0)
		{
			// Enter: 마지막 점에서 시작
			if (this.Ctx.LastPoint is { } lp)
			{
				this.OnPoint(lp.X, lp.Y);
			}
			return;
		}
		switch (this._await)
		{
			case "W1":
				this.Ask("W2", $"끝 폭 <{this._startWidth:0.0000}>:");
				return;
			case "H1":
				this.Ask("H2", $"끝 반폭 <{this._startWidth / 2:0.0000}>:");
				return;
			case "W2":
			case "H2":
				this._endWidth = this._startWidth;
				this.UpdatePrompt();
				return;
			case "CHORD":
				this.OnText(Inv(this._pendingDist));
				return;
			case not null:
				this.UpdatePrompt();
				return;
		}
		if (this._verts.Count >= 2) this.Create(closed: false);
		this.Finish();
	}

	private void Create(bool closed)
	{
		var verts = this._verts.ToList();
		if (!closed)
		{
			// 열린 폴리선의 마지막 꼭짓점 폭은 쓰이지 않으므로 앞 조각의 끝 폭과 같게 둔다
			var l = verts[^1];
			verts[^1] = new LwPolyline.Vertex(l.Location) { StartWidth = verts[^2].EndWidth, EndWidth = verts[^2].EndWidth };
		}
		var pl = new LwPolyline { IsClosed = closed };
		bool constant = verts.All(v => Math.Abs(v.StartWidth - verts[0].StartWidth) < 1e-12 && Math.Abs(v.EndWidth - v.StartWidth) < 1e-12);
		foreach (var v in verts)
		{
			pl.Vertices.Add(new LwPolyline.Vertex(v.Location) { Bulge = v.Bulge, StartWidth = constant ? 0 : v.StartWidth, EndWidth = constant ? 0 : v.EndWidth });
		}
		if (constant) pl.ConstantWidth = verts[0].StartWidth;
		this.Ctx!.Doc.Document.Header.PolylineWidthDefault = this._endWidth;
		this.Ctx.Doc.AddEntities(pl);
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._verts.Count == 0) yield break;
		var pl = new LwPolyline();
		var p = new XY(cursorX, cursorY);
		bool tangentPreview = this._await == null && this._arcMode;
		bool linePreview = this._await == null && !this._arcMode;
		for (int i = 0; i < this._verts.Count; i++)
		{
			var v = this._verts[i];
			double bulge = i == this._verts.Count - 1 && tangentPreview ? this.BulgeTangent(p) : v.Bulge;
			pl.Vertices.Add(new LwPolyline.Vertex(v.Location) { Bulge = bulge });
		}
		if (tangentPreview || linePreview)
		{
			pl.Vertices.Add(new LwPolyline.Vertex(p));
		}
		if (pl.Vertices.Count >= 2)
		{
			yield return pl;
		}
	}
}

/// <summary>POLYGON: 면의 수 → 중심 + 내접(I)/외접(C) + 반지름, 또는 모서리(E) 두 점.</summary>
public sealed class PolygonCommand : EditorCommand
{
	private int _sides = 4;
	private int _step;
	private (double X, double Y) _center;
	private (double X, double Y) _edge1;
	private bool _inscribed = true;
	private bool _edgeMode;

	public override string Name => "POLYGON";
	public override string[] Aliases => new[] { "POL" };

	/// <summary>리본에서 고른 방식: null = 묻기, true = 내접, false = 외접.</summary>
	public bool? PresetInscribed { get; set; }

	public bool PresetEdge { get; set; }

	private static int LastSides = 4;
	private static bool LastInscribed;
	private static double LastWidth;
	private bool _multiple;

	private string SidesPrompt => this._multiple ? $"다각형:  [너비(W)]/<변의 개수> <{this._sides}>:" : $"다각형:  [다중(M)/너비(W)]/<변의 개수> <{this._sides}>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._sides = LastSides;
		this._step = 0;
		this._edgeMode = false;
		this._multiple = false;
		this.Prompt = this.SidesPrompt;
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		switch (this._step)
		{
			case 0 when t == "M" && !this._multiple:
				this._multiple = true;
				this.Prompt = this.SidesPrompt;
				break;
			case 0 when t == "W":
				this._step = 20;
				this.Prompt = $"폴리선 폭 <{LastWidth:0.0000}> :";
				break;
			case 20:
				if (Num.TryParse(t, out double w) && w >= 0) LastWidth = w;
				this._step = 0;
				this.Prompt = this.SidesPrompt;
				break;
			case 0:
				if (int.TryParse(t, out int n) && n >= 3 && n <= 1024) this._sides = LastSides = n;
				else
				{
					this.Ctx?.Print("다각형은 3 ~ 1024 개의 변을 가질 수 있습니다.");
					return;
				}
				this.AfterSides();
				break;
			case 1 when t == "E":
				this._edgeMode = true;
				this.Prompt = "모서리의 첫번째 끝점:";
				break;
			case 2 when t is "I" or "C":
				this._inscribed = LastInscribed = t == "I";
				this._step = 3;
				this.Prompt = "원의 반지름을 지정하세요:";
				break;
			case 3 when Num.TryParse(t, out double r) && r > 0:
				this.Create(this.Vertices(this._center, r, Math.PI / 2));
				break;
		}
	}

	public override void OnEnter()
	{
		if (this._step == 0) this.AfterSides();
		else if (this._step == 20) { this._step = 0; this.Prompt = this.SidesPrompt; }
		else if (this._step == 2) { this._inscribed = LastInscribed; this._step = 3; this.Prompt = "원의 반지름을 지정하세요:"; }
		else if (this._multiple && this._step == 1) this.Finish();
	}

	private void AfterSides()
	{
		this._step = 1;
		if (this.PresetEdge)
		{
			this._edgeMode = true;
			this.Prompt = "모서리의 첫번째 끝점:";
		}
		else
		{
			this.Prompt = "다각형 중심 지정 or [모서리(E)]:";
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._step == 1 && this._edgeMode)
		{
			this._edge1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 10;
			this.Prompt = "모서리의 두번째 끝점:";
			return;
		}
		if (this._step == 10)
		{
			this.Create(this.EdgeVertices(this._edge1, (x, y)));
			return;
		}
		if (this._step == 1)
		{
			this._center = (x, y);
			this.Ctx.LastPoint = (x, y);
			if (this.PresetInscribed is { } ins)
			{
				this._inscribed = ins;
				this._step = 3;
				this.Prompt = "원의 반지름을 지정하세요:";
			}
			else
			{
				this._step = 2;
				this.Prompt = $"옵션 입력 [내접원(I)/외접원(C)] <{(LastInscribed ? "I" : "C")}>:";
			}
			return;
		}
		if (this._step == 3)
		{
			double r = Num.Dist(this._center, (x, y));
			double ang = Math.Atan2(y - this._center.Y, x - this._center.X);
			this.Create(this.Vertices(this._center, r, ang));
		}
	}

	/// <summary>내접: 꼭짓점이 원 위(커서 방향 = 꼭짓점), 외접: 변의 가운데가 원 위(커서 방향 = 변 가운데).</summary>
	private List<XY> Vertices((double X, double Y) c, double r, double ang)
	{
		var pts = new List<XY>();
		double R = this._inscribed ? r : r / Math.Cos(Math.PI / this._sides);
		double a0 = this._inscribed ? ang : ang + Math.PI / this._sides;
		for (int i = 0; i < this._sides; i++)
		{
			double a = a0 + 2 * Math.PI * i / this._sides;
			pts.Add(new XY(c.X + R * Math.Cos(a), c.Y + R * Math.Sin(a)));
		}
		return pts;
	}

	private List<XY> EdgeVertices((double X, double Y) a, (double X, double Y) b)
	{
		var pts = new List<XY> { new(a.X, a.Y) };
		double len = Num.Dist(a, b), dir = Math.Atan2(b.Y - a.Y, b.X - a.X);
		var cur = new XY(a.X, a.Y);
		for (int i = 1; i < this._sides; i++)
		{
			cur = new XY(cur.X + len * Math.Cos(dir), cur.Y + len * Math.Sin(dir));
			pts.Add(cur);
			dir += 2 * Math.PI / this._sides;
		}
		return pts;
	}

	private void Create(List<XY> pts)
	{
		var pl = new LwPolyline { IsClosed = true, ConstantWidth = LastWidth };
		foreach (var p in pts) pl.Vertices.Add(new LwPolyline.Vertex(p));
		this.Ctx!.Doc.AddEntities(pl);
		if (this._multiple)
		{
			// 다중: 같은 설정으로 다음 다각형
			this._edgeMode = false;
			this.AfterSides();
			return;
		}
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		List<XY>? pts = null;
		if (this._step == 3)
		{
			double r = Num.Dist(this._center, (cursorX, cursorY));
			if (r > 1e-9) pts = this.Vertices(this._center, r, Math.Atan2(cursorY - this._center.Y, cursorX - this._center.X));
		}
		else if (this._step == 10)
		{
			pts = this.EdgeVertices(this._edge1, (cursorX, cursorY));
		}
		if (pts != null)
		{
			var pl = new LwPolyline { IsClosed = true };
			foreach (var p in pts) pl.Vertices.Add(new LwPolyline.Vertex(p));
			yield return pl;
		}
	}
}

/// <summary>
/// ELLIPSE: 축 끝점 두 개 + 다른 축 거리(또는 회전 R), 중심(C) 방식, 타원형 호(A: 시작·끝 각도).
/// </summary>
public sealed class EllipseCommand : EditorCommand
{
	private int _step;
	private bool _centerMode, _arc, _rotation;
	private XY _a1, _a2, _center;
	private double _ratio;
	private double _startParam;

	public override string Name => "ELLIPSE";
	public override string[] Aliases => new[] { "EL" };

	public bool PresetCenter { get; set; }
	public bool PresetArc { get; set; }
	public bool PresetRotation { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._centerMode = this.PresetCenter;
		this._arc = this.PresetArc;
		this._rotation = this.PresetRotation;
		this.Prompt = this._centerMode ? "타원의 중심:" : this._arc ? "[중심(C)]/<타원 축의 첫번째 끝>:" : "[호(A)/중심(C)]/<타원 축의 첫번째 끝>:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._step == 0 && t == "A") { this._arc = true; this.Prompt = "[중심(C)]/<타원 축의 첫번째 끝>:"; return; }
		if (this._step == 0 && t == "C") { this._centerMode = true; this.Prompt = "타원의 중심:"; return; }
		if (this._step == 2 && t == "R") { this._rotation = true; this.Prompt = "장축 둘레 회전:"; return; }
		if (this._step == 4 && t == "I") { this._included = true; this.Prompt = "호의 사이각 <180>:"; return; }
		if (!Num.TryParse(t, out double v)) return;
		if (this._step == 2)
		{
			double major = Geom.Dist(this._a1, this._a2) / (this._centerMode ? 1 : 2);
			this.SetRatio(this._rotation ? Math.Cos(Num.Rad(v)) : v / major);
		}
		else if (this._step == 3) this.SetStart(Num.Rad(v));
		else if (this._step == 4) this.SetEnd(this._included ? this._startParam + Num.Rad(v) : Num.Rad(v));
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		switch (this._step)
		{
			case 0:
				if (this._centerMode) { this._center = p; this.Prompt = "축의 끝점:"; }
				else { this._a1 = p; this.Prompt = "축의 두번째 끝:"; }
				this.Ctx.LastPoint = (x, y);
				this._step = 1;
				break;
			case 1:
				if (this._centerMode) { this._a1 = this._center; this._a2 = p; }
				else { this._a2 = p; this._center = new XY((this._a1.X + p.X) / 2, (this._a1.Y + p.Y) / 2); }
				this._step = 2;
				this.Prompt = this._rotation ? "장축 둘레 회전:" : "[회전(R)]/<다른 축>:";
				break;
			case 2:
				{
					double major = this.MajorRadius();
					if (this._rotation)
					{
						this.SetRatio(Math.Abs(Math.Cos(Math.Atan2(y - this._center.Y, x - this._center.X))));
					}
					else
					{
						// 다른 축 거리 = 중심에서 장축 방향에 수직인 거리
						var u = this.MajorDir();
						double d = Math.Abs(-(x - this._center.X) * u.Y + (y - this._center.Y) * u.X);
						this.SetRatio(d / major);
					}
					break;
				}
			case 3:
				this.SetStart(this.ParamAt(p));
				break;
			case 4:
				this.SetEnd(this.ParamAt(p));
				break;
		}
	}

	private double MajorRadius() => this._centerMode ? Geom.Dist(this._center, this._a2) : Geom.Dist(this._a1, this._a2) / 2;

	private XY MajorDir()
	{
		var end = this._centerMode ? this._a2 : this._a2;
		double dx = end.X - this._center.X, dy = end.Y - this._center.Y, l = Math.Sqrt(dx * dx + dy * dy);
		return l < 1e-12 ? new XY(1, 0) : new XY(dx / l, dy / l);
	}

	private double ParamAt(XY p)
	{
		var u = this.MajorDir();
		double lx = (p.X - this._center.X) * u.X + (p.Y - this._center.Y) * u.Y;
		double ly = -(p.X - this._center.X) * u.Y + (p.Y - this._center.Y) * u.X;
		double r = Math.Max(this._ratio, 1e-9);
		return Math.Atan2(ly / r, lx);
	}

	private void SetRatio(double ratio)
	{
		ratio = Math.Abs(ratio);
		if (ratio < 1e-9) return;
		this._ratio = ratio;
		if (this._arc)
		{
			this._step = 3;
			this.Prompt = "[매개변수(P)]/<호의 시작 각도>:";
			return;
		}
		this.Create(0, 2 * Math.PI);
	}

	private void SetStart(double a)
	{
		this._startParam = a;
		this._step = 4;
		this.Prompt = "[매개변수(P)/포함각도(I)]/<끝 각도>:";
	}

	private bool _included;

	private void SetEnd(double a) => this.Create(this._startParam, a);

	private Ellipse Build(double t0, double t1)
	{
		double major = this.MajorRadius();
		var u = this.MajorDir();
		double ratio = this._ratio;
		var axis = new XYZ(u.X * major, u.Y * major, 0);
		if (ratio > 1)
		{
			// 장축이 다른 축이면 바꾼다
			axis = new XYZ(-u.Y * major * ratio, u.X * major * ratio, 0);
			ratio = 1 / ratio;
			t0 -= Math.PI / 2;
			t1 -= Math.PI / 2;
		}
		return new Ellipse { Center = Num.Z(this._center.X, this._center.Y), MajorAxisEndPoint = axis, RadiusRatio = ratio, StartParameter = Geom.Norm(t0), EndParameter = Math.Abs(t1 - t0 - 2 * Math.PI) < 1e-12 ? 2 * Math.PI : Geom.Norm(t1) };
	}

	private void Create(double t0, double t1)
	{
		this.Ctx!.Doc.AddEntities(this.Build(t0, t1));
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 1)
		{
			yield return new Line(Num.Z(this._centerMode ? this._center.X : this._a1.X, this._centerMode ? this._center.Y : this._a1.Y), Num.Z(cursorX, cursorY));
		}
		else if (this._step == 2 && !this._rotation)
		{
			var u = this.MajorDir();
			double d = Math.Abs(-(cursorX - this._center.X) * u.Y + (cursorY - this._center.Y) * u.X);
			double major = this.MajorRadius();
			if (d > 1e-9 && major > 1e-9)
			{
				double save = this._ratio;
				this._ratio = d / major;
				yield return this.Build(0, 2 * Math.PI);
				this._ratio = save;
			}
		}
	}
}

/// <summary>
/// SPLINE: 맞춤점(기본, 곡선이 점을 지남) 또는 조정점(CV) 방식. 닫기(C), 명령 취소(U), Enter = 끝.
/// 맞춤점은 3차 B-스플라인 보간(현의 길이 매개변수)으로 조정점·매듭을 계산해 함께 저장한다.
/// </summary>
public sealed class SplineCommand : EditorCommand
{
	private readonly List<XY> _pts = new();

	public override string Name => "SPLINE";
	public override string[] Aliases => new[] { "SPL" };

	/// <summary>true = 조정점(CV) 방식.</summary>
	public bool ControlVertices { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._tangentStep = 0;
		ctx.Print(this.ControlVertices ? "현재 설정: 순서 = CV  매듭 = 균일" : "현재 설정: 순서 = 적합  매듭 = 코드(C)");
		this.Prompt = "SPLINE 첫번째 점 지정 또는 [순서(M)/매듭(K)/객체(E)]:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this._tangentStep > 0)
		{
			this.NextTangent();
			return;
		}
		this._pts.Add(new XY(x, y));
		if (this.Ctx != null) this.Ctx.LastPoint = (x, y);
		this.UpdatePointPrompt();
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (t == "M" && this._pts.Count == 0)
		{
			this.ControlVertices = !this.ControlVertices;
			this.Start(this.Ctx!);
			return;
		}
		if (t == "U" && this._pts.Count > 0)
		{
			this._pts.RemoveAt(this._pts.Count - 1);
			this.UpdatePointPrompt();
		}
		if (t == "C" && this._pts.Count >= 3)
		{
			this._pts.Add(this._pts[0]);
			this.Create();
		}
	}

	private int _tangentStep;

	private void UpdatePointPrompt()
	{
		this.Prompt = this._pts.Count < 2 ? "SPLINE 다음 점 지정 또는 [공차(F)]:" : "[닫기(C)/적합_허용오차(F)/취소(U)]/<다음점(N)>:";
	}

	/// <summary>점을 다 찍은 뒤 기존 CAD처럼 시작·끝 접선을 묻는다 (Enter = 자동).</summary>
	private void NextTangent()
	{
		this._tangentStep++;
		if (this._tangentStep == 1)
		{
			this.Prompt = "시작 접점 선택:";
			return;
		}
		if (this._tangentStep == 2)
		{
			this.Prompt = "끝 접점의 접선 입력:";
			return;
		}
		this.Create();
	}

	public override void OnEnter()
	{
		if (this._pts.Count >= 2 && !this.ControlVertices)
		{
			this.NextTangent();
			return;
		}
		if (this._pts.Count >= 2) this.Create();
		this.Finish();
	}

	private void Create()
	{
		if (Build(this._pts, this.ControlVertices) is { } sp)
		{
			this.Ctx!.Doc.AddEntities(sp);
		}
		this.Finish();
	}

	/// <summary>스플라인 만들기 (맞춤점이면 보간, 조정점이면 그대로 + 균일 매듭).</summary>
	public static Spline? Build(IReadOnlyList<XY> pts, bool controlVertices)
	{
		if (pts.Count < 2) return null;
		var sp = new Spline { Degree = 3 };
		if (controlVertices)
		{
			int degree = Math.Min(3, pts.Count - 1);
			sp.Degree = degree;
			foreach (var p in pts) sp.ControlPoints.Add(Num.Z(p.X, p.Y));
			foreach (double k in ClampedUniformKnots(pts.Count, degree)) sp.Knots.Add(k);
			return sp;
		}
		var (ctrl, knots, deg) = Interpolate(pts);
		sp.Degree = deg;
		foreach (var p in pts) sp.FitPoints.Add(Num.Z(p.X, p.Y));
		foreach (var c in ctrl) sp.ControlPoints.Add(Num.Z(c.X, c.Y));
		foreach (double k in knots) sp.Knots.Add(k);
		return sp;
	}

	private static List<double> ClampedUniformKnots(int n, int p)
	{
		var k = new List<double>();
		for (int i = 0; i <= p; i++) k.Add(0);
		int inner = n - p - 1;
		for (int i = 1; i <= inner; i++) k.Add((double)i / (inner + 1));
		for (int i = 0; i <= p; i++) k.Add(1);
		return k;
	}

	/// <summary>전역 보간 (NURBS Book 알고리즘 9.1): 현의 길이 매개변수 + 평균 매듭.</summary>
	public static (List<XY> Ctrl, List<double> Knots, int Degree) Interpolate(IReadOnlyList<XY> q)
	{
		int n = q.Count - 1;
		int p = Math.Min(3, n);
		var u = new double[n + 1];
		double total = 0;
		for (int i = 1; i <= n; i++) total += Geom.Dist(q[i], q[i - 1]);
		for (int i = 1; i <= n; i++) u[i] = u[i - 1] + (total > 0 ? Geom.Dist(q[i], q[i - 1]) / total : 1.0 / n);
		u[n] = 1;
		var knots = new List<double>();
		for (int i = 0; i <= p; i++) knots.Add(0);
		for (int j = 1; j <= n - p; j++)
		{
			double s = 0;
			for (int i = j; i <= j + p - 1; i++) s += u[i];
			knots.Add(s / p);
		}
		for (int i = 0; i <= p; i++) knots.Add(1);
		var a = new double[n + 1, n + 1];
		for (int i = 0; i <= n; i++)
			for (int j = 0; j <= n; j++)
				a[i, j] = Basis(j, p, u[i], knots, n);
		var cx = SolveLinear(a, q.Select(v => v.X).ToArray());
		var cy = SolveLinear(a, q.Select(v => v.Y).ToArray());
		var ctrl = new List<XY>();
		for (int i = 0; i <= n; i++) ctrl.Add(new XY(cx[i], cy[i]));
		return (ctrl, knots, p);
	}

	private static double Basis(int i, int p, double u, List<double> k, int n)
	{
		if (p == 0)
		{
			if (u >= k[i] && u < k[i + 1]) return 1;
			return Math.Abs(u - 1) < 1e-12 && i == n && k[i] < k[i + 1] ? 1 : 0;
		}
		double left = k[i + p] - k[i] > 1e-12 ? (u - k[i]) / (k[i + p] - k[i]) * Basis(i, p - 1, u, k, n) : 0;
		double right = k[i + p + 1] - k[i + 1] > 1e-12 ? (k[i + p + 1] - u) / (k[i + p + 1] - k[i + 1]) * Basis(i + 1, p - 1, u, k, n) : 0;
		return left + right;
	}

	private static double[] SolveLinear(double[,] a, double[] b)
	{
		int n = b.Length;
		var m = (double[,])a.Clone();
		var x = (double[])b.Clone();
		for (int c = 0; c < n; c++)
		{
			int piv = c;
			for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
			if (piv != c)
			{
				for (int k = 0; k < n; k++) (m[c, k], m[piv, k]) = (m[piv, k], m[c, k]);
				(x[c], x[piv]) = (x[piv], x[c]);
			}
			double d = m[c, c];
			if (Math.Abs(d) < 1e-14) continue;
			for (int r = 0; r < n; r++)
			{
				if (r == c) continue;
				double f = m[r, c] / d;
				if (f == 0) continue;
				for (int k = c; k < n; k++) m[r, k] -= f * m[c, k];
				x[r] -= f * x[c];
			}
		}
		for (int i = 0; i < n; i++) x[i] = Math.Abs(m[i, i]) > 1e-14 ? x[i] / m[i, i] : 0;
		return x;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0) yield break;
		var pts = this._pts.Append(new XY(cursorX, cursorY)).ToList();
		if (pts.Count == 2)
		{
			yield return new Line(Num.Z(pts[0].X, pts[0].Y), Num.Z(pts[1].X, pts[1].Y));
			yield break;
		}
		if (Build(pts, this.ControlVertices) is { } sp) yield return sp;
	}
}

/// <summary>POINT: 점 찍기 (여러 개, Enter = 끝). 모양·크기는 점 스타일(PTYPE).</summary>
public sealed class PointCommand : EditorCommand
{
	public override string Name => "POINT";
	public override string[] Aliases => new[] { "PO" };

	private bool _multiple;

	/// <summary>점 스타일 대화상자를 열어 달라는 요청 (설정(S)).</summary>
	public event Action? SettingsRequested;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._multiple = false;
		this.Prompt = "[설정(S)/다중(M)]/<점위치(L)>:";
	}

	public override void OnText(string text)
	{
		switch (text.Trim().ToUpperInvariant())
		{
			case "M":
				this._multiple = true;
				this.Prompt = "[설정(S)]/<점위치(L)>:";
				break;
			case "S":
				this.SettingsRequested?.Invoke();
				break;
		}
	}

	/// <summary>기존 CAD: 점 하나를 놓고 끝, 다중(M)이면 Enter까지 계속.</summary>
	public override void OnPoint(double x, double y)
	{
		this.Ctx?.Doc.AddEntities(new Point(Num.Z(x, y)));
		if (this.Ctx != null) this.Ctx.LastPoint = (x, y);
		if (!this._multiple) this.Finish();
	}

	public override void OnEnter() => this.Finish();
}

/// <summary>DIVIDE / MEASURE: 객체를 개수로 나누거나 길이마다 점을 찍는다.</summary>
public sealed class DivideMeasureCommand : EditorCommand
{
	private readonly bool _measure;
	private Entity? _target;
	private XY _pick;

	public DivideMeasureCommand(bool measure) => this._measure = measure;

	public DivideMeasureCommand() : this(false)
	{
	}

	public override string Name => this._measure ? "MEASURE" : "DIVIDE";
	public override IEnumerable<Entity> Highlighted => this._target != null ? new Entity[] { this._target } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._target == null;

	public override string[] Aliases => this._measure ? new[] { "ME" } : new[] { "DIV" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._target = null;
		this.Prompt = this._measure ? "측정할 객체 선택:" : "등분할 객체 선택:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._target == null)
		{
			var picked = this.Ctx.PickAt(x, y);
			if (picked == null || Geom.SegmentsOf(picked).Count == 0)
			{
				this.Ctx.Print("선, 호, 원, 폴리선, 타원, 스플라인을 선택하십시오.");
				return;
			}
			this._target = picked;
			this._pick = new XY(x, y);
			this.Prompt = this._measure ? "세그먼트 길이 [블록(B)]:" : "세그먼트 수 or [Block]:";
			return;
		}
		if (this._measure && this.Ctx.LastPoint is { } lp)
		{
			this.OnText(Num.Dist(lp, (x, y)).ToString(System.Globalization.CultureInfo.InvariantCulture));
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null || this._target == null || !Num.TryParse(text, out double v)) return;
		var segs = Geom.SegmentsOf(this._target);
		double total = Geom.Length(segs);
		var dists = new List<double>();
		if (this._measure)
		{
			if (v <= 0) return;
			// 길이 재기는 고른 점에 가까운 끝에서 시작
			bool fromEnd = !Geom.IsClosed(this._target) && Geom.ParamOf(segs, this._pick) > segs.Count / 2.0;
			for (double d = v; d < total - 1e-9; d += v) dists.Add(fromEnd ? total - d : d);
		}
		else
		{
			int n = (int)v;
			if (n < 2 || n > 32767) { this.Ctx.Print("2 이상 32767 이하의 정수를 입력하십시오."); return; }
			for (int i = 1; i < n; i++) dists.Add(total * i / n);
			if (Geom.IsClosed(this._target)) dists.Add(0);
		}
		var points = dists.Select(d => PointAtLength(segs, d)).Select(p => (Entity)new Point(Num.Z(p.X, p.Y)) { Layer = this.Ctx.Doc.CurrentLayer }).ToList();
		if (points.Count > 0) this.Ctx.Doc.AddEntities(points);
		this.Ctx.Print($"점 {points.Count}개를 만들었습니다. (점이 보이지 않으면 점 스타일 PTYPE에서 모양을 바꾸십시오)");
		this.Finish();
	}

	public static XY PointAtLength(List<Geom.Seg> segs, double d)
	{
		foreach (var s in segs)
		{
			if (d <= s.Length + 1e-12) return s.At(s.Length > 0 ? d / s.Length : 0);
			d -= s.Length;
		}
		return segs[^1].At(1);
	}
}

/// <summary>XLINE (구성선, 양쪽 무한) / RAY (광선, 한쪽 무한). XLINE은 수평(H)·수직(V)·각도(A)·이등분(B)·간격띄우기(O).</summary>
public sealed class XLineCommand : EditorCommand
{
	private readonly bool _ray;
	private (double X, double Y)? _base;
	private string _mode = "";
	private double _angle;
	private readonly List<(double X, double Y)> _bisect = new();

	public XLineCommand(bool ray) => this._ray = ray;

	public XLineCommand() : this(false)
	{
	}

	public override string Name => this._ray ? "RAY" : "XLINE";
	public override string[] Aliases => this._ray ? Array.Empty<string>() : new[] { "XL" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._base = null;
		this._mode = "";
		this._bisect.Clear();
		this.Prompt = this._ray ? "무한 광선:  [이등분(B)/수평(H)/수직(V)/각도(A)/간격띄우기(O)]/<광선의 시작>:" : "무한선:  [이등분(B)/수평(H)/수직(V)/각도(A)/간격띄우기(O)]/<선 따라 점>:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._base == null && this._mode == "")
		{
			switch (t)
			{
				case "H": this._mode = "fixed"; this._angle = 0; this.Prompt = "위치:"; return;
				case "V": this._mode = "fixed"; this._angle = Math.PI / 2; this.Prompt = "위치:"; return;
				case "A": this._mode = "angle"; this.Prompt = "[참조(R)]/<각도 입력 (0)>:"; return;
				case "B": this._mode = "bisect"; this.Prompt = "[객체(E)]/<꼭짓점 지정>:"; return;
				case "O": this._mode = "offset"; this.Prompt = $"[통과점(T)]/<평행 무한선의 간격띄우기 거리> ({(LastOffset < 0 ? "통과점(T)" : LastOffset.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture))}):"; return;
			}
		}
		if (this._mode == "angle" && Num.TryParse(t, out double deg))
		{
			this._mode = "fixed";
			this._angle = Num.Rad(deg);
			this.Prompt = "위치:";
			return;
		}
		if (this._mode == "offset")
		{
			if (t == "T")
			{
				LastOffset = -1;
				this._mode = "offsetPick";
				this.Prompt = "평행 무한선의 객체 선택:";
			}
			else if (Num.TryParse(t, out double d) && d > 0)
			{
				LastOffset = d;
				this._mode = "offsetPick";
				this.Prompt = "평행 무한선의 객체 선택:";
			}
		}
	}

	private static double LastOffset = -1;
	private Line? _offsetSource;

	public override bool PickingObject => !this.IsFinished && this._mode == "offsetPick";

	public override IEnumerable<Entity> Highlighted => this._offsetSource != null ? new Entity[] { this._offsetSource } : Array.Empty<Entity>();

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._mode)
		{
			case "offsetPick":
				{
					var picked = this.Ctx.PickAt(x, y);
					Line? line = picked switch
					{
						Line l => l,
						XLine xl => new Line(xl.FirstPoint, xl.FirstPoint + xl.Direction),
						Ray r => new Line(r.StartPoint, r.StartPoint + r.Direction),
						_ => null,
					};
					if (line == null)
					{
						if (picked != null) this.Ctx.Print("선, 무한선 또는 광선을 선택하십시오.");
						return;
					}
					this._offsetSource = line;
					this._mode = "offsetSide";
					this.Prompt = LastOffset < 0 ? "통과점:" : "측면:";
					return;
				}
			case "offsetSide":
				{
					var l = this._offsetSource!;
					double dx = l.EndPoint.X - l.StartPoint.X, dy = l.EndPoint.Y - l.StartPoint.Y, len = Math.Sqrt(dx * dx + dy * dy);
					if (len < 1e-12) return;
					double nx = -dy / len, ny = dx / len;
					double side = (x - l.StartPoint.X) * nx + (y - l.StartPoint.Y) * ny;
					double d = LastOffset < 0 ? side : Math.Sign(side == 0 ? 1 : side) * LastOffset;
					var a = (l.StartPoint.X + nx * d, l.StartPoint.Y + ny * d);
					this.Add(a, (a.Item1 + dx, a.Item2 + dy));
					this._offsetSource = null;
					this._mode = "offsetPick";
					this.Prompt = "평행 무한선의 객체 선택:";
					return;
				}
			case "fixed":
				this.Add((x, y), (x + Math.Cos(this._angle), y + Math.Sin(this._angle)));
				return; // 여러 개 (Enter로 끝)
			case "bisect":
				this._bisect.Add((x, y));
				if (this._bisect.Count == 1) this.Prompt = "이등분 각도 시작점:";
				else if (this._bisect.Count == 2) this.Prompt = "이등분 각도 끝점:";
				else
				{
					var v = this._bisect[0];
					double a1 = Math.Atan2(this._bisect[1].Y - v.Y, this._bisect[1].X - v.X), a2 = Math.Atan2(y - v.Y, x - v.X);
					double mid = a1 + Math.Atan2(Math.Sin(a2 - a1), Math.Cos(a2 - a1)) / 2;
					this.Add(v, (v.X + Math.Cos(mid), v.Y + Math.Sin(mid)));
					this._bisect.RemoveAt(2);
				}
				return;
		}
		if (this._base == null)
		{
			this._base = (x, y);
			this.Ctx.LastPoint = (x, y);
			this.Prompt = "방향:";
			return;
		}
		if (Num.Dist(this._base.Value, (x, y)) > 1e-12)
		{
			this.Add(this._base.Value, (x, y));
		}
	}

	private void Add((double X, double Y) a, (double X, double Y) b)
	{
		double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
		var dir = new XYZ(dx / l, dy / l, 0);
		Entity e = this._ray ? new Ray { StartPoint = Num.Z(a.X, a.Y), Direction = dir } : new XLine { FirstPoint = Num.Z(a.X, a.Y), Direction = dir };
		this.Ctx!.Doc.AddEntities(e);
	}

	public override void OnEnter() => this.Finish();

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		(double X, double Y)? a = this._mode == "fixed" ? (cursorX, cursorY) : this._base;
		if (a is not { } p) yield break;
		double ang = this._mode == "fixed" ? this._angle : Math.Atan2(cursorY - p.Y, cursorX - p.X);
		double big = 1e4;
		var start = this._ray ? Num.Z(p.X, p.Y) : Num.Z(p.X - Math.Cos(ang) * big, p.Y - Math.Sin(ang) * big);
		yield return new Line(start, Num.Z(p.X + Math.Cos(ang) * big, p.Y + Math.Sin(ang) * big));
	}
}

/// <summary>DONUT: 안 지름·바깥 지름 → 폭 있는 닫힌 폴리선(반원 두 개). 중심을 계속 찍어 여러 개.</summary>
public sealed class DonutCommand : EditorCommand
{
	private static double LastInner = 0.5, LastOuter = 1.0;
	private int _step;

	public override string Name => "DONUT";
	public override string[] Aliases => new[] { "DO" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this.Prompt = $"2점(2P)/3점(3P)/반지름접선(RTT)/<도넛의 내부 지름> <{LastInner:0.0000}>:";
	}

	public override void OnText(string text)
	{
		if (!Num.TryParse(text, out double v) || v < 0) return;
		if (this._step == 0) { LastInner = v; this.ToOuter(); }
		else if (this._step == 1 && v > 0) { LastOuter = v; this.ToCenter(); }
	}

	public override void OnEnter()
	{
		if (this._step == 0) this.ToOuter();
		else if (this._step == 1) this.ToCenter();
		else this.Finish();
	}

	private void ToOuter()
	{
		this._step = 1;
		this.Prompt = $"도넛의 외부 지름 <{LastOuter:0.0000}>:";
	}

	private void ToCenter()
	{
		this._step = 2;
		this.Prompt = "도넛의 중심:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this._step == 2) this.Ctx?.Doc.AddEntities(Build(x, y));
	}

	private static LwPolyline Build(double x, double y)
	{
		double inner = Math.Min(LastInner, LastOuter), outer = Math.Max(LastInner, LastOuter);
		double r = (inner + outer) / 4, w = (outer - inner) / 2;
		var pl = new LwPolyline { IsClosed = true, ConstantWidth = w };
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(x - r, y)) { Bulge = 1 });
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(x + r, y)) { Bulge = 1 });
		return pl;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 2) yield return Build(cursorX, cursorY);
	}
}

/// <summary>REVCLOUD: 직사각형(기본) 또는 다각형(P), 객체(O: 닫힌 폴리선·원을 구름으로). 호 길이(A).</summary>
public sealed class RevCloudCommand : EditorCommand
{
	private static double ArcLength;
	private string _mode = "R";
	private readonly List<XY> _pts = new();
	private bool _awaitArc;

	public override string Name => "REVCLOUD";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._mode = "R";
		this._awaitArc = false;
		this.Prompt = $"첫 번째 구석점 지정 또는 [호 길이(A)/객체(O)/직사각형(R)/다각형(P)] <객체>: (호 길이 {this.CurrentArc(ctx):0.##})";
	}

	private double CurrentArc(CommandContext ctx)
	{
		if (ArcLength > 0) return ArcLength;
		var ext = ctx.Doc.Document.Header.ModelSpaceExtMax - ctx.Doc.Document.Header.ModelSpaceExtMin;
		double size = Math.Max(Math.Abs(ext.X), Math.Abs(ext.Y));
		return size > 0 && size < 1e9 ? size / 60 : 15;
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._awaitArc)
		{
			if (Num.TryParse(t, out double a) && a > 0) ArcLength = a;
			this._awaitArc = false;
			this.Start(this.Ctx!);
			return;
		}
		if (this._pts.Count == 0)
		{
			switch (t)
			{
				case "A": this._awaitArc = true; this.Prompt = "최소 호 길이 지정:"; return;
				case "O": this._mode = "O"; this.Prompt = "객체 선택 (닫힌 폴리선·원):"; return;
				case "R": this._mode = "R"; this.Prompt = "첫 번째 구석점 지정:"; return;
				case "P": this._mode = "P"; this.Prompt = "시작점 지정:"; return;
			}
		}
	}

	public override void OnEnter()
	{
		if (this._pts.Count == 0 && this._mode == "R")
		{
			this._mode = "O";
			this.Prompt = "객체 선택 (닫힌 폴리선·원):";
			return;
		}
		if (this._mode == "P" && this._pts.Count >= 3) this.Create(this._pts);
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		if (this._mode == "O")
		{
			var picked = this.Ctx.PickAt(x, y);
			if (picked is LwPolyline { IsClosed: true } or Circle and not Arc)
			{
				var segs = Geom.SegmentsOf(picked);
				double total = Geom.Length(segs);
				double step = this.CurrentArc(this.Ctx);
				int n = Math.Max(3, (int)Math.Round(total / step));
				var pts = Enumerable.Range(0, n).Select(i => DivideMeasureCommand.PointAtLength(segs, total * i / n)).ToList();
				this.Create(pts, picked);
			}
			else
			{
				this.Ctx.Print("닫힌 폴리선이나 원을 선택하십시오.");
			}
			return;
		}
		this._pts.Add(p);
		this.Ctx.LastPoint = (x, y);
		if (this._mode == "R" && this._pts.Count == 2)
		{
			var a = this._pts[0];
			this.Create(new List<XY> { a, new(p.X, a.Y), p, new(a.X, p.Y) });
			this.Finish();
		}
		else if (this._mode == "R")
		{
			this.Prompt = "반대 구석점 지정:";
		}
		else
		{
			this.Prompt = "다음 점 지정 <Enter = 완료>:";
		}
	}

	/// <summary>다각형 둘레를 호 길이마다 나눠 바깥으로 불룩한 호로 잇는다.</summary>
	private void Create(List<XY> poly, Entity? replace = null)
	{
		double step = this.CurrentArc(this.Ctx!);
		// 시계 반대 방향이면 바깥쪽 볼록 = 음수 볼록 값
		double area = 0;
		for (int i = 0; i < poly.Count; i++)
		{
			var a = poly[i];
			var b = poly[(i + 1) % poly.Count];
			area += a.X * b.Y - b.X * a.Y;
		}
		double bulgeSign = area > 0 ? -1 : 1;
		var cloud = new LwPolyline { IsClosed = true };
		for (int i = 0; i < poly.Count; i++)
		{
			var a = poly[i];
			var b = poly[(i + 1) % poly.Count];
			double len = Geom.Dist(a, b);
			int n = Math.Max(1, (int)Math.Round(len / step));
			for (int k = 0; k < n; k++)
			{
				var p = new XY(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
				cloud.Vertices.Add(new LwPolyline.Vertex(p) { Bulge = bulgeSign * Math.Tan(Math.PI / 6) }); // 120° 호
			}
		}
		if (replace != null)
		{
			Geom.CopyProps(replace, cloud);
			this.Ctx!.Doc.Replace(new[] { replace }, new[] { cloud }, "구름형 수정 기호");
		}
		else
		{
			this.Ctx!.Doc.AddEntities(cloud);
		}
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0) yield break;
		var pl = new LwPolyline { IsClosed = this._mode == "R" };
		if (this._mode == "R")
		{
			var a = this._pts[0];
			foreach (var p in new[] { a, new XY(cursorX, a.Y), new XY(cursorX, cursorY), new XY(a.X, cursorY) }) pl.Vertices.Add(new LwPolyline.Vertex(p));
		}
		else
		{
			foreach (var p in this._pts) pl.Vertices.Add(new LwPolyline.Vertex(p));
			pl.Vertices.Add(new LwPolyline.Vertex(new XY(cursorX, cursorY)));
		}
		yield return pl;
	}
}

/// <summary>SOLID (2D 채움): 세·네 점 (점 순서는 기존 CAD와 같이 1-2-3-4가 Z자).</summary>
public sealed class SolidCommand : EditorCommand
{
	private readonly List<XYZ> _pts = new();

	public override string Name => "SOLID";
	public override string[] Aliases => new[] { "SO" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this.Prompt = "첫 번째 점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		this._pts.Add(Num.Z(x, y));
		this.Prompt = this._pts.Count switch
		{
			1 => "두 번째 점 지정:",
			2 => "세 번째 점 지정:",
			_ => "네 번째 점 지정 또는 <종료>:",
		};
		if (this._pts.Count == 4) this.Create();
	}

	public override void OnEnter()
	{
		if (this._pts.Count == 3) this.Create();
		this.Finish();
	}

	private void Create()
	{
		var s = new Solid { FirstCorner = this._pts[0], SecondCorner = this._pts[1], ThirdCorner = this._pts[2], FourthCorner = this._pts.Count > 3 ? this._pts[3] : this._pts[2] };
		this.Ctx!.Doc.AddEntities(s);
		this.Finish();
	}
}
