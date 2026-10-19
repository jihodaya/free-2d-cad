using SkiaSharp;

namespace Free2DCad.Rendering;

/// <summary>
/// 문자 스타일의 글꼴 파일 이름을 Skia 글꼴로 매핑한다.
/// 찾는 순서: ① 실행 파일 폴더의 Fonts (사용자 글꼴) ② 사용자별 설치 글꼴 ③ Windows 글꼴 폴더.
/// SHX는 아직 그리지 못하므로 Fonts\fontmap.txt에 적은 TTF(예: whgtxt.shx=NanumGothic.ttf)로, 없으면 기본 글꼴(맑은 고딕)로 대체한다.
/// </summary>
internal sealed class FontResolver : IDisposable
{
	private const string DefaultFamily = "Malgun Gothic";

	private readonly Dictionary<string, Face> _cache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Face _default;

	/// <param name="CapRatio">대문자 높이 / 글꼴 크기. CAD 문자 높이 → 글꼴 크기 환산에 사용.</param>
	public sealed record Face(SKTypeface Typeface, double CapRatio);

	public FontResolver()
	{
		this._default = Create(SKTypeface.FromFamilyName(DefaultFamily) ?? SKTypeface.Default);
	}

	/// <summary>글리프가 없는 글자(예: Arial의 한글)를 그릴 대체 글꼴.</summary>
	public Face Fallback => this._default;

	public Face Resolve(string fontFile)
	{
		if (string.IsNullOrWhiteSpace(fontFile))
		{
			return this._default;
		}
		if (this._cache.TryGetValue(fontFile, out Face? cached))
		{
			return cached;
		}

		string name = Path.GetFileName(fontFile.Trim());
		// SHX를 선 글꼴로 그리지 못할 때(대체 글꼴을 TrueType으로 바꾼 경우): 대응표의 TTF → 대체 글꼴
		if (name.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
		{
			if (UserFonts.FontMap.TryGetValue(name, out string? mapped) && !mapped.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
			{
				name = mapped;
			}
			else if (!UserFonts.AlternateFont.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
			{
				name = UserFonts.AlternateFont;
			}
		}
		// 확장자 없이 적힌 경우(예: "NanumGothic") TTF로 가정
		if (!Path.HasExtension(name))
		{
			name += ".ttf";
		}

		Face face = this._default;
		string? path = UserFonts.Find(name);
		if (path != null)
		{
			try
			{
				var tf = SKTypeface.FromFile(path);
				if (tf != null)
				{
					face = Create(tf);
				}
			}
			catch (Exception)
			{
				// 읽을 수 없는 글꼴 파일은 기본 글꼴로
			}
		}
		this._cache[fontFile] = face;
		return face;
	}

	private static Face Create(SKTypeface typeface)
	{
		using var font = new SKFont(typeface, 100);
		float cap = font.Metrics.CapHeight;
		double ratio = cap > 0 ? cap / 100.0 : 0.7;
		return new Face(typeface, ratio);
	}

	public void Dispose()
	{
		foreach (Face f in this._cache.Values.Distinct())
		{
			if (!ReferenceEquals(f, this._default)) f.Typeface.Dispose();
		}
		this._default.Typeface.Dispose();
	}
}

/// <summary>
/// 사용자 글꼴 폴더 (실행 파일 폴더\Fonts). 쓰려는 글꼴을 여기에 넣으면 설치하지 않아도 도면 문자에 쓰인다.
/// </summary>
public static class UserFonts
{
	/// <summary>실행 파일 옆 Fonts 폴더.</summary>
	public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Fonts");

	private static readonly string[] Extensions = { ".ttf", ".otf", ".ttc" };

	private static string[] SearchFolders => new[]
	{
		Folder,
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"),
		Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
	};

	/// <summary>
	/// 도면이 쓰는 SHX 글꼴이 Fonts 폴더에 없을 때 대신 쓸 글꼴 (.shx 또는 .ttf). 기본은 SHX라 글자 폭·배치가 원본과 같다.
	/// 옵션(OPTIONS)의 '대체 글꼴'에서 바꾼다.
	/// </summary>
	public static string AlternateFont { get; set; } = DefaultAlternateFont;

	/// <summary>한글 등 2바이트 글자에 쓸 대체 큰 글꼴 (도면의 큰 글꼴이 없거나 지정되지 않았을 때).</summary>
	public static string AlternateBigFont { get; set; } = DefaultAlternateBigFont;

	public const string DefaultAlternateFont = "romans.shx";
	public const string DefaultAlternateBigFont = "whgtxt.shx";

	private static readonly Lazy<Dictionary<string, string>> IndexLazy = new(BuildIndex);

	/// <summary>Fonts 폴더(하위 폴더 포함)의 파일 이름 → 경로. 위 폴더의 파일이 먼저.</summary>
	private static Dictionary<string, string> BuildIndex()
	{
		var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			if (Directory.Exists(Folder))
			{
				foreach (string f in Directory.EnumerateFiles(Folder))
				{
					index.TryAdd(Path.GetFileName(f), f);
				}
				foreach (string f in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
				{
					index.TryAdd(Path.GetFileName(f), f);
				}
			}
		}
		catch (Exception)
		{
			// 폴더 접근 실패는 빈 목록
		}
		return index;
	}

	/// <summary>SHX 글꼴 파일을 이름으로 읽는다 (없거나 읽을 수 없으면 null).</summary>
	public static ShxFont? LoadShx(string? fileName) =>
		!string.IsNullOrWhiteSpace(fileName) && Find(Path.GetFileName(fileName.Trim())) is { } p ? ShxFont.Load(p) : null;

	/// <summary>Fonts 폴더(하위 폴더 포함)의 SHX 글꼴 파일 이름들.</summary>
	public static IReadOnlyList<string> ShxList() =>
		IndexLazy.Value.Keys.Where(n => n.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

	/// <summary>글꼴 파일 경로 찾기 (사용자 폴더와 그 하위 폴더 우선). 없으면 null.</summary>
	public static string? Find(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		if (IndexLazy.Value.TryGetValue(fileName, out string? indexed))
		{
			return indexed;
		}
		foreach (string dir in SearchFolders.Skip(1))
		{
			try
			{
				string p = Path.Combine(dir, fileName);
				if (File.Exists(p))
				{
					return p;
				}
			}
			catch (Exception)
			{
				// 폴더 접근 실패는 건너뜀
			}
		}
		return null;
	}

	/// <summary>사용자 글꼴 폴더의 글꼴 파일 이름들 (문자 스타일 창 목록용).</summary>
	public static IReadOnlyList<string> List()
	{
		try
		{
			return Directory.Exists(Folder)
				? Directory.EnumerateFiles(Folder).Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Select(Path.GetFileName).OfType<string>().OrderBy(n => n).ToList()
				: Array.Empty<string>();
		}
		catch (Exception)
		{
			return Array.Empty<string>();
		}
	}

	/// <summary>글꼴 목록 한 항목: 표시 이름(패밀리), 파일 이름, 사용자 폴더 글꼴인지.</summary>
	public sealed record FontEntry(string Family, string File, bool IsUser)
	{
		public string Display => this.IsUser ? $"{this.Family} ({this.File}) ★" : $"{this.Family} ({this.File})";
	}

	private static readonly Lazy<IReadOnlyList<FontEntry>> CatalogLazy = new(BuildCatalog);

	/// <summary>쓸 수 있는 TrueType 글꼴 파일 목록 (사용자 폴더 → 사용자별 설치 → Windows). 파일 이름 기준 중복 제거.</summary>
	public static IReadOnlyList<FontEntry> Catalog => CatalogLazy.Value;

	private static IReadOnlyList<FontEntry> BuildCatalog()
	{
		var list = new List<FontEntry>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var folders = SearchFolders;
		for (int fi = 0; fi < folders.Length; fi++)
		{
			IEnumerable<string> files;
			try
			{
				files = Directory.Exists(folders[fi]) ? Directory.EnumerateFiles(folders[fi]).ToList() : Enumerable.Empty<string>();
			}
			catch (Exception)
			{
				continue;
			}
			foreach (string f in files)
			{
				string ext = Path.GetExtension(f).ToLowerInvariant();
				string file = Path.GetFileName(f);
				if (!Extensions.Contains(ext) || !seen.Add(file))
				{
					continue;
				}
				string family = Path.GetFileNameWithoutExtension(f);
				try
				{
					using var tf = SKTypeface.FromFile(f);
					if (tf != null && !string.IsNullOrWhiteSpace(tf.FamilyName) && LooksReadable(tf.FamilyName))
					{
						family = tf.FamilyName;
					}
				}
				catch (Exception)
				{
					// 패밀리 이름을 못 읽으면 파일 이름
				}
				list.Add(new FontEntry(family, file, fi == 0));
			}
		}
		return list.OrderByDescending(e => e.IsUser).ThenBy(e => e.Family, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>
	/// 한글 이름 글꼴 일부는 이름표가 옛 인코딩이라 글자가 깨져 나온다 → 영문/숫자/한글 음절/흔한 기호만이면 읽을 수 있다고 본다.
	/// </summary>
	private static bool LooksReadable(string name) =>
		name.All(ch => ch < 0x7F && !char.IsControl(ch) || (ch >= 0xAC00 && ch <= 0xD7A3));

	private static readonly Lazy<Dictionary<string, string>> Map = new(LoadMap);

	/// <summary>Fonts\fontmap.txt: "whgtxt.shx=NanumGothic.ttf" 형식의 SHX → TTF 대응표 (;로 시작하면 주석).</summary>
	public static IReadOnlyDictionary<string, string> FontMap => Map.Value;

	private static Dictionary<string, string> LoadMap()
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string p = Path.Combine(Folder, "fontmap.txt");
			if (File.Exists(p))
			{
				foreach (string raw in File.ReadAllLines(p))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
					{
						continue;
					}
					int eq = line.IndexOf('=');
					if (eq > 0)
					{
						map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
					}
				}
			}
		}
		catch (Exception)
		{
			// 대응표 읽기 실패는 무시
		}
		return map;
	}
}
