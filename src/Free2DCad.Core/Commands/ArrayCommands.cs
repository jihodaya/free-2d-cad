using System.Globalization;
using ACadSharp.Entities;
using ACadSharp.Objects;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 배열 설정 (직사각형·원형·경로). 연관 배열은 무명 그룹의 설명에 이 값을 적어 두고 ARRAYEDIT로 고친다.
/// </summary>
public sealed class ArraySpec
{
	public const string Tag = "F2DARRAY";

	public ArrayCommand.ArrayKind Kind = ArrayCommand.ArrayKind.Rect;
	public int Cols = 4, Rows = 3, Items = 6, PathItems = 6;
	public double ColDist = double.NaN, RowDist = double.NaN;
	public double CenterX, CenterY, Fill = 360;
	public bool Rotate = true, Align = true, Divide = true;
	public double PathSpacing = double.NaN;
	public ulong PathHandle;
	public int SourceCount;

	public string Encode() => string.Join(';', new[]
	{
		Tag, ((int)this.Kind).ToString(CultureInfo.InvariantCulture), this.Cols.ToString(CultureInfo.InvariantCulture), this.Rows.ToString(CultureInfo.InvariantCulture),
		this.Items.ToString(CultureInfo.InvariantCulture), this.PathItems.ToString(CultureInfo.InvariantCulture),
		D(this.ColDist), D(this.RowDist), D(this.CenterX), D(this.CenterY), D(this.Fill),
		this.Rotate ? "1" : "0", this.Align ? "1" : "0", this.Divide ? "1" : "0", D(this.PathSpacing),
		this.PathHandle.ToString(CultureInfo.InvariantCulture), this.SourceCount.ToString(CultureInfo.InvariantCulture),
	});

	private static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);

	public static ArraySpec? Decode(string? s)
	{
		if (string.IsNullOrEmpty(s) || !s.StartsWith(Tag + ";", StringComparison.Ordinal)) return null;
		var p = s.Split(';');
		if (p.Length < 17) return null;
		double Dd(string x) => double.Parse(x, CultureInfo.InvariantCulture);
		int Ii(string x) => int.Parse(x, CultureInfo.InvariantCulture);
		return new ArraySpec
		{
			Kind = (ArrayCommand.ArrayKind)Ii(p[1]),
			Cols = Ii(p[2]),
			Rows = Ii(p[3]),
			Items = Ii(p[4]),
			PathItems = Ii(p[5]),
			ColDist = Dd(p[6]),
			RowDist = Dd(p[7]),
			CenterX = Dd(p[8]),
			CenterY = Dd(p[9]),
			Fill = Dd(p[10]),
			Rotate = p[11] == "1",
			Align = p[12] == "1",
			Divide = p[13] == "1",
			PathSpacing = Dd(p[14]),
			PathHandle = ulong.Parse(p[15], CultureInfo.InvariantCulture),
			SourceCount = Ii(p[16]),
		};
	}

	public ArraySpec Clone() => (ArraySpec)this.MemberwiseClone();
}

/// <summary>배열 계산 (원본 객체들의 복사본 위치 행렬).</summary>
public static class ArrayMath
{
	public static (XY Min, XY Max) Bounds(IEnumerable<Entity> entities)
	{
		var pts = entities.SelectMany(Geom.SegmentsOf).SelectMany(s => new[] { s.At(0), s.At(0.5), s.At(1) }).ToList();
		pts.AddRange(entities.OfType<Insert>().Select(i => Geom.P(i.InsertPoint)));
		pts.AddRange(entities.OfType<TextEntity>().Select(t => Geom.P(t.InsertPoint)));
		pts.AddRange(entities.OfType<MText>().Select(t => Geom.P(t.InsertPoint)));
		if (pts.Count == 0) return (new XY(0, 0), new XY(10, 10));
		return (new XY(pts.Min(p => p.X), pts.Min(p => p.Y)), new XY(pts.Max(p => p.X), pts.Max(p => p.Y)));
	}

	public static XY BasePoint(IEnumerable<Entity> entities)
	{
		var (mn, mx) = Bounds(entities);
		return new XY((mn.X + mx.X) / 2, (mn.Y + mx.Y) / 2);
	}

	/// <summary>원본 자리(항등)를 뺀 복사본 행렬들.</summary>
	public static List<Matrix4> Placements(ArraySpec s, IReadOnlyList<Entity> sources, Entity? path)
	{
		var list = new List<Matrix4>();
		var bp = BasePoint(sources);
		switch (s.Kind)
		{
			case ArrayCommand.ArrayKind.Rect:
				for (int r = 0; r < Math.Max(1, s.Rows); r++)
					for (int c = 0; c < Math.Max(1, s.Cols); c++)
						if (r != 0 || c != 0) list.Add(Matrix4.CreateTranslation(c * s.ColDist, r * s.RowDist, 0));
				break;
			case ArrayCommand.ArrayKind.Polar:
				{
					int n = Math.Max(1, s.Items);
					bool full = Math.Abs(Math.Abs(s.Fill) - 360) < 1e-9;
					double step = Num.Rad(s.Fill) / (full ? n : Math.Max(1, n - 1));
					int polarRows = double.IsNaN(s.RowDist) ? 1 : Math.Max(1, s.Rows);
					for (int r = 0; r < polarRows; r++)
					{
						// 행: 중심에서 바깥으로 행 간격만큼
						double dxr = bp.X - s.CenterX, dyr = bp.Y - s.CenterY, lr = Math.Sqrt(dxr * dxr + dyr * dyr);
						var rowMove = lr < 1e-12 || double.IsNaN(s.RowDist) ? Matrix4.Identity : Matrix4.CreateTranslation(dxr / lr * s.RowDist * r, dyr / lr * s.RowDist * r, 0);
						for (int i = 0; i < n; i++)
						{
							if (i == 0 && r == 0) continue;
							double a = step * i;
							Matrix4 m;
							if (s.Rotate)
							{
								m = Matrix4.CreateTranslation(s.CenterX, s.CenterY, 0) * Matrix4.CreateFromAxisAngle(XYZ.AxisZ, a) * Matrix4.CreateTranslation(-s.CenterX, -s.CenterY, 0);
							}
							else
							{
								double dx = bp.X - s.CenterX, dy = bp.Y - s.CenterY;
								double nx = s.CenterX + dx * Math.Cos(a) - dy * Math.Sin(a), ny = s.CenterY + dx * Math.Sin(a) + dy * Math.Cos(a);
								m = Matrix4.CreateTranslation(nx - bp.X, ny - bp.Y, 0);
							}
							list.Add(m * rowMove);
						}
					}
					break;
				}
			case ArrayCommand.ArrayKind.Path when path != null:
				{
					var segs = Geom.SegmentsOf(path);
					double total = Geom.Length(segs);
					if (total <= 0) break;
					bool closed = Geom.IsClosed(path);
					int count;
					double spacing;
					if (s.Divide)
					{
						count = Math.Max(1, s.PathItems);
						spacing = closed ? total / count : total / Math.Max(1, count - 1);
					}
					else
					{
						spacing = double.IsNaN(s.PathSpacing) || s.PathSpacing <= 0 ? total / Math.Max(1, s.PathItems - 1) : s.PathSpacing;
						count = Math.Min(Math.Max(1, s.PathItems), (int)Math.Floor(total / spacing + 1e-9) + 1);
					}
					var p0 = DivideMeasureCommand.PointAtLength(segs, 0);
					double a0 = Tangent(segs, 0);
					int pathRows = double.IsNaN(s.RowDist) ? 1 : Math.Max(1, s.Rows);
					for (int r = 0; r < pathRows; r++)
					{
						for (int i = 0; i < count; i++)
						{
							double d = i * spacing;
							if (d > total + 1e-9) break;
							var p = DivideMeasureCommand.PointAtLength(segs, d);
							double ta = Tangent(segs, d);
							double off = double.IsNaN(s.RowDist) ? 0 : s.RowDist * r;
							var m = Matrix4.CreateTranslation(p.X - Math.Sin(ta) * off, p.Y + Math.Cos(ta) * off, 0);
							if (s.Align) m *= Matrix4.CreateFromAxisAngle(XYZ.AxisZ, ta - a0);
							m *= Matrix4.CreateTranslation(-p0.X, -p0.Y, 0);
							// 원본을 경로 시작으로 옮긴 위치 기준: 원본 기준점 → 경로 시작
							m *= Matrix4.CreateTranslation(p0.X - bp.X, p0.Y - bp.Y, 0);
							list.Add(m);
						}
					}
					break;
				}
		}
		return list;
	}

	private static double Tangent(List<Geom.Seg> segs, double d)
	{
		double total = Geom.Length(segs);
		double e = Math.Max(total * 1e-4, 1e-9);
		var a = DivideMeasureCommand.PointAtLength(segs, Math.Max(0, d - e));
		var b = DivideMeasureCommand.PointAtLength(segs, Math.Min(total, d + e));
		return Math.Atan2(b.Y - a.Y, b.X - a.X);
	}

	public static List<Entity> Copies(ArraySpec s, IReadOnlyList<Entity> sources, Entity? path)
	{
		var clones = new List<Entity>();
		foreach (var m in Placements(s, sources, path))
		{
			var tr = new Transform(m);
			foreach (var e in sources)
			{
				var c = (Entity)e.Clone();
				c.ApplyTransform(tr);
				clones.Add(c);
			}
		}
		return clones;
	}
}

/// <summary>
/// ARRAY / ARRAYRECT / ARRAYPOLAR / ARRAYPATH (기존 CAD와 같은 흐름): 객체 선택 → (원형: 중심 / 경로: 경로 곡선) →
/// 기본값으로 미리보기 → 옵션 반복 → 종료(X)/Enter로 작성. 연관(AS)이면 ARRAYEDIT로 다시 고칠 수 있다.
/// </summary>
public sealed class ArrayCommand : EntityModificationCommand
{
	public enum ArrayKind { Ask, Rect, Polar, Path }

	private static bool Associative = true;
	private ArraySpec _s = new();
	private string _step = "";
	private Entity? _path;

	public ArrayCommand() : this(ArrayKind.Ask)
	{
	}

	public ArrayCommand(ArrayKind kind) => this.Preset = kind;

	public ArrayKind Preset { get; }

	public override string Name => this.Preset switch
	{
		ArrayKind.Rect => "ARRAYRECT",
		ArrayKind.Polar => "ARRAYPOLAR",
		ArrayKind.Path => "ARRAYPATH",
		_ => "ARRAY",
	};

	public override string[] Aliases => this.Preset == ArrayKind.Ask ? new[] { "AR" } : Array.Empty<string>();

	public override bool WantsSelection => this.WaitingForSelection || this._step == "path";

	protected override string SelectPrompt => this.Preset == ArrayKind.Ask ? "ARRAY 객체 선택:" : "객체 선택:";
	public override IEnumerable<Entity> Highlighted => this._path != null ? new[] { this._path } : Array.Empty<Entity>();


	protected override void OnSelectionReady()
	{
		this._s = new ArraySpec { Kind = this.Preset == ArrayKind.Ask ? ArrayKind.Rect : this.Preset };
		var (mn, mx) = ArrayMath.Bounds(this.Targets);
		this._s.ColDist = Math.Max(mx.X - mn.X, 1e-6) * 1.5;
		this._s.RowDist = Math.Max(mx.Y - mn.Y, 1e-6) * 1.5;
		if (this.Preset == ArrayKind.Ask)
		{
			this._step = "kind";
			this.Prompt = "배열 유형을 입력하세요 [직사각형(R)/경로(PA)/극좌표(PO)] <직사각형(R)>:";
			return;
		}
		this.Begin();
	}

	private void Begin()
	{
		string kindText = this._s.Kind switch { ArrayKind.Polar => "극좌표(PO)", ArrayKind.Path => "경로(PA)", _ => "직사각형(R)" };
		this.Ctx?.Print($"Type = {kindText}  Associative = {(Associative ? "예(Y)" : "아니오(N)")}");
		switch (this._s.Kind)
		{
			case ArrayKind.Rect:
				this.Options();
				break;
			case ArrayKind.Polar:
				this._s.RowDist = double.NaN;
				this._s.Rows = 1;
				this._step = "center";
				this.Prompt = "배열의 중심점을 지정하거나 [기준점(B)/회전축(A)]:";
				break;
			case ArrayKind.Path:
				this._s.RowDist = double.NaN;
				this._step = "path";
				this.Prompt = "경로 곡선을 선택하세요:";
				break;
		}
	}

	private void Options()
	{
		this._step = "opt";
		this.Prompt = this._s.Kind switch
		{
			ArrayKind.Rect => "옵션을 입력하세요 [연관(AS)/기준점(B)/수량(COU)/간격(S)/열(C)/행(R)/레벨(L)/종료(X)]<종료>:",
			ArrayKind.Polar => "옵션을 입력하세요 [연관(AS)/기준점(B)/아이템(I)/각도사이(A)/채우기각도(F)/행(ROW)/레벨(L)/항목회전(ROT)/종료(X)]<종료>:",
			_ => "옵션을 입력하세요 [연관(AS)/방법(M)/기준점(B)/접선방향(T)/아이템(I)/행(R)/레벨(L)/아이템정렬(A)/Z방향(Z)/종료(X)]<종료>:",
		};
	}

	private void Ask(string step, string prompt)
	{
		this._step = step;
		this.Prompt = prompt;
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			base.OnEnter();
			return;
		}
		switch (this._step)
		{
			case "kind":
				this._s.Kind = ArrayKind.Rect;
				this.Begin();
				return;
			case "opt":
				this.Create();
				return;
			case "path":
			case "center":
				this.Finish();
				return;
		}
		// 하위 질문에서 Enter = 기본값 유지
		this.OnText("");
	}

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		double v = double.NaN;
		bool hasNum = t.Length > 0 && Num.TryParse(t, out v);
		var s = this._s;
		switch (this._step)
		{
			case "kind":
				s.Kind = t.StartsWith("PO") ? ArrayKind.Polar : t.StartsWith("PA") ? ArrayKind.Path : ArrayKind.Rect;
				this.Begin();
				return;
			case "opt":
				this.Option(t);
				return;
			case "as":
				if (t.Length > 0) Associative = !t.StartsWith("N");
				this.Options();
				return;
			case "cou-c":
				if (hasNum && v >= 1) s.Cols = (int)v;
				this.Ask("cou-r", $"행 수를 입력하세요 <{s.Rows}>:");
				return;
			case "cou-r":
				if (hasNum && v >= 1) s.Rows = (int)v;
				this.Options();
				return;
			case "s-c":
				if (hasNum && v != 0) s.ColDist = v;
				this.Ask("s-r", $"행 간 거리를 지정하세요 <{s.RowDist:0.####}>:");
				return;
			case "s-r":
				if (hasNum && v != 0) s.RowDist = v;
				this.Options();
				return;
			case "c-n":
				if (hasNum && v >= 1) s.Cols = (int)v;
				this.Ask("c-d", $"열 간 거리를 지정하거나 [합계] <{s.ColDist:0.####}>:");
				return;
			case "c-d":
				if (hasNum && v != 0) s.ColDist = v;
				this.Options();
				return;
			case "r-n":
				if (hasNum && v >= 1) s.Rows = (int)v;
				this.Ask("r-d", $"행 간 거리를 지정하거나 [Total] <{(double.IsNaN(s.RowDist) ? 0 : s.RowDist):0.####}>:");
				return;
			case "r-d":
				if (hasNum && v != 0) s.RowDist = v;
				this.Options();
				return;
			case "lvl":
				this.Options();
				return;
			case "i":
				if (hasNum && v >= 1)
				{
					if (s.Kind == ArrayKind.Path) s.PathItems = (int)v;
					else s.Items = (int)v;
				}
				this.Options();
				return;
			case "pdist":
				if (hasNum && v > 0) s.PathSpacing = v;
				this.Ask("i", $"아이템 수를 입력하세요 <{s.PathItems}>:");
				return;
			case "a":
				if (hasNum && v != 0)
				{
					bool full = Math.Abs(Math.Abs(s.Fill) - 360) < 1e-9;
					s.Fill = Math.Clamp(v * (full ? s.Items : s.Items - 1), -360, 360);
				}
				this.Options();
				return;
			case "f":
				if (hasNum && v != 0) s.Fill = Math.Clamp(v, -360, 360);
				this.Options();
				return;
			case "rot":
				if (t.Length > 0) s.Rotate = !t.StartsWith("N");
				this.Options();
				return;
			case "align":
				if (t.Length > 0) s.Align = !t.StartsWith("N");
				this.Options();
				return;
			case "m":
				if (t.StartsWith("D")) s.Divide = true;
				else if (t.StartsWith("M")) s.Divide = false;
				if (!s.Divide && double.IsNaN(s.PathSpacing) && this._path != null)
				{
					s.PathSpacing = Geom.Length(Geom.SegmentsOf(this._path)) / Math.Max(1, s.PathItems - 1);
				}
				this.Options();
				return;
			case "b":
			case "z":
			case "t":
				this.Options();
				return;
		}
	}

	private void Option(string t)
	{
		var s = this._s;
		switch (t)
		{
			case "":
			case "X":
				this.Create();
				return;
			case "AS":
				this.Ask("as", $"연관 배열을 생성할지 [예(Y)/아니오(N)] <{(Associative ? "예" : "아니오")}>:");
				return;
			case "B":
				this.Ask("b", "기준점을 지정하세요:");
				return;
			case "L":
				this.Ask("lvl", "레벨 수를 입력하세요 <1>:");
				return;
		}
		switch (s.Kind)
		{
			case ArrayKind.Rect:
				switch (t)
				{
					case "COU": this.Ask("cou-c", $"열 수를 입력하세요 <{s.Cols}>:"); break;
					case "S": this.Ask("s-c", $"열 간 거리를 지정하거나 [합계] <{s.ColDist:0.####}>:"); break;
					case "C": this.Ask("c-n", $"열 수를 입력하세요 <{s.Cols}>:"); break;
					case "R": this.Ask("r-n", $"행 수를 입력하세요 <{s.Rows}>:"); break;
				}
				break;
			case ArrayKind.Polar:
				switch (t)
				{
					case "I": this.Ask("i", $"항목 수를 입력하세요 <{s.Items}>:"); break;
					case "A": this.Ask("a", $"항목 사이 각도를 지정하세요 <{s.Fill / (Math.Abs(Math.Abs(s.Fill) - 360) < 1e-9 ? s.Items : Math.Max(1, s.Items - 1)):0.####}>:"); break;
					case "F": this.Ask("f", $"채우기 각도를 지정하세요 (+=반시계, -=시계방향) <{s.Fill:0.####}>:"); break;
					case "ROW": this.Ask("r-n", $"행 수를 입력하세요 <{s.Rows}>:"); break;
					case "ROT": this.Ask("rot", "배열된 항목을 회전시킬지 [예(Y)/아니오(N)]:"); break;
				}
				break;
			case ArrayKind.Path:
				switch (t)
				{
					case "M": this.Ask("m", $"경로 방법을 선택하세요 [분할(D)/측정(M)] <{(s.Divide ? "분할" : "측정")}>:"); break;
					case "I":
						if (s.Divide) this.Ask("i", $"아이템 수를 입력하세요 <{s.PathItems}>:");
						else this.Ask("pdist", $"아이템 간 거리를 지정하세요 <{s.PathSpacing:0.####}>:");
						break;
					case "R": this.Ask("r-n", $"행 수를 입력하세요 <{s.Rows}>:"); break;
					case "A": this.Ask("align", $"배열된 항목을 정렬하시겠습니까 [예(Y)/아니오(N)] <{(s.Align ? "예" : "아니오")}>:"); break;
					case "T": this.Ask("t", "첫번째 점지정 of tangent direction vector or [Normal]:"); break;
					case "Z": this.Ask("z", "배열의 모든 항목에서 Z 방향을 유지하시겠습니까? [예(Y)/아니오(N)] <예>:"); break;
				}
				break;
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._step == "path")
		{
			this.SetPath(entities.FirstOrDefault());
			return;
		}
		base.OnSelection(entities);
	}

	public override void OnPoint(double x, double y)
	{
		if (this.WaitingForSelection || this.Ctx == null) return;
		switch (this._step)
		{
			case "center":
				this._s.CenterX = x;
				this._s.CenterY = y;
				this.Options();
				break;
			case "path":
				this.SetPath(this.Ctx.PickAt(x, y));
				break;
			case "b":
			case "t":
				this.Options();
				break;
		}
	}

	private void SetPath(Entity? path)
	{
		if (path == null || this.Targets.Contains(path) || Geom.SegmentsOf(path).Count == 0) return;
		this._path = path;
		this._s.PathHandle = path.Handle;
		this._s.Rows = 1;
		this.Options();
	}

	private void Create()
	{
		var doc = this.Ctx!.Doc;
		var clones = ArrayMath.Copies(this._s, this.Targets, this._path);
		if (clones.Count == 0)
		{
			this.Ctx.Print("한 요소의 배열, 할 일이 없습니다.");
			this.Finish();
			return;
		}
		doc.AddEntities(clones);
		if (Associative)
		{
			var spec = this._s.Clone();
			spec.SourceCount = this.Targets.Count;
			ArrayEditCommand.MakeGroup(doc, this.Targets.Concat(clones).ToList(), spec);
		}
		int items = clones.Count / Math.Max(1, this.Targets.Count) + 1;
		this.Ctx.Print($"항목 {items}개로 배열했습니다{(Associative ? " (연관 배열: ARRAYEDIT로 편집)" : "")}.");
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this.WaitingForSelection || this._step is "kind" or "path" or "" || (this._step == "center"))
		{
			if (this._step == "center" && this.Targets.Count > 0)
			{
				var s = this._s.Clone();
				s.CenterX = cursorX;
				s.CenterY = cursorY;
				if (this.Targets.Count * s.Items <= 2000) return ArrayMath.Copies(s, this.Targets, null);
			}
			return Enumerable.Empty<Entity>();
		}
		var placements = ArrayMath.Placements(this._s, this.Targets, this._path).Count;
		if (this.Targets.Count * placements > 2000) return Enumerable.Empty<Entity>();
		return ArrayMath.Copies(this._s, this.Targets, this._path);
	}
}

/// <summary>ARRAYEDIT: 연관 배열(F2D 배열 그룹)의 개수·간격·각도 등을 고쳐 다시 만든다.</summary>
public sealed class ArrayEditCommand : EditorCommand
{
	private Group? _group;
	private ArraySpec? _s;
	private string _step = "";

	public override string Name => "ARRAYEDIT";

	public override bool WantsSelection => this._group == null;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._group = null;
		if (ctx.Selection.Count > 0 && Find(ctx.Doc, ctx.Selection.First()) is { } g)
		{
			this.Begin(g);
			return;
		}
		this.Prompt = "배열 객체를 선택하세요:";
	}

	public static Group? Find(EditorDocument doc, Entity e) =>
		doc.Document.Groups.FirstOrDefault(g => ArraySpec.Decode(g.Description) != null && g.Entities.Contains(e));

	public static void MakeGroup(EditorDocument doc, List<Entity> members, ArraySpec spec)
	{
		var groups = doc.Document.Groups;
		var group = new Group(string.Empty) { Description = spec.Encode() };
		doc.Execute(new DelegateAction("연관 배열",
			() => { groups.Add(group); group.AddRange(members.Where(m => m.Document == doc.Document)); },
			() => { group.Clear(); groups.Remove(group.Name); }));
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._group != null) return;
		foreach (var e in entities)
		{
			if (Find(this.Ctx!.Doc, e) is { } g)
			{
				this.Begin(g);
				return;
			}
		}
		this.Ctx!.Print("선택된 객체는 연관 배열이 아닙니다.");
	}

	public override void OnPoint(double x, double y)
	{
		if (this._group == null && this.Ctx?.PickAt(x, y) is { } e) this.OnSelection(new[] { e });
	}

	private void Begin(Group g)
	{
		this._group = g;
		this._s = ArraySpec.Decode(g.Description)!;
		this.Options();
	}

	private void Options()
	{
		this._step = "opt";
		this.Prompt = this._s!.Kind switch
		{
			ArrayCommand.ArrayKind.Rect => "옵션을 입력하세요 [기준점(B)/행(R)/열(C)/레벨(L)/종료(X)]<종료>:",
			ArrayCommand.ArrayKind.Polar => "옵션을 입력하세요 [기준점(B)/아이템(I)/각도사이(A)/채우기각도(F)/행(R)/레벨(L)/항목회전(ROT)/종료(X)] <종료>:",
			_ => "옵션을 입력하세요 [방법(M)/기준점(B)/아이템(I)/행(R)/레벨(L)/아이템정렬(A)/Z방향(Z)/종료(X)] <종료>:",
		};
	}

	public override void OnEnter()
	{
		if (this._step == "opt") this.Apply();
		else if (this._group == null) this.Finish();
		else this.OnText("");
	}

	public override void OnText(string text)
	{
		if (this._s == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = t.Length > 0 && Num.TryParse(t, out double v);
		_ = Num.TryParse(t, out v);
		var s = this._s;
		switch (this._step)
		{
			case "opt":
				switch (t)
				{
					case "":
					case "X": this.Apply(); return;
					case "C" when s.Kind == ArrayCommand.ArrayKind.Rect: this.Ask("c-n", $"열 수를 입력하세요 <{s.Cols}>:"); return;
					case "R": this.Ask("r-n", $"행 수를 입력하세요 <{s.Rows}>:"); return;
					case "I": this.Ask("i", s.Kind == ArrayCommand.ArrayKind.Path ? $"아이템 수를 입력하세요 <{s.PathItems}>:" : $"항목 수를 입력하세요 <{s.Items}>:"); return;
					case "A" when s.Kind == ArrayCommand.ArrayKind.Polar: this.Ask("a", "항목 사이 각도를 지정하세요:"); return;
					case "A" when s.Kind == ArrayCommand.ArrayKind.Path: this.Ask("align", "배열된 항목을 정렬하시겠습니까 [예(Y)/아니오(N)]:"); return;
					case "F": this.Ask("f", $"채우기 각도를 지정하세요 (+=반시계, -=시계방향) <{s.Fill:0.####}>:"); return;
					case "ROT": this.Ask("rot", "배열된 항목을 회전시킬지 [예(Y)/아니오(N)]:"); return;
					case "M": this.Ask("m", "경로 방법을 선택하세요 [분할(D)/측정(M)]:"); return;
					case "B":
					case "L":
					case "Z": this.Ask("skip", "Enter로 돌아갑니다:"); return;
				}
				return;
			case "c-n": if (num && v >= 1) s.Cols = (int)v; this.Ask("c-d", $"열 간 거리를 지정하거나 [합계] <{s.ColDist:0.####}>:"); return;
			case "c-d": if (num && v != 0) s.ColDist = v; this.Options(); return;
			case "r-n": if (num && v >= 1) s.Rows = (int)v; this.Ask("r-d", $"행 간 거리를 지정하세요 <{(double.IsNaN(s.RowDist) ? 0 : s.RowDist):0.####}>:"); return;
			case "r-d": if (num && v != 0) s.RowDist = v; this.Options(); return;
			case "i":
				if (num && v >= 1)
				{
					if (s.Kind == ArrayCommand.ArrayKind.Path) s.PathItems = (int)v;
					else s.Items = (int)v;
				}
				this.Options();
				return;
			case "a":
				if (num && v != 0)
				{
					bool full = Math.Abs(Math.Abs(s.Fill) - 360) < 1e-9;
					s.Fill = Math.Clamp(v * (full ? s.Items : s.Items - 1), -360, 360);
				}
				this.Options();
				return;
			case "f": if (num && v != 0) s.Fill = Math.Clamp(v, -360, 360); this.Options(); return;
			case "rot": if (t.Length > 0) s.Rotate = !t.StartsWith("N"); this.Options(); return;
			case "align": if (t.Length > 0) s.Align = !t.StartsWith("N"); this.Options(); return;
			case "m": if (t.StartsWith("D")) s.Divide = true; else if (t.StartsWith("M")) s.Divide = false; this.Options(); return;
			default: this.Options(); return;
		}
	}

	private void Ask(string step, string prompt)
	{
		this._step = step;
		this.Prompt = prompt;
	}

	private void Apply()
	{
		var doc = this.Ctx!.Doc;
		var g = this._group!;
		var s = this._s!;
		var members = g.Entities.ToList();
		var sources = members.Take(Math.Min(s.SourceCount, members.Count)).ToList();
		var oldCopies = members.Skip(sources.Count).Where(e => e.Document == doc.Document).ToList();
		Entity? path = s.Kind == ArrayCommand.ArrayKind.Path
			? doc.Document.ModelSpace.Entities.Concat(doc.CurrentSpace.Entities).FirstOrDefault(e => e.Handle == s.PathHandle)
			: null;
		var newCopies = ArrayMath.Copies(s, sources, path);
		string oldDesc = g.Description;
		string newDesc = s.Encode();
		foreach (var c in newCopies) Geom.CopyProps(sources[0], c);
		doc.Execute(new CompositeAction("배열 편집", new List<IUndoableAction>
		{
			new RemoveEntitiesAction(oldCopies, "배열 항목 지우기"),
			new AddEntitiesAction(doc.CurrentSpace, newCopies, "배열 항목 추가"),
			new DelegateAction("배열 그룹",
				() => { foreach (var c in oldCopies) g.Remove(c); g.AddRange(newCopies); g.Description = newDesc; },
				() => { foreach (var c in newCopies) g.Remove(c); g.AddRange(oldCopies.Where(c => c.Document == doc.Document)); g.Description = oldDesc; }),
		}));
		this.Ctx.Print($"배열을 다시 만들었습니다 (항목 {newCopies.Count / Math.Max(1, sources.Count) + 1}개).");
		this.Finish();
	}
}
