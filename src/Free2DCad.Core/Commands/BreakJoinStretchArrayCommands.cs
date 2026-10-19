using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// BREAK (기존 CAD와 같은 문구·옵션): 객체 선택(고른 점 = 첫 끊기점) →
/// "두번째 끊기점 지정 or [첫번째점(F)/분할(S)]:". 첫번째점(F): "첫번째 끊기점:" → "두번째 끊기점:".
/// 분할(S): 고른 점에서 둘로 나눔 (닫힌 객체는 "닫힌 객체를 하나의 지점에서 분할할 수 없습니다.").
/// 선·호·원·폴리선(호 조각 포함) 지원. 원·닫힌 폴리선은 첫 점에서 두 번째 점까지 반시계(진행 방향)로 지운다.
/// </summary>
public sealed class BreakCommand : EditorCommand
{
	private int _step; // 0 선택, 1 두 번째 점, 2 첫 점 다시, 3 두 번째 점
	private Entity? _target;
	private (double X, double Y) _pt1;

	public override string Name => "BREAK";
	public override string[] Aliases => new[] { "BR" };

	public override bool PickingObject => !this.IsFinished && this._step == 0;

	public override IEnumerable<Entity> Highlighted => this._target != null ? new[] { this._target } : Array.Empty<Entity>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._target = null;
		this.Prompt = "객체 선택:";
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		if (this._step == 1 && t == "F")
		{
			this._step = 2;
			this.Prompt = "첫번째 끊기점:";
			return;
		}
		if (this._step == 1 && t == "S")
		{
			this.Split(this._target!, this._pt1);
			return;
		}
		if (CoordinateParser.TryParse(text.Trim(), this.Ctx.LastPoint, null, false, out (double X, double Y) pt))
		{
			this.OnPoint(pt.X, pt.Y);
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 0:
				{
					var picked = this.Ctx.PickAt(x, y);
					if (picked is Line or Arc or Circle or LwPolyline)
					{
						this._target = picked;
						this._pt1 = (x, y);
						this.Ctx.LastPoint = (x, y);
						this._step = 1;
						this.Prompt = "두번째 끊기점 지정 or [첫번째점(F)/분할(S)]:";
					}
					else if (picked != null)
					{
						this.Ctx.Print("이 객체는 끊을 수 없습니다.");
					}
					return;
				}
			case 1:
			case 3:
				this.Break(this._target!, this._pt1, (x, y));
				return;
			case 2:
				this._pt1 = (x, y);
				this.Ctx.LastPoint = (x, y);
				this._step = 3;
				this.Prompt = "두번째 끊기점:";
				return;
		}
	}

	public override void OnEnter()
	{
		if (this._step == 1 && this._target != null)
		{
			// 두 번째 점 없이 Enter = 첫 점에서 나눔
			this.Split(this._target, this._pt1);
			return;
		}
		this.Finish();
	}

	private void Split(Entity target, (double X, double Y) p)
	{
		if (Geom.IsClosed(target))
		{
			this.Ctx!.Print("닫힌 객체를 하나의 지점에서 분할할 수 없습니다.");
			return;
		}
		this.Break(target, p, p);
	}

	/// <summary>두 점 사이를 지운다. 같은 점이면 그 점에서 둘로 나눈다.</summary>
	private void Break(Entity target, (double X, double Y) p1, (double X, double Y) p2)
	{
		var segs = Geom.SegmentsOf(target);
		if (segs.Count == 0)
		{
			this.Finish();
			return;
		}
		double n = segs.Count;
		double t1 = Geom.ParamOf(segs, new XY(p1.X, p1.Y));
		double t2 = Geom.ParamOf(segs, new XY(p2.X, p2.Y));
		var pieces = new List<List<Geom.Seg>>();
		if (Geom.IsClosed(target))
		{
			// 진행 방향으로 t1 → t2 구간을 지우고 나머지(t2 → 한 바퀴 → t1)를 남긴다
			if (Math.Abs(t1 - t2) < 1e-9)
			{
				this.Ctx!.Print("닫힌 객체를 하나의 지점에서 분할할 수 없습니다.");
				return;
			}
			var keep = new List<Geom.Seg>();
			if (t2 > t1)
			{
				keep.AddRange(Geom.SubRange(segs, t2, n));
				keep.AddRange(Geom.SubRange(segs, 0, t1));
			}
			else
			{
				keep.AddRange(Geom.SubRange(segs, t2, t1));
			}
			pieces.Add(keep);
		}
		else
		{
			double a = Math.Min(t1, t2), b = Math.Max(t1, t2);
			pieces.Add(Geom.SubRange(segs, 0, a));
			pieces.Add(Geom.SubRange(segs, b, n));
		}
		var add = new List<Entity>();
		foreach (var piece in pieces)
		{
			// 원은 호 하나로, 나머지는 원래 종류를 따라 (폴리선 → 폴리선)
			var merged = MergeArcs(piece);
			if (merged.Count == 0 || Geom.Length(merged) < 1e-9) continue;
			if (Geom.ToEntity(merged, target) is { } e)
			{
				add.Add(e);
			}
		}
		this.Ctx!.Doc.Replace(new[] { target }, add, "끊기");
		this.Finish();
	}

	/// <summary>원을 감아 돌며 생긴 같은 원의 이어진 호 조각을 하나로 합친다.</summary>
	private static List<Geom.Seg> MergeArcs(List<Geom.Seg> segs)
	{
		var result = new List<Geom.Seg>();
		foreach (var s in segs)
		{
			if (result.Count > 0 && result[^1] is Geom.ArcSeg prev && s is Geom.ArcSeg cur
				&& Geom.Dist(prev.C, cur.C) < 1e-9 && Math.Abs(prev.R - cur.R) < 1e-9 && Math.Sign(prev.Sweep) == Math.Sign(cur.Sweep)
				&& Geom.Dist(prev.At(1), cur.At(0)) < 1e-9)
			{
				result[^1] = prev with { Sweep = prev.Sweep + cur.Sweep };
				continue;
			}
			result.Add(s);
		}
		return result;
	}
}

/// <summary>
/// JOIN 명령: 일직선 상의 Line들을 하나로 합치거나 끝점이 이어진 Line/Arc를 LwPolyline으로 결합한다.
/// </summary>
public sealed class JoinCommand : EditorCommand
{
	/// <summary>결합할 객체 선택</summary>
	public override bool WantsSelection => !this.IsFinished;

	private readonly List<Entity> _selected = new();

	public override string Name => "JOIN";
	public override string[] Aliases => new[] { "J" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._selected.Clear();
		this.Prompt = "원본 객체 또는 여러 객체를 한 번에 연결할 객체를 선택하세요:";
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		foreach (var e in entities)
		{
			if (!this._selected.Contains(e))
			{
				this._selected.Add(e);
			}
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
		var index = new SceneIndex(scene);
		var picked = index.Pick(x, y, 5.0);
		if (picked != null && !this._selected.Contains(picked))
		{
			this._selected.Add(picked);
		}
	}

	public override void OnEnter()
	{
		if (this.Ctx == null || this._selected.Count < 2)
		{
			this.Finish();
			return;
		}

		// 1. 모든 객체가 Line이고 일직선 상에 있는 경우: Line 결합
		if (this._selected.All(e => e is Line))
		{
			var lines = this._selected.Cast<Line>().ToList();
			if (TryMergeCollinearLines(this.Ctx, lines))
			{
				this.Finish();
				return;
			}
		}

		// 2. 끝점이 이어진 Line / Arc인 경우: LwPolyline으로 결합
		if (TryJoinIntoPolyline(this.Ctx, this._selected))
		{
			this.Finish();
			return;
		}

		this.Finish();
	}

	private static bool TryMergeCollinearLines(CommandContext ctx, List<Line> lines)
	{
		Line l0 = lines[0];
		double dx = l0.EndPoint.X - l0.StartPoint.X;
		double dy = l0.EndPoint.Y - l0.StartPoint.Y;
		double len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 1e-9) return false;

		double ux = dx / len;
		double uy = dy / len;

		// 모든 선의 시작점/끝점이 l0의 직선 상에 있는지 확인
		var pts = new List<XYZ>();
		foreach (var l in lines)
		{
			double d1 = PerpendicularDistance(l.StartPoint.X, l.StartPoint.Y, l0.StartPoint.X, l0.StartPoint.Y, ux, uy);
			double d2 = PerpendicularDistance(l.EndPoint.X, l.EndPoint.Y, l0.StartPoint.X, l0.StartPoint.Y, ux, uy);
			if (d1 > 1e-3 || d2 > 1e-3) return false;

			pts.Add(l.StartPoint);
			pts.Add(l.EndPoint);
		}

		// u 방향 투영값 기준으로 최소/최대 점 산출
		XYZ minPt = pts.OrderBy(p => (p.X - l0.StartPoint.X) * ux + (p.Y - l0.StartPoint.Y) * uy).First();
		XYZ maxPt = pts.OrderByDescending(p => (p.X - l0.StartPoint.X) * ux + (p.Y - l0.StartPoint.Y) * uy).First();

		var merged = new Line(minPt, maxPt) { Layer = l0.Layer, Color = l0.Color };
		ctx.Doc.Erase(lines);
		ctx.Doc.AddEntities(merged);
		return true;
	}

	private static double PerpendicularDistance(double px, double py, double ox, double oy, double ux, double uy)
	{
		double vx = px - ox;
		double vy = py - oy;
		return Math.Abs(vx * uy - vy * ux);
	}

	private static bool TryJoinIntoPolyline(CommandContext ctx, List<Entity> entities)
	{
		var remaining = new List<Entity>(entities);
		var vertices = new List<LwPolyline.Vertex>();

		Entity first = remaining[0];
		remaining.RemoveAt(0);

		XYZ currentEnd;
		if (first is Line l)
		{
			vertices.Add(new LwPolyline.Vertex(new XY(l.StartPoint.X, l.StartPoint.Y)));
			vertices.Add(new LwPolyline.Vertex(new XY(l.EndPoint.X, l.EndPoint.Y)));
			currentEnd = l.EndPoint;
		}
		else if (first is Arc a)
		{
			double startA = a.StartAngle;
			double endA = a.EndAngle;
			if (endA < startA) endA += 2.0 * Math.PI;
			double bulge = Math.Tan((endA - startA) / 4.0);

			double sx = a.Center.X + a.Radius * Math.Cos(a.StartAngle);
			double sy = a.Center.Y + a.Radius * Math.Sin(a.StartAngle);
			double ex = a.Center.X + a.Radius * Math.Cos(a.EndAngle);
			double ey = a.Center.Y + a.Radius * Math.Sin(a.EndAngle);

			vertices.Add(new LwPolyline.Vertex(new XY(sx, sy)) { Bulge = bulge });
			vertices.Add(new LwPolyline.Vertex(new XY(ex, ey)));
			currentEnd = new XYZ(ex, ey, 0);
		}
		else
		{
			return false;
		}

		while (remaining.Count > 0)
		{
			bool attached = false;
			for (int i = 0; i < remaining.Count; i++)
			{
				Entity next = remaining[i];
				if (next is Line nl)
				{
					if (DistanceSq(currentEnd, nl.StartPoint) < 1e-6)
					{
						vertices.Add(new LwPolyline.Vertex(new XY(nl.EndPoint.X, nl.EndPoint.Y)));
						currentEnd = nl.EndPoint;
						remaining.RemoveAt(i);
						attached = true;
						break;
					}
					else if (DistanceSq(currentEnd, nl.EndPoint) < 1e-6)
					{
						vertices.Add(new LwPolyline.Vertex(new XY(nl.StartPoint.X, nl.StartPoint.Y)));
						currentEnd = nl.StartPoint;
						remaining.RemoveAt(i);
						attached = true;
						break;
					}
				}
				else if (next is Arc na)
				{
					double sx = na.Center.X + na.Radius * Math.Cos(na.StartAngle);
					double sy = na.Center.Y + na.Radius * Math.Sin(na.StartAngle);
					double ex = na.Center.X + na.Radius * Math.Cos(na.EndAngle);
					double ey = na.Center.Y + na.Radius * Math.Sin(na.EndAngle);
					XYZ startPt = new XYZ(sx, sy, 0);
					XYZ endPt = new XYZ(ex, ey, 0);

					if (DistanceSq(currentEnd, startPt) < 1e-6)
					{
						double span = na.EndAngle > na.StartAngle ? na.EndAngle - na.StartAngle : (na.EndAngle + 2 * Math.PI) - na.StartAngle;
						double bulge = Math.Tan(span / 4.0);
						vertices[^1].Bulge = bulge;
						vertices.Add(new LwPolyline.Vertex(new XY(ex, ey)));
						currentEnd = endPt;
						remaining.RemoveAt(i);
						attached = true;
						break;
					}
				}
			}

			if (!attached)
			{
				break;
			}
		}

		if (vertices.Count >= 2)
		{
			var poly = new LwPolyline();
			poly.Layer = first.Layer;
			poly.Color = first.Color;
			foreach (var v in vertices)
			{
				poly.Vertices.Add(v);
			}

			// 처음 점과 마지막 점이 같으면 닫힌 폴리라인으로 설정
			if (DistanceSq(new XYZ(vertices[0].Location.X, vertices[0].Location.Y, 0), currentEnd) < 1e-6 && vertices.Count > 2)
			{
				poly.Vertices.RemoveAt(poly.Vertices.Count - 1);
				poly.IsClosed = true;
			}

			ctx.Doc.Erase(entities);
			ctx.Doc.AddEntities(poly);
			return true;
		}

		return false;
	}

	private static double DistanceSq(XYZ a, XYZ b)
	{
		double dx = a.X - b.X;
		double dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}
}

/// <summary>
/// STRETCH 명령: 교차 창으로 선택한 끝점/꼭짓점만 신축(이동)한다.
/// </summary>
public sealed class StretchCommand : EditorCommand
{
	private int _step;
	private (double X, double Y) _corner1;
	private (double X, double Y) _corner2;
	private (double X, double Y) _basePt;
	private readonly List<Entity> _candidates = new();

	public override string Name => "STRETCH";
	public override string[] Aliases => new[] { "S" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._candidates.Clear();
		this.Prompt = "객체 선택:";
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		foreach (var e in entities)
		{
			if (!this._candidates.Contains(e))
			{
				this._candidates.Add(e);
			}
		}
	}

	public override void OnEnter()
	{
		if (this._step <= 1)
		{
			this._step = 2;
			this.Prompt = "기준점 또는 변위:";
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 0)
		{
			this._corner1 = (x, y);
			this._step = 1;
			this.Prompt = "반대쪽 구석 지정:";
		}
		else if (this._step == 1)
		{
			this._corner2 = (x, y);
			this.CollectEnclosedEntities(this.Ctx);
			this._step = 2;
			this.Prompt = "기준점 또는 변위:";
		}
		else if (this._step == 2)
		{
			this._basePt = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 3;
			this.Prompt = "변위의 두번째 점:";
		}
		else if (this._step == 3)
		{
			double dx = x - this._basePt.X;
			double dy = y - this._basePt.Y;
			this.ApplyStretch(this.Ctx, dx, dy);
			this.Finish();
		}
	}

	private void CollectEnclosedEntities(CommandContext ctx)
	{
		double minX = Math.Min(this._corner1.X, this._corner2.X);
		double maxX = Math.Max(this._corner1.X, this._corner2.X);
		double minY = Math.Min(this._corner1.Y, this._corner2.Y);
		double maxY = Math.Max(this._corner1.Y, this._corner2.Y);

		foreach (var e in ctx.Doc.Document.Entities)
		{
			if (e is Line l)
			{
				if (IsInside(l.StartPoint.X, l.StartPoint.Y, minX, maxX, minY, maxY) ||
					IsInside(l.EndPoint.X, l.EndPoint.Y, minX, maxX, minY, maxY))
				{
					if (!this._candidates.Contains(l)) this._candidates.Add(l);
				}
			}
			else if (e is LwPolyline p)
			{
				if (p.Vertices.Any(v => IsInside(v.Location.X, v.Location.Y, minX, maxX, minY, maxY)))
				{
					if (!this._candidates.Contains(p)) this._candidates.Add(p);
				}
			}
		}
	}

	private void ApplyStretch(CommandContext ctx, double dx, double dy)
	{
		double minX = Math.Min(this._corner1.X, this._corner2.X);
		double maxX = Math.Max(this._corner1.X, this._corner2.X);
		double minY = Math.Min(this._corner1.Y, this._corner2.Y);
		double maxY = Math.Max(this._corner1.Y, this._corner2.Y);

		var toErase = new List<Entity>();
		var toAdd = new List<Entity>();

		foreach (var e in this._candidates)
		{
			if (e is Line l)
			{
				bool sIn = IsInside(l.StartPoint.X, l.StartPoint.Y, minX, maxX, minY, maxY);
				bool eIn = IsInside(l.EndPoint.X, l.EndPoint.Y, minX, maxX, minY, maxY);

				if (sIn || eIn)
				{
					XYZ newS = sIn ? new XYZ(l.StartPoint.X + dx, l.StartPoint.Y + dy, 0) : l.StartPoint;
					XYZ newE = eIn ? new XYZ(l.EndPoint.X + dx, l.EndPoint.Y + dy, 0) : l.EndPoint;
					var newLine = new Line(newS, newE) { Layer = l.Layer, Color = l.Color };
					toErase.Add(l);
					toAdd.Add(newLine);
				}
			}
			else if (e is LwPolyline p)
			{
				var newPoly = new LwPolyline { Layer = p.Layer, Color = p.Color, IsClosed = p.IsClosed };
				bool anyMoved = false;
				foreach (var v in p.Vertices)
				{
					if (IsInside(v.Location.X, v.Location.Y, minX, maxX, minY, maxY))
					{
						newPoly.Vertices.Add(new LwPolyline.Vertex(new XY(v.Location.X + dx, v.Location.Y + dy)) { Bulge = v.Bulge });
						anyMoved = true;
					}
					else
					{
						newPoly.Vertices.Add(new LwPolyline.Vertex(v.Location) { Bulge = v.Bulge });
					}
				}

				if (anyMoved)
				{
					toErase.Add(p);
					toAdd.Add(newPoly);
				}
			}
		}

		if (toErase.Count > 0)
		{
			ctx.Doc.Erase(toErase);
		}
		if (toAdd.Count > 0)
		{
			ctx.Doc.AddEntities(toAdd);
		}
	}

	private static bool IsInside(double x, double y, double minX, double maxX, double minY, double maxY)
	{
		return x >= minX - 1e-4 && x <= maxX + 1e-4 && y >= minY - 1e-4 && y <= maxY + 1e-4;
	}
}

