using System.IO.Compression;
using System.Reflection;

namespace Free2DCad.App;

/// <summary>
/// 문자 편집기 맞춤법 검사 (기존 CAD처럼 영어 단어 아래 빨간 물결선, 오른쪽 클릭에 추천 단어).
/// 사전: Hunspell en_US (SCOWL, Kevin Atkinson — 저작권·허가 문구는 Assets/Spelling/README_en_US.txt) 를
/// tools/spelldict/make_wordlist.py로 펼친 단어 목록 (앱에 포함, 설치·인터넷 필요 없음). 기존 CAD도 같은 en_US 사전을 쓴다.
/// 한글·숫자 등 영문이 아닌 글자는 검사하지 않는다.
/// </summary>
internal static class TextSpellChecker
{
	private static readonly Lazy<HashSet<string>?> Words = new(Load);
	private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>맞춤법 검사 켜짐 (기존 CAD 기본: 켜짐).</summary>
	public static bool Enabled { get; set; } = true;

	public static bool IsAvailable => Words.Value != null;

	private static HashSet<string>? Load()
	{
		try
		{
			var asm = Assembly.GetExecutingAssembly();
			string? name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("en_US.words.gz", StringComparison.OrdinalIgnoreCase));
			if (name == null)
			{
				return null;
			}
			using var s = asm.GetManifestResourceStream(name)!;
			using var gz = new GZipStream(s, CompressionMode.Decompress);
			using var reader = new StreamReader(gz, System.Text.Encoding.UTF8);
			var set = new HashSet<string>(StringComparer.Ordinal);
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				if (line.Length > 0) set.Add(line);
			}
			return set;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>Hunspell처럼: 그대로 있거나, 첫 글자만 대문자·모두 대문자면 소문자/첫 대문자 형태가 있으면 맞음.</summary>
	public static bool IsCorrect(string word)
	{
		if (Words.Value is not { } w || word.Length == 0 || Ignored.Contains(word))
		{
			return true;
		}
		if (w.Contains(word))
		{
			return true;
		}
		string lower = word.ToLowerInvariant();
		bool allCaps = word.All(c => !char.IsLetter(c) || char.IsUpper(c));
		bool initialCap = char.IsUpper(word[0]) && word.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c));
		if ((allCaps || initialCap) && w.Contains(lower))
		{
			return true;
		}
		if (allCaps && w.Contains(char.ToUpperInvariant(lower[0]) + lower[1..]))
		{
			return true; // ENGLISH → English
		}
		return false;
	}

	/// <summary>틀린 단어들 (보이는 글자 위치, 길이). 영문 글자와 단어 안의 아포스트로피만.</summary>
	public static List<(int Start, int Length)> Check(string text)
	{
		var result = new List<(int, int)>();
		if (!Enabled || string.IsNullOrEmpty(text) || Words.Value == null)
		{
			return result;
		}
		int i = 0;
		while (i < text.Length)
		{
			if (!char.IsAsciiLetter(text[i]))
			{
				i++;
				continue;
			}
			int start = i;
			while (i < text.Length && (char.IsAsciiLetter(text[i]) || (text[i] is '\'' or '’' && i + 1 < text.Length && char.IsAsciiLetter(text[i + 1]))))
			{
				i++;
			}
			// 숫자·한글이 바로 붙은 토막(예: M10, 3EA, 품번A)은 기호로 보고 넘긴다
			bool glued = (start > 0 && char.IsLetterOrDigit(text[start - 1])) || (i < text.Length && char.IsLetterOrDigit(text[i]));
			if (glued)
			{
				continue;
			}
			string word = text[start..i].Replace('’', '\'');
			if (!IsCorrect(word))
			{
				result.Add((start, i - start));
			}
		}
		return result;
	}

	/// <summary>추천 단어 (편집 거리 1, 없으면 2; 글자 쌍이 많이 같은 순 — 기존 CAD "wrld" → world, weld, wild).</summary>
	public static List<string> Suggest(string word, int count = 10)
	{
		if (Words.Value is not { } w || word.Length == 0)
		{
			return new List<string>();
		}
		string lower = word.ToLowerInvariant();
		var found = new HashSet<string>(Edits(lower).Where(w.Contains));
		if (found.Count == 0 && lower.Length <= 12)
		{
			foreach (string e1 in Edits(lower).Distinct())
			{
				foreach (string e2 in Edits(e1))
				{
					if (w.Contains(e2)) found.Add(e2);
				}
				if (found.Count > 200) break;
			}
		}
		found.Remove(lower);
		var grams = Bigrams(lower);
		var ranked = found
			.OrderByDescending(c => Bigrams(c).Count(grams.Contains))
			.ThenBy(c => Math.Abs(c.Length - lower.Length))
			.ThenBy(c => c, StringComparer.Ordinal)
			.Take(count)
			.ToList();
		// 원래 글자 모양 따르기 (첫 대문자 / 모두 대문자)
		bool allCaps = word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));
		bool initialCap = char.IsUpper(word[0]);
		return ranked.Select(c => allCaps ? c.ToUpperInvariant() : initialCap ? char.ToUpperInvariant(c[0]) + c[1..] : c).ToList();
	}

	private static HashSet<string> Bigrams(string s)
	{
		var set = new HashSet<string>();
		for (int i = 0; i + 1 < s.Length; i++) set.Add(s.Substring(i, 2));
		return set;
	}

	private const string Alphabet = "abcdefghijklmnopqrstuvwxyz'";

	private static IEnumerable<string> Edits(string w)
	{
		for (int i = 0; i <= w.Length; i++)
		{
			string a = w[..i], b = w[i..];
			if (b.Length > 0) yield return a + b[1..]; // 지우기
			if (b.Length > 1) yield return a + b[1] + b[0] + b[2..]; // 바꿔 놓기
			foreach (char c in Alphabet)
			{
				if (b.Length > 0 && c != b[0]) yield return a + c + b[1..]; // 바꾸기
				yield return a + c + b; // 넣기
			}
		}
	}

	/// <summary>이번 실행 동안 이 단어는 틀렸다고 하지 않음 (모두 무시).</summary>
	public static void Ignore(string word) => Ignored.Add(word);
}
