using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>명령이 화면 보기를 바꿀 때 쓰는 앱 쪽 기능 (ZOOM·PAN).</summary>
public interface IViewController
{
	void ZoomExtents();

	void ZoomWindow(double x1, double y1, double x2, double y2);

	/// <summary>배율 곱하기 (2 = 두 배 확대).</summary>
	void ZoomBy(double factor);

	void ZoomCenter(double x, double y, double? height);

	bool ZoomPrevious();

	void PanBy(double dx, double dy);

	/// <summary>현재 화면 높이 (도면 단위).</summary>
	double ViewHeight { get; }
}

/// <summary>끝나면 앱이 화면 선택을 이 결과로 바꾸는 명령 (유사 선택 등).</summary>
public interface ISelectionResultCommand
{
	IReadOnlyCollection<Entity>? SelectionResult { get; }
}

/// <summary>ZOOM: [전체(A)/중심(C)/범위(E)/이전(P)/축척(S)/윈도우(W)/객체(O)], 숫자X = 현재 화면의 배율, 두 점 = 윈도우.</summary>
public sealed class ZoomCommand : EditorCommand
{
	private string _step = "";
	private (double X, double Y)? _p1;
	private readonly List<Entity> _objects = new();

	public override string Name => "ZOOM";
	public override string[] Aliases => new[] { "Z" };

	public override bool WantsSelection => this._step == "O";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = "";
		this._p1 = null;
		this._objects.Clear();
		this.Prompt = "윈도우 구석 지정, 축척 비율 (nX 또는 nXP) 입력 또는 [전체(A)/중심(C)/범위(E)/이전(P)/축척(S)/윈도우(W)/객체(O)] <실시간>:";
	}

	private IViewController? V => this.Ctx?.View;

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		switch (this._step)
		{
			case "C2":
				if (Num.TryParse(t.TrimEnd('X'), out double h) && h > 0)
				{
					this.V?.ZoomCenter(this._p1!.Value.X, this._p1.Value.Y, t.EndsWith("X") ? this.V.ViewHeight / h : h);
				}
				this.Finish();
				return;
			case "S":
				this.ApplyScale(t);
				this.Finish();
				return;
		}
		switch (t)
		{
			case "A":
			case "E":
				this.V?.ZoomExtents();
				this.Finish();
				return;
			case "P":
				if (this.V != null && !this.V.ZoomPrevious()) this.Ctx?.Print("이전 뷰가 없습니다.");
				this.Finish();
				return;
			case "W":
				this._step = "W";
				this.Prompt = "첫 번째 구석 지정:";
				return;
			case "C":
				this._step = "C";
				this.Prompt = "중심점 지정:";
				return;
			case "S":
				this._step = "S";
				this.Prompt = "축척 비율 (nX 또는 nXP) 입력:";
				return;
			case "O":
				this._step = "O";
				this.Prompt = "객체 선택:";
				return;
		}
		this.ApplyScale(t);
		this.Finish();
	}

	private void ApplyScale(string t)
	{
		if (t.EndsWith("XP")) t = t[..^2];
		bool relative = t.EndsWith("X");
		if (relative) t = t[..^1];
		if (Num.TryParse(t, out double f) && f > 0)
		{
			this.V?.ZoomBy(f);
		}
	}

	public override void OnPoint(double x, double y)
	{
		switch (this._step)
		{
			case "C":
				this._p1 = (x, y);
				this._step = "C2";
				this.Prompt = $"배율 또는 높이 입력 <{this.V?.ViewHeight ?? 0:0.####}>:";
				return;
			case "O":
				if (this.Ctx?.PickAt(x, y) is { } e) this._objects.Add(e);
				return;
		}
		if (this._p1 == null)
		{
			this._p1 = (x, y);
			this._step = "W";
			this.Prompt = "반대 구석 지정:";
			return;
		}
		this.V?.ZoomWindow(this._p1.Value.X, this._p1.Value.Y, x, y);
		this.Finish();
	}

	public override void OnSelection(IEnumerable<Entity> entities) => this._objects.AddRange(entities);

	public override void OnEnter()
	{
		if (this._step == "O" && this._objects.Count > 0)
		{
			var segs = this._objects.SelectMany(Geom.SegmentsOf).SelectMany(s => new[] { s.At(0), s.At(0.5), s.At(1) }).ToList();
			var pts = segs.Count > 0 ? segs : this._objects.OfType<Insert>().Select(i => Geom.P(i.InsertPoint)).ToList();
			if (pts.Count > 0)
			{
				double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
				double pad = Math.Max(maxX - minX, maxY - minY) * 0.05 + 1e-6;
				this.V?.ZoomWindow(minX - pad, minY - pad, maxX + pad, maxY + pad);
			}
		}
		else if (this._step == "C2" && this._p1 is { } c)
		{
			this.V?.ZoomCenter(c.X, c.Y, null);
		}
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == "W" && this._p1 is { } a)
		{
			var pl = new LwPolyline { IsClosed = true };
			foreach (var p in new[] { new XY(a.X, a.Y), new XY(cursorX, a.Y), new XY(cursorX, cursorY), new XY(a.X, cursorY) }) pl.Vertices.Add(new LwPolyline.Vertex(p));
			yield return pl;
		}
	}
}

/// <summary>PAN (-PAN): 기준점 → 두 번째 점 만큼 화면 이동.</summary>
public sealed class PanCommand : EditorCommand
{
	private (double X, double Y)? _base;

	public override string Name => "PAN";
	public override string[] Aliases => new[] { "P", "-PAN" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._base = null;
		this.Prompt = "기준점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this._base == null)
		{
			this._base = (x, y);
			this.Prompt = "두 번째 점 지정:";
			return;
		}
		this.Ctx?.View?.PanBy(x - this._base.Value.X, y - this._base.Value.Y);
		this.Finish();
	}

	public override void OnEnter() => this.Finish();

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._base is { } b) yield return new Line(Num.Z(b.X, b.Y), Num.Z(cursorX, cursorY));
	}
}

/// <summary>SELECTSIMILAR: 고른 객체와 종류·도면층이 같은 객체를 현재 공간에서 모두 선택.</summary>
public sealed class SelectSimilarCommand : EntityModificationCommand, ISelectionResultCommand
{
	public override string Name => "SELECTSIMILAR";

	public IReadOnlyCollection<Entity>? SelectionResult { get; private set; }

	protected override void OnSelectionReady()
	{
		var keys = this.Targets.Select(e => (e.GetType(), e.Layer?.Name)).ToHashSet();
		this.SelectionResult = this.Ctx!.Doc.CurrentSpace.Entities.Where(e => keys.Contains((e.GetType(), e.Layer?.Name))).ToList();
		this.Ctx.Print($"객체 {this.SelectionResult.Count}개를 선택했습니다.");
		this.Finish();
	}
}

/// <summary>DIMEDIT: [홈(H)/새로운(N)/회전(R)/기울기(O)] 후 치수 선택.</summary>
public sealed class DimEditCommand : EditorCommand
{
	private string _mode = "";
	private string? _value;
	private readonly List<Dimension> _dims = new();

	public override string Name => "DIMEDIT";
	public override string[] Aliases => new[] { "DED" };

	public string? Preset { get; set; }

	public override bool WantsSelection => this._mode != "" && this._value != null;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._dims.Clear();
		this._value = null;
		this._mode = "";
		if (this.Preset != null)
		{
			this.OnText(this.Preset);
			return;
		}
		this.Prompt = "치수 편집 유형을 입력하세요:  [홈(H)/새로운(N)/회전(R)/기울기(O)] <홈>:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._mode == "")
		{
			this._mode = t.Length == 0 ? "H" : t.ToUpperInvariant()[..1];
			switch (this._mode)
			{
				case "N": this.Prompt = "새 치수 문자 <<>>:"; break;
				case "R": this.Prompt = "문자 각도:"; break;
				case "O": this.Prompt = "기울기 각도 입력:"; break;
				default:
					this._mode = "H";
					this._value = "";
					this.Prompt = "치수 선택:";
					break;
			}
			return;
		}
		if (this._value == null)
		{
			this._value = t;
			this.Prompt = "치수 선택:";
		}
	}

	public override void OnEnter()
	{
		if (this._mode == "")
		{
			this.OnText("");
			return;
		}
		if (this._value == null)
		{
			this._value = this._mode == "N" ? "<>" : "0";
			this.Prompt = "치수 선택:";
			return;
		}
		this.Apply();
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		this._dims.AddRange(entities.OfType<Dimension>().Where(d => !this._dims.Contains(d)));
		this.Prompt = $"치수 선택: {this._dims.Count}개 <완료는 Enter>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.WantsSelection && this.Ctx?.PickAt(x, y) is Dimension d) this.OnSelection(new[] { d });
	}

	private void Apply()
	{
		var actions = new List<IUndoableAction>();
		foreach (var d in this._dims)
		{
			var (text, rot, user, mid) = (d.Text, d.TextRotation, d.IsTextUserDefinedLocation, d.TextMiddlePoint);
			double? oldOblique = d is DimensionLinear lin0 ? lin0.ExtLineRotation : null;
			string mode = this._mode, value = this._value ?? "";
			actions.Add(new EntityEditAction("치수 편집", d, () =>
			{
				switch (mode)
				{
					case "H": d.IsTextUserDefinedLocation = false; d.TextRotation = 0; break;
					case "N": d.Text = value == "<>" ? string.Empty : value; break;
					case "R": d.TextRotation = Num.TryParse(value, out double r) ? Num.Rad(r) : 0; break;
					case "O" when d is DimensionLinear lin: lin.ExtLineRotation = Num.TryParse(value, out double o) ? Num.Rad(o) : 0; break;
				}
				TryUpdate(d);
			}, () =>
			{
				d.Text = text;
				d.TextRotation = rot;
				d.IsTextUserDefinedLocation = user;
				d.TextMiddlePoint = mid;
				if (d is DimensionLinear lin && oldOblique is { } ob) lin.ExtLineRotation = ob;
				TryUpdate(d);
			}));
		}
		if (actions.Count > 0) this.Ctx!.Doc.Execute(new CompositeAction("치수 편집", actions));
		this.Finish();
	}

	internal static void TryUpdate(Dimension d)
	{
		try
		{
			d.UpdateBlock();
		}
		catch (Exception)
		{
			// 블록을 다시 만들 수 없는 치수는 그대로
		}
	}
}

/// <summary>DIMTEDIT: 치수 문자 위치 [왼쪽정렬(L)/오른쪽정렬(R)/가운데정렬(C)/홈(H)/각도(A)] 또는 새 위치 클릭.</summary>
public sealed class DimTEditCommand : EditorCommand
{
	private Dimension? _dim;
	private bool _awaitAngle;

	public override string Name => "DIMTEDIT";
	public override IEnumerable<Entity> Highlighted => this._dim != null ? new Entity[] { this._dim } : Array.Empty<Entity>();

	public override string[] Aliases => new[] { "DIMTED" };

	public override bool WantsSelection => this._dim == null;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._dim = ctx.Selection.OfType<Dimension>().FirstOrDefault();
		this.Prompt = this._dim == null ? "치수 선택:" : MovePrompt;
	}

	private const string MovePrompt = "치수 텍스트의 새 위치를 지정하거나 [왼쪽정렬(L)/오른쪽정렬(R)/가운데정렬(C)/홈(H)/각도(A)]:";

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._dim != null) return;
		this._dim = entities.OfType<Dimension>().FirstOrDefault();
		if (this._dim != null) this.Prompt = MovePrompt;
	}

	public override void OnPoint(double x, double y)
	{
		if (this._dim == null)
		{
			if (this.Ctx?.PickAt(x, y) is Dimension d) this.OnSelection(new[] { d });
			return;
		}
		this.Edit(d => { if (d is DimensionAligned al) Editing.DimensionGeometry.MoveText(al, Num.Z(x, y)); else { d.TextMiddlePoint = Num.Z(x, y); d.IsTextUserDefinedLocation = true; } });
	}

	public override void OnText(string text)
	{
		if (this._dim == null) return;
		string t = text.Trim().ToUpperInvariant();
		if (this._awaitAngle)
		{
			if (Num.TryParse(t, out double a)) this.Edit(d => d.TextRotation = Num.Rad(a));
			return;
		}
		switch (t)
		{
			case "H":
			case "C":
				this.Edit(d => { d.IsTextUserDefinedLocation = false; });
				break;
			case "L":
			case "R":
				if (this._dim is DimensionAligned al)
				{
					// 치수선 위에서 첫 번째(L) / 두 번째(R) 보조선 쪽으로
					var a = al.FirstPoint;
					var b = al.SecondPoint;
					var mid = al.TextMiddlePoint;
					double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
					double shift = len / 4 * (t == "L" ? -1 : 1);
					if (al is DimensionLinear lin)
					{
						(dx, dy) = (Math.Cos(lin.Rotation), Math.Sin(lin.Rotation));
						len = 1;
						double span = Math.Abs((b.X - a.X) * dx + (b.Y - a.Y) * dy);
						shift = span / 4 * (t == "L" ? -1 : 1) * Math.Sign((b.X - a.X) * dx + (b.Y - a.Y) * dy + 1e-12);
					}
					var p = Num.Z(mid.X + dx / len * shift, mid.Y + dy / len * shift);
					this.Edit(d => { d.TextMiddlePoint = p; d.IsTextUserDefinedLocation = true; });
				}
				break;
			case "A":
				this._awaitAngle = true;
				this.Prompt = "문자 각도:";
				break;
		}
	}

	private void Edit(Action<Dimension> change)
	{
		var d = this._dim!;
		var (rot, user, mid) = (d.TextRotation, d.IsTextUserDefinedLocation, d.TextMiddlePoint);
		var snapshot = d.Clone() as Dimension;
		change(d);
		var (rot2, user2, mid2) = (d.TextRotation, d.IsTextUserDefinedLocation, d.TextMiddlePoint);
		d.TextRotation = rot;
		d.IsTextUserDefinedLocation = user;
		d.TextMiddlePoint = mid;
		this.Ctx!.Doc.Execute(new EntityEditAction("치수 문자 편집", d,
			() => { d.TextRotation = rot2; d.IsTextUserDefinedLocation = user2; d.TextMiddlePoint = mid2; DimEditCommand.TryUpdate(d); },
			() => { d.TextRotation = rot; d.IsTextUserDefinedLocation = user; d.TextMiddlePoint = mid; DimEditCommand.TryUpdate(d); }));
		this.Finish();
	}

	public override void OnEnter() => this.Finish();
}

/// <summary>SCALETEXT: 문자 높이를 새 높이로 또는 축척 비율(S)로.</summary>
public sealed class ScaleTextCommand : EntityModificationCommand
{
	private bool _byFactor;

	public override string Name => "SCALETEXT";

	protected override void OnSelectionReady()
	{
		var first = this.Targets.Select(H).FirstOrDefault(h => h > 0);
		this.Prompt = $"새 높이 <{first:0.####}> 또는 [축척 비율(S)]:";
	}

	private static double H(Entity e) => e switch { MText m => m.Height, TextEntity t => t.Height, _ => 0 };

	public override void OnText(string text)
	{
		if (this.WaitingForSelection) return;
		string t = text.Trim().ToUpperInvariant();
		if (t == "S")
		{
			this._byFactor = true;
			this.Prompt = "축척 비율 지정 <1.0> :";
			return;
		}
		if (!Num.TryParse(t, out double v) || v <= 0) return;
		var actions = new List<IUndoableAction>();
		foreach (var e in this.Targets.Where(e => H(e) > 0))
		{
			double old = H(e), nh = this._byFactor ? old * v : v;
			actions.Add(new EntityEditAction("문자 축척", e, () => Set(e, nh), () => Set(e, old)));
		}
		if (actions.Count > 0) this.Ctx!.Doc.Execute(new CompositeAction("문자 축척", actions));
		this.Ctx!.Print($"문자 {actions.Count}개의 높이를 바꿨습니다.");
		this.Finish();
	}

	private static void Set(Entity e, double h)
	{
		if (e is MText m) m.Height = h;
		else if (e is TextEntity t) t.Height = h;
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		this.Finish();
	}
}

/// <summary>TOTALLENGTH: 선택한 선·호·원·폴리선·스플라인·타원의 길이 합.</summary>
public sealed class TotalLengthCommand : EntityModificationCommand
{
	public override string Name => "TOTALLENGTH";

	protected override void OnSelectionReady()
	{
		var parts = this.Targets.Select(e => Geom.Length(Geom.SegmentsOf(e))).Where(l => l > 0).ToList();
		this.Ctx!.Print($"객체 {parts.Count}개, 전체 길이 = {parts.Sum():0.####}");
		this.Finish();
	}
}

/// <summary>STATUS: 도면 상태 (객체 수, 범위, 현재 도면층·스타일, 단위).</summary>
public sealed class StatusCommand : EditorCommand
{
	public override string Name => "STATUS";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var d = ctx.Doc.Document;
		var h = d.Header;
		ctx.Print($"도면: {ctx.Doc.FilePath ?? "새 도면"}  (버전 {h.Version})");
		ctx.Print($"모형 공간 객체 {d.ModelSpace.Entities.Count}개, 현재 공간 객체 {ctx.Doc.CurrentSpace.Entities.Count}개");
		ctx.Print($"도면층 {d.Layers.Count}개, 블록 {d.BlockRecords.Count(b => !b.Name.StartsWith('*'))}개, 문자 스타일 {d.TextStyles.Count}개, 치수 스타일 {d.DimensionStyles.Count}개");
		ctx.Print($"범위: ({h.ModelSpaceExtMin.X:0.####}, {h.ModelSpaceExtMin.Y:0.####}) - ({h.ModelSpaceExtMax.X:0.####}, {h.ModelSpaceExtMax.Y:0.####})");
		ctx.Print($"현재 도면층: {ctx.Doc.CurrentLayer.Name}, 현재 문자 스타일: {h.CurrentTextStyleName}, 현재 치수 스타일: {h.CurrentDimensionStyleName}");
		ctx.Print($"선종류 축척(LTSCALE): {h.LineTypeScale:0.####}, 점 스타일(PDMODE): {h.PointDisplayMode}, 단위 정밀도: {h.LinearUnitPrecision}");
		this.Finish();
	}
}

/// <summary>CENTERMARK: 원·호에 중심 표시(십자 + 연장선), CENTER 선종류가 있으면 그것으로.</summary>
public sealed class CenterMarkCommand : EditorCommand
{
	private int _count;

	public override string Name => "CENTERMARK";
	public override bool PickingObject => !this.IsFinished;

	public override string[] Aliases => new[] { "CM" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._count = 0;
		this.Prompt = "중심 표시를 추가할 원 또는 호 선택 <완료는 Enter>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx?.PickAt(x, y) is not Circle c) return;
		var lines = Make(this.Ctx.Doc.Document, c);
		this.Ctx.Doc.AddEntities(lines);
		this._count++;
	}

	public override void OnEnter()
	{
		this.Ctx?.Print($"중심 표시 {this._count}개를 만들었습니다.");
		this.Finish();
	}

	/// <summary>기존 CAD 기본값과 같은 비율: 십자 반지름의 0.1, 간격 0.05, 원 밖으로 연장 반지름의 0.12.</summary>
	public static List<Entity> Make(CadDocument doc, Circle c)
	{
		double r = c.Radius, cross = r * 0.1, gap = r * 0.05, ext = r * 0.12;
		double cx = c.Center.X, cy = c.Center.Y;
		var list = new List<Entity>
		{
			new Line(Num.Z(cx - cross, cy), Num.Z(cx + cross, cy)),
			new Line(Num.Z(cx, cy - cross), Num.Z(cx, cy + cross)),
			new Line(Num.Z(cx + cross + gap, cy), Num.Z(cx + r + ext, cy)),
			new Line(Num.Z(cx - cross - gap, cy), Num.Z(cx - r - ext, cy)),
			new Line(Num.Z(cx, cy + cross + gap), Num.Z(cx, cy + r + ext)),
			new Line(Num.Z(cx, cy - cross - gap), Num.Z(cx, cy - r - ext)),
		};
		ApplyCenterLinetype(doc, list);
		return list;
	}

	internal static void ApplyCenterLinetype(CadDocument doc, IEnumerable<Entity> list)
	{
		var lt = doc.LineTypes.FirstOrDefault(l => l.Name.Equals("CENTER", StringComparison.OrdinalIgnoreCase))
			?? doc.LineTypes.FirstOrDefault(l => l.Name.StartsWith("CENTER", StringComparison.OrdinalIgnoreCase));
		if (lt == null) return;
		foreach (var e in list) e.LineType = lt;
	}
}

/// <summary>CENTERLINE: 두 선의 가운데에 중심선 (두 선의 겹치는 범위 + 양끝 연장).</summary>
public sealed class CenterLineCommand : EditorCommand
{
	private Line? _first;

	public override string Name => "CENTERLINE";
	public override IEnumerable<Entity> Highlighted => this._first != null ? new Entity[] { this._first } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished;

	public override string[] Aliases => new[] { "CL" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._first = null;
		this.Prompt = "첫번째 선 선택:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this.Ctx.PickAt(x, y) is not Line l)
		{
			this.Ctx.Print("선택된 객체는 선이 아닙니다.");
			return;
		}
		if (this._first == null)
		{
			this._first = l;
			this.Prompt = "두번째 선 선택:";
			return;
		}
		if (Make(this._first, l) is { } center)
		{
			CenterMarkCommand.ApplyCenterLinetype(this.Ctx.Doc.Document, new[] { center });
			this.Ctx.Doc.AddEntities(center);
			this.Ctx.Print("중심선이 성공적으로 생성되었습니다.");
		}
		this.Finish();
	}

	/// <summary>두 선의 끝점끼리 짝지은 가운데 점을 잇고 양쪽으로 길이의 12%만큼 연장.</summary>
	public static Line? Make(Line a, Line b)
	{
		var a1 = Geom.P(a.StartPoint);
		var a2 = Geom.P(a.EndPoint);
		var b1 = Geom.P(b.StartPoint);
		var b2 = Geom.P(b.EndPoint);
		// 방향이 반대면 짝을 바꾼다
		if ((a2.X - a1.X) * (b2.X - b1.X) + (a2.Y - a1.Y) * (b2.Y - b1.Y) < 0) (b1, b2) = (b2, b1);
		var s = new XY((a1.X + b1.X) / 2, (a1.Y + b1.Y) / 2);
		var e = new XY((a2.X + b2.X) / 2, (a2.Y + b2.Y) / 2);
		double len = Geom.Dist(s, e);
		if (len < 1e-12) return null;
		double ux = (e.X - s.X) / len, uy = (e.Y - s.Y) / len, ext = len * 0.12;
		return new Line(Num.Z(s.X - ux * ext, s.Y - uy * ext), Num.Z(e.X + ux * ext, e.Y + uy * ext));
	}
}

/// <summary>DIMSPACE: 기준 치수와 평행한 선형·정렬 치수의 간격을 같게 (Auto = 문자 높이의 2배).</summary>
public sealed class DimSpaceCommand : EditorCommand
{
	private DimensionAligned? _base;
	private readonly List<DimensionAligned> _dims = new();
	private bool _awaitValue;

	public override string Name => "DIMSPACE";
	public override IEnumerable<Entity> Highlighted => this._base != null ? this._dims.Cast<Entity>().Prepend(this._base).ToArray() : Array.Empty<Entity>();


	public override bool WantsSelection => !this._awaitValue;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._base = null;
		this._dims.Clear();
		this._awaitValue = false;
		this.Prompt = "기준 치수 선택:";
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		foreach (var e in entities)
		{
			if (e is not DimensionAligned d)
			{
				continue;
			}
			if (this._base == null)
			{
				this._base = d;
				this.Prompt = "공간을 두기 위한 치수 선택:";
			}
			else if (d != this._base && !this._dims.Contains(d))
			{
				this._dims.Add(d);
			}
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (!this._awaitValue && this.Ctx?.PickAt(x, y) is { } e) this.OnSelection(new[] { e });
	}

	public override void OnEnter()
	{
		if (this._base == null)
		{
			this.Finish();
			return;
		}
		if (!this._awaitValue)
		{
			this._awaitValue = true;
			this.Prompt = "공간 값 지정 또는 [Auto] <Auto>:";
			return;
		}
		this.Apply(null);
	}

	public override void OnText(string text)
	{
		if (!this._awaitValue) return;
		string t = text.Trim().ToUpperInvariant();
		this.Apply(t.StartsWith("A") || t.Length == 0 ? null : (Num.TryParse(t, out double v) ? v : null));
	}

	private static (double Ux, double Uy) Normal(DimensionAligned d)
	{
		double ang = d is DimensionLinear l ? l.Rotation : Math.Atan2(d.SecondPoint.Y - d.FirstPoint.Y, d.SecondPoint.X - d.FirstPoint.X);
		return (-Math.Sin(ang), Math.Cos(ang));
	}

	private void Apply(double? spacing)
	{
		var b = this._base!;
		var (ux, uy) = Normal(b);
		double baseAng = Math.Atan2(uy, ux);
		var parallel = this._dims.Where(d =>
		{
			var (vx, vy) = Normal(d);
			return Math.Abs(Math.Sin(Math.Atan2(vy, vx) - baseAng)) < 1e-6;
		}).ToList();
		if (parallel.Count == 0)
		{
			this.Ctx!.Print("기준 치수에 대해 공간을 둘 수 있는 선택된 치수가 없습니다.");
			this.Finish();
			return;
		}
		var style = b.Style ?? DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		double step = spacing ?? 2 * style.TextHeight * (style.ScaleFactor > 0 ? style.ScaleFactor : 1);
		double Off(DimensionAligned d) => d.DefinitionPoint.X * ux + d.DefinitionPoint.Y * uy;
		double baseOff = Off(b);
		// 기준 치수에서 멀어지는 방향(처음 치수가 있는 쪽)으로 차례로
		double side = parallel.Average(Off) >= baseOff ? 1 : -1;
		var ordered = parallel.OrderBy(d => Math.Abs(Off(d) - baseOff)).ToList();
		var actions = new List<IUndoableAction>();
		for (int i = 0; i < ordered.Count; i++)
		{
			var d = ordered[i];
			double target = baseOff + side * step * (i + 1);
			double delta = target - Off(d);
			var old = d.DefinitionPoint;
			var oldText = d.TextMiddlePoint;
			var nw = new XYZ(old.X + ux * delta, old.Y + uy * delta, 0);
			var nt = new XYZ(oldText.X + ux * delta, oldText.Y + uy * delta, 0);
			actions.Add(new EntityEditAction("치수 간격", d,
				() => { d.DefinitionPoint = nw; d.TextMiddlePoint = nt; DimEditCommand.TryUpdate(d); },
				() => { d.DefinitionPoint = old; d.TextMiddlePoint = oldText; DimEditCommand.TryUpdate(d); }));
		}
		this.Ctx!.Doc.Execute(new CompositeAction("치수 간격", actions));
		if (parallel.Count < this._dims.Count) this.Ctx.Print("기준 치수에 평행한 치수만 이동되었습니다.");
		this.Finish();
	}
}

/// <summary>WBLOCK: 선택 객체를 기준점과 함께 새 도면으로 (앱이 Result로 저장 창을 띄움).</summary>
public sealed class WblockCommand : EntityModificationCommand
{
	public override string Name => "WBLOCK";
	public override string[] Aliases => new[] { "W" };

	public EditorDocument? Result { get; private set; }

	protected override void OnSelectionReady()
	{
		this.Prompt = "기준점 지정 <0,0>:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.WaitingForSelection) return;
		this.Make(Num.Z(x, y));
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		this.Make(XYZ.Zero);
	}

	private void Make(XYZ basePoint)
	{
		this.Result = Export(this.Targets, basePoint);
		this.Ctx!.Print($"객체 {this.Targets.Count}개를 새 도면으로 내보냅니다.");
		this.Finish();
	}

	/// <summary>객체 복사본을 기준점이 원점이 되도록 옮겨 새 도면에 넣는다 (도면층·스타일은 이름으로 따라감).</summary>
	public static EditorDocument Export(IEnumerable<Entity> entities, XYZ basePoint)
	{
		var target = EditorDocument.New();
		foreach (var e in target.Document.ModelSpace.Entities.ToList()) target.Document.ModelSpace.Entities.Remove(e);
		var move = Transform.CreateTranslation(new XYZ(-basePoint.X, -basePoint.Y, 0));
		var clones = new List<Entity>();
		foreach (var e in entities)
		{
			var c = (Entity)e.Clone();
			c.ApplyTransform(move);
			clones.Add(c);
		}
		target.Document.ModelSpace.Entities.AddRange(clones);
		target.Document.Header.ModelSpaceInsertionBase = XYZ.Zero;
		return target;
	}
}
