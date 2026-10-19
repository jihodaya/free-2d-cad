using System.Globalization;
using System.Text;

namespace Free2DCad.Rendering;

/// <summary>글자 서식 (MTEXT 서식 코드로 바뀌는 값). null이면 문자 스타일 값.</summary>
public sealed record MTextFormat
{
	/// <summary>\f 글꼴 패밀리 이름(TrueType) 또는 \F 글꼴 파일(SHX). null = 문자 스타일 글꼴.</summary>
	public string? Font { get; init; }

	public bool Bold { get; init; }

	public bool Italic { get; init; }

	/// <summary>문자 높이 배율 (\H0.7x) — 기본 높이에 곱한다.</summary>
	public double HeightScale { get; init; } = 1;

	/// <summary>\H2.5; 절대 높이 (있으면 HeightScale보다 우선).</summary>
	public double? Height { get; init; }

	/// <summary>\W 폭 비율 (스타일 폭 비율에 곱한다).</summary>
	public double Width { get; init; } = 1;

	/// <summary>\T 자간 비율.</summary>
	public double Tracking { get; init; } = 1;

	/// <summary>\Q 기울기(도). null = 문자 스타일 값.</summary>
	public double? ObliqueDeg { get; init; }

	/// <summary>\C 색상 번호 또는 \c 트루컬러. null = 객체 색.</summary>
	public Rgb? Color { get; init; }

	public bool Underline { get; init; }

	public bool Overline { get; init; }

	public bool Strike { get; init; }

	public static readonly MTextFormat Default = new();
}

/// <summary>
/// 문단 서식 (\p): 첫 줄 들여쓰기(왼쪽 들여쓰기 기준), 왼쪽·오른쪽 들여쓰기, 탭 위치, 정렬.
/// 값의 단위 (기존 CAD에서 확인): \p...; 는 도면 단위, \px...; 는 문자 높이 배수 (높이 2.5에서 \pxl2; = 5만큼 들여씀).
/// </summary>
public sealed record MTextParagraph(double FirstIndent, double LeftIndent, double RightIndent, double[] Tabs, char Align)
{
	public static readonly MTextParagraph Default = new(0, 0, 0, Array.Empty<double>(), '\0');

	/// <summary>탭 종류 (Tabs와 같은 순서): 'l' 왼쪽, 'c' 가운데, 'r' 오른쪽, 'd' 소수점.</summary>
	public char[] TabKinds { get; init; } = Array.Empty<char>();

	/// <summary>소수점 탭의 구분 기호 (기본 '.').</summary>
	public char DecimalChar { get; init; } = '.';

	/// <summary>문단 앞 간격 (b, 높이 배수).</summary>
	public double Before { get; init; }

	/// <summary>문단 뒤 간격 (a, 높이 배수).</summary>
	public double After { get; init; }

	/// <summary>문단 줄 간격 (s): 'm' 배수, 'e' 정확히, 'a' 최소, '\0' 없음 (문자 전체 값).</summary>
	public char SpacingKind { get; init; }

	/// <summary>줄 간격 값 ('m'은 배수, 'e'·'a'는 도면 단위).</summary>
	public double Spacing { get; init; }

	public char TabKind(int i) => i < this.TabKinds.Length ? this.TabKinds[i] : 'l';

	/// <summary>\px 형식 (들여쓰기·탭 값이 문자 높이 배수). 아니면 도면 단위.</summary>
	public bool Relative { get; init; }

	/// <summary>문자 높이 배수 값으로 (도면 단위 값이면 높이로 나눔). 앞뒤 간격은 원래 높이 배수.</summary>
	public MTextParagraph ToRelative(double height)
	{
		if (this.Relative || height <= 0)
		{
			return this with { Relative = true };
		}
		double f = 1 / height;
		return new MTextParagraph(this.FirstIndent * f, this.LeftIndent * f, this.RightIndent * f, this.Tabs.Select(t => t * f).ToArray(), this.Align)
		{
			TabKinds = this.TabKinds,
			DecimalChar = this.DecimalChar,
			Before = this.Before,
			After = this.After,
			SpacingKind = this.SpacingKind,
			Spacing = this.Spacing,
			Relative = true,
		};
	}
}

public enum MTextTokenKind { Char, Tab, Paragraph, Stack }

/// <summary>MTEXT의 한 단위: 글자 하나, 탭, 문단 끝, 분수(쌓기). Src = 원래 문자열에서의 위치 [SrcStart, SrcEnd).</summary>
public sealed record MTextToken(MTextTokenKind Kind, char Char, MTextFormat Format, MTextParagraph Paragraph, int SrcStart, int SrcEnd)
{
	public string? StackTop { get; init; }

	public string? StackBottom { get; init; }

	/// <summary>'/' 가로선 분수, '#' 사선 분수, '^' 공차(선 없음).</summary>
	public char StackType { get; init; }
}

/// <summary>
/// MTEXT 서식 문자열(\P 문단, \f 글꼴, \H 높이, \W 폭, \p 들여쓰기·탭, \L 밑줄, \S 분수, {} 묶음 등)을
/// 글자 단위 목록으로 푼다. 단일 행 문자(TEXT)는 isMText=false로 %%코드만 처리한다.
/// 문자 바로 편집에서 원래 서식을 지키려고 글자마다 원래 위치를 기억한다.
/// </summary>
public static class MTextTokenizer
{
	private static readonly Encoding? Cp949 = GetCp949();

	public static List<MTextToken> Tokenize(string? value, bool isMText = true)
	{
		var tokens = new List<MTextToken>();
		if (string.IsNullOrEmpty(value))
		{
			return tokens;
		}
		var fmt = MTextFormat.Default;
		var para = MTextParagraph.Default;
		var stack = new Stack<MTextFormat>();
		int i = 0;
		void Add(MTextTokenKind kind, char ch, int start, int end) => tokens.Add(new MTextToken(kind, ch, fmt, para, start, end));

		while (i < value.Length)
		{
			char c = value[i];
			int start = i;

			// %%c 지름, %%d 도, %%p 플러스마이너스, %%u 밑줄, %%o 윗줄, %%nnn 문자 번호
			if (c == '%' && i + 2 < value.Length && value[i + 1] == '%')
			{
				char k = char.ToLowerInvariant(value[i + 2]);
				switch (k)
				{
					case 'c': Add(MTextTokenKind.Char, 'Ø', start, i + 3); i += 3; continue;
					case 'd': Add(MTextTokenKind.Char, '°', start, i + 3); i += 3; continue;
					case 'p': Add(MTextTokenKind.Char, '±', start, i + 3); i += 3; continue;
					case '%': Add(MTextTokenKind.Char, '%', start, i + 3); i += 3; continue;
					case 'u': fmt = fmt with { Underline = !fmt.Underline }; i += 3; continue;
					case 'o': fmt = fmt with { Overline = !fmt.Overline }; i += 3; continue;
				}
				if (char.IsDigit(k))
				{
					int j = i + 2, code = 0;
					while (j < value.Length && j < i + 5 && char.IsDigit(value[j]))
					{
						code = code * 10 + (value[j] - '0');
						j++;
					}
					Add(MTextTokenKind.Char, (char)code, start, j);
					i = j;
					continue;
				}
			}

			if (!isMText)
			{
				// 한 줄 문자도 \U+XXXX (옛 버전 DWG에 코드 페이지 밖 글자를 이렇게 저장)
				if (c == '\\' && i + 6 < value.Length && (value[i + 1] == 'U' || value[i + 1] == 'u') && value[i + 2] == '+' && TryHex(value, i + 3, 4, out int ucp))
				{
					Add(MTextTokenKind.Char, (char)ucp, start, i + 7);
					i += 7;
					continue;
				}
				Add(c == '\t' ? MTextTokenKind.Tab : MTextTokenKind.Char, c, start, i + 1);
				i++;
				continue;
			}

			if (c == '\\' && i + 1 < value.Length)
			{
				char code = value[i + 1];
				switch (code)
				{
					case 'P':
					case 'X': // 치수 문자의 줄 구분
					case 'N': // 단 나누기 → 문단으로
						Add(MTextTokenKind.Paragraph, '\n', start, i + 2);
						i += 2;
						continue;
					case '~':
						Add(MTextTokenKind.Char, '\u00A0', start, i + 2);
						i += 2;
						continue;
					case '\\':
					case '{':
					case '}':
						Add(MTextTokenKind.Char, code, start, i + 2);
						i += 2;
						continue;
					case 'U':
					case 'u':
						if (i + 2 < value.Length && value[i + 2] == '+' && TryHex(value, i + 3, 4, out int cp))
						{
							Add(MTextTokenKind.Char, (char)cp, start, i + 7);
							i += 7;
							continue;
						}
						break;
					case 'M':
					case 'm':
						// \M+nXXXX: n=3(완성형 한글)은 CP949 2바이트
						if (i + 3 < value.Length && value[i + 2] == '+' && TryHex(value, i + 4, 4, out int mb))
						{
							string s = DecodeMbcs(value[i + 3], mb);
							Add(MTextTokenKind.Char, s.Length > 0 ? s[0] : '?', start, i + 8);
							i += 8;
							continue;
						}
						break;
					case 'L': fmt = fmt with { Underline = true }; i += 2; continue;
					case 'l': fmt = fmt with { Underline = false }; i += 2; continue;
					case 'O': fmt = fmt with { Overline = true }; i += 2; continue;
					case 'o': fmt = fmt with { Overline = false }; i += 2; continue;
					case 'K': fmt = fmt with { Strike = true }; i += 2; continue;
					case 'k': fmt = fmt with { Strike = false }; i += 2; continue;
					case 'S':
						{
							int end = FindSemicolon(value, i + 2);
							string st = value.Substring(i + 2, end - i - 2);
							int sep = st.IndexOfAny(new[] { '^', '/', '#' });
							var tok = new MTextToken(MTextTokenKind.Stack, '/', fmt, para, start, Math.Min(end + 1, value.Length))
							{
								StackTop = Unescape(sep < 0 ? st : st[..sep]),
								StackBottom = sep < 0 ? string.Empty : Unescape(st[(sep + 1)..]),
								StackType = sep < 0 ? '^' : st[sep],
							};
							tokens.Add(tok);
							i = Math.Min(end + 1, value.Length);
							continue;
						}
					case 'f':
					case 'F':
					case 'H':
					case 'h':
					case 'C':
					case 'c':
					case 'T':
					case 't':
					case 'Q':
					case 'q':
					case 'W':
					case 'w':
					case 'A':
					case 'a':
					case 'p':
						{
							int end = FindSemicolon(value, i + 2);
							string arg = value.Substring(i + 2, end - i - 2);
							if (code == 'p')
							{
								para = ParseParagraph(arg, para);
							}
							else
							{
								fmt = ApplyCode(fmt, code, arg);
							}
							i = Math.Min(end + 1, value.Length);
							continue;
						}
				}
				// 알 수 없는 코드는 백슬래시만 버린다
				i += 1;
				continue;
			}

			switch (c)
			{
				case '{':
					stack.Push(fmt);
					i++;
					continue;
				case '}':
					if (stack.Count > 0)
					{
						fmt = stack.Pop();
					}
					i++;
					continue;
				case '\r':
					i++;
					continue;
				case '\t':
					Add(MTextTokenKind.Tab, '\t', start, i + 1);
					i++;
					continue;
				case '\n':
					Add(MTextTokenKind.Paragraph, '\n', start, i + 1);
					i++;
					continue;
				case '^' when i + 1 < value.Length && value[i + 1] == 'I':
					Add(MTextTokenKind.Tab, '\t', start, i + 2);
					i += 2;
					continue;
				case '^' when i + 1 < value.Length && value[i + 1] == 'J':
					Add(MTextTokenKind.Paragraph, '\n', start, i + 2);
					i += 2;
					continue;
			}

			Add(MTextTokenKind.Char, c, start, i + 1);
			i++;
		}
		return tokens;
	}

	private static int FindSemicolon(string value, int from)
	{
		int end = value.IndexOf(';', from);
		return end < 0 ? value.Length : end;
	}

	private static string Unescape(string s) => s.Replace("\\\\", "\\").Replace("\\;", ";").Trim();

	private static double Num(string s, double fallback) =>
		double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

	private static MTextFormat ApplyCode(MTextFormat fmt, char code, string arg)
	{
		switch (code)
		{
			case 'f':
			case 'F':
				{
					// \fArial|b1|i0|c0|p34;  \Fromans.shx|c0;
					string[] parts = arg.Split('|');
					string name = parts[0].Trim();
					bool bold = parts.Any(p => p == "b1");
					bool italic = parts.Any(p => p == "i1");
					if (code == 'F' && !name.Contains('.'))
					{
						name += ".shx";
					}
					return fmt with { Font = name.Length > 0 ? name : null, Bold = bold, Italic = italic };
				}
			case 'H':
			case 'h':
				if (arg.EndsWith('x') || arg.EndsWith('X'))
				{
					double k = Num(arg[..^1], 1);
					return k > 0 ? fmt with { HeightScale = fmt.HeightScale * k, Height = fmt.Height * k } : fmt;
				}
				else
				{
					double hAbs = Num(arg, 0);
					return hAbs > 0 ? fmt with { Height = hAbs, HeightScale = 1 } : fmt;
				}
			case 'W':
			case 'w':
				{
					double w = Num(arg.TrimEnd('x', 'X'), 1);
					return w > 0 ? fmt with { Width = w } : fmt;
				}
			case 'T':
			case 't':
				{
					double t = Num(arg.TrimEnd('x', 'X'), 1);
					return t > 0 ? fmt with { Tracking = t } : fmt;
				}
			case 'Q':
			case 'q':
				return fmt with { ObliqueDeg = Num(arg, 0) };
			case 'C':
				{
					int aci = (int)Num(arg, 256);
					if (aci is >= 1 and <= 255)
					{
						var col = new ACadSharp.Color((short)aci);
						return fmt with { Color = aci == 7 ? Rgb.White : new Rgb(col.R, col.G, col.B) };
					}
					return fmt with { Color = null };
				}
			case 'c':
				{
					int v = (int)Num(arg, -1);
					if (v >= 0)
					{
						// \c는 BGR 순서로 저장된 24비트 값
						return fmt with { Color = new Rgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF)) };
					}
					return fmt;
				}
		}
		return fmt;
	}

	/// <summary>
	/// \pxi-6.4,l6.4,t6.4,150.4,qc; 같은 문단 서식. 기존 CAD 단락 대화상자는 a(뒤)·b(앞)·i·l·r·q·s(줄 간격 m/e/a)·t(탭, 앞에 c 가운데·r 오른쪽·D. 소수점) 순서로 쓴다.
	/// t 다음 항목은 모두 탭 (r8은 오른쪽 탭 8).
	/// </summary>
	private static MTextParagraph ParseParagraph(string arg, MTextParagraph cur)
	{
		bool relative = arg.StartsWith('x');
		string a = relative ? arg[1..] : arg;
		double first = cur.FirstIndent, left = cur.LeftIndent, right = cur.RightIndent;
		double before = cur.Before, after = cur.After, spacing = cur.Spacing;
		char spacingKind = cur.SpacingKind, decimalChar = cur.DecimalChar;
		var tabs = new List<(double Pos, char Kind)>();
		for (int i = 0; i < cur.Tabs.Length; i++)
		{
			tabs.Add((cur.Tabs[i], cur.TabKind(i)));
		}
		char align = cur.Align;
		bool inTabs = false;
		foreach (string raw in a.Split(','))
		{
			string p = raw.Trim();
			if (p.Length == 0)
			{
				continue;
			}
			char k = p[0];
			if (inTabs || char.IsDigit(k) || k == '-' || k == '.')
			{
				if (!inTabs)
				{
					continue;
				}
				// 탭 항목: [c|r|D구분자|d]위치
				char kind = 'l';
				string pos = p;
				if (k == 't')
				{
					pos = p[1..];
					if (pos.Length == 0) continue;
					k = pos[0];
				}
				if (k is 'c' or 'r' or 'd')
				{
					kind = k;
					pos = pos[1..];
				}
				else if (k == 'D')
				{
					kind = 'd';
					pos = pos[1..];
					if (pos.Length > 0 && !char.IsDigit(pos[0]) && pos[0] != '-')
					{
						decimalChar = pos[0];
						pos = pos[1..];
					}
				}
				tabs.Add((Num(pos, 0), kind));
				continue;
			}
			string rest = p[1..];
			switch (k)
			{
				case 'i': first = Num(rest, 0); break;
				case 'l': left = Num(rest, 0); break;
				case 'r': right = Num(rest, 0); break;
				case 'a': after = Num(rest, 0); break;
				case 'b': before = Num(rest, 0); break;
				case 's':
					if (rest.Length > 0 && rest[0] is 'm' or 'e' or 'a')
					{
						spacingKind = rest[0];
						spacing = Num(rest[1..], 0);
					}
					else if (rest == "*")
					{
						spacingKind = '\0';
						spacing = 0;
					}
					break;
				case 't':
					tabs.Clear();
					inTabs = true;
					if (rest.Length > 0)
					{
						// "t4" 또는 "tc4" — 같은 항목에 첫 탭
						string first0 = rest;
						char kind = 'l';
						if (first0[0] is 'c' or 'r' or 'd') { kind = first0[0]; first0 = first0[1..]; }
						else if (first0[0] == 'D') { kind = 'd'; first0 = first0[1..]; if (first0.Length > 0 && !char.IsDigit(first0[0])) { decimalChar = first0[0]; first0 = first0[1..]; } }
						tabs.Add((Num(first0, 0), kind));
					}
					break;
				case 'q':
					align = rest.Length > 0 ? char.ToLowerInvariant(rest[0]) : '\0';
					if (align == '*') align = '\0';
					break;
			}
		}
		var sorted = tabs.Where(t => t.Pos > 0).OrderBy(t => t.Pos).ToList();
		return new MTextParagraph(first, left, right, sorted.Select(t => t.Pos).ToArray(), align)
		{
			TabKinds = sorted.Select(t => t.Kind).ToArray(),
			DecimalChar = decimalChar,
			Before = before,
			After = after,
			SpacingKind = spacingKind,
			Spacing = spacing,
			Relative = relative,
		};
	}

	/// <summary>문단 서식을 기존 CAD 단락 대화상자와 같은 \px...; 코드로 (0인 들여쓰기·간격은 뺌, 기본 문단이면 빈 문자열).</summary>
	public static string FormatParagraph(MTextParagraph p, bool explicitZeros = false)
	{
		static string F(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
		var parts = new List<string>();
		if (p.After != 0) parts.Add("a" + F(p.After));
		if (p.Before != 0) parts.Add("b" + F(p.Before));
		if (p.FirstIndent != 0 || explicitZeros) parts.Add("i" + F(p.FirstIndent));
		if (p.LeftIndent != 0 || explicitZeros) parts.Add("l" + F(p.LeftIndent));
		if (p.RightIndent != 0 || explicitZeros) parts.Add("r" + F(p.RightIndent));
		if (p.Align is 'l' or 'c' or 'r' or 'j' or 'd') parts.Add("q" + p.Align);
		if (p.SpacingKind is 'm' or 'e' or 'a')
		{
			parts.Add("s" + p.SpacingKind + F(p.Spacing));
			parts.Add(string.Empty); // 기존 CAD: 줄 간격 다음 빈 항목 ("sm1.5,,t4")
		}
		if (p.Tabs.Length > 0)
		{
			var tabs = new List<string>();
			for (int i = 0; i < p.Tabs.Length; i++)
			{
				string prefix = p.TabKind(i) switch { 'c' => "c", 'r' => "r", 'd' => "D" + p.DecimalChar, _ => string.Empty };
				tabs.Add(prefix + F(p.Tabs[i]));
			}
			parts.Add("t" + string.Join(",", tabs));
		}
		else if (explicitZeros)
		{
			parts.Add("t0");
		}
		if (parts.Count == 0)
		{
			return string.Empty;
		}
		if (parts[^1].Length == 0)
		{
			parts.RemoveAt(parts.Count - 1);
		}
		return "\\px" + string.Join(",", parts) + ";";
	}

	/// <summary>보이는 글자 위치 plain이 든 문단의 서식 (문단 앞 \p 코드 바로 뒤에 표시 글자를 넣어 그 글자의 문단 서식을 본다).</summary>
	public static MTextParagraph ParagraphAt(string raw, int plain)
	{
		var starts = ParagraphStarts(raw);
		int k = 0;
		while (k + 1 < starts.Count && starts[k + 1].Plain <= plain)
		{
			k++;
		}
		int at = starts[k].Raw;
		while (at + 1 < raw.Length && raw[at] == '\\' && raw[at + 1] == 'p')
		{
			int semi = raw.IndexOf(';', at + 2);
			if (semi < 0) break;
			at = semi + 1;
		}
		const char mark = '\u0001';
		var tok = Tokenize(raw.Insert(at, mark.ToString())).FirstOrDefault(t => t.Kind == MTextTokenKind.Char && t.Char == mark);
		return tok?.Paragraph ?? MTextParagraph.Default;
	}

	/// <summary>
	/// 단락 대화상자: 보이는 글 [start, end)에 걸친 문단마다 문단 앞의 \p 코드를 새 코드로 바꾼다.
	/// 바꾼 문단 다음 문단이 자기 코드 없이 앞 문단 서식을 이어 받던 경우에는 원래 서식을 적어 두어 그대로 보이게 한다.
	/// </summary>
	public static string SetParagraph(string raw, int start, int end, MTextParagraph p, char defaultAlign, double height)
	{
		var starts = ParagraphStarts(raw);
		p = p.ToRelative(height);
		// 원래 문단별 서식 (뒤 문단 지키기용, \px 높이 배수 값으로)
		var orig = starts.Select(st => ParagraphAt(raw, st.Plain).ToRelative(height)).ToList();
		var sb = new StringBuilder(raw);
		int lastTouched = -1;
		var touched = new bool[starts.Count];
		for (int k = 0; k < starts.Count; k++)
		{
			int pStart = starts[k].Plain;
			int pEnd = k + 1 < starts.Count ? starts[k + 1].Plain - 1 : int.MaxValue;
			touched[k] = start == end ? (start >= pStart && start <= pEnd) : (start <= pEnd && end > pStart);
			if (touched[k]) lastTouched = k;
		}
		// 앞 문단 서식을 이어 받으므로, 0 값은 이어 받은 값이 0이 아닐 때만 적는다 (기존 CAD처럼 짧게)
		string CodeFor(MTextParagraph want, MTextParagraph inherited)
		{
			string shortCode = FormatParagraph(want);
			var parsed = shortCode.Length == 0 ? inherited : ParseParagraph(shortCode[2..^1], inherited);
			return parsed.Relative && FormatParagraph(parsed, explicitZeros: true) == FormatParagraph(want, explicitZeros: true) ? shortCode : FormatParagraph(want, explicitZeros: true);
		}
		for (int k = starts.Count - 1; k >= 0; k--)
		{
			int at = starts[k].Raw;
			bool restoreNext = k == lastTouched + 1 && !touched[k] && !HasParagraphCode(sb.ToString(), at);
			if (!touched[k] && !restoreNext)
			{
				continue;
			}
			// 이 문단이 이어 받는 서식 = 앞 문단의 (새) 서식
			var inherited = k == 0 ? MTextParagraph.Default : touched[k - 1] ? p : orig[k - 1];
			if (restoreNext)
			{
				var o = orig[k];
				if (o.Align == '\0' && p.Align != '\0') o = o with { Align = defaultAlign };
				sb.Insert(at, CodeFor(o, inherited));
				continue;
			}
			RemoveParagraphCodes(sb, at);
			sb.Insert(at, CodeFor(p, inherited));
		}
		return sb.ToString();
	}

	private static bool HasParagraphCode(string raw, int at) => at + 1 < raw.Length && raw[at] == '\\' && raw[at + 1] == 'p';

	private static void RemoveParagraphCodes(StringBuilder sb, int at)
	{
		while (at + 1 < sb.Length && sb[at] == '\\' && sb[at + 1] == 'p')
		{
			int semi = -1;
			for (int j = at + 2; j < sb.Length; j++)
			{
				if (sb[j] == ';') { semi = j; break; }
			}
			if (semi < 0) break;
			sb.Remove(at, semi - at + 1);
		}
	}

	// ===== 문자 바로 편집용: 보이는 글자 ↔ 원래 서식 문자열 =====

	/// <summary>보이는 글자만 (문단 = 줄 바꿈, 탭 = \t, 분수 = 위/아래). 편집 창에 보여 줄 글.</summary>
	public static string PlainText(string? value, bool isMText = true) => string.Concat(Tokenize(value, isMText).Select(PlainOf));

	/// <summary>\uD1A0\uD070 \uD558\uB098\uAC00 \uD3B8\uC9D1 \uCC3D\uC5D0\uC11C \uCC28\uC9C0\uD558\uB294 \uAE00\uC790 (\uBB38\uB2E8 = \uC904 \uBC14\uAFC8, \uD0ED = \t, \uBD84\uC218 = \uC704/\uC544\uB798).</summary>
	public static string PlainOf(MTextToken t) => t.Kind switch
	{
		MTextTokenKind.Paragraph => "\n",
		MTextTokenKind.Tab => "\t",
		MTextTokenKind.Stack => $"{t.StackTop}{(t.StackType == '^' ? "^" : t.StackType.ToString())}{t.StackBottom}",
		_ => t.Char == '\u00A0' ? " " : t.Char.ToString(),
	};

	private static IEnumerable<(string Text, int Start, int End)> PlainUnits(List<MTextToken> tokens) =>
		tokens.Select(t => (PlainOf(t), t.SrcStart, t.SrcEnd));

	/// <summary>
	/// 편집한 글(보이는 글자)을 원래 서식 문자열에 되돌려 넣는다: 앞뒤로 같은 부분은 그대로 두고
	/// 바뀐 가운데 부분만 원래 문자열의 그 자리에서 바꾼다 → 글꼴·들여쓰기·밑줄 같은 서식이 유지된다.
	/// </summary>
	public static string ReplacePlain(string original, string edited, bool isMText = true)
	{
		var units = PlainUnits(Tokenize(original, isMText)).ToList();
		// 보이는 글자 단위(분수 등은 여러 글자일 수 있음)를 글자 하나씩으로 펼침
		var chars = new List<(char Ch, int Start, int End)>();
		foreach (var u in units)
		{
			foreach (char ch in u.Text)
			{
				chars.Add((ch, u.Start, u.End));
			}
		}
		string plain = new string(chars.Select(c => c.Ch).ToArray());
		edited = edited.Replace("\r\n", "\n");
		if (plain == edited)
		{
			return original;
		}
		int pre = 0;
		while (pre < plain.Length && pre < edited.Length && plain[pre] == edited[pre])
		{
			pre++;
		}
		int suf = 0;
		while (suf < plain.Length - pre && suf < edited.Length - pre && plain[plain.Length - 1 - suf] == edited[edited.Length - 1 - suf])
		{
			suf++;
		}
		// 분수 같은 여러 글자 단위의 가운데에서 잘리지 않도록 단위 경계로 넓힌다
		while (pre > 0 && pre < chars.Count && chars[pre].Start == chars[pre - 1].Start)
		{
			pre--;
		}
		while (suf > 0 && plain.Length - suf > 0 && plain.Length - suf < chars.Count && chars[plain.Length - suf].Start == chars[plain.Length - suf - 1].Start)
		{
			suf--;
		}
		int removeFrom = plain.Length - suf; // 지울 보이는 글자 [pre, removeFrom)
		string middle = edited.Substring(pre, edited.Length - suf - pre);
		middle = isMText ? Escape(middle) : middle.Replace('\n', ' '); // 단일 행 문자는 줄 바꿈 없음

		int srcStart, srcEnd;
		if (pre < removeFrom)
		{
			srcStart = chars[pre].Start;
			srcEnd = chars[removeFrom - 1].End;
		}
		else if (pre < chars.Count)
		{
			srcStart = srcEnd = chars[pre].Start; // 끼워 넣기: 다음 글자 앞
		}
		else if (chars.Count > 0)
		{
			srcStart = srcEnd = chars[^1].End; // 끝에 덧붙이기: 마지막 글자 바로 뒤 (닫는 괄호 안쪽)
		}
		else
		{
			return isMText ? Escape(edited) : edited.Replace('\n', ' ');
		}
		return original[..srcStart] + middle + original[srcEnd..];
	}

	/// <summary>보이는 글자 [start, end)가 원래 문자열에서 차지하는 자리 [SrcStart, SrcEnd). 글자가 없으면 null.</summary>
	public static (int SrcStart, int SrcEnd)? RawRange(string raw, int start, int end, bool isMText = true)
	{
		var chars = new List<(int Start, int End)>();
		foreach (var t in Tokenize(raw, isMText))
		{
			foreach (char _ in PlainOf(t))
			{
				chars.Add((t.SrcStart, t.SrcEnd));
			}
		}
		start = Math.Clamp(start, 0, chars.Count);
		end = Math.Clamp(end, start, chars.Count);
		if (start >= end)
		{
			return null;
		}
		return (chars[start].Start, chars[end - 1].End);
	}

	/// <summary>문단마다 (보이는 글 시작 위치, 원래 문자열 시작 위치). 첫 문단은 (0, 0).</summary>
	public static List<(int Plain, int Raw)> ParagraphStarts(string raw)
	{
		var list = new List<(int, int)> { (0, 0) };
		int i = 0;
		foreach (var t in Tokenize(raw))
		{
			i += PlainOf(t).Length;
			if (t.Kind == MTextTokenKind.Paragraph)
			{
				list.Add((i, t.SrcEnd));
			}
		}
		return list;
	}

	/// <summary>
	/// 문단 정렬 (\pq): 보이는 글 [start, end)에 걸친 문단마다 앞에 \pq{a}; 를 둔다 (있던 \pq?; 는 바꾼다).
	/// a = l 왼쪽, c 가운데, r 오른쪽, j 양쪽, d 균등.
	/// </summary>
	public static string SetParagraphAlign(string raw, int start, int end, char a)
	{
		var starts = ParagraphStarts(raw);
		var sb = new StringBuilder(raw);
		for (int k = starts.Count - 1; k >= 0; k--)
		{
			int pStart = starts[k].Plain;
			int pEnd = k + 1 < starts.Count ? starts[k + 1].Plain : int.MaxValue;
			bool touches = start == end ? (start >= pStart && start <= pEnd) : (start < pEnd && end > pStart);
			if (!touches)
			{
				continue;
			}
			int at = starts[k].Raw;
			string code = $"\\pq{a};";
			if (at + 5 <= sb.Length && sb.ToString(at, 3) == "\\pq" && sb[at + 4] == ';')
			{
				sb.Remove(at, 5);
			}
			sb.Insert(at, code);
		}
		return sb.ToString();
	}

	/// <summary>보이는 글자 [start, end)의 서식 (문자 편집기 탭의 굵게·기울임 등 켜짐 상태 판단용).</summary>
	public static List<MTextFormat> FormatsOf(string raw, int start, int end)
	{
		var list = new List<MTextFormat>();
		int i = 0;
		foreach (var t in Tokenize(raw))
		{
			foreach (char _ in PlainOf(t))
			{
				if (i >= start && i < end) list.Add(t.Format);
				i++;
			}
		}
		return list;
	}

	/// <summary>
	/// 보이는 글자 [start, end)에 서식 코드를 입힌다: 원래 문자열에서 그 글자들만 {코드…}로 감싼다.
	/// 기존 묶음({ })의 경계를 넘으면 묶음마다 따로 감싸 중괄호 짝이 어긋나지 않게 한다. code(글자 서식) = 넣을 코드.
	/// </summary>
	public static string ApplyFormat(string raw, int start, int end, Func<MTextFormat, string> code)
	{
		var chars = new List<(int Start, int End, MTextFormat Format)>();
		foreach (var t in Tokenize(raw))
		{
			foreach (char _ in PlainOf(t))
			{
				chars.Add((t.SrcStart, t.SrcEnd, t.Format));
			}
		}
		start = Math.Clamp(start, 0, chars.Count);
		end = Math.Clamp(end, start, chars.Count);
		if (start >= end)
		{
			return raw;
		}
		// 이어진 글자 묶음: 사이에 중괄호가 없고 서식 코드가 같은 글자들
		var runs = new List<(int SrcStart, int SrcEnd, string Code)>();
		int k = start;
		while (k < end)
		{
			int a = chars[k].Start, b = chars[k].End;
			string c = code(chars[k].Format);
			int m = k + 1;
			while (m < end && chars[m].Start >= b)
			{
				string between = raw[b..chars[m].Start];
				if (between.Contains('{') || between.Contains('}') || code(chars[m].Format) != c)
				{
					break;
				}
				b = chars[m].End;
				m++;
			}
			if (runs.Count == 0 || runs[^1].SrcEnd <= a)
			{
				runs.Add((a, b, c));
			}
			k = m;
		}
		var sb = new StringBuilder(raw);
		for (int r = runs.Count - 1; r >= 0; r--)
		{
			sb.Insert(runs[r].SrcEnd, "}");
			sb.Insert(runs[r].SrcStart, "{" + runs[r].Code);
		}
		return sb.ToString();
	}

	/// <summary>보이는 글 → MTEXT 문자열 (줄 바꿈 = \P, 역슬래시·중괄호는 이스케이프).</summary>
	public static string Escape(string text) =>
		text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\r\n", "\n").Replace("\n", "\\P");

	private static bool TryHex(string s, int start, int len, out int result)
	{
		result = 0;
		if (start + len > s.Length)
		{
			return false;
		}
		for (int k = start; k < start + len; k++)
		{
			if (!Uri.IsHexDigit(s[k]))
			{
				return false;
			}
			result = result * 16 + Uri.FromHex(s[k]);
		}
		return true;
	}

	private static string DecodeMbcs(char codePageDigit, int twoBytes)
	{
		if (codePageDigit == '3' && Cp949 != null)
		{
			return Cp949.GetString(new[] { (byte)(twoBytes >> 8), (byte)(twoBytes & 0xFF) });
		}
		return "?";
	}

	private static Encoding? GetCp949()
	{
		try
		{
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
			return Encoding.GetEncoding(949);
		}
		catch (Exception)
		{
			return null;
		}
	}
}
