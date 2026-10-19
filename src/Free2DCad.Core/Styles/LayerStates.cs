using ACadSharp;
using ACadSharp.Objects;
using ACadSharp.Tables;
using ACadSharp.Tables.Collections;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Styles;

/// <summary>
/// 도면층 상태 (LAYERSTATE): 이름을 붙여 저장한 도면층들의 켜기·동결·잠금·플롯·색·선종류·선가중치 + 현재 도면층.
/// 기존 CAD와 같은 자리·형식으로 저장한다: 도면층 테이블의 확장 사전 → "ACAD_LAYERSTATES" → 상태마다 XRecord
/// (91 마스크, 301 설명, 290, 302 현재 도면층, 도면층마다 330 도면층·90 플래그·62 색·370 선가중치·331 선종류·1 플롯 스타일·440 투명도).
/// 예전 F2D 형식(루트 사전 "F2D_LAYERSTATES")도 읽는다.
/// </summary>
public static class LayerStates
{
	private const string AcadDictName = "ACAD_LAYERSTATES";
	private const string LegacyDictName = "F2D_LAYERSTATES";

	// 90 플래그 (기존 CAD 도면층 상태)
	private const int FlagOff = 1, FlagFrozen = 2, FlagLocked = 4, FlagPlot = 8;

	public sealed record LayerProps(string Name, bool On, bool Frozen, bool Locked, bool Plot, Color Color, string LineType, LineWeightType LineWeight);

	public sealed record State(string Name, string Description, string CurrentLayer, List<LayerProps> Layers);

	private static CadDictionary? AcadDict(CadDocument doc, bool create)
	{
		var xd = doc.Layers.XDictionary;
		if (xd == null)
		{
			if (!create) return null;
			xd = doc.Layers.CreateExtendedDictionary();
		}
		if (xd.TryGetEntry<CadDictionary>(AcadDictName, out var d)) return d;
		if (!create) return null;
		d = new CadDictionary(AcadDictName);
		xd.Add(AcadDictName, d);
		return d;
	}

	private static CadDictionary? LegacyDict(CadDocument doc) =>
		doc.RootDictionary.TryGetEntry<CadDictionary>(LegacyDictName, out var d) ? d : null;

	public static IReadOnlyList<State> List(CadDocument doc)
	{
		var result = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);
		if (LegacyDict(doc) is { } legacy)
		{
			foreach (var s in legacy.OfType<XRecord>().Select(ReadLegacy).OfType<State>()) result[s.Name] = s;
		}
		if (AcadDict(doc, false) is { } acad)
		{
			foreach (var s in acad.OfType<XRecord>().Select(ReadAcad).OfType<State>()) result[s.Name] = s;
		}
		return result.Values.OrderBy(s => s.Name).ToList();
	}

	public static State Capture(EditorDocument doc, string name, string description) =>
		new(name, description, doc.CurrentLayer.Name, doc.Document.Layers.Select(l => new LayerProps(
			l.Name, l.IsOn, l.Flags.HasFlag(LayerFlags.Frozen), l.Flags.HasFlag(LayerFlags.Locked), l.PlotFlag,
			l.Color, l.LineType?.Name ?? "Continuous", l.LineWeight)).ToList());

	/// <summary>저장 (같은 이름이 있으면 바꿈). 되돌리기 가능.</summary>
	public static void Save(EditorDocument doc, string name, string description = "")
	{
		var cad = doc.Document;
		var rec = WriteAcad(Capture(doc, name, description), cad);
		XRecord? old = null;
		if (AcadDict(cad, false) is { } existing && existing.TryGetEntry<XRecord>(name, out var o)) old = o;
		XRecord? oldLegacy = LegacyDict(cad) is { } lg && lg.TryGetEntry<XRecord>(name, out var ol) ? ol : null;
		doc.Execute(new DelegateAction("도면층 상태 저장",
			() =>
			{
				var d = AcadDict(cad, true)!;
				if (d.TryGetEntry<XRecord>(name, out _)) d.Remove(name);
				d.Add(name, rec);
				if (oldLegacy != null) LegacyDict(cad)?.Remove(name);
			},
			() =>
			{
				var d = AcadDict(cad, true)!;
				d.Remove(name);
				if (old != null) d.Add(name, old);
				if (oldLegacy != null && LegacyDict(cad) is { } l2 && !l2.TryGetEntry<XRecord>(name, out _)) l2.Add(name, oldLegacy);
			}));
	}

	public static void Delete(EditorDocument doc, string name)
	{
		var cad = doc.Document;
		var actions = new List<IUndoableAction>();
		foreach (var d in new[] { AcadDict(cad, false), LegacyDict(cad) })
		{
			if (d != null && d.TryGetEntry<XRecord>(name, out var rec))
			{
				var dict = d;
				actions.Add(new DelegateAction("도면층 상태 삭제", () => dict.Remove(name), () => dict.Add(name, rec)));
			}
		}
		if (actions.Count > 0) doc.Execute(new CompositeAction("도면층 상태 삭제", actions));
	}

	/// <summary>복원: 저장된 도면층의 상태를 되돌린다 (없는 도면층은 건너뜀). 되돌리기 가능.</summary>
	public static bool Restore(EditorDocument doc, string name)
	{
		var state = List(doc.Document).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (state == null) return false;
		var lineTypes = doc.Document.LineTypes;
		var before = Capture(doc, "", "");
		Apply(doc, state, lineTypes);
		var after = Capture(doc, "", "");
		Apply(doc, before, lineTypes);
		doc.Execute(new DelegateAction("도면층 상태 복원", () => Apply(doc, after, lineTypes), () => Apply(doc, before, lineTypes)));
		return true;
	}

	private static void Apply(EditorDocument doc, State state, LineTypesTable lineTypes)
	{
		foreach (var p in state.Layers)
		{
			if (!doc.Document.Layers.TryGetValue(p.Name, out var l)) continue;
			l.IsOn = p.On;
			l.Flags = p.Frozen ? l.Flags | LayerFlags.Frozen : l.Flags & ~LayerFlags.Frozen;
			l.Flags = p.Locked ? l.Flags | LayerFlags.Locked : l.Flags & ~LayerFlags.Locked;
			l.PlotFlag = p.Plot;
			l.Color = p.Color;
			if (lineTypes.TryGetValue(p.LineType, out var lt)) l.LineType = lt;
			l.LineWeight = p.LineWeight;
		}
		if (doc.Document.Layers.TryGetValue(state.CurrentLayer, out var cur)) doc.CurrentLayer = cur;
	}

	/// <summary>기존 CAD 형식 XRecord.</summary>
	private static XRecord WriteAcad(State s, CadDocument doc)
	{
		var rec = new XRecord(s.Name);
		rec.CreateEntry(91, 2047); // 저장한 속성 마스크 (모두)
		rec.CreateEntry(301, s.Description ?? string.Empty);
		rec.CreateEntry(290, false);
		rec.CreateEntry(302, s.CurrentLayer);
		foreach (var p in s.Layers)
		{
			if (!doc.Layers.TryGetValue(p.Name, out var layer)) continue;
			int flags = (p.On ? 0 : FlagOff) | (p.Frozen ? FlagFrozen : 0) | (p.Locked ? FlagLocked : 0) | (p.Plot ? FlagPlot : 0);
			rec.CreateEntry(330, layer);
			rec.CreateEntry(90, flags);
			rec.CreateEntry(62, (short)(p.Color.IsTrueColor ? p.Color.GetApproxIndex() : p.Color.Index));
			if (p.Color.IsTrueColor) rec.CreateEntry(421, (p.Color.R << 16) | (p.Color.G << 8) | p.Color.B);
			rec.CreateEntry(370, (short)p.LineWeight);
			var lt = doc.LineTypes.TryGetValue(p.LineType, out var t) ? t : layer.LineType;
			rec.CreateEntry(331, lt);
			rec.CreateEntry(1, $"Color_{(p.Color.IsTrueColor ? p.Color.GetApproxIndex() : Math.Abs((int)p.Color.Index))}");
			rec.CreateEntry(440, 0);
		}
		return rec;
	}

	/// <summary>
	/// 기존 CAD 형식 읽기. DWG에서 읽으면 도면층·선종류 참조(330/331)가 속성 묶음 뒤에 모여 있어 순서대로 짝짓는다.
	/// </summary>
	private static State? ReadAcad(XRecord rec)
	{
		string desc = string.Empty, cur = "0";
		var layerRefs = new List<string?>();
		var ltRefs = new List<string?>();
		var groups = new List<(int Flags, short Color, int True, short Lw)>();
		int flags = 0, tc = -1;
		short ci = 7;
		bool open = false;
		void Close(short lw)
		{
			if (!open) return;
			groups.Add((flags, ci, tc, lw));
			open = false;
			tc = -1;
		}
		foreach (var e in rec.Entries)
		{
			switch (e.Code)
			{
				case 301: desc = e.Value?.ToString() ?? string.Empty; break;
				case 302: cur = e.Value?.ToString() ?? "0"; break;
				case 330: layerRefs.Add(NameOf(e)); break;
				case 331: ltRefs.Add(NameOf(e)); break;
				case 90: flags = Convert.ToInt32(e.Value); open = true; break;
				case 62: ci = Math.Abs(Convert.ToInt16(e.Value)); break;
				case 421: tc = Convert.ToInt32(e.Value); break;
				case 370: Close(Convert.ToInt16(e.Value)); break;
			}
		}
		var layers = new List<LayerProps>();
		for (int i = 0; i < groups.Count && i < layerRefs.Count; i++)
		{
			if (layerRefs[i] is not { } name) continue;
			var g = groups[i];
			var color = g.True >= 0 ? new Color((byte)(g.True >> 16), (byte)(g.True >> 8), (byte)g.True) : new Color(g.Color);
			string lt = i < ltRefs.Count && ltRefs[i] is { } l ? l : "Continuous";
			layers.Add(new LayerProps(name, (g.Flags & FlagOff) == 0, (g.Flags & FlagFrozen) != 0, (g.Flags & FlagLocked) != 0, (g.Flags & FlagPlot) != 0, color, lt, (LineWeightType)g.Lw));
		}
		return new State(rec.Name, desc, cur, layers);
	}

	private static string? NameOf(XRecord.Entry e) => e.Value switch
	{
		Layer l => l.Name,
		LineType t => t.Name,
		_ => e.HasLinkedObject ? (e.GetReference() as TableEntry)?.Name : null,
	};

	/// <summary>예전 F2D 형식 (8 이름, 90 플래그(1=켜짐), 62/420 색, 6 선종류, 370 선가중치).</summary>
	private static State? ReadLegacy(XRecord rec)
	{
		string desc = string.Empty, cur = "0";
		var layers = new List<LayerProps>();
		string? name = null;
		int flags = 1;
		short ci = 7;
		int tc = -1;
		string lt = "Continuous";
		void Flush(short lw)
		{
			if (name == null) return;
			var color = tc >= 0 ? new Color((byte)(tc >> 16), (byte)(tc >> 8), (byte)tc) : new Color(ci);
			layers.Add(new LayerProps(name, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0, color, lt, (LineWeightType)lw));
			name = null;
		}
		foreach (var e in rec.Entries)
		{
			switch (e.Code)
			{
				case 1: desc = e.Value?.ToString() ?? string.Empty; break;
				case 3: cur = e.Value?.ToString() ?? "0"; break;
				case 8: name = e.Value?.ToString(); break;
				case 90: flags = Convert.ToInt32(e.Value); break;
				case 62: ci = Convert.ToInt16(e.Value); break;
				case 420: tc = Convert.ToInt32(e.Value); break;
				case 6: lt = e.Value?.ToString() ?? "Continuous"; break;
				case 370: Flush(Convert.ToInt16(e.Value)); break;
			}
		}
		return new State(rec.Name, desc, cur, layers);
	}
}
