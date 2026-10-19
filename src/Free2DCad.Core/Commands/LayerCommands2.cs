using System.Runtime.CompilerServices;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Commands;

/// <summary>도면층 켜기·동결·잠금 상태와 현재 도면층 (LAYERP·LAYUNISO 복원용).</summary>
public sealed class LayerState
{
	private readonly List<(Layer Layer, bool On, LayerFlags Flags)> _layers;
	private readonly Layer _current;

	private LayerState(EditorDocument doc)
	{
		this._layers = doc.Document.Layers.Select(l => (l, l.IsOn, l.Flags)).ToList();
		this._current = doc.CurrentLayer;
	}

	public static LayerState Capture(EditorDocument doc) => new(doc);

	public void Restore(EditorDocument doc)
	{
		foreach (var (layer, on, flags) in this._layers)
		{
			layer.IsOn = on;
			layer.Flags = flags;
		}
		if (doc.Document.Layers.Contains(this._current.Name)) doc.CurrentLayer = this._current;
	}

	private static readonly ConditionalWeakTable<EditorDocument, Stack<LayerState>> History = new();
	private static readonly ConditionalWeakTable<EditorDocument, LayerState> Isolation = new();

	/// <summary>도면층 상태를 바꾼다 (되돌리기 가능, LAYERP 기록).</summary>
	public static void Change(EditorDocument doc, string name, Action change)
	{
		var before = Capture(doc);
		change();
		var after = Capture(doc);
		before.Restore(doc);
		doc.Execute(new DelegateAction(name, () => after.Restore(doc), () => before.Restore(doc)));
		History.GetOrCreateValue(doc).Push(before);
	}

	public static LayerState? PopPrevious(EditorDocument doc) =>
		History.TryGetValue(doc, out var s) && s.Count > 0 ? s.Pop() : null;

	public static void SaveIsolation(EditorDocument doc, LayerState state) => Isolation.AddOrUpdate(doc, state);

	public static LayerState? TakeIsolation(EditorDocument doc)
	{
		if (!Isolation.TryGetValue(doc, out var s)) return null;
		Isolation.Remove(doc);
		return s;
	}
}

/// <summary>
/// 객체를 골라 그 도면층에 무언가를 하는 도면층 명령 (LAYOFF, LAYFRZ, LAYLCK, LAYULK, LAYMCUR, LAYISO, LAYCUR, LAYMCH, LAYDEL).
/// </summary>
public sealed class LayerPickCommand : EditorCommand
{
	public enum Op { MakeCurrent, Off, Freeze, Lock, Unlock, Isolate, ChangeToCurrent, Match, Delete }

	private readonly Op _op;
	private readonly List<Entity> _picked = new();
	private int _step; // 0 = 객체 선택, 1 = 대상(LAYMCH) / 확인(LAYDEL)
	private Layer? _deleteLayer;

	public LayerPickCommand() : this(Op.Off)
	{
	}

	public LayerPickCommand(Op op) => this._op = op;

	public override string Name => this._op switch
	{
		Op.MakeCurrent => "LAYMCUR",
		Op.Off => "LAYOFF",
		Op.Freeze => "LAYFRZ",
		Op.Lock => "LAYLCK",
		Op.Unlock => "LAYULK",
		Op.Isolate => "LAYISO",
		Op.ChangeToCurrent => "LAYCUR",
		Op.Match => "LAYMCH",
		_ => "LAYDEL",
	};

	public override string[] Aliases => Array.Empty<string>();

	/// <summary>한 번 클릭으로 바로 처리하는 명령 (여러 개 고를 필요 없음).</summary>
	private bool SinglePick => this._op is Op.MakeCurrent or Op.Off or Op.Freeze or Op.Lock or Op.Unlock;

	public override bool WantsSelection => !this.IsFinished && (this._step == 0 || (this._op == Op.Match && this._step == 1));

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._picked.Clear();
		this._step = 0;
		if (ctx.Selection.Count > 0 && !this.SinglePick)
		{
			this._picked.AddRange(ctx.Selection);
			this.AfterSelection();
			return;
		}
		if (ctx.Selection.Count > 0 && this._op == Op.MakeCurrent)
		{
			this.OnSelection(new[] { ctx.Selection.First() });
			return;
		}
		this.Prompt = this._op switch
		{
			Op.MakeCurrent => "현재로 지정할 도면층의 객체 선택:",
			Op.Off => "끌 도면층의 객체 선택 <완료는 Enter>:",
			Op.Freeze => "동결할 도면층의 객체 선택 <완료는 Enter>:",
			Op.Lock => "잠글 도면층의 객체 선택 <완료는 Enter>:",
			Op.Unlock => "잠금 해제할 도면층의 객체 선택 <완료는 Enter>:",
			Op.Isolate => "격리할 도면층의 객체 선택:",
			Op.ChangeToCurrent => "현재 도면층으로 바꿀 객체 선택:",
			Op.Match => "변경할 객체 선택:",
			_ => "삭제할 도면층의 객체 선택 또는 [이름(N)]:",
		};
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this.Ctx == null) return;
		var list = entities.ToList();
		if (list.Count == 0) return;
		if (this._op == Op.Match && this._step == 1)
		{
			this.MatchTo(list[0].Layer);
			return;
		}
		if (this.SinglePick)
		{
			var layer = list[0].Layer;
			if (layer == null) return;
			this.ApplySingle(layer);
			if (this._op == Op.MakeCurrent) this.Finish();
			return;
		}
		if (this._op == Op.Delete)
		{
			this.AskDelete(list[0].Layer);
			return;
		}
		foreach (var e in list)
		{
			if (!this._picked.Contains(e)) this._picked.Add(e);
		}
		this.Prompt = $"객체 선택: {this._picked.Count}개 <완료는 Enter>:";
	}

	public override void OnPoint(double x, double y)
	{
		// 캔버스가 WantsSelection 동안 클릭을 OnSelection으로 보낸다. 시험·명령줄 좌표 입력용
		if (this.Ctx?.PickAt(x, y) is { } e) this.OnSelection(new[] { e });
	}

	public override void OnEnter()
	{
		if (this._step == 0 && !this.SinglePick && this._picked.Count > 0 && this._op != Op.Delete)
		{
			this.AfterSelection();
			return;
		}
		if (this._op == Op.Delete && this._step == 1)
		{
			this.Finish();
			return;
		}
		this.Finish();
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim();
		if (this._op == Op.Delete)
		{
			if (this._step == 0 && t.Equals("N", StringComparison.OrdinalIgnoreCase))
			{
				this._step = 2;
				this.Prompt = "삭제할 도면층 이름 입력:";
				return;
			}
			if (this._step == 2)
			{
				if (this.Ctx.Doc.Document.Layers.TryGetValue(t, out var l)) this.AskDelete(l);
				else this.Ctx.Print($"도면층 \"{t}\"이(가) 없습니다.");
				return;
			}
			if (this._step == 1)
			{
				if (t.StartsWith("Y", StringComparison.OrdinalIgnoreCase)) this.DoDelete();
				this.Finish();
			}
			return;
		}
		if (this._op == Op.Match && this._step == 1 && t.Equals("N", StringComparison.OrdinalIgnoreCase))
		{
			this._step = 2;
			this.Prompt = "대상 도면층 이름 입력:";
			return;
		}
		if (this._op == Op.Match && this._step == 2)
		{
			if (this.Ctx.Doc.Document.Layers.TryGetValue(t, out var l)) this.MatchTo(l);
			else this.Ctx.Print($"도면층 \"{t}\"이(가) 없습니다.");
		}
	}

	private void ApplySingle(Layer layer)
	{
		var doc = this.Ctx!.Doc;
		switch (this._op)
		{
			case Op.MakeCurrent:
				LayerState.Change(doc, "도면층 현재로", () => doc.CurrentLayer = layer);
				this.Ctx.Print($"\"{layer.Name}\" 도면층이 현재 도면층입니다.");
				break;
			case Op.Off:
				LayerState.Change(doc, "도면층 끄기", () => layer.IsOn = false);
				this.Ctx.Print(layer == doc.CurrentLayer ? $"\"{layer.Name}\" 도면층(현재 도면층)을 껐습니다." : $"\"{layer.Name}\" 도면층을 껐습니다.");
				break;
			case Op.Freeze:
				if (layer == doc.CurrentLayer)
				{
					this.Ctx.Print("현재 도면층은 동결할 수 없습니다.");
					return;
				}
				LayerState.Change(doc, "도면층 동결", () => layer.Flags |= LayerFlags.Frozen);
				this.Ctx.Print($"\"{layer.Name}\" 도면층을 동결했습니다.");
				break;
			case Op.Lock:
				LayerState.Change(doc, "도면층 잠금", () => layer.Flags |= LayerFlags.Locked);
				this.Ctx.Print($"\"{layer.Name}\" 도면층을 잠갔습니다.");
				break;
			case Op.Unlock:
				LayerState.Change(doc, "도면층 잠금 해제", () => layer.Flags &= ~LayerFlags.Locked);
				this.Ctx.Print($"\"{layer.Name}\" 도면층의 잠금을 풀었습니다.");
				break;
		}
	}

	private void AfterSelection()
	{
		var doc = this.Ctx!.Doc;
		switch (this._op)
		{
			case Op.Isolate:
				{
					var keep = this._picked.Select(e => e.Layer).Where(l => l != null).Distinct().ToHashSet();
					if (keep.Count == 0) { this.Finish(); return; }
					LayerState.SaveIsolation(doc, LayerState.Capture(doc));
					LayerState.Change(doc, "도면층 격리", () =>
					{
						foreach (var l in doc.Document.Layers)
						{
							if (!keep.Contains(l)) l.IsOn = false;
							else l.IsOn = true;
						}
						if (!keep.Contains(doc.CurrentLayer)) doc.CurrentLayer = keep.First();
					});
					this.Ctx.Print($"도면층 {keep.Count}개를 격리했습니다 (나머지 도면층 끔).");
					this.Finish();
					break;
				}
			case Op.ChangeToCurrent:
				doc.SetLayer(this._picked, doc.CurrentLayer);
				this.Ctx.Print($"객체 {this._picked.Count}개를 현재 도면층 \"{doc.CurrentLayer.Name}\"(으)로 바꿨습니다.");
				this.Finish();
				break;
			case Op.Match:
				this._step = 1;
				this.Prompt = "대상 도면층의 객체 선택 또는 [이름(N)]:";
				break;
			default:
				this.Finish();
				break;
		}
	}

	private void MatchTo(Layer? layer)
	{
		if (layer == null) return;
		this.Ctx!.Doc.SetLayer(this._picked, layer);
		this.Ctx.Print($"객체 {this._picked.Count}개를 \"{layer.Name}\" 도면층으로 바꿨습니다.");
		this.Finish();
	}

	private void AskDelete(Layer? layer)
	{
		var doc = this.Ctx!.Doc;
		if (layer == null) return;
		if (layer.Name == "0" || layer.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase) || layer == doc.CurrentLayer)
		{
			this.Ctx.Print("0, Defpoints, 현재 도면층은 삭제할 수 없습니다.");
			return;
		}
		this._deleteLayer = layer;
		int n = doc.Document.BlockRecords.SelectMany(b => b.Entities).Count(e => e.Layer == layer);
		this._step = 1;
		this.Prompt = $"\"{layer.Name}\" 도면층과 객체 {n}개를 삭제합니다. 계속하시겠습니까? [예(Y)/아니오(N)] <N>:";
	}

	private void DoDelete()
	{
		var doc = this.Ctx!.Doc;
		var layer = this._deleteLayer!;
		var victims = doc.Document.BlockRecords.SelectMany(b => b.Entities).Where(e => e.Layer == layer).ToList();
		doc.Execute(new CompositeAction("도면층 삭제", new List<IUndoableAction>
		{
			new RemoveEntitiesAction(victims, "도면층 객체 삭제"),
			new RemoveLayerAction(doc.Document, layer),
		}));
		this.Ctx.Print($"\"{layer.Name}\" 도면층과 객체 {victims.Count}개를 삭제했습니다.");
	}
}

/// <summary>LAYON / LAYTHW / LAYUNISO / LAYERP: 객체 선택 없이 바로 실행하는 도면층 명령.</summary>
public sealed class LayerAllCommand : EditorCommand
{
	public enum Op { AllOn, ThawAll, Unisolate, Previous }

	private readonly Op _op;

	public LayerAllCommand() : this(Op.AllOn)
	{
	}

	public LayerAllCommand(Op op) => this._op = op;

	public override string Name => this._op switch
	{
		Op.AllOn => "LAYON",
		Op.ThawAll => "LAYTHW",
		Op.Unisolate => "LAYUNISO",
		_ => "LAYERP",
	};

	public override string[] Aliases => Array.Empty<string>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var doc = ctx.Doc;
		switch (this._op)
		{
			case Op.AllOn:
				LayerState.Change(doc, "모든 도면층 켜기", () => { foreach (var l in doc.Document.Layers) l.IsOn = true; });
				ctx.Print("모든 도면층을 켰습니다.");
				break;
			case Op.ThawAll:
				LayerState.Change(doc, "모든 도면층 해동", () => { foreach (var l in doc.Document.Layers) l.Flags &= ~LayerFlags.Frozen; });
				ctx.Print("모든 도면층을 해동했습니다.");
				break;
			case Op.Unisolate:
				if (LayerState.TakeIsolation(doc) is { } iso)
				{
					LayerState.Change(doc, "도면층 격리 해제", () => iso.Restore(doc));
					ctx.Print("격리 전 도면층 상태로 되돌렸습니다.");
				}
				else
				{
					ctx.Print("LAYISO로 격리한 도면층이 없습니다.");
				}
				break;
			case Op.Previous:
				if (LayerState.PopPrevious(doc) is { } prev)
				{
					var before = LayerState.Capture(doc);
					prev.Restore(doc);
					var after = LayerState.Capture(doc);
					before.Restore(doc);
					doc.Execute(new DelegateAction("도면층 이전", () => after.Restore(doc), () => before.Restore(doc)));
					ctx.Print("도면층 상태를 이전으로 되돌렸습니다.");
				}
				else
				{
					ctx.Print("되돌릴 이전 도면층 상태가 없습니다.");
				}
				break;
		}
		this.Finish();
	}
}

/// <summary>
/// -LAYERSTATE / LAYERSTATESAVE: 도면층 상태 [?/저장(S)/복원(R)/삭제(D)] (대화상자 LAYERSTATE는 앱).
/// </summary>
public sealed class LayerStateCommand : EditorCommand
{
	private readonly bool _saveOnly;
	private string? _op;

	public LayerStateCommand() : this(false)
	{
	}

	public LayerStateCommand(bool saveOnly) => this._saveOnly = saveOnly;

	public override string Name => this._saveOnly ? "LAYERSTATESAVE" : "-LAYERSTATE";
	public override string[] Aliases => Array.Empty<string>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (this._saveOnly)
		{
			this._op = "S";
			this.Prompt = "저장할 도면층 상태 이름 입력:";
			return;
		}
		this._op = null;
		this.Prompt = "옵션 입력 [?/저장(S)/복원(R)/삭제(D)]:";
	}

	public override void OnText(string text)
	{
		var doc = this.Ctx!.Doc;
		string t = text.Trim();
		if (this._op == null)
		{
			switch (t.ToUpperInvariant())
			{
				case "?":
					var list = Free2DCad.Core.Styles.LayerStates.List(doc.Document);
					this.Ctx.Print(list.Count == 0 ? "저장된 도면층 상태가 없습니다." : "도면층 상태: " + string.Join(", ", list.Select(s => s.Name)));
					return;
				case "S": this._op = "S"; this.Prompt = "저장할 도면층 상태 이름 입력:"; return;
				case "R": this._op = "R"; this.Prompt = "복원할 도면층 상태 이름 입력:"; return;
				case "D": this._op = "D"; this.Prompt = "삭제할 도면층 상태 이름 입력:"; return;
			}
			return;
		}
		if (t.Length == 0) return;
		switch (this._op)
		{
			case "S":
				Free2DCad.Core.Styles.LayerStates.Save(doc, t);
				this.Ctx.Print($"도면층 상태 \"{t}\"을(를) 저장했습니다.");
				break;
			case "R":
				this.Ctx.Print(Free2DCad.Core.Styles.LayerStates.Restore(doc, t) ? $"도면층 상태 \"{t}\"을(를) 복원했습니다." : $"도면층 상태 \"{t}\"이(가) 없습니다.");
				break;
			case "D":
				Free2DCad.Core.Styles.LayerStates.Delete(doc, t);
				break;
		}
		this.Finish();
	}

	public override void OnEnter() => this.Finish();
}
