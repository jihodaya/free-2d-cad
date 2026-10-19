namespace Free2DCad.Rendering;

/// <summary>기하 공차(TOLERANCE) 문자열의 한 칸 안 항목: GDT 기호 하나 또는 글.</summary>
public sealed record GdtItem(char Symbol, string Text)
{
	public bool IsSymbol => this.Symbol != '\0';
}

/// <summary>
/// 기존 CAD 기하 공차 문자열: 줄은 ^J, 칸은 %%v, 기호는 {\Fgdt;j} (gdt 글꼴 글자).
/// 예) {\Fgdt;j}%%v{\Fgdt;n}0.1{\Fgdt;m}%%v%%vA%%vB%%v
/// </summary>
public static class GdtText
{
	/// <summary>gdt 글꼴 글자 → 이름 (대화상자·설명용).</summary>
	public static readonly (char Code, string Name)[] Symbols =
	{
		('j', "위치도"), ('k', "선의 윤곽도"), ('e', "진원도"), ('a', "경사도"), ('i', "대칭도"), ('t', "온 흔들림"),
		('d', "면의 윤곽도"), ('c', "평면도"), ('b', "직각도"), ('r', "동심도"), ('h', "원주 흔들림"), ('u', "진직도"),
		('g', "원통도"), ('f', "평행도"),
	};

	/// <summary>줄 → 칸 → 항목.</summary>
	public static List<List<List<GdtItem>>> Parse(string? text)
	{
		var rows = new List<List<List<GdtItem>>>();
		if (string.IsNullOrEmpty(text))
		{
			return rows;
		}
		foreach (string line in text.Split(new[] { "^J", "\\P" }, StringSplitOptions.None))
		{
			var fields = new List<List<GdtItem>>();
			foreach (string field in line.Split(new[] { "%%v", "%%V" }, StringSplitOptions.None))
			{
				fields.Add(ParseField(field));
			}
			rows.Add(fields);
		}
		return rows;
	}

	private static List<GdtItem> ParseField(string s)
	{
		var items = new List<GdtItem>();
		var sb = new System.Text.StringBuilder();
		void Flush()
		{
			if (sb.Length > 0)
			{
				items.Add(new GdtItem('\0', sb.ToString()));
				sb.Clear();
			}
		}
		int i = 0;
		while (i < s.Length)
		{
			if (s[i] == '{' && s.IndexOf(';', i) is int semi && semi > i && s.Substring(i + 1, semi - i - 1).StartsWith("\\Fgdt", StringComparison.OrdinalIgnoreCase))
			{
				int close = s.IndexOf('}', semi);
				if (close < 0) close = s.Length;
				Flush();
				foreach (char ch in s[(semi + 1)..close])
				{
					items.Add(new GdtItem(char.ToLowerInvariant(ch), string.Empty));
				}
				i = close + 1;
				continue;
			}
			if (s[i] == '%' && i + 2 < s.Length && s[i + 1] == '%')
			{
				char c = char.ToLowerInvariant(s[i + 2]);
				string? rep = c switch { 'c' => "Ø", 'd' => "°", 'p' => "±", _ => null };
				if (rep != null)
				{
					sb.Append(rep);
					i += 3;
					continue;
				}
			}
			if (s[i] is '{' or '}')
			{
				i++;
				continue;
			}
			sb.Append(s[i]);
			i++;
		}
		Flush();
		return items;
	}

	/// <summary>한 줄 만들기: 기호, 공차1(지름·값·재료 조건), 공차2, 데이텀 1~3(값·재료 조건).</summary>
	public static string Row(char symbol, (bool Dia, string Value, char Mc) tol1, (bool Dia, string Value, char Mc) tol2,
		(string Value, char Mc) d1, (string Value, char Mc) d2, (string Value, char Mc) d3)
	{
		static string G(char c) => c == '\0' ? string.Empty : $"{{\\Fgdt;{c}}}";
		static string T((bool Dia, string Value, char Mc) t) =>
			string.IsNullOrEmpty(t.Value) && !t.Dia && t.Mc == '\0' ? string.Empty : (t.Dia ? G('n') : string.Empty) + t.Value + G(t.Mc);
		static string D((string Value, char Mc) d) => string.IsNullOrEmpty(d.Value) ? string.Empty : d.Value + G(d.Mc);
		return string.Join("%%v", G(symbol), T(tol1), T(tol2), D(d1), D(d2), D(d3));
	}

	/// <summary>줄이 비었는지 (칸이 모두 빔).</summary>
	public static bool IsEmptyRow(string row) => row.Replace("%%v", string.Empty).Length == 0;
}
