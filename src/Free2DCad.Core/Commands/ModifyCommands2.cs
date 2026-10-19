using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>폴리선 꼭짓점·닫힘·폭 상태 (제자리 편집의 되돌리기용).</summary>
internal sealed record PolySnapshot(List<LwPolyline.Vertex> Vertices, bool Closed, double Width)
{
	public static PolySnapshot Of(LwPolyline pl) =>
		new(pl.Vertices.Select(v => new LwPolyline.Vertex(v.Location) { Bulge = v.Bulge, StartWidth = v.StartWidth, EndWidth = v.EndWidth }).ToList(), pl.IsClosed, pl.ConstantWidth);

	public void ApplyTo(LwPolyline pl)
	{
		pl.Vertices.Clear();
		foreach (var v in this.Vertices)
		{
			pl.Vertices.Add(new LwPolyline.Vertex(v.Location) { Bulge = v.Bulge, StartWidth = v.StartWidth, EndWidth = v.EndWidth });
		}
		pl.IsClosed = this.Closed;
		pl.ConstantWidth = this.Width;
	}
}

/// <summary>
/// LENGTHEN: 선·호·열린 폴리선의 길이를 증분(DE)·퍼센트(P)·합계(T)·동적(DY)으로 바꾼다. 고른 점에 가까운 끝이 바뀐다.
/// </summary>
public sealed class LengthenCommand : EditorCommand
{
	private static string LastMode = "DE";
	private static double LastDelta, LastPercent = 100, LastTotal = 100;

	private string? _mode;
	private bool _awaitValue;
	private Entity? _dynTarget;
	private XY _dynPick;

	public override string Name => "LENGTHEN";
	public override IEnumerable<Entity> Highlighted => this._dynTarget != null ? new Entity[] { this._dynTarget } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && !this._awaitValue && this._dynTarget == null;

	public override string[] Aliases => new[] { "LEN" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._mode = null;
		this._awaitValue = false;
		this._dynTarget = null;
		this.Prompt = "길이 편집: [동적(DY)/증분(I)/퍼센트(P)/합계(T)]/<길이를 나열할 객체 선택>:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._awaitValue)
		{
			if (!Num.TryParse(t, out double v)) return;
			switch (this._mode)
			{
				case "DE": LastDelta = v; break;
				case "P": if (v <= 0) return; LastPercent = v; break;
				case "T": if (v <= 0) return; LastTotal = v; break;
			}
			this._awaitValue = false;
			this.Prompt = "[모드(M)]/<변경할 객체 선택>:";
			return;
		}
		switch (t)
		{
			case "DE" or "I": this.SetMode("DE", $"[각도(A)]/<길이 증분 입력 ({LastDelta:0.0000})>:"); break;
			case "P": this.SetMode("P", $"퍼센트 길이 입력 <{LastPercent:0.0000}>:"); break;
			case "T": this.SetMode("T", $"[각도(A)]/<전체 길이 입력 ({LastTotal:0.0000})>:"); break;
			case "M" when this._mode != null:
				this._mode = null;
				this._dynTarget = null;
				this.Prompt = "길이 편집: [동적(DY)/증분(I)/퍼센트(P)/합계(T)]/<길이를 나열할 객체 선택>:";
				break;
			case "DY": this._mode = "DY"; LastMode = "DY"; this.Prompt = "[모드(M)]/<변경할 객체 선택>:"; break;
			case "U": this.Ctx?.Doc.UndoLastStep(); break;
		}
	}

	private void SetMode(string mode, string prompt)
	{
		this._mode = mode;
		LastMode = mode;
		this._awaitValue = true;
		this.Prompt = prompt;
	}

	public override void OnEnter()
	{
		if (this._awaitValue)
		{
			this._awaitValue = false;
			this.Prompt = "[모드(M)]/<변경할 객체 선택>:";
			return;
		}
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		if (this._mode == "DY" && this._dynTarget != null)
		{
			var segs = Geom.SegmentsOf(this._dynTarget);
			var end = Geom.ParamOf(segs, this._dynPick) > segs.Count / 2.0 ? segs[^1] : segs[0];
			// 새 끝점까지 길이: 끝 조각 방향으로 투영
			double total = Geom.Length(segs);
			double newTotal = total + this.DynDelta(this._dynTarget, this._dynPick, p);
			this.Apply(this._dynTarget, this._dynPick, _ => newTotal);
			this._dynTarget = null;
			this.Prompt = "[모드(M)]/<변경할 객체 선택>:";
			return;
		}
		var picked = this.Ctx.PickAt(x, y);
		if (picked == null) return;
		if (this._mode == null)
		{
			var segs = Geom.SegmentsOf(picked);
			if (segs.Count == 0) return;
			string extra = picked is Arc a ? $", 사이각: {Num.Deg(Geom.Norm(a.EndAngle - a.StartAngle)):0.####}" : "";
			this.Ctx.Print($"현재 길이: {Geom.Length(segs):0.####}{extra}");
			this._mode = LastMode == "DY" ? "DE" : LastMode;
			this.Prompt = "길이 편집: [동적(DY)/증분(I)/퍼센트(P)/합계(T)]/<길이를 나열할 객체 선택>:";
			this._mode = null;
			return;
		}
		if (this._mode == "DY")
		{
			this._dynTarget = picked;
			this._dynPick = p;
			this.Prompt = "새 끝점 지정:";
			return;
		}
		string mode = this._mode;
		this.Apply(picked, p, total => mode switch
		{
			"DE" => total + LastDelta,
			"P" => total * LastPercent / 100,
			_ => LastTotal,
		});
	}

	private double DynDelta(Entity e, XY pick, XY to)
	{
		var segs = Geom.SegmentsOf(e);
		bool atEnd = Geom.ParamOf(segs, pick) > segs.Count / 2.0;
		var seg = atEnd ? segs[^1] : segs[0];
		var endPt = seg.At(atEnd ? 1 : 0);
		if (seg is Geom.ArcSeg arc)
		{
			double a = Math.Atan2(to.Y - arc.C.Y, to.X - arc.C.X);
			double endA = arc.Start + (atEnd ? arc.Sweep : 0);
			double d = Math.Atan2(Math.Sin(a - endA), Math.Cos(a - endA));
			double outward = atEnd ? Math.Sign(arc.Sweep) : -Math.Sign(arc.Sweep);
			return d * outward * arc.R;
		}
		var other = seg.At(atEnd ? 0 : 1);
		double dx = endPt.X - other.X, dy = endPt.Y - other.Y, l = Math.Sqrt(dx * dx + dy * dy);
		return l < 1e-12 ? 0 : ((to.X - endPt.X) * dx + (to.Y - endPt.Y) * dy) / l;
	}

	private void Apply(Entity e, XY pick, Func<double, double> newTotal)
	{
		var result = Lengthen(e, pick, newTotal);
		if (result == null)
		{
			this.Ctx?.Print("이 객체의 길이는 바꿀 수 없습니다 (선, 호, 열린 폴리선만, 길이는 0보다 커야 함).");
			return;
		}
		this.Ctx!.Doc.Replace(new[] { e }, new[] { result }, "길이 조정");
	}

	/// <summary>길이 바꾼 새 객체 (고른 점에 가까운 끝). 불가능하면 null.</summary>
	public static Entity? Lengthen(Entity e, XY pick, Func<double, double> newTotal)
	{
		if (e is not (Line or Arc or LwPolyline { IsClosed: false }) || (e is Circle and not Arc)) return null;
		var segs = Geom.SegmentsOf(e);
		if (segs.Count == 0) return null;
		double total = Geom.Length(segs);
		double delta = newTotal(total) - total;
		bool atEnd = Geom.ParamOf(segs, pick) > segs.Count / 2.0;
		int idx = atEnd ? segs.Count - 1 : 0;
		var seg = segs[idx];
		double newLen = seg.Length + delta;
		if (newLen <= 1e-9) return null;
		Geom.Seg changed;
		switch (seg)
		{
			case Geom.LineSeg l:
				{
					double f = newLen / l.Length;
					changed = atEnd
						? new Geom.LineSeg(l.A, new XY(l.A.X + (l.B.X - l.A.X) * f, l.A.Y + (l.B.Y - l.A.Y) * f))
						: new Geom.LineSeg(new XY(l.B.X + (l.A.X - l.B.X) * f, l.B.Y + (l.A.Y - l.B.Y) * f), l.B);
					break;
				}
			case Geom.ArcSeg a:
				{
					double sweep = Math.Sign(a.Sweep) * newLen / a.R;
					if (Math.Abs(sweep) >= 2 * Math.PI) return null;
					changed = atEnd ? a with { Sweep = sweep } : a with { Start = a.Start + a.Sweep - sweep, Sweep = sweep };
					break;
				}
			default:
				return null;
		}
		segs[idx] = changed;
		var result = Geom.ToEntity(segs, e);
		if (result is LwPolyline npl && e is LwPolyline opl)
		{
			// 꼭짓점 폭 유지
			for (int i = 0; i < Math.Min(npl.Vertices.Count, opl.Vertices.Count); i++)
			{
				npl.Vertices[i] = new LwPolyline.Vertex(npl.Vertices[i].Location) { Bulge = npl.Vertices[i].Bulge, StartWidth = opl.Vertices[i].StartWidth, EndWidth = opl.Vertices[i].EndWidth };
			}
		}
		return result;
	}
}

/// <summary>BREAKATPOINT: 객체를 한 점에서 두 개로 나눈다.</summary>
public sealed class BreakAtPointCommand : EditorCommand
{
	private Entity? _target;

	public override string Name => "BREAKATPOINT";
	public override IEnumerable<Entity> Highlighted => this._target != null ? new Entity[] { this._target } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._target == null;


	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._target = ctx.Selection.Count == 1 ? ctx.Selection.First() : null;
		this.Prompt = this._target == null ? "객체 선택:" : "끊기 점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._target == null)
		{
			this._target = this.Ctx.PickAt(x, y);
			if (this._target != null) this.Prompt = "끊기 점 지정:";
			return;
		}
		var parts = Split(this._target, new XY(x, y));
		if (parts == null)
		{
			this.Ctx.Print(Geom.IsClosed(this._target) ? "닫힌 객체는 한 점에서 끊을 수 없습니다. BREAK를 쓰십시오." : "이 점에서 끊을 수 없습니다.");
		}
		else
		{
			this.Ctx.Doc.Replace(new[] { this._target }, parts, "점에서 끊기");
		}
		this.Finish();
	}

	public static List<Entity>? Split(Entity e, XY at)
	{
		if (Geom.IsClosed(e) || e is Spline or Ellipse) return null;
		var segs = Geom.SegmentsOf(e);
		if (segs.Count == 0) return null;
		double p = Geom.ParamOf(segs, at);
		if (p < 1e-9 || p > segs.Count - 1e-9) return null;
		var a = Geom.ToEntity(Geom.SubRange(segs, 0, p), e);
		var b = Geom.ToEntity(Geom.SubRange(segs, p, segs.Count), e);
		return a != null && b != null ? new List<Entity> { a, b } : null;
	}
}

/// <summary>REVERSE: 선·폴리선·스플라인의 방향(꼭짓점 순서)을 뒤집는다.</summary>
public sealed class ReverseCommand : EntityModificationCommand
{
	public override string Name => "REVERSE";

	protected override void OnSelectionReady()
	{
		var targets = this.Targets.Where(e => e is Line or LwPolyline or Spline).ToList();
		if (targets.Count > 0)
		{
			this.Ctx!.Doc.Execute(new CompositeAction("방향 반대로", targets.Select(e => (IUndoableAction)new EntityEditAction("방향 반대로", e, () => Reverse(e), () => Reverse(e))).ToList()));
		}
		this.Ctx!.Print($"객체 {targets.Count}개의 방향을 반대로 했습니다.");
		this.Finish();
	}

	/// <summary>제자리에서 방향 뒤집기 (두 번 하면 원래대로).</summary>
	public static void Reverse(Entity e)
	{
		switch (e)
		{
			case Line l:
				(l.StartPoint, l.EndPoint) = (l.EndPoint, l.StartPoint);
				break;
			case LwPolyline pl:
				{
					var v = pl.Vertices.ToList();
					int n = v.Count;
					var nv = new List<LwPolyline.Vertex>();
					for (int k = 0; k < n; k++)
					{
						var src = v[n - 1 - k];
						// 새 조각 k = 옛 조각 (n-2-k) 를 거꾸로. 닫힌 폴리선의 마지막 조각은 옛 닫는 조각.
						int seg = (n - 2 - k + n) % n;
						var segV = v[seg];
						bool exists = pl.IsClosed || k < n - 1;
						nv.Add(new LwPolyline.Vertex(src.Location)
						{
							Bulge = exists ? -segV.Bulge : 0,
							StartWidth = exists ? segV.EndWidth : 0,
							EndWidth = exists ? segV.StartWidth : 0,
						});
					}
					if (pl.IsClosed)
					{
						// 시작점은 그대로 두기 위해 회전 (옛 v0이 새 시작)
						var rot = nv.Skip(n - 1).Concat(nv.Take(n - 1)).ToList();
						nv = rot;
					}
					pl.Vertices.Clear();
					foreach (var x in nv) pl.Vertices.Add(x);
					break;
				}
			case Spline sp:
				{
					var cps = sp.ControlPoints.ToList();
					cps.Reverse();
					sp.ControlPoints.Clear();
					foreach (var c in cps) sp.ControlPoints.Add(c);
					if (sp.Knots.Count > 0)
					{
						double kmax = sp.Knots[^1], kmin = sp.Knots[0];
						var ks = sp.Knots.Select(k => kmax + kmin - k).Reverse().ToList();
						sp.Knots.Clear();
						foreach (var k in ks) sp.Knots.Add(k);
					}
					if (sp.Weights.Count > 0)
					{
						var ws = sp.Weights.ToList();
						ws.Reverse();
						sp.Weights.Clear();
						foreach (var w in ws) sp.Weights.Add(w);
					}
					var fps = sp.FitPoints.ToList();
					fps.Reverse();
					sp.FitPoints.Clear();
					foreach (var f in fps) sp.FitPoints.Add(f);
					break;
				}
		}
	}
}

/// <summary>ALIGN: 원본 점 1~2쌍을 대상 점에 맞춰 이동·회전(·축척).</summary>
public sealed class AlignCommand : EntityModificationCommand
{
	private readonly List<XY> _pts = new();
	private bool _askScale;

	public override string Name => "ALIGN";
	public override string[] Aliases => new[] { "AL" };

	protected override void OnSelectionReady()
	{
		this._pts.Clear();
		this._askScale = false;
		this.Prompt = "첫 번째 원본 점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.WaitingForSelection || this._askScale || this.Ctx == null) return;
		this._pts.Add(new XY(x, y));
		this.Ctx.LastPoint = (x, y);
		this.Prompt = this._pts.Count switch
		{
			1 => "첫 번째 대상 점 지정:",
			2 => "두 번째 원본 점 지정 또는 <계속>:",
			3 => "두 번째 대상 점 지정:",
			_ => "세 번째 원본 점 지정 또는 <계속>:",
		};
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		if (this._askScale)
		{
			this.Run(false);
			return;
		}
		if (this._pts.Count == 2)
		{
			this.Run(false);
		}
		else if (this._pts.Count >= 4)
		{
			this._askScale = true;
			this.Prompt = "정렬 점을 기준으로 객체에 축척을 적용하시겠습니까? [예(Y)/아니오(N)] <N>:";
		}
	}

	public override void OnText(string text)
	{
		if (this._askScale) this.Run(text.Trim().ToUpperInvariant().StartsWith("Y"));
	}

	private void Run(bool scale)
	{
		var s1 = this._pts[0];
		var d1 = this._pts[1];
		double ang = 0, k = 1;
		if (this._pts.Count >= 4)
		{
			var s2 = this._pts[2];
			var d2 = this._pts[3];
			ang = Math.Atan2(d2.Y - d1.Y, d2.X - d1.X) - Math.Atan2(s2.Y - s1.Y, s2.X - s1.X);
			double ls = Geom.Dist(s1, s2);
			if (scale && ls > 1e-12) k = Geom.Dist(d1, d2) / ls;
		}
		var m = Matrix4.CreateTranslation(d1.X, d1.Y, 0) * Matrix4.CreateFromAxisAngle(XYZ.AxisZ, ang) * Matrix4.CreateScale(new XYZ(k, k, 1), XYZ.Zero) * Matrix4.CreateTranslation(-s1.X, -s1.Y, 0);
		var inv = Matrix4.CreateTranslation(s1.X, s1.Y, 0) * Matrix4.CreateScale(new XYZ(1 / k, 1 / k, 1), XYZ.Zero) * Matrix4.CreateFromAxisAngle(XYZ.AxisZ, -ang) * Matrix4.CreateTranslation(-d1.X, -d1.Y, 0);
		this.Ctx!.Doc.Execute(new TransformEntitiesAction(this.Targets, new Transform(m), new Transform(inv), "정렬"));
		this.Finish();
	}
}

/// <summary>
/// PEDIT / EDITPLINE: 폴리선 편집 — 닫기(C)/열기(O)/결합(J)/폭(W)/곡선 해제(D)/스플라인(S)/반전(R)/명령 취소(U).
/// 선·호를 고르면 폴리선으로 바꿀지 묻는다.
/// </summary>
public sealed class PeditCommand : EditorCommand
{
	private LwPolyline? _pl;
	private Entity? _convert;
	private string? _await;
	private readonly List<Entity> _joinSet = new();
	private int _undoDepth;

	public override string Name => "PEDIT";
	public override IEnumerable<Entity> Highlighted => this._pl != null ? new Entity[] { this._pl }.Concat(this._joinSet).ToArray() : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && (this._pl == null || this._await == "J");

	public override string[] Aliases => new[] { "PE", "EDITPLINE" };

	public override bool WantsSelection => this._await == "J";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pl = ctx.Selection.Count == 1 ? ctx.Selection.First() as LwPolyline : null;
		this._await = null;
		this._undoDepth = 0;
		if (this._pl != null) this.Options();
		else this.Prompt = "폴리선 선택:";
	}

	private void Options()
	{
		this._await = null;
		this.Prompt = this._pl!.IsClosed
			? "옵션 입력 [열기(O)/결합(J)/폭(W)/곡선 해제(D)/스플라인(S)/반전(R)/명령 취소(U)]:"
			: "옵션 입력 [닫기(C)/결합(J)/폭(W)/곡선 해제(D)/스플라인(S)/반전(R)/명령 취소(U)]:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._pl == null && this._convert == null)
		{
			var picked = this.Ctx.PickAt(x, y);
			if (picked is LwPolyline pl)
			{
				this._pl = pl;
				this.Options();
			}
			else if (picked is Line or Arc)
			{
				this._convert = picked;
				this.Prompt = "선택한 객체가 폴리선이 아닙니다. 폴리선으로 바꾸시겠습니까? <Y>:";
			}
			return;
		}
		if (this._await == "J")
		{
			var picked = this.Ctx.PickAt(x, y);
			if (picked != null && picked != this._pl && !this._joinSet.Contains(picked)) this._joinSet.Add(picked);
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		foreach (var e in entities)
		{
			if (e != this._pl && !this._joinSet.Contains(e)) this._joinSet.Add(e);
		}
	}

	public override void OnEnter()
	{
		if (this._convert != null && this._pl == null)
		{
			this.OnText("Y");
			return;
		}
		if (this._await == "J")
		{
			this.DoJoin();
			return;
		}
		if (this._await == "W")
		{
			this.Options();
			return;
		}
		this.Finish();
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		if (this._convert != null && this._pl == null)
		{
			if (t.StartsWith("N")) { this.Finish(); return; }
			var pl = (LwPolyline)Geom.ToEntity(Geom.SegmentsOf(this._convert), new LwPolyline())!;
			Geom.CopyProps(this._convert, pl);
			this.Ctx.Doc.Replace(new[] { this._convert }, new[] { pl }, "폴리선으로 바꾸기");
			this._undoDepth++;
			this._pl = pl;
			this._convert = null;
			this.Options();
			return;
		}
		if (this._pl == null) return;
		if (this._await == "W")
		{
			if (Num.TryParse(t, out double w) && w >= 0)
			{
				this.Edit("폭", pl =>
				{
					pl.ConstantWidth = w;
					for (int i = 0; i < pl.Vertices.Count; i++)
						pl.Vertices[i] = new LwPolyline.Vertex(pl.Vertices[i].Location) { Bulge = pl.Vertices[i].Bulge };
				});
			}
			this.Options();
			return;
		}
		switch (t)
		{
			case "C": this.Edit("닫기", pl => pl.IsClosed = true); this.Options(); break;
			case "O": this.Edit("열기", pl => pl.IsClosed = false); this.Options(); break;
			case "D":
				this.Edit("곡선 해제", pl =>
				{
					for (int i = 0; i < pl.Vertices.Count; i++)
						pl.Vertices[i] = new LwPolyline.Vertex(pl.Vertices[i].Location) { StartWidth = pl.Vertices[i].StartWidth, EndWidth = pl.Vertices[i].EndWidth };
				});
				break;
			case "R": this.Edit("반전", ReverseCommand.Reverse); break;
			case "W": this._await = "W"; this.Prompt = "모든 세그먼트에 대한 새 폭 지정:"; break;
			case "J": this._await = "J"; this._joinSet.Clear(); this.Prompt = "결합할 객체 선택 <완료는 Enter>:"; break;
			case "S":
				{
					var pts = this._pl.Vertices.Select(v => new XY(v.Location.X, v.Location.Y)).ToList();
					if (this._pl.IsClosed) pts.Add(pts[0]);
					if (SplineCommand.Build(pts, controlVertices: true) is { } sp)
					{
						Geom.CopyProps(this._pl, sp);
						this.Ctx.Doc.Replace(new[] { this._pl }, new[] { sp }, "스플라인으로");
					}
					this.Finish();
					break;
				}
			case "U":
				if (this._undoDepth > 0)
				{
					this.Ctx.Doc.UndoLastStep();
					this._undoDepth--;
				}
				break;
		}
	}

	private void Edit(string name, Action<LwPolyline> change)
	{
		var pl = this._pl!;
		var before = PolySnapshot.Of(pl);
		change(pl);
		var after = PolySnapshot.Of(pl);
		before.ApplyTo(pl);
		this.Ctx!.Doc.Execute(new EntityEditAction("폴리선 편집 - " + name, pl, () => after.ApplyTo(pl), () => before.ApplyTo(pl)));
		this._undoDepth++;
	}

	/// <summary>끝점이 닿는 선·호·폴리선을 폴리선 끝에 차례로 잇는다.</summary>
	private void DoJoin()
	{
		var pl = this._pl!;
		var segs = Geom.SegmentsOf(pl);
		var pool = this._joinSet.Where(e => e is Line or Arc or LwPolyline { IsClosed: false }).ToList();
		var used = new List<Entity>();
		const double tol = 1e-6;
		bool grew = true;
		while (grew && !pl.IsClosed)
		{
			grew = false;
			foreach (var e in pool.ToList())
			{
				var es = Geom.SegmentsOf(e);
				if (es.Count == 0) continue;
				var start = segs[0].At(0);
				var end = segs[^1].At(1);
				var a = es[0].At(0);
				var b = es[^1].At(1);
				List<Geom.Seg>? merged = null;
				if (Geom.Dist(end, a) < tol) merged = segs.Concat(es).ToList();
				else if (Geom.Dist(end, b) < tol) merged = segs.Concat(Reversed(es)).ToList();
				else if (Geom.Dist(start, b) < tol) merged = es.Concat(segs).ToList();
				else if (Geom.Dist(start, a) < tol) merged = Reversed(es).Concat(segs).ToList();
				if (merged == null) continue;
				segs = merged;
				pool.Remove(e);
				used.Add(e);
				grew = true;
			}
		}
		if (used.Count == 0)
		{
			this.Ctx!.Print("결합할 수 있는 객체가 없습니다 (끝점이 맞닿아야 함).");
			this.Options();
			return;
		}
		var joined = (LwPolyline)Geom.ToEntity(segs, pl)!;
		bool closed = Geom.Dist(segs[0].At(0), segs[^1].At(1)) < tol;
		if (closed)
		{
			joined.Vertices.RemoveAt(joined.Vertices.Count - 1);
			joined.IsClosed = true;
		}
		var before = PolySnapshot.Of(pl);
		var after = PolySnapshot.Of(joined);
		this.Ctx!.Doc.Execute(new CompositeAction("폴리선 결합", new List<IUndoableAction>
		{
			new EntityEditAction("폴리선 결합", pl, () => after.ApplyTo(pl), () => before.ApplyTo(pl)),
			new RemoveEntitiesAction(used, "결합한 객체 지우기"),
		}));
		this._undoDepth++;
		this.Ctx.Print($"객체 {used.Count}개를 폴리선에 결합했습니다.");
		this.Options();
	}

	private static IEnumerable<Geom.Seg> Reversed(List<Geom.Seg> segs) =>
		Enumerable.Reverse(segs).Select<Geom.Seg, Geom.Seg>(s => s switch
		{
			Geom.LineSeg l => new Geom.LineSeg(l.B, l.A),
			Geom.ArcSeg a => new Geom.ArcSeg(a.C, a.R, a.Start + a.Sweep, -a.Sweep),
			_ => s,
		});
}

/// <summary>
/// DRAWORDER: 선택 객체를 맨 앞(F)/맨 뒤(B)/참조 객체 위(A)/아래(U)로. Preset을 주면 바로 그 방식.
/// </summary>
public sealed class DrawOrderCommand : EntityModificationCommand
{
	private string? _mode;
	private bool _awaitRef;

	public override string Name => "DRAWORDER";
	public override string[] Aliases => new[] { "DR" };

	public string? Preset { get; set; }

	public override bool WantsSelection => this.WaitingForSelection || this._awaitRef;

	protected override void OnSelectionReady()
	{
		if (this.Preset != null)
		{
			this.Choose(this.Preset);
			return;
		}
		this.Prompt = "옵션 입력 [객체 위로(A)/객체 아래로(U)/맨 앞으로(F)/맨 뒤로(B)] <맨 뒤로>:";
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		if (this._mode == null) this.Choose("B");
	}

	public override void OnText(string text)
	{
		if (!this.WaitingForSelection && this._mode == null) this.Choose(text.Trim().ToUpperInvariant());
	}

	private void Choose(string mode)
	{
		this._mode = mode;
		switch (mode)
		{
			case "F":
			case "B":
				Apply(this.Ctx!.Doc, this.Targets, mode == "F" ? Placement.Front : Placement.Back, null);
				this.Finish();
				break;
			case "A":
			case "U":
				this._awaitRef = true;
				this.Prompt = "참조 객체 선택:";
				break;
			default:
				this._mode = null;
				break;
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._awaitRef)
		{
			this.Reference(entities.FirstOrDefault());
			return;
		}
		base.OnSelection(entities);
	}

	public override void OnPoint(double x, double y)
	{
		if (this._awaitRef) this.Reference(this.Ctx?.PickAt(x, y));
	}

	private void Reference(Entity? r)
	{
		if (r == null || this.Targets.Contains(r)) return;
		Apply(this.Ctx!.Doc, this.Targets, this._mode == "A" ? Placement.Above : Placement.Under, r);
		this.Finish();
	}

	public enum Placement { Front, Back, Above, Under }

	/// <summary>객체들의 그리기 순서를 바꾼다 (공간마다, 되돌리기 가능).</summary>
	public static void Apply(EditorDocument doc, IEnumerable<Entity> entities, Placement placement, Entity? reference)
	{
		var set = entities.ToHashSet();
		var owners = set.Select(e => e.Owner).OfType<BlockRecord>().Distinct().ToList();
		var actions = new List<IUndoableAction>();
		foreach (var owner in owners)
		{
			var before = owner.Entities.ToList();
			var moving = before.Where(set.Contains).ToList();
			var rest = before.Where(e => !set.Contains(e)).ToList();
			List<Entity> after;
			switch (placement)
			{
				case Placement.Front: after = rest.Concat(moving).ToList(); break;
				case Placement.Back: after = moving.Concat(rest).ToList(); break;
				default:
					{
						int i = reference != null ? rest.IndexOf(reference) : -1;
						if (i < 0) continue;
						after = new List<Entity>(rest);
						after.InsertRange(placement == Placement.Above ? i + 1 : i, moving);
						break;
					}
			}
			actions.Add(new DelegateAction("그리기 순서", () => owner.Entities.Reorder(after), () => owner.Entities.Reorder(before)));
		}
		if (actions.Count > 0) doc.Execute(new CompositeAction("그리기 순서", actions));
	}
}

/// <summary>HATCHTOBACK: 모든 해치를 맨 뒤로.</summary>
public sealed class HatchToBackCommand : EditorCommand
{
	public override string Name => "HATCHTOBACK";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var hatches = ctx.Doc.CurrentSpace.Entities.OfType<Hatch>().Cast<Entity>().ToList();
		if (hatches.Count > 0) DrawOrderCommand.Apply(ctx.Doc, hatches, DrawOrderCommand.Placement.Back, null);
		ctx.Print($"해치 {hatches.Count}개를 맨 뒤로 보냈습니다.");
		this.Finish();
	}
}

/// <summary>TEXTTOFRONT: 문자(T)·치수(D)·지시선(L)·모두(A)를 맨 앞으로.</summary>
public sealed class TextToFrontCommand : EditorCommand
{
	public override string Name => "TEXTTOFRONT";

	public string? Preset { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (this.Preset != null)
		{
			this.OnText(this.Preset);
			return;
		}
		this.Prompt = "앞으로 가져오기 [문자(T)/치수(D)/지시선(L)/모든 주석(A)] <모든 주석>:";
	}

	public override void OnEnter() => this.OnText("A");

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		Func<Entity, bool> pick = t switch
		{
			"T" or "TEXT" => e => e is TextEntity or MText,
			"D" or "DIMENSION" => e => e is Dimension,
			"L" or "LEADERS" => e => e is Leader or MultiLeader,
			_ => e => e is TextEntity or MText or Dimension or Leader or MultiLeader,
		};
		var items = this.Ctx!.Doc.CurrentSpace.Entities.Where(pick).ToList();
		if (items.Count > 0) DrawOrderCommand.Apply(this.Ctx.Doc, items, DrawOrderCommand.Placement.Front, null);
		this.Ctx.Print($"객체 {items.Count}개를 맨 앞으로 가져왔습니다.");
		this.Finish();
	}
}

/// <summary>SETBYLAYER: 색·선종류·선가중치·투명도를 ByLayer로.</summary>
public sealed class SetByLayerCommand : EntityModificationCommand
{
	public override string Name => "SETBYLAYER";

	protected override void OnSelectionReady()
	{
		var actions = new List<IUndoableAction>();
		foreach (var e in this.Targets)
		{
			var (c, lt, lw, tr) = (e.Color, e.LineType, e.LineWeight, e.Transparency);
			var byLayerLt = this.Ctx!.Doc.Document.LineTypes.TryGetValue(LineType.ByLayerName, out var blt) ? blt : lt;
			actions.Add(new EntityEditAction("ByLayer로 설정", e,
				() => { e.Color = Color.ByLayer; e.LineType = byLayerLt; e.LineWeight = LineWeightType.ByLayer; e.Transparency = Transparency.ByLayer; },
				() => { e.Color = c; e.LineType = lt; e.LineWeight = lw; e.Transparency = tr; }));
		}
		if (actions.Count > 0) this.Ctx!.Doc.Execute(new CompositeAction("ByLayer로 설정", actions));
		this.Ctx!.Print($"객체 {actions.Count}개를 ByLayer로 바꿨습니다.");
		this.Finish();
	}
}

