using System.Text;

namespace Free2DCad.Rendering;

/// <summary>
/// MTEXT 서식 문자열을 표시용 평문 줄로 변환한다. 글자별 서식(색, 높이, 글꼴)은 아직 무시한다.
/// </summary>
internal static class MTextParser
{
	private static readonly Encoding? Cp949 = GetCp949();

	public static string[] Parse(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return Array.Empty<string>();
		}

		var lines = new List<string>();
		var sb = new StringBuilder();
		int i = 0;
		while (i < value.Length)
		{
			char c = value[i];
			if (c == '\\' && i + 1 < value.Length)
			{
				char code = value[i + 1];
				switch (code)
				{
					case 'P':
					case 'X': // 치수 문자의 줄 구분
						lines.Add(sb.ToString());
						sb.Clear();
						i += 2;
						continue;
					case '~':
						sb.Append(' ');
						i += 2;
						continue;
					case '\\':
					case '{':
					case '}':
						sb.Append(code);
						i += 2;
						continue;
					case 'U':
					case 'u':
						// \U+XXXX
						if (i + 2 < value.Length && value[i + 2] == '+' && TryHex(value, i + 3, 4, out int cp))
						{
							// ∅(U+2205, 치수의 지름 기호)는 대부분의 TTF에 없으므로 Ø로 표시
							sb.Append(cp == 0x2205 ? 'Ø' : (char)cp);
							i += 7;
							continue;
						}
						break;
					case 'M':
					case 'm':
						// \M+nXXXX: n=3(완성형 한글)은 CP949 2바이트
						if (i + 3 < value.Length && value[i + 2] == '+' && TryHex(value, i + 4, 4, out int mb))
						{
							sb.Append(DecodeMbcs(value[i + 3], mb));
							i += 8;
							continue;
						}
						break;
					case 'S':
						{
							int end = value.IndexOf(';', i + 2);
							if (end < 0) end = value.Length;
							string stack = value.Substring(i + 2, end - i - 2);
							int sep = stack.IndexOfAny(new[] { '^', '/', '#' });
							sb.Append(sep < 0 ? stack : stack[..sep].Trim() + "/" + stack[(sep + 1)..].Trim());
							i = end + 1;
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
							int end = value.IndexOf(';', i + 2);
							i = end < 0 ? value.Length : end + 1;
							continue;
						}
					case 'L':
					case 'l':
					case 'O':
					case 'o':
					case 'K':
					case 'k':
					case 'N':
						i += 2;
						continue;
				}
				// 알 수 없는 코드는 백슬래시만 버린다
				i += 1;
				continue;
			}

			switch (c)
			{
				case '{':
				case '}':
				case '\r':
					i++;
					continue;
				case '\t':
					sb.Append("    ");
					i++;
					continue;
				case '\n':
					lines.Add(sb.ToString());
					sb.Clear();
					i++;
					continue;
				case '^' when i + 1 < value.Length && value[i + 1] == 'I':
					sb.Append("    ");
					i += 2;
					continue;
				case '^' when i + 1 < value.Length && value[i + 1] == 'J':
					lines.Add(sb.ToString());
					sb.Clear();
					i += 2;
					continue;
			}

			sb.Append(c);
			i++;
		}
		lines.Add(sb.ToString());

		return lines.Select(TextFormat.DecodeSpecialCodes).ToArray();
	}

	/// <summary>TEXT 엔티티 값: 제어 코드와 탭만 처리.</summary>
	public static string ParseSingleLine(string value) =>
		TextFormat.DecodeSpecialCodes(value ?? string.Empty).Replace("\t", "    ");

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
