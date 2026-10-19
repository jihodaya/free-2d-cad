using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// SPLINEDIT: 스플라인닫기(C)/스플라인열기(O)/정점이동(M)/세밀화(R)/역방향(E)/폴리선(P)/취소(U)/종료(X).
/// 맞춤점이 있는 스플라인은 맞춤점을, 없으면 조정점을 편집한다.
/// </summary>
public sealed class SplineEditCommand : EditorCommand
{
	private Spline? _sp;
	private string? _await;
	private int _vertex = -1;
	private int _undo;

	public override string Name => "SPLINEDIT";

	public override IEnumerable<Entity> Highlighted => this._sp != null ? new Entity[] { this._sp } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._sp == null;

	public override string[] Aliases => new[] { "SPE" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._sp = ctx.Selection.OfType<Spline>().FirstOrDefault();
		this._undo = 0;
		if (this._sp != null) this.Options();
		else this.Prompt = "스플라인 선택:";
	}

	private void Options()
	{
		this._await = null;
		bool closed = this._sp!.Flags.HasFlag(SplineFlags.Closed);
		this.Prompt = closed
			? "스플라인열기(O)/정점이동(M)/세밀화(R)/역방향(E)/폴리선(P)/취소(U)/<종료(X)>:"
			: "스플라인닫기(C)/정점이동(M)/세밀화(R)/역방향(E)/폴리선(P)/취소(U)/<종료(X)>:";
	}

	private bool UsesFit => this._sp!.FitPoints.Count >= 2;

	private List<XYZ> EditPoints => this.UsesFit ? this._sp!.FitPoints : this._sp!.ControlPoints;

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._sp == null)
		{
			if (this.Ctx.PickAt(x, y) is Spline sp)
			{
				this._sp = sp;
				this.Options();
			}
			else
			{
				this.Ctx.Print("스플라인을 선택하십시오.");
			}
			return;
		}
		switch (this._await)
		{
			case "M1":
				{
					var pts = this.EditPoints;
					this._vertex = Enumerable.Range(0, pts.Count).OrderBy(i => (pts[i].X - x) * (pts[i].X - x) + (pts[i].Y - y) * (pts[i].Y - y)).First();
					this._await = "M2";
					this.Prompt = "새 위치 지정:";
					break;
				}
			case "M2":
				{
					int idx = this._vertex;
					bool fit = this.UsesFit;
					this.Edit("정점 이동", sp =>
					{
						if (fit)
						{
							var pts = sp.FitPoints.Select(p => new XY(p.X, p.Y)).ToList();
							pts[idx] = new XY(x, y);
							Rebuild(sp, pts);
						}
						else
						{
							sp.ControlPoints[idx] = Num.Z(x, y);
						}
					});
					this._await = "M1";
					this.Prompt = "이동할 정점 선택 <종료>:";
					break;
				}
			case "R":
				{
					double u = NearestParam(this._sp, new XY(x, y));
					this.Edit("세밀화", sp => InsertKnot(sp, u));
					this.Ctx.Print("조정점을 하나 추가했습니다.");
					break;
				}
		}
	}

	public override void OnText(string text)
	{
		if (this._sp == null || this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		switch (t)
		{
			case "C":
			case "O":
				{
					bool close = t == "C";
					bool fit = this.UsesFit;
					this.Edit(close ? "스플라인 닫기" : "스플라인 열기", sp =>
					{
						if (fit)
						{
							var pts = sp.FitPoints.Select(p => new XY(p.X, p.Y)).ToList();
							if (close && Geom.Dist(pts[0], pts[^1]) > 1e-9) pts.Add(pts[0]);
							if (!close && pts.Count > 2 && Geom.Dist(pts[0], pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
							Rebuild(sp, pts);
						}
						else
						{
							var cps = sp.ControlPoints.Select(p => new XY(p.X, p.Y)).ToList();
							if (close && Geom.Dist(cps[0], cps[^1]) > 1e-9) cps.Add(cps[0]);
							if (!close && cps.Count > 2 && Geom.Dist(cps[0], cps[^1]) < 1e-9) cps.RemoveAt(cps.Count - 1);
							var nsp = SplineCommand.Build(cps, controlVertices: true)!;
							Copy(nsp, sp);
						}
						if (close) sp.Flags |= SplineFlags.Closed;
						else sp.Flags &= ~SplineFlags.Closed;
					});
					this.Options();
					break;
				}
			case "M":
				this._await = "M1";
				this.Prompt = "이동할 정점 선택 <종료>:";
				break;
			case "R":
				if (this.UsesFit)
				{
					// 세밀화는 조정점 편집: 맞춤점 정보는 버린다 (기존 CAD와 같음)
					this.Edit("맞춤점 제거", sp => sp.FitPoints.Clear());
				}
				this._await = "R";
				this.Prompt = "조정점을 추가할 위치 지정 <종료>:";
				break;
			case "E":
				this.Edit("역방향", ReverseCommand.Reverse);
				this.Ctx.Print("스플라인 방향을 반대로 했습니다.");
				break;
			case "P":
				{
					var poly = Geom.ToEntity(Geom.SegmentsOf(this._sp), new LwPolyline());
					if (poly is LwPolyline pl)
					{
						Geom.CopyProps(this._sp, pl);
						if (this._sp.Flags.HasFlag(SplineFlags.Closed) && pl.Vertices.Count > 2)
						{
							pl.Vertices.RemoveAt(pl.Vertices.Count - 1);
							pl.IsClosed = true;
						}
						this.Ctx.Doc.Replace(new[] { this._sp }, new[] { pl }, "스플라인을 폴리선으로");
					}
					this.Finish();
					break;
				}
			case "U":
				if (this._undo > 0)
				{
					this.Ctx.Doc.UndoLastStep();
					this._undo--;
				}
				break;
			case "X":
				this.Finish();
				break;
		}
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this.Options();
			return;
		}
		this.Finish();
	}

	private sealed record Snap(List<XYZ> Cps, List<double> Knots, List<double> Weights, List<XYZ> Fit, int Degree, SplineFlags Flags);

	private static Snap Take(Spline s) => new(s.ControlPoints.ToList(), s.Knots.ToList(), s.Weights.ToList(), s.FitPoints.ToList(), s.Degree, s.Flags);

	private static void Put(Spline s, Snap v)
	{
		s.ControlPoints.Clear();
		s.ControlPoints.AddRange(v.Cps);
		s.Knots.Clear();
		s.Knots.AddRange(v.Knots);
		s.Weights.Clear();
		s.Weights.AddRange(v.Weights);
		s.FitPoints.Clear();
		s.FitPoints.AddRange(v.Fit);
		s.Degree = v.Degree;
		s.Flags = v.Flags;
	}

	private static void Copy(Spline from, Spline to) => Put(to, Take(from) with { Flags = to.Flags });

	private void Edit(string name, Action<Spline> change)
	{
		var sp = this._sp!;
		var before = Take(sp);
		change(sp);
		var after = Take(sp);
		Put(sp, before);
		this.Ctx!.Doc.Execute(new EntityEditAction("스플라인 편집 - " + name, sp, () => Put(sp, after), () => Put(sp, before)));
		this._undo++;
	}

	private static void Rebuild(Spline sp, List<XY> fit)
	{
		var (ctrl, knots, deg) = SplineCommand.Interpolate(fit);
		sp.Degree = deg;
		sp.FitPoints.Clear();
		sp.FitPoints.AddRange(fit.Select(p => Num.Z(p.X, p.Y)));
		sp.ControlPoints.Clear();
		sp.ControlPoints.AddRange(ctrl.Select(p => Num.Z(p.X, p.Y)));
		sp.Knots.Clear();
		sp.Knots.AddRange(knots);
		sp.Weights.Clear();
	}

	/// <summary>B-스플라인 점 (가중치 없는 경우 de Boor).</summary>
	public static XY Evaluate(Spline sp, double u)
	{
		int p = sp.Degree;
		var k = sp.Knots;
		var c = sp.ControlPoints;
		int n = c.Count - 1;
		int span = p;
		for (int i = p; i <= n; i++)
		{
			if (u >= k[i] && u < k[i + 1]) span = i;
		}
		if (u >= k[n + 1]) span = n;
		var d = new XY[p + 1];
		for (int j = 0; j <= p; j++) d[j] = new XY(c[span - p + j].X, c[span - p + j].Y);
		for (int r = 1; r <= p; r++)
		{
			for (int j = p; j >= r; j--)
			{
				int i = span - p + j;
				double den = k[i + p - r + 1] - k[i];
				double a = den < 1e-14 ? 0 : (u - k[i]) / den;
				d[j] = new XY((1 - a) * d[j - 1].X + a * d[j].X, (1 - a) * d[j - 1].Y + a * d[j].Y);
			}
		}
		return d[p];
	}

	private static double NearestParam(Spline sp, XY p)
	{
		double u0 = sp.Knots[sp.Degree], u1 = sp.Knots[sp.ControlPoints.Count];
		double best = u0, bestD = double.MaxValue;
		for (int i = 0; i <= 600; i++)
		{
			double u = u0 + (u1 - u0) * i / 600;
			double d = Geom.Dist(Evaluate(sp, u), p);
			if (d < bestD)
			{
				bestD = d;
				best = u;
			}
		}
		return best;
	}

	/// <summary>뵘(Boehm) 매듭 삽입: 모양은 그대로 조정점 하나 추가.</summary>
	public static void InsertKnot(Spline sp, double u)
	{
		int p = sp.Degree;
		var k = sp.Knots.ToList();
		var c = sp.ControlPoints.ToList();
		int n = c.Count - 1;
		int span = p;
		for (int i = p; i <= n; i++)
		{
			if (u >= k[i] && u < k[i + 1]) span = i;
		}
		var nc = new List<XYZ>();
		for (int i = 0; i <= span - p; i++) nc.Add(c[i]);
		for (int i = span - p + 1; i <= span; i++)
		{
			double den = k[i + p] - k[i];
			double a = den < 1e-14 ? 0 : (u - k[i]) / den;
			nc.Add(new XYZ((1 - a) * c[i - 1].X + a * c[i].X, (1 - a) * c[i - 1].Y + a * c[i].Y, 0));
		}
		for (int i = span; i <= n; i++) nc.Add(c[i]);
		k.Insert(span + 1, u);
		sp.ControlPoints.Clear();
		sp.ControlPoints.AddRange(nc);
		sp.Knots.Clear();
		sp.Knots.AddRange(k);
		sp.Weights.Clear();
	}
}
