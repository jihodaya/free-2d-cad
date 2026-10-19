using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Commands;

/// <summary>ATTDEF: 속성 정의 (태그 → 프롬프트 → 기본값 → 높이 → 시작점). 모드 옵션: 숨김(I)/상수(C).</summary>
public sealed class AttDefCommand : EditorCommand
{
	private static double LastHeight = 2.5;
	private string _tag = "", _prompt = "", _value = "";
	private AttributeFlags _flags;
	private int _step;

	public override string Name => "ATTDEF";
	public override string[] Aliases => new[] { "ATT" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._flags = AttributeFlags.None;
		this.Prompt = "속성 태그 입력 또는 [숨김(I)/상수(C)]:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		switch (this._step)
		{
			case 0:
				if (t.Equals("I", StringComparison.OrdinalIgnoreCase)) { this._flags ^= AttributeFlags.Hidden; this.Prompt = $"속성 태그 입력 (숨김={(this._flags.HasFlag(AttributeFlags.Hidden) ? "예" : "아니오")}):"; return; }
				if (t.Equals("C", StringComparison.OrdinalIgnoreCase)) { this._flags ^= AttributeFlags.Constant; this.Prompt = $"속성 태그 입력 (상수={(this._flags.HasFlag(AttributeFlags.Constant) ? "예" : "아니오")}):"; return; }
				if (t.Length == 0 || t.Contains(' ')) { this.Ctx?.Print("태그에는 공백을 쓸 수 없습니다."); return; }
				this._tag = t.ToUpperInvariant();
				this._step = this._flags.HasFlag(AttributeFlags.Constant) ? 2 : 1;
				this.Prompt = this._step == 1 ? "속성 프롬프트 입력:" : "속성 값 입력:";
				break;
			case 1:
				this._prompt = t;
				this._step = 2;
				this.Prompt = "기본 속성 값 입력:";
				break;
			case 2:
				this._value = t;
				this._step = 3;
				this.Prompt = $"문자 높이 지정 <{LastHeight:0.####}>:";
				break;
			case 3:
				if (Num.TryParse(t, out double h) && h > 0) LastHeight = h;
				this._step = 4;
				this.Prompt = "시작점 지정:";
				break;
		}
	}

	public override void OnEnter()
	{
		if (this._step is 1 or 2 or 3) this.OnText("");
		else if (this._step == 0) this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this._step != 4 || this.Ctx == null) return;
		var doc = this.Ctx.Doc.Document;
		var def = new AttributeDefinition
		{
			Tag = this._tag,
			Prompt = this._prompt,
			Value = this._value,
			Height = LastHeight,
			InsertPoint = Num.Z(x, y),
			Flags = this._flags,
			Style = doc.TextStyles.TryGetValue(doc.Header.CurrentTextStyleName ?? "Standard", out var st) ? st : TextStyle.Default,
		};
		this.Ctx.Doc.AddEntities(def);
		this.Ctx.Print($"속성 정의 \"{this._tag}\"를 만들었습니다. BLOCK으로 도형과 함께 블록을 만드십시오.");
		this.Finish();
	}
}

/// <summary>ATTSYNC: 블록 참조의 속성을 블록 정의에 맞춘다 (없는 속성 추가, 정의에서 빠진 속성 제거, 위치·스타일 갱신, 값은 유지).</summary>
public sealed class AttSyncCommand : EditorCommand
{
	public override string Name => "ATTSYNC";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (ctx.Selection.OfType<Insert>().FirstOrDefault() is { } ins)
		{
			this.Sync(ins.Block);
			return;
		}
		this.Prompt = "블록 선택 또는 [이름(N)]:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx?.PickAt(x, y) is Insert ins) this.Sync(ins.Block);
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (t.Equals("N", StringComparison.OrdinalIgnoreCase))
		{
			this.Prompt = "블록 이름 입력:";
			return;
		}
		if (this.Ctx!.Doc.Document.BlockRecords.TryGetValue(t, out var br)) this.Sync(br);
		else this.Ctx.Print($"블록 \"{t}\"이(가) 없습니다.");
	}

	private void Sync(BlockRecord block)
	{
		var doc = this.Ctx!.Doc;
		var defs = block.AttributeDefinitions.ToList();
		var inserts = doc.Document.BlockRecords.SelectMany(b => b.Entities).OfType<Insert>().Where(i => i.Block == block).ToList();
		var actions = new List<IUndoableAction>();
		foreach (var ins in inserts)
		{
			var before = ins.Attributes.ToList();
			var values = before.GroupBy(a => a.Tag).ToDictionary(g => g.Key, g => g.First().Value);
			var tr = ins.GetTransform();
			var after = defs.Select(d =>
			{
				var a = new AttributeEntity(d);
				a.ApplyTransform(tr);
				if (values.TryGetValue(d.Tag, out var v) && !d.Flags.HasFlag(AttributeFlags.Constant)) a.Value = v;
				return a;
			}).ToList();
			actions.Add(new EntityEditAction("속성 동기화", ins,
				() => { ins.Attributes.Clear(); foreach (var a in after) ins.Attributes.Add(a); },
				() => { ins.Attributes.Clear(); foreach (var a in before) ins.Attributes.Add(a); }));
		}
		if (actions.Count > 0) doc.Execute(new CompositeAction("속성 동기화", actions));
		this.Ctx.Print($"블록 \"{block.Name}\"의 참조 {inserts.Count}개를 동기화했습니다.");
		this.Finish();
	}
}

/// <summary>ATTDISP: 속성 표시 [일반(N)/켜기(ON)/끄기(OFF)].</summary>
public sealed class AttDispCommand : EditorCommand
{
	public override string Name => "ATTDISP";

	public string? Preset { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (this.Preset != null)
		{
			this.OnText(this.Preset);
			return;
		}
		string cur = ctx.Doc.Document.Header.AttributeVisibility switch
		{
			AttributeVisibilityMode.All => "켜기",
			AttributeVisibilityMode.None => "끄기",
			_ => "일반",
		};
		this.Prompt = $"속성 가시성 설정 입력 [일반(N)/켜기(ON)/끄기(OFF)] <{cur}>:";
	}

	public override void OnEnter() => this.Finish();

	public override void OnText(string text)
	{
		var mode = text.Trim().ToUpperInvariant() switch
		{
			"ON" => AttributeVisibilityMode.All,
			"OFF" => AttributeVisibilityMode.None,
			"N" or "NORMAL" => AttributeVisibilityMode.Normal,
			_ => (AttributeVisibilityMode?)null,
		};
		if (mode is { } m)
		{
			var header = this.Ctx!.Doc.Document.Header;
			var old = header.AttributeVisibility;
			this.Ctx.Doc.Execute(new DelegateAction("속성 표시", () => header.AttributeVisibility = m, () => header.AttributeVisibility = old));
		}
		this.Finish();
	}
}

/// <summary>BASE: 도면의 삽입 기준점 (다른 도면에 블록으로 넣을 때의 기준).</summary>
public sealed class BaseCommand : EditorCommand
{
	public override string Name => "BASE";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var b = ctx.Doc.Document.Header.ModelSpaceInsertionBase;
		this.Prompt = $"기준점 입력 <{b.X:0.####},{b.Y:0.####},{b.Z:0.####}>:";
	}

	public override void OnPoint(double x, double y)
	{
		var header = this.Ctx!.Doc.Document.Header;
		var old = header.ModelSpaceInsertionBase;
		var p = Num.Z(x, y);
		this.Ctx.Doc.Execute(new DelegateAction("기준점", () => header.ModelSpaceInsertionBase = p, () => header.ModelSpaceInsertionBase = old));
		this.Finish();
	}

	public override void OnEnter() => this.Finish();
}

/// <summary>-ATTEDIT / EATTEDIT: 블록 참조를 골라 속성 값을 차례로 바꾼다 (Enter = 그대로).</summary>
public sealed class AttEditCommand : EditorCommand
{
	private readonly string _name;
	private Insert? _ins;
	private List<AttributeEntity> _atts = new();
	private int _i;
	private readonly List<(AttributeEntity Att, string Old, string New)> _changes = new();

	public AttEditCommand() : this("EATTEDIT")
	{
	}

	public AttEditCommand(string name) => this._name = name;

	public override string Name => this._name;
	public override string[] Aliases => this._name == "EATTEDIT" ? new[] { "ATTEDIT" } : Array.Empty<string>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._changes.Clear();
		if (ctx.Selection.OfType<Insert>().FirstOrDefault(i => i.HasAttributes) is { } ins)
		{
			this.Begin(ins);
			return;
		}
		this.Prompt = "블록 선택:";
	}

	private void Begin(Insert ins)
	{
		this._ins = ins;
		this._atts = ins.Attributes.Where(a => !a.Flags.HasFlag(AttributeFlags.Constant)).ToList();
		this._i = 0;
		if (this._atts.Count == 0)
		{
			this.Ctx!.Print("이 블록에는 편집할 속성이 없습니다.");
			this.Finish();
			return;
		}
		this.Ask();
	}

	private void Ask() => this.Prompt = $"{this._atts[this._i].Tag} 값 입력 <{this._atts[this._i].Value}>:";

	public override void OnPoint(double x, double y)
	{
		if (this._ins == null && this.Ctx?.PickAt(x, y) is Insert ins) this.Begin(ins);
	}

	public override void OnText(string text) => this.Next(text);

	public override void OnEnter()
	{
		if (this._ins == null) this.Finish();
		else this.Next(null);
	}

	private void Next(string? value)
	{
		if (this._ins == null) return;
		var att = this._atts[this._i];
		if (value != null && value != att.Value) this._changes.Add((att, att.Value, value));
		this._i++;
		if (this._i < this._atts.Count)
		{
			this.Ask();
			return;
		}
		if (this._changes.Count > 0)
		{
			var ch = this._changes.ToList();
			this.Ctx!.Doc.Execute(new EntityEditAction("속성 편집", this._ins,
				() => { foreach (var c in ch) c.Att.Value = c.New; },
				() => { foreach (var c in ch) c.Att.Value = c.Old; }));
		}
		this.Finish();
	}
}

/// <summary>GROUP / QGROUP: 선택 객체로 그룹 (GROUP은 이름을 묻는다, QGROUP은 무명 그룹).</summary>
public sealed class GroupCommand : EntityModificationCommand
{
	private readonly bool _quick;
	private bool _awaitName;

	public GroupCommand() : this(false)
	{
	}

	public GroupCommand(bool quick) => this._quick = quick;

	public override string Name => this._quick ? "QGROUP" : "GROUP";
	public override string[] Aliases => this._quick ? Array.Empty<string>() : new[] { "G" };

	protected override void OnSelectionReady()
	{
		if (this._quick)
		{
			this.Create("");
			return;
		}
		this._awaitName = true;
		this.Prompt = "그룹 이름 입력 <무명>:";
	}

	public override void OnText(string text)
	{
		if (this._awaitName) this.Create(text.Trim());
	}

	public override void OnEnter()
	{
		if (this._awaitName) this.Create("");
		else base.OnEnter();
	}

	private void Create(string name)
	{
		var doc = this.Ctx!.Doc;
		var groups = doc.Document.Groups;
		if (name.Length > 0 && groups.ContainsKey(name))
		{
			this.Ctx.Print($"그룹 \"{name}\"이(가) 이미 있습니다.");
			return;
		}
		var members = this.Targets.Where(e => e.Document == doc.Document).ToList();
		if (members.Count == 0)
		{
			this.Finish();
			return;
		}
		var group = new Group(name);
		doc.Execute(new DelegateAction("그룹",
			() => { groups.Add(group); group.AddRange(members); },
			() => { group.Clear(); groups.Remove(group.Name); }));
		this.Ctx.Print($"객체 {members.Count}개로 그룹 \"{group.Name}\"을(를) 만들었습니다.");
		this.Finish();
	}
}

/// <summary>UNGROUP: 고른 객체가 속한 그룹을 푼다.</summary>
public sealed class UngroupCommand : EntityModificationCommand
{
	public override string Name => "UNGROUP";

	protected override void OnSelectionReady()
	{
		var doc = this.Ctx!.Doc;
		var groups = GroupsOf(doc, this.Targets).ToList();
		var actions = new List<IUndoableAction>();
		foreach (var g in groups)
		{
			var members = g.Entities.ToList();
			var name = g.Name;
			actions.Add(new DelegateAction("그룹 해제",
				() => { g.Clear(); doc.Document.Groups.Remove(name); },
				() => { g.Name = name; doc.Document.Groups.Add(g); g.AddRange(members.Where(m => m.Document == doc.Document)); }));
		}
		if (actions.Count > 0) doc.Execute(new CompositeAction("그룹 해제", actions));
		this.Ctx.Print(groups.Count > 0 ? $"그룹 {groups.Count}개를 풀었습니다." : "선택한 객체는 그룹에 속해 있지 않습니다.");
		this.Finish();
	}

	/// <summary>객체들이 속한 그룹.</summary>
	public static IEnumerable<Group> GroupsOf(EditorDocument doc, IEnumerable<Entity> entities)
	{
		var set = entities.ToHashSet();
		return doc.Document.Groups.Where(g => g.Entities.Any(set.Contains));
	}
}

/// <summary>ADDSELECTED (복제): 고른 객체와 같은 종류를 그리는 명령을 같은 속성으로 시작 (앱이 Source로 이어서 실행).</summary>
public sealed class AddSelectedCommand : EditorCommand
{
	public override string Name => "ADDSELECTED";

	public Entity? Source { get; private set; }

	public override bool WantsSelection => !this.IsFinished;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (ctx.Selection.Count == 1)
		{
			this.Source = ctx.Selection.First();
			this.Finish();
			return;
		}
		this.Prompt = "객체 선택:";
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		this.Source = entities.FirstOrDefault();
		if (this.Source != null) this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx?.PickAt(x, y) is { } e) this.OnSelection(new[] { e });
	}

	/// <summary>객체 종류에 맞는 그리기 명령 이름.</summary>
	public static string? CommandFor(Entity e) => e switch
	{
		Arc => "ARC",
		Circle => "CIRCLE",
		Line => "LINE",
		LwPolyline => "PLINE",
		Ellipse => "ELLIPSE",
		Spline => "SPLINE",
		XLine => "XLINE",
		Ray => "RAY",
		Point => "POINT",
		Hatch => "HATCH",
		MText => "MTEXT",
		AttributeDefinition => "ATTDEF",
		TextEntity => "TEXT",
		Insert => "INSERT",
		DimensionLinear => "DIMLINEAR",
		DimensionAligned => "DIMALIGNED",
		DimensionRadius => "DIMRADIUS",
		DimensionDiameter => "DIMDIAMETER",
		DimensionAngular2Line or DimensionAngular3Pt => "DIMANGULAR",
		DimensionArc => "DIMARC",
		DimensionOrdinate => "DIMORDINATE",
		Leader => "QLEADER",
		Solid => "SOLID",
		_ => null,
	};
}
