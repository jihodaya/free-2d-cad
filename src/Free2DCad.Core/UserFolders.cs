namespace Free2DCad.Core;

/// <summary>
/// 사용자 폴더 (실행 파일 옆). 표준 자료(글꼴·패턴·템플릿 등)를 넣으면 설치 없이 쓰이고, 프로그램 폴더째 복사하면 같이 옮겨진다.
/// Fonts: 글꼴 · Patterns: 해치 패턴(.pat) · Templates: 새 도면 템플릿(.dwg) · Linetypes: 선종류(.lin) · Blocks: 블록 라이브러리(.dwg)
/// </summary>
public static class UserFolders
{
	public static string Base { get; } = AppContext.BaseDirectory;

	public static string Fonts => Path.Combine(Base, "Fonts");

	public static string Patterns => Path.Combine(Base, "Patterns");

	public static string Templates => Path.Combine(Base, "Templates");

	public static string Linetypes => Path.Combine(Base, "Linetypes");

	public static string Blocks => Path.Combine(Base, "Blocks");

	/// <summary>(폴더, 설명) 목록 — 폴더가 없으면 만들고 사용법 README.txt를 넣는다.</summary>
	public static readonly (Func<string> Path, string Readme)[] All =
	{
		(() => Fonts, "이 폴더에 글꼴 파일(*.ttf, *.otf, *.ttc)을 넣으면 설치하지 않아도 도면 문자에 쓰이고, 문자 스타일 글꼴 목록 맨 위에 ★로 나옵니다.\r\nfontmap.txt에 \"whgtxt.shx=NanumGothic.ttf\"처럼 적으면 SHX 글꼴을 그 TTF로 표시합니다."),
		(() => Patterns, "이 폴더에 해치 패턴 파일(*.pat, 예: 가지고 있는 CAD 프로그램의 패턴 파일·회사 표준 패턴)을 넣으면 해치 패턴 창의 '사용자' 탭에 나옵니다.\r\n내장 패턴과 이름이 같으면(예: ANSI31) 넣은 파일의 정의로 그립니다."),
		(() => Templates, "이 폴더에 템플릿 도면(*.dwg, *.dwt, *.dxf — 2000·2004·2007 등 버전 무관)을 넣으면 '새로 만들기' 때 고를 수 있습니다.\r\n회사 표준 도면층·문자 스타일·치수 스타일·도곽을 넣어 둔 도면을 템플릿으로 쓰면 됩니다."),
		(() => Linetypes, "이 폴더에 선종류 파일(*.lin, 예: 가지고 있는 CAD 프로그램의 선종류 파일·회사 표준 선종류)을 넣으면 선종류 관리자의 '불러오기' 목록에 나옵니다.\r\n내장 선종류와 이름이 같으면(예: CENTER) 넣은 파일의 정의를 씁니다. 문자·모양(ltypeshp.shx)이 들어간 복합 선종류도 됩니다."),
		(() => Blocks, "이 폴더에 블록으로 쓸 도면(*.dwg)을 넣으면 삽입 > 블록 라이브러리에서 골라 넣을 수 있습니다.\r\n파일 이름이 블록 이름이 됩니다. 도면의 (0,0)이 삽입 기준점입니다."),
	};

	/// <summary>사용자 폴더들을 만들고 README를 넣는다 (이미 있으면 그대로).</summary>
	public static void EnsureAll()
	{
		foreach (var (path, readme) in All)
		{
			try
			{
				string dir = path();
				Directory.CreateDirectory(dir);
				string rm = Path.Combine(dir, "README.txt");
				if (!File.Exists(rm))
				{
					File.WriteAllText(rm, readme + "\r\n넣거나 바꾼 뒤 Free 2D CAD를 다시 실행하면 적용됩니다.\r\n", new System.Text.UTF8Encoding(true));
				}
			}
			catch (Exception)
			{
				// 쓰기 권한이 없는 곳(예: Program Files)에 설치된 경우는 건너뜀
			}
		}
	}

	/// <summary>폴더의 파일들 (확장자 필터, 이름순). 폴더가 없으면 빈 목록.</summary>
	public static IReadOnlyList<string> Files(string folder, string pattern)
	{
		try
		{
			return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList() : Array.Empty<string>();
		}
		catch (Exception)
		{
			return Array.Empty<string>();
		}
	}
}
