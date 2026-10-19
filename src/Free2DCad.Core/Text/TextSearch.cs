using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Tables;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Text;

/// <summary>찾기 및 바꾸기 옵션 (기존 CAD FIND 대화상자의 검색 옵션·텍스트 유형).</summary>
public sealed record FindOptions(
	bool MatchCase = false,
	bool WholeWord = false,
	bool Wildcards = false,
	bool IgnoreHidden = true,
	bool Attributes = true,
	bool DimensionsAndLeaders = true,
	bool Texts = true,
	bool Tables = false);

/// <summary>찾은 문자 하나. Row/Col은 표 칸.</summary>
public sealed record FindMatch(Entity Entity, string Kind, string Text, string Location, int Row = -1, int Col = -1);

/// <summary>
/// FIND: 도면의 문자(단일 행·여러 줄·속성·치수·다중지시선·표)에서 찾고 바꾼다. 서식 코드는 그대로 두고 보이는 글만 바꾼다.
/// </summary>
public static class TextSearch
{
	public static Regex BuildRegex(string pattern, FindOptions o)
	{
		string p = Regex.Escape(pattern);
		if (o.Wildcards)
		{
			p = p.Replace("\\*", ".*").Replace("\\?", ".");
		}
		if (o.WholeWord)
		{
			p = $@"(?<![\w]){p}(?![\w])";
		}
		return new Regex(p, (o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant);
	}

	/// <summary>찾기 범위의 객체: scope가 있으면 그것만, 없으면 모형 공간과 모든 배치.</summary>
	private static IEnumerable<(Entity Ent, string Location)> Candidates(CadDocument doc, IEnumerable<Entity>? scope)
	{
		if (scope != null)
		{
			foreach (var e in scope) yield return (e, LocationOf(doc, e));
			yield break;
		}
		foreach (var e in doc.ModelSpace.Entities) yield return (e, "모형");
		foreach (var layout in doc.Layouts.Where(l => !l.Name.Equals("Model", StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.TabOrder))
		{
			if (layout.AssociatedBlock == null) continue;
			foreach (var e in layout.AssociatedBlock.Entities) yield return (e, layout.Name);
		}
	}

	private static string LocationOf(CadDocument doc, Entity e)
	{
		if (ReferenceEquals(e.Owner, doc.ModelSpace)) return "모형";
		var layout = doc.Layouts.FirstOrDefault(l => ReferenceEquals(l.AssociatedBlock, e.Owner));
		return layout?.Name ?? "모형";
	}

	private static bool Hidden(Entity e) =>
		e.IsInvisible || e.Layer is { } l && (!l.IsOn || l.Flags.HasFlag(LayerFlags.Frozen));

	public static List<FindMatch> Find(CadDocument doc, string pattern, FindOptions o, IEnumerable<Entity>? scope = null)
	{
		var result = new List<FindMatch>();
		if (string.IsNullOrEmpty(pattern)) return result;
		var re = BuildRegex(pattern, o);
		foreach (var (e, loc) in Candidates(doc, scope))
		{
			if (o.IgnoreHidden && Hidden(e)) continue;
			foreach (var m in TextsOf(e, o))
			{
				if (re.IsMatch(m.Text)) result.Add(m with { Location = loc });
			}
		}
		return result;
	}

	/// <summary>객체의 보이는 글 (옵션의 텍스트 유형에 맞는 것만).</summary>
	private static IEnumerable<FindMatch> TextsOf(Entity e, FindOptions o)
	{
		switch (e)
		{
			case MText mt when o.Texts:
				yield return new FindMatch(mt, "여러 줄 문자", MTextTokenizer.PlainText(mt.Value), string.Empty);
				break;
			case TableEntity table when o.Tables:
				for (int r = 0; r < table.Rows.Count; r++)
				{
					for (int c = 0; c < table.Columns.Count; c++)
					{
						string t = TableEditor.GetCellText(table, r, c);
						if (t.Length > 0) yield return new FindMatch(table, "표", t, string.Empty, r, c);
					}
				}
				break;
			case Insert ins when o.Attributes && e is not TableEntity:
				foreach (var a in ins.Attributes)
				{
					if (!string.IsNullOrEmpty(a.Value)) yield return new FindMatch(a, "블록 속성", a.Value, string.Empty);
				}
				break;
			case TextEntity t when o.Texts && e is not AttributeEntity:
				yield return new FindMatch(t, "문자", MTextTokenizer.PlainText(t.Value, false), string.Empty);
				break;
			case Dimension d when o.DimensionsAndLeaders && !string.IsNullOrEmpty(d.Text) && d.Text != "<>":
				yield return new FindMatch(d, "치수", MTextTokenizer.PlainText(d.Text), string.Empty);
				break;
			case MultiLeader ml when o.DimensionsAndLeaders && !string.IsNullOrEmpty(ml.ContextData.TextLabel):
				yield return new FindMatch(ml, "다중 지시선", MTextTokenizer.PlainText(ml.ContextData.TextLabel), string.Empty);
				break;
		}
	}

	/// <summary>찾은 문자 하나를 바꾸는 되돌리기 가능한 동작 (바뀌지 않으면 null).</summary>
	public static IUndoableAction? ReplaceAction(CadDocument doc, FindMatch m, Regex re, string replacement)
	{
		string newPlain = re.Replace(m.Text, _ => replacement);
		if (newPlain == m.Text) return null;
		switch (m.Entity)
		{
			case MText mt:
				return new ChangeTextAction(mt, ReplaceInRaw(mt.Value, true, re, replacement));
			case AttributeEntity a:
				return new ChangeTextAction(a, newPlain);
			case TextEntity t:
				return new ChangeTextAction(t, ReplaceInRaw(t.Value, false, re, replacement));
			case MultiLeader ml:
				return new ChangeTextAction(ml, ReplaceInRaw(ml.ContextData.TextLabel ?? string.Empty, true, re, replacement));
			case Dimension d:
				{
					string old = d.Text ?? string.Empty;
					string nw = ReplaceInRaw(old, true, re, replacement);
					return new EntityEditAction("치수 문자 바꾸기", d,
						() => { d.Text = nw; Editing.GripEditor.RefreshDimension(d); },
						() => { d.Text = old; Editing.GripEditor.RefreshDimension(d); });
				}
			case TableEntity table when m.Row >= 0:
				{
					string old = TableEditor.GetCellText(table, m.Row, m.Col);
					return new EntityEditAction("표 문자 바꾸기", table,
						() => TableEditor.SetCellText(doc, table, m.Row, m.Col, newPlain),
						() => TableEditor.SetCellText(doc, table, m.Row, m.Col, old));
				}
		}
		return null;
	}

	/// <summary>
	/// 서식 문자열 안에서 찾은 글마다 그 자리만 바꾼다 (뒤에서부터). 찾은 글이 서식 묶음 경계에 걸치면 중괄호는 남긴다.
	/// </summary>
	public static string ReplaceInRaw(string raw, bool isMText, Regex re, string replacement)
	{
		string plain = MTextTokenizer.PlainText(raw, isMText);
		var matches = re.Matches(plain).Cast<Match>().Where(m => m.Length > 0).Reverse().ToList();
		string rep = isMText ? MTextTokenizer.Escape(replacement) : replacement;
		foreach (var m in matches)
		{
			if (MTextTokenizer.RawRange(raw, m.Index, m.Index + m.Length, isMText) is not { } r) continue;
			string slice = raw[r.SrcStart..r.SrcEnd];
			var braces = new System.Text.StringBuilder();
			for (int i = 0; i < slice.Length; i++)
			{
				if (slice[i] == '\\') { i++; continue; } // \{ \} 같은 글자는 건너뜀
				if (slice[i] is '{' or '}') braces.Append(slice[i]);
			}
			raw = raw[..r.SrcStart] + rep + braces + raw[r.SrcEnd..];
		}
		return raw;
	}

	/// <summary>모두 바꾸기 (한 번에 되돌리기). 바꾼 개수.</summary>
	public static int ReplaceAll(EditorDocument doc, IEnumerable<FindMatch> matches, string pattern, string replacement, FindOptions o)
	{
		var re = BuildRegex(pattern, o);
		var actions = matches.Select(m => ReplaceAction(doc.Document, m, re, replacement)).OfType<IUndoableAction>().ToList();
		if (actions.Count == 0) return 0;
		doc.Execute(new CompositeAction("모두 바꾸기", actions));
		return actions.Count;
	}

	/// <summary>하나 바꾸기. 바꿨으면 true.</summary>
	public static bool Replace(EditorDocument doc, FindMatch match, string pattern, string replacement, FindOptions o)
	{
		var a = ReplaceAction(doc.Document, match, BuildRegex(pattern, o), replacement);
		if (a == null) return false;
		doc.Execute(a);
		return true;
	}
}
