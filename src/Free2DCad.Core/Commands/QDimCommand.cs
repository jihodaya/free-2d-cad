using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// QDIM (빠른 치수, 기존 CAD와 같은 순서·옵션): 도형 선택 → 치수선 위치
/// [Continuous/Staggered/Baseline/Ordinate/Radius/Diameter/datumPoint/Edit/seTtings] &lt;Continuous&gt;.
/// 선택한 도형의 끝점(설정에서 교차점 포함)을 모아 한 번에 치수를 만든다. 커서가 도형의 위·아래면 가로, 왼쪽·오른쪽이면 세로.
/// </summary>
public sealed class QDimCommand : EntityModificationCommand
{
	public enum Mode { Continuous, Staggered, Baseline, Ordinate, Radius, Diameter }

	private static Mode _mode = Mode.Continuous;
	private static bool _intersections;

	private XY? _datum;
	private string? _await;
	private readonly List<XY> _removed = new();

	public override string Name => "QDIM";

	private const string SelectPrompt = "치수를 지정할 기하학적 도형을 선택하세요:";

	private string PlacePrompt => $"치수선 위치를 지정하세요. 또는 [Continuous/Staggered/Baseline/Ordinate/Radius/Diameter/datumPoint/Edit/seTtings] <{_mode}>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._datum = null;
		this._await = null;
		this._removed.Clear();
		if (this.WaitingForSelection) this.Prompt = SelectPrompt;
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		base.OnSelection(entities);
		if (this.WaitingForSelection) this.Prompt = SelectPrompt;
	}

	protected override void OnSelectionReady() => this.Prompt = this.PlacePrompt;

	public override void OnText(string text)
	{
		if (this.WaitingForSelection || this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		if (this._await == "T")
		{
			if (t is "E" or "I") _intersections = t == "I";
			this._await = null;
			this.Prompt = this.PlacePrompt;
			return;
		}
		if (this._await is "E" or "EA")
		{
			// 편집: 치수 점 빼기·더하기
			switch (t)
			{
				case "A":
					this._await = "EA";
					this.Prompt = "추가할 치수 점 지정 또는 [제거(R)/종료(X)] <종료>:";
					return;
				case "R":
					this._await = "E";
					this.Prompt = "제거할 치수 점 지정 또는 [추가(A)/종료(X)] <종료>:";
					return;
				case "X":
					this._await = null;
					this.Prompt = this.PlacePrompt;
					return;
			}
			return;
		}
		switch (t)
		{
			case "C": _mode = Mode.Continuous; break;
			case "S": _mode = Mode.Staggered; break;
			case "B": _mode = Mode.Baseline; break;
			case "O": _mode = Mode.Ordinate; break;
			case "R": _mode = Mode.Radius; break;
			case "D": _mode = Mode.Diameter; break;
			case "P":
				this._await = "P";
				this.Prompt = "새 데이텀 점 선택:";
				return;
			case "E":
				this._await = "E";
				this.Prompt = "제거할 치수 점 지정 또는 [추가(A)/종료(X)] <종료>:";
				return;
			case "T":
				this._await = "T";
				this.Prompt = $"연관 치수 우선 순위 [끝점(E)/교차점(I)] <{(_intersections ? "교차점" : "끝점")}>:";
				return;
			default:
				if (CoordinateParser.TryParse(text.Trim(), this.Ctx.LastPoint, null, false, out (double X, double Y) p))
				{
					this.OnPoint(p.X, p.Y);
				}
				return;
		}
		this.Prompt = this.PlacePrompt;
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		if (this._await != null)
		{
			this._await = null;
			this.Prompt = this.PlacePrompt;
			return;
		}
		this.Finish();
	}

	private readonly List<XY> _added = new();

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this.WaitingForSelection) return;
		var p = new XY(x, y);
		switch (this._await)
		{
			case "P":
				this._datum = p;
				this._await = null;
				this.Prompt = this.PlacePrompt;
				return;
			case "E":
				{
					// 가장 가까운 치수 점을 뺀다
					var pts = this.Points();
					if (pts.Count > 0) this._removed.Add(pts.OrderBy(q => Geom.Dist(q, p)).First());
					return;
				}
			case "EA":
				this._added.Add(p);
				return;
		}
		var dims = this.Build(p).ToList();
		if (dims.Count == 0)
		{
			this.Ctx.Print("치수를 만들 점이 없습니다.");
			return;
		}
		this.Ctx.Doc.AddEntities(dims.ToArray());
		foreach (var d in dims)
		{
			this.Ctx.Print($"치수 텍스트 = {d.GetMeasurementText()}");
		}
		DimensionMemory.Remember(this.Ctx.Doc, dims[^1]);
		this.Finish();
	}

	/// <summary>치수 점: 끝점(선·폴리선 꼭짓점·호 끝), 원은 사분점, 설정이 교차점이면 교차점도. 겹친 점은 하나로.</summary>
	private List<XY> Points()
	{
		var pts = new List<XY>();
		foreach (var e in this.Targets)
		{
			switch (e)
			{
				case Line l:
					pts.Add(Geom.P(l.StartPoint));
					pts.Add(Geom.P(l.EndPoint));
					break;
				case Arc a:
					pts.Add(new XY(a.Center.X + a.Radius * Math.Cos(a.StartAngle), a.Center.Y + a.Radius * Math.Sin(a.StartAngle)));
					pts.Add(new XY(a.Center.X + a.Radius * Math.Cos(a.EndAngle), a.Center.Y + a.Radius * Math.Sin(a.EndAngle)));
					break;
				case Circle c:
					pts.Add(new XY(c.Center.X - c.Radius, c.Center.Y));
					pts.Add(new XY(c.Center.X + c.Radius, c.Center.Y));
					pts.Add(new XY(c.Center.X, c.Center.Y - c.Radius));
					pts.Add(new XY(c.Center.X, c.Center.Y + c.Radius));
					break;
				case LwPolyline pl:
					pts.AddRange(pl.Vertices.Select(v => v.Location));
					break;
				case Polyline2D p2:
					pts.AddRange(p2.Vertices.Select(v => Geom.P(v.Location)));
					break;
				case Point pt:
					pts.Add(Geom.P(pt.Location));
					break;
			}
		}
		if (_intersections)
		{
			for (int i = 0; i < this.Targets.Count; i++)
			{
				for (int j = i + 1; j < this.Targets.Count; j++)
				{
					pts.AddRange(Geom.Intersections(this.Targets[i], this.Targets[j]));
				}
			}
		}
		pts.AddRange(this._added);
		var result = new List<XY>();
		foreach (var p in pts)
		{
			if (this._removed.Any(r => Geom.Dist(r, p) < 1e-6)) continue;
			if (!result.Any(q => Geom.Dist(q, p) < 1e-6)) result.Add(p);
		}
		return result;
	}

	/// <summary>커서 위치·방식에 따른 치수들 (미리보기와 같은 결과).</summary>
	private IEnumerable<Dimension> Build(XY cursor)
	{
		var doc = this.Ctx!.Doc.Document;
		var style = DimensionStyleHelper.Current(doc);
		double scale = style.ScaleFactor > 0 ? style.ScaleFactor : 1;

		if (_mode is Mode.Radius or Mode.Diameter)
		{
			foreach (var c in this.Targets.OfType<Circle>())
			{
				var center = Geom.P(c.Center);
				double ang = Math.Atan2(cursor.Y - center.Y, cursor.X - center.X);
				if (c is Arc arc && Geom.Norm(ang - arc.StartAngle) > Geom.Norm(arc.EndAngle - arc.StartAngle))
				{
					ang = arc.StartAngle + Geom.Norm(arc.EndAngle - arc.StartAngle) / 2; // 호 가운데
				}
				var on = new XY(center.X + c.Radius * Math.Cos(ang), center.Y + c.Radius * Math.Sin(ang));
				Dimension d = _mode == Mode.Radius
					? new DimensionRadius { DefinitionPoint = c.Center, AngleVertex = Num.Z(on.X, on.Y), Style = style }
					: new DimensionDiameter { AngleVertex = Num.Z(on.X, on.Y), DefinitionPoint = Num.Z(2 * center.X - on.X, 2 * center.Y - on.Y), Style = style };
				if (TryUpdate(d)) yield return d;
			}
			yield break;
		}

		var pts = this.Points();
		if (pts.Count < (_mode == Mode.Ordinate ? 1 : 2)) yield break;
		double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
		// 커서가 위·아래 → 가로 치수(가로 거리), 왼쪽·오른쪽 → 세로 치수
		bool outsideX = cursor.X < minX || cursor.X > maxX, outsideY = cursor.Y < minY || cursor.Y > maxY;
		bool horizontal = outsideY && !outsideX || (outsideX == outsideY &&
			Math.Abs(cursor.Y - (minY + maxY) / 2) - (maxY - minY) / 2 >= Math.Abs(cursor.X - (minX + maxX) / 2) - (maxX - minX) / 2);
		double rot = horizontal ? 0 : Math.PI / 2;
		double Key(XY p) => horizontal ? p.X : p.Y;
		// 같은 위치(측정 방향)인 점은 하나만: 치수선에 가까운 쪽
		var sorted = pts.GroupBy(p => Math.Round(Key(p), 6))
			.Select(g => g.OrderBy(p => horizontal ? Math.Abs(p.Y - cursor.Y) : Math.Abs(p.X - cursor.X)).First())
			.OrderBy(Key).ToList();

		DimensionLinear Lin(XY a, XY b, XY through)
		{
			var d = new DimensionLinear
			{
				FirstPoint = Num.Z(a.X, a.Y),
				SecondPoint = Num.Z(b.X, b.Y),
				DefinitionPoint = Num.Z(through.X, through.Y),
				Rotation = rot,
				Style = style,
			};
			Editing.DimensionGeometry.PlaceDimensionLine(d, d.DefinitionPoint);
			return d;
		}

		double step = (style.DimensionLineIncrement > 0 ? style.DimensionLineIncrement : 3.75) * scale;
		double sign = horizontal ? (cursor.Y >= (minY + maxY) / 2 ? 1 : -1) : (cursor.X >= (minX + maxX) / 2 ? 1 : -1);
		XY Offset(XY c, double k) => horizontal ? new XY(c.X, c.Y + sign * step * k) : new XY(c.X + sign * step * k, c.Y);

		switch (_mode)
		{
			case Mode.Continuous:
				for (int i = 0; i + 1 < sorted.Count; i++)
				{
					var d = Lin(sorted[i], sorted[i + 1], cursor);
					if (TryUpdate(d)) yield return d;
				}
				break;
			case Mode.Baseline:
				{
					var datum = this._datum is { } dp ? sorted.OrderBy(p => Math.Abs(Key(p) - Key(dp))).First() : sorted[0];
					int k = 0;
					foreach (var p in sorted.Where(p => !p.Equals(datum)).OrderBy(p => Math.Abs(Key(p) - Key(datum))))
					{
						var d = Lin(datum, p, Offset(cursor, k++));
						if (TryUpdate(d)) yield return d;
					}
					break;
				}
			case Mode.Staggered:
				{
					// 바깥 짝부터 안쪽으로: 안쪽 치수가 도형에 가깝고 바깥 치수일수록 멀리 (기존 CAD 엇갈림)
					int n = sorted.Count, pairs = n / 2;
					for (int i = 0; i < pairs; i++)
					{
						var d = Lin(sorted[i], sorted[n - 1 - i], Offset(cursor, pairs - 1 - i));
						if (TryUpdate(d)) yield return d;
					}
					break;
				}
			case Mode.Ordinate:
				{
					var origin = this._datum ?? new XY(0, 0);
					foreach (var p in sorted)
					{
						// 세로좌표: 가로 치수면 X 좌표(지시선 세로), 세로 치수면 Y 좌표
						var leaderEnd = horizontal ? new XY(p.X, cursor.Y) : new XY(cursor.X, p.Y);
						var d = new DimensionOrdinate
						{
							DefinitionPoint = Num.Z(origin.X, origin.Y),
							FeatureLocation = Num.Z(p.X, p.Y),
							LeaderEndpoint = Num.Z(leaderEnd.X, leaderEnd.Y),
							IsOrdinateTypeX = horizontal,
							Style = style,
						};
						if (TryUpdate(d)) yield return d;
					}
					break;
				}
		}
	}

	private static bool TryUpdate(Dimension d)
	{
		try
		{
			d.UpdateBlock();
			return d.Measurement > 1e-9 || d is DimensionOrdinate;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this.WaitingForSelection || this._await != null || this.Ctx == null) return Array.Empty<Entity>();
		return this.Build(new XY(cursorX, cursorY)).ToList();
	}
}
