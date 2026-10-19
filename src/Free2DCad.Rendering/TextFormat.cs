using System.Text;

namespace Free2DCad.Rendering;

internal static class TextFormat
{
	/// <summary>
	/// 기존 CAD 제어 코드 변환: %%c → Ø, %%d → °, %%p → ±, %%% → %, %%nnn → 문자 코드.
	/// %%u/%%o(밑줄/윗줄 토글)는 제거한다.
	/// </summary>
	public static string DecodeSpecialCodes(string value)
	{
		if (string.IsNullOrEmpty(value) || !value.Contains("%%"))
		{
			return value ?? string.Empty;
		}

		var sb = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '%' && i + 2 < value.Length && value[i + 1] == '%')
			{
				char c = char.ToLowerInvariant(value[i + 2]);
				switch (c)
				{
					case 'c': sb.Append('Ø'); i += 2; continue;
					case 'd': sb.Append('°'); i += 2; continue;
					case 'p': sb.Append('±'); i += 2; continue;
					case '%': sb.Append('%'); i += 2; continue;
					case 'u':
					case 'o':
						i += 2;
						continue;
				}
				if (char.IsDigit(c))
				{
					int j = i + 2, code = 0;
					while (j < value.Length && j < i + 5 && char.IsDigit(value[j]))
					{
						code = code * 10 + (value[j] - '0');
						j++;
					}
					sb.Append((char)code);
					i = j - 1;
					continue;
				}
			}
			sb.Append(value[i]);
		}
		return sb.ToString();
	}
}
