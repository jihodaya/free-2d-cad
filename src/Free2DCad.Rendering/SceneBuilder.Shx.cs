using System.Text;
using ACadSharp.Tables;
using SkiaSharp;

namespace Free2DCad.Rendering;

/// <summary>
/// SHX 글꼴 문자: 문자 스타일의 기본 글꼴(Filename)이나 큰 글꼴(BigFontFilename)이 .shx이면
/// 글자를 선으로 풀어 장면에 넣는다. 영문·숫자는 기본 SHX, 한글 등 2바이트 문자는 큰 글꼴(CP949 코드)에서 찾는다.
/// 글꼴 파일이 Fonts 폴더에 없으면 ① fontmap.txt 대응 ② 대체 글꼴(기본 romans.shx / 큰 글꼴 whgtxt.shx) 순으로 대신 쓴다.
/// 대체도 SHX라 글자 폭·배치가 원본 도면과 같다. SHX 어디에도 없는 글자만 TrueType 윤곽을 채운 도형으로 섞어 그린다.
/// 대체 글꼴을 TrueType으로 바꿨으면 false → 호출한 쪽이 TrueType 문자로 그린다.
/// </summary>
public sealed partial class SceneBuilder
{
	private static readonly Encoding Cp949 = CreateCp949();

	/// <summary>지금 SHX 글자 획을 넣는 중 (장면 선에 IsText 표시).</summary>
	private bool _inShxText;

	private static Encoding CreateCp949()
	{
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		return Encoding.GetEncoding(949);
	}

	/// <summary>글꼴 파일 이름으로 SHX 글꼴 찾기 (fontmap.txt의 shx→shx 대응 포함). 없으면 null.</summary>
	internal static ShxFont? FindShx(string? fileName)
	{
		string? name = NormalizeShxName(fileName);
		if (name == null)
		{
			return null;
		}
		string? path = UserFonts.Find(name);
		if (path == null && UserFonts.FontMap.TryGetValue(name, out string? mapped) && mapped.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
		{
			path = UserFonts.Find(mapped);
		}
		return path != null ? ShxFont.Load(path) : null;
	}

	/// <summary>
	/// 도면을 열 때 명령줄에 알릴 글꼴 대체 목록: "[대신 쓰는 글꼴] 글꼴을 [원래 글꼴] 글꼴 대신 사용."
	/// (문자 스타일의 글꼴 파일이 Fonts 폴더·Windows 글꼴에 없을 때)
	/// </summary>
	public static IReadOnlyList<string> FontSubstitutions(IEnumerable<TextStyle> styles)
	{
		var lines = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		void Check(string? file, bool big)
		{
			if (string.IsNullOrWhiteSpace(file))
			{
				return;
			}
			string name = Path.GetFileName(file.Trim());
			if (!seen.Add(name))
			{
				return;
			}
			if (NormalizeShxName(name) is { } shx)
			{
				if (UserFonts.Find(shx) != null)
				{
					return;
				}
				string? target = UserFonts.FontMap.TryGetValue(shx, out string? m) && UserFonts.Find(m) != null ? m
					: big ? UserFonts.AlternateBigFont : UserFonts.AlternateFont;
				lines.Add($"[{target}] 글꼴을 [{name}] 글꼴 대신 사용.");
			}
			else if (UserFonts.Find(Path.HasExtension(name) ? name : name + ".ttf") == null)
			{
				lines.Add($"[맑은 고딕] 글꼴을 [{name}] 글꼴 대신 사용.");
			}
		}
		foreach (TextStyle st in styles)
		{
			if (st.IsShapeFile)
			{
				continue; // 모양(shape) 파일 스타일은 문자 글꼴이 아님
			}
			Check(st.Filename, big: false);
			Check(st.BigFontFilename, big: true);
		}
		return lines;
	}

	/// <summary>fontmap.txt에서 이 SHX를 TrueType으로 바꾸라고 적었는지 (사용자가 일부러 고른 경우).</summary>
	private static bool MappedToTtf(string? shxName) =>
		NormalizeShxName(shxName) is { } n && UserFonts.FontMap.TryGetValue(n, out string? m) && !m.EndsWith(".shx", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// 문자 스타일의 SHX 글꼴을 실제로 쓸 글꼴로: 파일(또는 대응표의 SHX)이 있으면 그것, 대응표가 TTF를 가리키면 null(TTF로),
	/// 아니면 대체 SHX 글꼴. isShx=false면 스타일이 SHX를 쓰지 않는 것.
	/// </summary>
	private static ShxFont? ResolveShx(string? fileName, string alternate, out bool isShx)
	{
		isShx = NormalizeShxName(fileName) != null;
		if (!isShx)
		{
			return null;
		}
		if (FindShx(fileName) is { } font)
		{
			return font;
		}
		return MappedToTtf(fileName) ? null : FindShx(alternate);
	}

	private static string? NormalizeShxName(string? fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		string name = Path.GetFileName(fileName.Trim());
		if (!Path.HasExtension(name))
		{
			name += ".shx";
		}
		return name.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? name : null;
	}

	// ===== 대체 TrueType 글자 (SHX에 없는 글자를 채운 도형으로) =====

	/// <summary>대문자 높이 = 1로 맞춘 글자 윤곽(짝홀 채움 루프들)과 다음 글자까지 거리.</summary>
	private sealed record TtfGlyph(List<double[]> Loops, double Advance);

	private static readonly Dictionary<string, SKTypeface?> TtfFaces = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<(string Face, char Ch), TtfGlyph?> TtfCache = new();
	private static readonly object TtfGate = new();

	/// <summary>SHX 이름에 대응하는 대체 TrueType 파일 (fontmap.txt). 없으면 맑은 고딕.</summary>
	private static string FallbackTtfFor(string? shxName)
	{
		string? n = NormalizeShxName(shxName);
		if (n != null && UserFonts.FontMap.TryGetValue(n, out string? mapped) && !mapped.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
		{
			return mapped;
		}
		return UserFonts.AlternateFont.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? "malgun.ttf" : UserFonts.AlternateFont;
	}

	private static readonly string[] FontFileExtensions = { ".ttf", ".ttc", ".otf" };

	/// <summary>글꼴 파일 이름(Fonts 폴더·Windows 글꼴) 또는 설치된 패밀리 이름으로 글꼴을 연다. 없으면 맑은 고딕.</summary>
	private static SKTypeface? LoadFace(string name, bool bold, bool italic)
	{
		var style = new SKFontStyle(bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
			italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
		SKTypeface? face = null;
		try
		{
			if (FontFileExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
			{
				string? path = UserFonts.Find(name);
				face = path != null ? SKTypeface.FromFile(path) : null;
			}
			else if (name.Length > 0)
			{
				face = SKFontManager.Default.MatchFamily(name, style);
			}
		}
		catch (Exception)
		{
			face = null;
		}
		return face ?? SKFontManager.Default.MatchFamily("Malgun Gothic", style) ?? SKTypeface.FromFamilyName("Malgun Gothic");
	}

	private static TtfGlyph? GetTtfGlyph(string ttfFile, char ch)
	{
		lock (TtfGate)
		{
			if (TtfCache.TryGetValue((ttfFile, ch), out var cached))
			{
				return cached;
			}
			// 키: "파일.ttf" 또는 "패밀리 이름", 뒤에 |b(굵게) |i(기울임)가 붙을 수 있다 (MTEXT \fDotum|b1 등)
			string[] parts = ttfFile.Split('|');
			bool bold = parts.Contains("b"), italic = parts.Contains("i");
			if (!TtfFaces.TryGetValue(ttfFile, out SKTypeface? face))
			{
				face = LoadFace(parts[0], bold, italic);
				TtfFaces[ttfFile] = face;
			}
			TtfGlyph? result = null;
			if (face != null)
			{
				// 이 글꼴에 없는 글자(예: osifont의 한글)는 한글이 되는 글꼴로
				// 지름 기호 ∅(U+2205, 기존 CAD 치수 접두)가 없으면 같은 글꼴의 Ø로
				string drawn = ch.ToString();
				if (ch == '∅' && face.GetGlyph(ch) == 0 && face.GetGlyph('Ø') != 0)
				{
					drawn = "Ø";
				}
				else if (face.GetGlyph(ch) == 0)
				{
					face = SKFontManager.Default.MatchCharacter(ch) ?? face;
				}
				using var font = new SKFont(face, 100);
				// 굵게·기울임 글꼴 파일이 따로 없으면 흉내 낸다
				if (bold && !face.IsBold) font.Embolden = true;
				if (italic && !face.IsItalic) font.SkewX = -0.25f;
				float cap = font.Metrics.CapHeight > 0 ? font.Metrics.CapHeight : 70;
				using var path = font.GetTextPath(drawn, new SKPoint(0, 0));
				var loops = new List<double[]>();
				if (path != null && !path.IsEmpty)
				{
					using var measure = new SKPathMeasure(path, false, 1);
					do
					{
						float len = measure.Length;
						if (len <= 0) continue;
						int n = Math.Clamp((int)(len / 3.5f), 4, 300);
						var pts = new double[n * 2];
						for (int i = 0; i < n; i++)
						{
							measure.GetPosition(len * i / n, out SKPoint pt);
							pts[2 * i] = pt.X / cap;
							pts[2 * i + 1] = -pt.Y / cap; // 화면 좌표(아래 +) → 도면 좌표(위 +)
						}
						loops.Add(pts);
					}
					while (measure.NextContour());
				}
				result = new TtfGlyph(loops, font.MeasureText(drawn) / cap);
			}
			TtfCache[(ttfFile, ch)] = result;
			return result;
		}
	}

	// ===== 배치 =====

	private readonly record struct ShxChar(ShxFont Font, ShxFont.Glyph Glyph, double Scale);

	/// <summary>SHX에서 찾아볼 코드들 (특수 기호는 옛 SHX 코드 127°, 128±, 129Ø도 시도).</summary>
	private static IEnumerable<int> Candidates(char ch)
	{
		yield return ch;
		switch (ch)
		{
			case '°': yield return 127; break;
			case '±': yield return 128; break;
			case 'Ø' or '⌀' or 'ø' or '∅': yield return 129; yield return 0x2205; yield return 216; break;
		}
	}
}
