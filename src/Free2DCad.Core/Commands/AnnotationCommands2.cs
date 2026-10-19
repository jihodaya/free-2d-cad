using System.Runtime.CompilerServices;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Geometry;
using Free2DCad.Core.Hatching;

namespace Free2DCad.Core.Commands;

/// <summary>마지막으로 만든 치수 (DIMBASELINE·DIMCONTINUE의 기준).</summary>
public static class DimensionMemory
{
	private static readonly ConditionalWeakTable<EditorDocument, Dimension> Last = new();

	public static void Remember(EditorDocument doc, Dimension dim) => Last.AddOrUpdate(doc, dim);

	public static Dimension? Get(EditorDocument doc) =>
		Last.TryGetValue(doc, out var d) && d.Owner != null ? d : null;

	internal static double Scale(DimensionStyle s) => s.ScaleFactor > 0 ? s.ScaleFactor : 1.0;

	/// <summary>치수 문자 옵션(T/M) 입력값을 치수에 적용 ("&lt;&gt;" = 측정값).</summary>
	internal static void ApplyText(Dimension dim, string? text)
	{
		if (!string.IsNullOrEmpty(text)) dim.Text = text;
	}
}

/// <summary>
/// DIMLINEAR / DIMALIGNED (기존 CAD와 같은 옵션): 두 점 또는 Enter → 객체 선택, 치수선 위치에서
/// [여러 줄 문자(M)/문자(T)/각도(A)/수평(H)/수직(V)/회전(R)].
/// </summary>
public sealed class DimLinearCommand : EditorCommand
{
	private readonly bool _aligned;
	private int _step;
	private XY _p1, _p2;
	private string? _text;
	private double? _textAngle;
	private double? _rotation; // null = 자동(수평/수직), 값 = 고정 회전
	private string? _await;

	public DimLinearCommand() : this(false)
	{
	}

	public DimLinearCommand(bool aligned) => this._aligned = aligned;

	public override string Name => this._aligned ? "DIMALIGNED" : "DIMLINEAR";
	public override string[] Aliases => this._aligned ? new[] { "DAL" } : new[] { "DLI" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._text = null;
		this._textAngle = null;
		this._rotation = null;
		this._await = null;
		this.Prompt = "객체를 선택하려면 ENTER/<첫번째 치수보조선의 원점>:";
	}

	private void PlacePrompt()
	{
		this._step = 2;
		this.Prompt = this._aligned
			? "[각도(A)/Mtext/텍스트(T)]/<치수선의위치(L)>:"
			: "[각도(A)/Mtext/텍스트(T)/수평(H)/수직(V)/회전(R)]/<치수선의위치(L)>:";
	}

	public override void OnEnter()
	{
		if (this._step == 0)
		{
			this._step = 10;
			this.Prompt = "치수를 지정할 객체 선택:";
			return;
		}
		if (this._await != null)
		{
			this._await = null;
			this.PlacePrompt();
		}
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._await != null)
		{
			switch (this._await)
			{
				case "T":
					this._text = t;
					break;
				case "A":
					if (Num.TryParse(t, out double a)) this._textAngle = Num.Rad(a);
					break;
				case "R":
					if (Num.TryParse(t, out double r)) this._rotation = Num.Rad(r);
					break;
			}
			this._await = null;
			this.PlacePrompt();
			return;
		}
		if (this._step != 2) return;
		switch (t.ToUpperInvariant())
		{
			case "M":
			case "T":
				this._await = "T";
				this.Prompt = "새 치수 문자 <<>>:";
				break;
			case "A":
				this._await = "A";
				this.Prompt = "치수 문자의 각도:";
				break;
			case "H" when !this._aligned:
				this._rotation = 0;
				break;
			case "V" when !this._aligned:
				this._rotation = Math.PI / 2;
				break;
			case "R" when !this._aligned:
				this._await = "R";
				this.Prompt = "치수선 각도 <0>:";
				break;
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._await != null) return;
		switch (this._step)
		{
			case 0:
				this._p1 = new XY(x, y);
				this.Ctx.LastPoint = (x, y);
				this._step = 1;
				this.Prompt = "두번째 치수보조선의 원점:";
				break;
			case 1:
				this._p2 = new XY(x, y);
				this.Ctx.LastPoint = (x, y);
				this.PlacePrompt();
				break;
			case 10:
				{
					var picked = this.Ctx.PickAt(x, y);
					switch (picked)
					{
						case Line l:
							this._p1 = Geom.P(l.StartPoint);
							this._p2 = Geom.P(l.EndPoint);
							break;
						case Arc a:
							this._p1 = new XY(a.Center.X + a.Radius * Math.Cos(a.StartAngle), a.Center.Y + a.Radius * Math.Sin(a.StartAngle));
							this._p2 = new XY(a.Center.X + a.Radius * Math.Cos(a.EndAngle), a.Center.Y + a.Radius * Math.Sin(a.EndAngle));
							break;
						case Circle c:
							// 원: 고른 점 방향의 지름
							{
								double ang = Math.Atan2(y - c.Center.Y, x - c.Center.X);
								this._p1 = new XY(c.Center.X + c.Radius * Math.Cos(ang), c.Center.Y + c.Radius * Math.Sin(ang));
								this._p2 = new XY(c.Center.X - c.Radius * Math.Cos(ang), c.Center.Y - c.Radius * Math.Sin(ang));
								break;
							}
						case LwPolyline pl:
							{
								var segs = Geom.SegmentsOf(pl);
								var seg = segs[(int)Math.Min(segs.Count - 1, Math.Floor(Geom.ParamOf(segs, new XY(x, y))))];
								this._p1 = seg.At(0);
								this._p2 = seg.At(1);
								break;
							}
						default:
							this.Ctx.Print("선, 호, 원, 폴리선을 선택하십시오.");
							return;
					}
					this.PlacePrompt();
					break;
				}
			case 2:
				{
					var dim = this.Build(x, y);
					this.Ctx.Doc.AddEntities(dim);
					DimensionMemory.Remember(this.Ctx.Doc, dim);
					this.Ctx.Print($"치수 텍스트 = {dim.GetMeasurementText()}");
					this.Finish();
					break;
				}
		}
	}

	private Dimension Build(double x, double y)
	{
		var style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		Dimension dim;
		if (this._aligned)
		{
			dim = new DimensionAligned
			{
				FirstPoint = Num.Z(this._p1.X, this._p1.Y),
				SecondPoint = Num.Z(this._p2.X, this._p2.Y),
				DefinitionPoint = Num.Z(x, y),
				Style = style,
			};
		}
		else
		{
			double rot;
			if (this._rotation is { } r) rot = r;
			else
			{
				// 커서가 두 점 범위의 위·아래면 수평, 왼쪽·오른쪽이면 수직
				double minX = Math.Min(this._p1.X, this._p2.X), maxX = Math.Max(this._p1.X, this._p2.X);
				double minY = Math.Min(this._p1.Y, this._p2.Y), maxY = Math.Max(this._p1.Y, this._p2.Y);
				bool outsideX = x < minX || x > maxX, outsideY = y < minY || y > maxY;
				if (outsideX && !outsideY) rot = Math.PI / 2;
				else if (!outsideX && outsideY) rot = 0;
				else
				{
					double midX = (minX + maxX) / 2, midY = (minY + maxY) / 2;
					rot = Math.Abs(x - midX) - (maxX - minX) / 2 > Math.Abs(y - midY) - (maxY - minY) / 2 ? Math.PI / 2 : 0;
				}
			}
			dim = new DimensionLinear
			{
				FirstPoint = Num.Z(this._p1.X, this._p1.Y),
				SecondPoint = Num.Z(this._p2.X, this._p2.Y),
				DefinitionPoint = Num.Z(x, y),
				Rotation = rot,
				Style = style,
			};
		}
		DimensionMemory.ApplyText(dim, this._text);
		if (this._textAngle is { } ta) dim.TextRotation = ta;
		try
		{
			Free2DCad.Core.Editing.DimensionGeometry.PlaceDimensionLine((DimensionAligned)dim, dim.DefinitionPoint);
			dim.UpdateBlock();
		}
		catch (Exception)
		{
			// 같은 점 두 개 등 블록을 만들 수 없는 경우
		}
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 2 && this._await == null && this.Ctx != null)
		{
			yield return this.Build(cursorX, cursorY);
		}
	}
}

/// <summary>
/// DIMBASELINE / DIMCONTINUE: 마지막(또는 고른) 선형·정렬·각도 치수에 이어 기준선/연속 치수.
/// </summary>
public sealed class DimChainCommand : EditorCommand
{
	private readonly bool _continue;
	private Dimension? _base;
	private XYZ _origin; // 기준선: 첫 보조선 원점, 연속: 직전 두 번째 원점
	private int _count;

	public DimChainCommand() : this(false)
	{
	}

	public DimChainCommand(bool cont) => this._continue = cont;

	public override string Name => this._continue ? "DIMCONTINUE" : "DIMBASELINE";
	public override IEnumerable<Entity> Highlighted => this._base != null ? new Entity[] { this._base } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._base == null;

	public override string[] Aliases => this._continue ? new[] { "DCO" } : new[] { "DBA" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._count = 0;
		var last = DimensionMemory.Get(ctx.Doc);
		if (last is DimensionLinear or DimensionAligned or DimensionAngular3Pt or DimensionAngular2Line)
		{
			this.SetBase(last);
		}
		else
		{
			this.Prompt = this._continue ? "계속하려면 치수 선택:" : "기준선의 치수 선택:";
		}
	}

	private void SetBase(Dimension dim)
	{
		this._base = dim;
		this._origin = dim switch
		{
			DimensionLinear l => this._continue ? l.SecondPoint : l.FirstPoint,
			DimensionAligned a => this._continue ? a.SecondPoint : a.FirstPoint,
			DimensionAngular3Pt p => this._continue ? p.SecondPoint : p.FirstPoint,
			_ => XYZ.Zero,
		};
		this.Prompt = this._continue ? "계속하기: ENTER 를 눌러 시작 치수를 선택하세요 [선택(S)/취소(U)]/<다음 연장선의 원점>:" : "기준선: ENTER 를 눌러 시작 치수를 선택하세요 [선택(S)/취소(U)]/<다음 연장선의 원점>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._base == null)
		{
			if (this.Ctx.PickAt(x, y) is Dimension d and (DimensionLinear or DimensionAligned or DimensionAngular3Pt))
			{
				// 고른 점에 가까운 보조선을 기준으로
				this.SetBase(d);
				if (d is DimensionLinear dl)
				{
					double d1 = Geom.Dist(Geom.P(dl.FirstPoint), new XY(x, y)), d2 = Geom.Dist(Geom.P(dl.SecondPoint), new XY(x, y));
					this._origin = (this._continue ? d1 < d2 : d1 < d2) ? dl.FirstPoint : dl.SecondPoint;
				}
			}
			else
			{
				this.Ctx.Print("선형, 정렬, 각도 치수를 선택하십시오.");
			}
			return;
		}
		var dim = this.Next(x, y);
		if (dim == null) return;
		this.Ctx.Doc.AddEntities(dim);
		DimensionMemory.Remember(this.Ctx.Doc, dim);
		this._count++;
		this.Ctx.Print($"치수 텍스트 = {dim.GetMeasurementText()}");
		this._base = dim;
		if (this._continue) this._origin = dim switch
		{
			DimensionLinear l => l.SecondPoint,
			DimensionAligned a => a.SecondPoint,
			DimensionAngular3Pt p => p.SecondPoint,
			_ => this._origin,
		};
	}

	private Dimension? Next(double x, double y)
	{
		var style = this._base!.Style ?? DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		double inc = this._continue ? 0 : (style.DimensionLineIncrement > 0 ? style.DimensionLineIncrement : 3.75) * DimensionMemory.Scale(style);
		var p2 = Num.Z(x, y);
		Dimension dim;
		switch (this._base)
		{
			case DimensionLinear l:
				{
					// 치수선: 기준 치수선을 바깥쪽(기준선) 또는 같은 줄(연속)
					double ux = -Math.Sin(l.Rotation), uy = Math.Cos(l.Rotation); // 치수선에 수직
					double side = (l.DefinitionPoint.X - l.FirstPoint.X) * ux + (l.DefinitionPoint.Y - l.FirstPoint.Y) * uy >= 0 ? 1 : -1;
					var defPt = new XYZ(l.DefinitionPoint.X + ux * inc * side, l.DefinitionPoint.Y + uy * inc * side, 0);
					var nd = new DimensionLinear { FirstPoint = this._origin, SecondPoint = p2, Rotation = l.Rotation, Style = style, DefinitionPoint = defPt };
					Free2DCad.Core.Editing.DimensionGeometry.PlaceDimensionLine(nd, defPt);
					dim = nd;
					break;
				}
			case DimensionAligned a:
				{
					double dx = a.SecondPoint.X - a.FirstPoint.X, dy = a.SecondPoint.Y - a.FirstPoint.Y, len = Math.Sqrt(dx * dx + dy * dy);
					double ux = -dy / len, uy = dx / len;
					double off = (a.DefinitionPoint.X - a.FirstPoint.X) * ux + (a.DefinitionPoint.Y - a.FirstPoint.Y) * uy;
					double s = off >= 0 ? 1 : -1;
					// 새 정렬 치수: 원점→새 점 방향, 같은 거리(+증분)만큼 떨어진 치수선
					double ndx = x - this._origin.X, ndy = y - this._origin.Y, nl = Math.Sqrt(ndx * ndx + ndy * ndy);
					if (nl < 1e-12) return null;
					double nux = -ndy / nl, nuy = ndx / nl;
					double d = Math.Abs(off) + inc;
					var defPt = new XYZ(x + nux * d * s, y + nuy * d * s, 0);
					var nd = new DimensionAligned { FirstPoint = this._origin, SecondPoint = p2, Style = style, DefinitionPoint = defPt };
					Free2DCad.Core.Editing.DimensionGeometry.PlaceDimensionLine(nd, defPt);
					dim = nd;
					break;
				}
			case DimensionAngular3Pt p:
				{
					double r = Geom.Dist(Geom.P(p.AngleVertex), Geom.P(p.DefinitionPoint)) + inc;
					double ang = Math.Atan2(y - p.AngleVertex.Y, x - p.AngleVertex.X);
					double a0 = Math.Atan2(this._origin.Y - p.AngleVertex.Y, this._origin.X - p.AngleVertex.X);
					double mid = a0 + Math.Atan2(Math.Sin(ang - a0), Math.Cos(ang - a0)) / 2;
					var nd = new DimensionAngular3Pt
					{
						AngleVertex = p.AngleVertex,
						FirstPoint = this._origin,
						SecondPoint = p2,
						DefinitionPoint = new XYZ(p.AngleVertex.X + r * Math.Cos(mid), p.AngleVertex.Y + r * Math.Sin(mid), 0),
						Style = style,
					};
					dim = nd;
					break;
				}
			default:
				return null;
		}
		try
		{
			dim.UpdateBlock();
		}
		catch (Exception)
		{
		}
		return dim;
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (t == "U" && this._count > 0 && this.Ctx != null)
		{
			this.Ctx.Doc.UndoLastStep();
			this._count--;
		}
		else if (t == "S")
		{
			this._base = null;
			this.Prompt = this._continue ? "계속하려면 치수 선택:" : "기준선의 치수 선택:";
		}
	}

	public override void OnEnter()
	{
		if (this._base != null && this._count == 0)
		{
			this.OnText("S");
			return;
		}
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._base == null || this.Ctx == null) yield break;
		Dimension? d = null;
		try { d = this.Next(cursorX, cursorY); } catch (Exception) { }
		if (d != null) yield return d;
	}
}

/// <summary>DIMORDINATE: 피쳐 위치 → 지시선 끝점 (끌기 방향으로 X/Y 자동, X·Y 옵션, 문자 T).</summary>
public sealed class DimOrdinateCommand : EditorCommand
{
	private XY? _feature;
	private bool? _forceX;
	private string? _text;
	private bool _awaitText;

	public override string Name => "DIMORDINATE";
	public override string[] Aliases => new[] { "DOR" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._feature = null;
		this.Prompt = "피쳐 위치 지정:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._awaitText)
		{
			this._text = t;
			this._awaitText = false;
			this.Prompt = "지시선 끝점 지정 또는 [X데이텀(X)/Y데이텀(Y)/문자(T)]:";
			return;
		}
		switch (t.ToUpperInvariant())
		{
			case "X": this._forceX = true; break;
			case "Y": this._forceX = false; break;
			case "T":
			case "M":
				this._awaitText = true;
				this.Prompt = "새 치수 문자 <<>>:";
				break;
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._feature == null)
		{
			this._feature = new XY(x, y);
			this.Ctx.LastPoint = (x, y);
			this.Prompt = "지시선 끝점 지정 또는 [X데이텀(X)/Y데이텀(Y)/문자(T)]:";
			return;
		}
		var dim = this.Build(x, y);
		this.Ctx.Doc.AddEntities(dim);
		DimensionMemory.Remember(this.Ctx.Doc, dim);
		this.Finish();
	}

	private DimensionOrdinate Build(double x, double y)
	{
		var f = this._feature!.Value;
		bool isX = this._forceX ?? Math.Abs(y - f.Y) > Math.Abs(x - f.X); // 위아래로 끌면 X 좌표
		var dim = new DimensionOrdinate
		{
			DefinitionPoint = XYZ.Zero,
			FeatureLocation = Num.Z(f.X, f.Y),
			LeaderEndpoint = Num.Z(x, y),
			IsOrdinateTypeX = isX,
			Style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document),
		};
		DimensionMemory.ApplyText(dim, this._text);
		try { dim.UpdateBlock(); } catch (Exception) { }
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._feature != null && !this._awaitText && this.Ctx != null) yield return this.Build(cursorX, cursorY);
	}
}

/// <summary>DIMARC: 호(또는 폴리선 호 조각)의 길이 치수.</summary>
public sealed class DimArcCommand : EditorCommand
{
	private XY _c;
	private double _r, _a0, _a1;
	private bool _picked;
	private string? _text;
	private bool _awaitText;

	public override string Name => "DIMARC";
	public override bool PickingObject => !this.IsFinished && !this._picked;

	public override string[] Aliases => new[] { "DAR" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._picked = false;
		this.Prompt = "호 또는 폴리선 호 세그먼트 선택:";
	}

	public override void OnText(string text)
	{
		if (this._awaitText)
		{
			this._text = text.Trim();
			this._awaitText = false;
			this.Prompt = "호 길이 치수 위치 지정 또는 [문자(T)]:";
		}
		else if (this._picked && text.Trim().ToUpperInvariant() is "T" or "M")
		{
			this._awaitText = true;
			this.Prompt = "새 치수 문자 <<>>:";
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (!this._picked)
		{
			var e = this.Ctx.PickAt(x, y);
			Geom.ArcSeg? arc = null;
			if (e is Arc a) arc = (Geom.ArcSeg)Geom.SegmentsOf(a)[0];
			else if (e is LwPolyline pl)
			{
				var segs = Geom.SegmentsOf(pl);
				arc = segs[(int)Math.Min(segs.Count - 1, Math.Floor(Geom.ParamOf(segs, new XY(x, y))))] as Geom.ArcSeg;
			}
			if (arc == null)
			{
				this.Ctx.Print("호를 선택하십시오.");
				return;
			}
			this._c = arc.C;
			this._r = arc.R;
			this._a0 = arc.Sweep >= 0 ? arc.Start : arc.Start + arc.Sweep;
			this._a1 = this._a0 + Math.Abs(arc.Sweep);
			this._picked = true;
			this.Prompt = "호 길이 치수 위치 지정 또는 [문자(T)]:";
			return;
		}
		var dim = this.Build(x, y);
		this.Ctx.Doc.AddEntities(dim);
		this.Finish();
	}

	private DimensionArc Build(double x, double y)
	{
		double dist = Math.Max(Geom.Dist(this._c, new XY(x, y)), 1e-6);
		double mid = (this._a0 + this._a1) / 2;
		var dim = new DimensionArc
		{
			Center = Num.Z(this._c.X, this._c.Y),
			FirstPoint = Num.Z(this._c.X + this._r * Math.Cos(this._a0), this._c.Y + this._r * Math.Sin(this._a0)),
			SecondPoint = Num.Z(this._c.X + this._r * Math.Cos(this._a1), this._c.Y + this._r * Math.Sin(this._a1)),
			StartAngle = Geom.Norm(this._a0),
			EndAngle = Geom.Norm(this._a1),
			DefinitionPoint = Num.Z(this._c.X + dist * Math.Cos(mid), this._c.Y + dist * Math.Sin(mid)),
			Style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document),
		};
		dim.LeaderPoint1 = dim.FirstPoint;
		dim.LeaderPoint2 = dim.SecondPoint;
		DimensionMemory.ApplyText(dim, this._text);
		try { dim.UpdateBlock(); } catch (Exception) { }
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._picked && !this._awaitText && this.Ctx != null) yield return this.Build(cursorX, cursorY);
	}
}

/// <summary>DIMCENTER: 원·호의 중심 표시 (치수 스타일 중심 표시 크기, 음수면 중심선까지).</summary>
public sealed class DimCenterCommand : EditorCommand
{
	public override string Name => "DIMCENTER";
	public override bool PickingObject => !this.IsFinished;

	public override string[] Aliases => new[] { "DCE" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this.Prompt = "원 또는 호 선택:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this.Ctx.PickAt(x, y) is not Circle c)
		{
			this.Ctx.Print("원 또는 호를 선택하십시오.");
			return;
		}
		var style = DimensionStyleHelper.Current(this.Ctx.Doc.Document);
		double size = style.CenterMarkSize * DimensionMemory.Scale(style);
		if (Math.Abs(size) < 1e-12) size = 2.5 * DimensionMemory.Scale(style);
		var lines = Mark(c, size);
		foreach (var l in lines)
		{
			l.Color = style.DimensionLineColor;
		}
		this.Ctx.Doc.AddEntities(lines);
		this.Finish();
	}

	public static List<Entity> Mark(Circle c, double size)
	{
		double s = Math.Abs(size), cx = c.Center.X, cy = c.Center.Y;
		var list = new List<Entity>
		{
			new Line(Num.Z(cx - s, cy), Num.Z(cx + s, cy)),
			new Line(Num.Z(cx, cy - s), Num.Z(cx, cy + s)),
		};
		if (size < 0)
		{
			double r = c.Radius;
			// 중심선: 표시 끝에서 간격(크기)을 두고 원 밖으로 크기만큼
			list.Add(new Line(Num.Z(cx + 2 * s, cy), Num.Z(cx + r + s, cy)));
			list.Add(new Line(Num.Z(cx - 2 * s, cy), Num.Z(cx - r - s, cy)));
			list.Add(new Line(Num.Z(cx, cy + 2 * s), Num.Z(cx, cy + r + s)));
			list.Add(new Line(Num.Z(cx, cy - 2 * s), Num.Z(cx, cy - r - s)));
		}
		return list;
	}
}

/// <summary>
/// QLEADER (LEADER는 ClassicLeaderCommand): 화살표 점 → 연결선 점(여러 개) → 문자. 주석 MText를 지시선에 연결한다.
/// 문자를 명령줄에서 입력하지 않고 Enter를 누르면 화면에서 바로 입력(앱이 Created로 편집 시작).
/// </summary>
public sealed class LeaderCommand : EditorCommand
{
	private readonly string _name;
	private readonly List<XY> _pts = new();
	private readonly List<string> _lines = new();
	private bool _textStep;

	public LeaderCommand() : this("QLEADER")
	{
	}

	public LeaderCommand(string name) => this._name = name;

	public override string Name => this._name;
	public override string[] Aliases => this._name switch
	{
		"QLEADER" => new[] { "LE" },
		"MLEADER" => new[] { "MLD" },
		_ => Array.Empty<string>(),
	};

	/// <summary>문자를 비운 채 만든 주석 (앱이 바로 화면 편집을 시작).</summary>
	public MText? Created { get; private set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._pts.Clear();
		this._lines.Clear();
		this._textStep = false;
		this.Prompt = "지시선 화살촉 위치 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._textStep) return;
		var p = new XY(x, y);
		if (this._pts.Count > 0 && this.Ctx.Ortho)
		{
			var o = CoordinateParser.ApplyOrtho((this._pts[^1].X, this._pts[^1].Y), (x, y));
			p = new XY(o.X, o.Y);
		}
		this._pts.Add(p);
		this.Ctx.LastPoint = (p.X, p.Y);
		this.Prompt = this._pts.Count == 1 ? "지시선 연결선 위치 지정:" : "다음 점 지정 <문자 입력>:";
		if (this._name == "MLEADER" && this._pts.Count == 2) this.ToText();
	}

	private void ToText()
	{
		this._textStep = true;
		this.Prompt = "주석 문자의 첫 번째 줄 입력 <화면에서 입력>:";
	}

	public override void OnEnter()
	{
		if (!this._textStep)
		{
			if (this._pts.Count >= 2) this.ToText();
			else this.Finish();
			return;
		}
		this.Create();
	}

	public override void OnText(string text)
	{
		if (!this._textStep) return;
		this._lines.Add(text);
		this.Prompt = "주석 문자의 다음 줄 입력 <완료>:";
	}

	private void Create()
	{
		var doc = this.Ctx!.Doc;
		var style = DimensionStyleHelper.Current(doc.Document);
		double scale = DimensionMemory.Scale(style);
		double h = (style.TextHeight > 0 ? style.TextHeight : 2.5) * scale;
		double gap = Math.Abs(style.DimensionLineGap) * scale;
		var last = this._pts[^1];
		var prev = this._pts[^2];
		double dir = last.X >= prev.X ? 1 : -1;
		var verts = this._pts.Select(p => Num.Z(p.X, p.Y)).ToList();
		// 마지막 조각이 수평이 아니면 짧은 연결선(화살표 크기)을 붙인다
		if (Math.Abs(last.Y - prev.Y) > 1e-9)
		{
			double hook = (style.ArrowSize > 0 ? style.ArrowSize : 2.5) * scale;
			last = new XY(last.X + dir * hook, last.Y);
			verts.Add(Num.Z(last.X, last.Y));
		}
		var mtext = new MText
		{
			Value = string.Join("\\P", this._lines.Select(MTextValue)),
			Height = h,
			InsertPoint = Num.Z(last.X + dir * gap, last.Y + h / 2),
			AttachmentPoint = dir > 0 ? AttachmentPointType.TopLeft : AttachmentPointType.TopRight,
			Style = style.Style ?? doc.Document.TextStyles.FirstOrDefault() ?? TextStyle.Default,
		};
		var leader = new Leader
		{
			Vertices = verts,
			ArrowHeadEnabled = true,
			Style = style,
			PathType = LeaderPathType.StraightLineSegments,
			CreationType = LeaderCreationType.CreatedWithTextAnnotation,
			HookLineDirection = dir > 0 ? HookLineDirection.Same : HookLineDirection.Opposite,
			TextHeight = h,
			TextWidth = 0,
			AssociatedAnnotation = mtext,
		};
		doc.AddEntities(leader, mtext);
		if (this._lines.Count == 0) this.Created = mtext;
		this.Finish();
	}

	private static string MTextValue(string s) => s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._pts.Count == 0 || this._textStep) yield break;
		var pl = new LwPolyline();
		foreach (var p in this._pts) pl.Vertices.Add(new LwPolyline.Vertex(p));
		pl.Vertices.Add(new LwPolyline.Vertex(new XY(cursorX, cursorY)));
		yield return pl;
	}
}

/// <summary>TXT2MTXT: 한 줄 문자 여러 개를 위에서 아래 순서로 하나의 여러 줄 문자로.</summary>
public sealed class Txt2MtxtCommand : EntityModificationCommand
{
	public override string Name => "TXT2MTXT";

	protected override void OnSelectionReady()
	{
		var texts = this.Targets.OfType<TextEntity>().Where(t => t is not AttributeEntity and not AttributeDefinition)
			.OrderByDescending(t => t.InsertPoint.Y).ThenBy(t => t.InsertPoint.X).ToList();
		if (texts.Count == 0)
		{
			this.Ctx!.Print("문자(TEXT)를 선택하십시오.");
			this.Finish();
			return;
		}
		var first = texts[0];
		double left = texts.Min(t => t.InsertPoint.X);
		var mt = new MText
		{
			Value = string.Join("\\P", texts.Select(t => t.Value)),
			Height = first.Height,
			InsertPoint = Num.Z(left, first.InsertPoint.Y + first.Height),
			AttachmentPoint = AttachmentPointType.TopLeft,
			AlignmentPoint = new XYZ(Math.Cos(first.Rotation), Math.Sin(first.Rotation), 0),
			Style = first.Style,
		};
		Geom.CopyProps(first, mt);
		this.Ctx!.Doc.Replace(texts, new[] { mt }, "문자를 여러 줄 문자로");
		this.Ctx.Print($"문자 {texts.Count}개를 여러 줄 문자 하나로 바꿨습니다.");
		this.Finish();
	}
}

/// <summary>TEXTEDIT / DDEDIT: 문자·여러 줄 문자·치수를 골라 화면에서 바로 편집 (앱이 Picked로 편집 시작).</summary>
public sealed class TextEditCommand : EditorCommand
{
	public override string Name => "TEXTEDIT";
	public override string[] Aliases => new[] { "DDEDIT", "ED" };

	public Entity? Picked { get; private set; }

	public override bool WantsSelection => !this.IsFinished;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (ctx.Selection.Count == 1 && IsEditable(ctx.Selection.First()))
		{
			this.Picked = ctx.Selection.First();
			this.Finish();
			return;
		}
		this.Prompt = "주석 객체 선택:";
	}

	private static bool IsEditable(Entity e) => e is TextEntity or MText or Dimension or TableEntity;

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		var e = entities.FirstOrDefault(IsEditable);
		if (e == null) return;
		this.Picked = e;
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx?.PickAt(x, y) is { } e) this.OnSelection(new[] { e });
	}

	public override void OnEnter() => this.Finish();
}

/// <summary>BOUNDARY: 안쪽 점을 찍어 둘러싼 영역의 경계를 닫힌 폴리선으로 만든다 (섬 포함).</summary>
public sealed class BoundaryCommand : EditorCommand
{
	private int _made;

	public override string Name => "BOUNDARY";
	public override string[] Aliases => new[] { "BO" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._made = 0;
		this.Prompt = "내부 점 선택 <완료는 Enter>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var scene = Free2DCad.Rendering.SceneBuilder.Build(this.Ctx.Doc.Document);
		var loops = HatchBoundaryFinder.FindAt(scene, x, y, ACadSharp.Entities.HatchStyleType.Normal);
		if (loops == null || loops.Count == 0)
		{
			this.Ctx.Print("유효한 경계를 찾을 수 없습니다.");
			return;
		}
		var made = new List<Entity>();
		foreach (var loop in loops)
		{
			var pl = new LwPolyline { IsClosed = true };
			for (int i = 0; i + 1 < loop.Length; i += 2) pl.Vertices.Add(new LwPolyline.Vertex(new XY(loop[i], loop[i + 1])));
			// 끝점이 시작점과 같으면 하나 뺀다
			if (pl.Vertices.Count > 2 && Geom.Dist(pl.Vertices[0].Location, pl.Vertices[^1].Location) < 1e-9) pl.Vertices.RemoveAt(pl.Vertices.Count - 1);
			made.Add(pl);
		}
		this.Ctx.Doc.AddEntities(made);
		this._made += made.Count;
		this.Ctx.Print($"폴리선 {made.Count}개를 만들었습니다.");
	}

	public override void OnEnter()
	{
		this.Ctx?.Print($"BOUNDARY: 폴리선 {this._made}개 작성");
		this.Finish();
	}
}
