using System.Globalization;
using ACadSharp;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Styles;

/// <summary>
/// 복합 선종류의 문자·모양 요소 (.lin의 [...]). Segment = 붙는 패턴 구간 순번. 원점 = 그 구간의 끝에서 선 방향 X, 왼쪽 Y.
/// 문자는 Value가 글자, Style이 문자 스타일 이름. 모양은 Value가 모양 이름(TRACK1 등), Style이 모양 파일(ltypeshp.shx).
/// Rotation은 도(°), Absolute면 선 방향과 상관없는 절대 각도.
/// </summary>
public sealed record LinetypeElement(int Segment, bool IsText, string Value, string Style, double Scale, double Rotation, bool Absolute, double X, double Y);

/// <summary>선종류 관리자 '불러오기'용 선종류 (mm 기준). 이름, 설명, 대시(+)/공백(-)/점(0) 길이, 복합 요소.</summary>
public sealed record LinetypeDefinition(string Name, string Description, double[] Pattern)
{
	public IReadOnlyList<LinetypeElement> Elements { get; init; } = Array.Empty<LinetypeElement>();
}

public static class LinetypeLibrary
{
	/// <summary>선종류 모양 파일 (기존 CAD와 같은 이름). 모양은 F2D가 직접 그린다.</summary>
	public const string ShapeFile = "ltypeshp.shx";

	/// <summary>ltypeshp.shx의 모양 이름 → 번호.</summary>
	public static readonly IReadOnlyDictionary<string, short> ShapeNumbers = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
	{
		["TRACK1"] = 130,
		["ZIG"] = 131,
		["BOX"] = 132,
		["CIRC1"] = 133,
		["BAT"] = 134,
	};

	private static LinetypeDefinition D(string name, string desc, params double[] p) => new(name, desc, p);

	/// <summary>내장 선종류 + 사용자 폴더(Linetypes\*.lin)의 선종류. 이름이 같으면 사용자 파일의 정의로 바꾼다 (먼저 읽은 파일 우선).</summary>
	public static IReadOnlyList<LinetypeDefinition> All => AllLazy.Value;

	private static readonly Lazy<IReadOnlyList<LinetypeDefinition>> AllLazy = new(() =>
	{
		var list = BuiltIn.ToList();
		var fromUser = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string file in UserFolders.Files(UserFolders.Linetypes, "*.lin"))
		{
			try
			{
				foreach (var d in ParseLin(ReadText(file)))
				{
					if (!fromUser.Add(d.Name))
					{
						continue; // 사용자 파일끼리 겹치면 먼저 읽은 것
					}
					var def = d with { Description = $"{d.Description} ({Path.GetFileName(file)})".Trim() };
					int at = list.FindIndex(x => string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase));
					if (at >= 0)
					{
						list[at] = def;
					}
					else
					{
						list.Add(def);
					}
				}
			}
			catch (Exception)
			{
				// 읽을 수 없는 파일은 건너뜀
			}
		}
		return list;
	});

	/// <summary>
	/// 기존 CAD .lin 형식: "*이름,설명" 다음 줄 "A,12.7,-6.35,0,-6.35".
	/// 복합 선종류는 [모양,파일,S=..,R=..,X=..,Y=..] 또는 ["문자",스타일,...]가 바로 앞 구간에 붙는다.
	/// </summary>
	public static IEnumerable<LinetypeDefinition> ParseLin(string text)
	{
		string? name = null, desc = null;
		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith(';'))
			{
				continue;
			}
			if (line.StartsWith('*'))
			{
				int comma = line.IndexOf(',');
				name = (comma > 0 ? line[1..comma] : line[1..]).Trim();
				desc = comma > 0 ? line[(comma + 1)..].Trim() : string.Empty;
				continue;
			}
			if (name != null && (line.StartsWith("A,", StringComparison.OrdinalIgnoreCase) || line.StartsWith("A ,", StringComparison.OrdinalIgnoreCase)))
			{
				if (TryParsePattern(line[(line.IndexOf(',') + 1)..], out double[] nums, out var elements))
				{
					yield return new LinetypeDefinition(name, desc ?? string.Empty, nums) { Elements = elements };
				}
				name = null;
			}
		}
	}

	/// <summary>"12.7,-5.08,[\"GAS\",STANDARD,S=2.54,X=-2.54,Y=-1.27],-6.35" → 패턴과 요소. 알 수 없는 모양이면 실패.</summary>
	private static bool TryParsePattern(string body, out double[] pattern, out List<LinetypeElement> elements)
	{
		var nums = new List<double>();
		elements = new List<LinetypeElement>();
		pattern = Array.Empty<double>();
		foreach (string token in SplitTop(body))
		{
			if (token.StartsWith('['))
			{
				if (nums.Count == 0 || !token.EndsWith(']'))
				{
					return false;
				}
				var el = ParseElement(token[1..^1], nums.Count - 1);
				if (el == null)
				{
					return false;
				}
				elements.Add(el);
				continue;
			}
			if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
			{
				return false;
			}
			nums.Add(d);
		}
		pattern = nums.ToArray();
		return nums.Count > 0;
	}

	/// <summary>괄호·따옴표 밖의 쉼표로 나눈다.</summary>
	private static IEnumerable<string> SplitTop(string s)
	{
		int depth = 0, start = 0;
		bool quote = false;
		for (int i = 0; i <= s.Length; i++)
		{
			char c = i < s.Length ? s[i] : ',';
			if (c == '"')
			{
				quote = !quote;
			}
			else if (!quote && c == '[')
			{
				depth++;
			}
			else if (!quote && c == ']')
			{
				depth--;
			}
			else if (!quote && depth == 0 && c == ',')
			{
				string part = s[start..i].Trim();
				if (part.Length > 0)
				{
					yield return part;
				}
				start = i + 1;
			}
		}
	}

	private static LinetypeElement? ParseElement(string inner, int segment)
	{
		var parts = new List<string>();
		bool quote = false;
		int start = 0;
		for (int i = 0; i <= inner.Length; i++)
		{
			char c = i < inner.Length ? inner[i] : ',';
			if (c == '"')
			{
				quote = !quote;
			}
			else if (!quote && c == ',')
			{
				parts.Add(inner[start..i].Trim());
				start = i + 1;
			}
		}
		if (parts.Count < 2)
		{
			return null;
		}
		bool isText = parts[0].StartsWith('"');
		string value = isText ? parts[0].Trim('"') : parts[0];
		if (!isText && !ShapeNumbers.ContainsKey(value))
		{
			return null; // ltypeshp.shx 밖의 모양은 그릴 수 없음
		}
		double scale = 1, rot = 0, x = 0, y = 0;
		bool abs = false;
		foreach (string p in parts.Skip(2))
		{
			int eq = p.IndexOf('=');
			if (eq <= 0)
			{
				continue;
			}
			string key = p[..eq].Trim().ToUpperInvariant();
			string v = p[(eq + 1)..].Trim().TrimEnd('d', 'D');
			if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double num))
			{
				continue;
			}
			switch (key)
			{
				case "S": scale = num; break;
				case "R": rot = num; break;
				case "A": rot = num; abs = true; break;
				case "U": rot = num; break;
				case "X": x = num; break;
				case "Y": y = num; break;
			}
		}
		return new LinetypeElement(segment, isText, value, parts[1], scale, rot, abs, x, y);
	}

	private static string ReadText(string path)
	{
		byte[] bytes = File.ReadAllBytes(path);
		try
		{
			return new System.Text.UTF8Encoding(false, true).GetString(bytes);
		}
		catch (System.Text.DecoderFallbackException)
		{
			System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
			return System.Text.Encoding.GetEncoding(949).GetString(bytes);
		}
	}

	// ===== 내장 선종류: 모두 이 프로젝트에서 직접 정한 값 (mm) =====
	// 기본 선은 기준 길이 하나에 0.5배("이름2")·2배("이름X2")를 함께 만든다.
	// ISO 선은 ISO 128-20의 요소 길이(선 굵기 d 기준: 대시 12d, 장대시 24d, 단대시 6d, 공백 3d, 넓은 공백 18d, 점 0.5d)로 계산한다.

	/// <summary>기본 선 (이름, 설명, 기준 길이 mm: 대시(+)/공백(-)/점(0)).</summary>
	private static readonly (string Name, string Desc, double[] Pattern)[] BasicBase =
	{
		("CENTER", "중심선 ______ _ ______ _", new[] { 20.0, -2, 4, -2 }),
		("HIDDEN", "숨은선 __ __ __ __", new[] { 5.0, -2 }),
		("DASHED", "파선 ____ ____ ____", new[] { 10.0, -3 }),
		("PHANTOM", "가상선 ______ _ _ ______", new[] { 20.0, -2, 3, -2, 3, -2 }),
		("DASHDOT", "일점쇄선 ____ . ____ .", new[] { 12.0, -2, 0, -2 }),
		("DIVIDE", "이점쇄선 ____ . . ____", new[] { 12.0, -2, 0, -2, 0, -2 }),
		("BORDER", "경계선 ____ ____ . ____", new[] { 12.0, -2, 12, -2, 0, -2 }),
		("DOT", "점선 . . . . . .", new[] { 0.0, -2 }),
		("LONGDASH", "긴 파선 ________  ________", new[] { 30.0, -4 }),
		("DASHDASHDOT", "이중 파선 점 ___ ___ . ___", new[] { 8.0, -2, 8, -2, 0, -2 }),
	};

	private static IEnumerable<LinetypeDefinition> BasicLines()
	{
		foreach (var (name, desc, p) in BasicBase)
		{
			yield return D(name, desc, p);
			yield return D(name + "2", desc + " (0.5배)", p.Select(v => v * 0.5).ToArray());
			yield return D(name + "X2", desc + " (2배)", p.Select(v => v * 2).ToArray());
		}
		yield return D("INVISIBLE", "보이지 않는 선", 0, -10000);
	}

	/// <summary>ISO 128 선 모양 02~15 (선 굵기 1mm일 때 길이).</summary>
	private static readonly (string Desc, double[] Pattern)[] Iso =
	{
		("파선 __ __ __", new[] { 12.0, -3 }),
		("간격 넓은 파선 __    __", new[] { 12.0, -18 }),
		("일점 장쇄선 ____ .", new[] { 24.0, -3, 0.5, -3 }),
		("이점 장쇄선 ____ ..", new[] { 24.0, -3, 0.5, -3, 0.5, -3 }),
		("삼점 장쇄선 ____ ...", new[] { 24.0, -3, 0.5, -3, 0.5, -3, 0.5, -3 }),
		("점선 . . . .", new[] { 0.5, -3 }),
		("장단 파선 ____ __", new[] { 24.0, -3, 6, -3 }),
		("장 이단 파선 ____ __ __", new[] { 24.0, -3, 6, -3, 6, -3 }),
		("일점 쇄선 __ .", new[] { 12.0, -3, 0.5, -3 }),
		("이중 파선 일점 __ __ .", new[] { 12.0, -3, 12, -3, 0.5, -3 }),
		("파선 이점 __ . .", new[] { 12.0, -3, 0.5, -3, 0.5, -3 }),
		("이중 파선 이점 __ __ . .", new[] { 12.0, -3, 12, -3, 0.5, -3, 0.5, -3 }),
		("파선 삼점 __ . . .", new[] { 12.0, -3, 0.5, -3, 0.5, -3, 0.5, -3 }),
		("이중 파선 삼점 __ __ . . .", new[] { 12.0, -3, 12, -3, 0.5, -3, 0.5, -3, 0.5, -3 }),
	};

	/// <summary>ISO 선: "ISO02"~"ISO15"(굵기 1) + 펜 굵기별 "ISO02_0.25" 등 (길이 = 굵기 1 길이 × 굵기).</summary>
	private static IEnumerable<LinetypeDefinition> IsoLines()
	{
		double[] widths = { 0.25, 0.35, 0.5, 0.7 };
		for (int i = 0; i < Iso.Length; i++)
		{
			string baseName = $"ISO{i + 2:00}";
			yield return D(baseName, $"ISO 128 {Iso[i].Desc} (굵기 1)", Iso[i].Pattern);
			foreach (double w in widths)
			{
				string ws = w.ToString("0.##", CultureInfo.InvariantCulture);
				yield return D($"{baseName}_{ws}", $"ISO 128 {Iso[i].Desc} (굵기 {ws})", Iso[i].Pattern.Select(v => Math.Round(v * w, 6)).ToArray());
			}
		}
	}

	/// <summary>
	/// 모양이 들어간 복합 선종류 (.lin 형식, mm). 모양은 ltypeshp.shx 이름(도면 호환)으로 가리키고, 그리는 모양은 이 프로그램이 직접 정의한다.
	/// 요소는 바로 앞 구간의 끝에 붙고 X만큼 선 방향으로 옮긴다. CIRC1·BOX는 원점에서 오른쪽으로 폭 2×S, TRACK1은 위아래 S.
	/// </summary>
	private const string ShapeLin = """
		*FENCE_CIRCLE,울타리 (원) ----o----o----o----
		A,10,-2,[CIRC1,ltypeshp.shx,S=1,X=-1],-2
		*FENCE_SQUARE,울타리 (사각) ----[]----[]----[]----
		A,10,-2,[BOX,ltypeshp.shx,S=1,X=-1],-2
		*RAILWAY,철도 -|-|-|-|-|-|-|-|-|-|-|-
		A,4,[TRACK1,ltypeshp.shx,S=1.5],4
		*RAILWAY2,철도 (촘촘) -|-|-|-|-|-|-|-|-|-|-|-|-|-|-
		A,2,[TRACK1,ltypeshp.shx,S=1],2
		*ZIGZAG_LINE,지그재그 /\/\/\/\/\/\/\/\/\
		A,0.001,-2,[ZIG,ltypeshp.shx,S=2,X=-2],-4,[ZIG,ltypeshp.shx,S=2,R=180,X=2],-2
		*INSULATION_LINE,단열재 선 SSSSSSSSSSSSSSSS
		A,0.001,-1,[BAT,ltypeshp.shx,S=1,X=-1],-2,[BAT,ltypeshp.shx,S=1,R=180,X=1],-1
		""";

	/// <summary>글자가 들어간 배관·설비 선: 대시 12 + 공백(글자 폭에 맞춤)에 높이 2.5 글자. (이름, 글자, 설명).</summary>
	private static readonly (string Name, string Text, string Desc)[] TextLineDefs =
	{
		("PIPE_GAS", "GAS", "가스 배관"),
		("PIPE_WATER", "W", "급수 배관"),
		("PIPE_HOT", "HW", "급탕 배관"),
		("PIPE_RETURN", "HR", "환탕 배관"),
		("PIPE_DRAIN", "D", "배수 배관"),
		("PIPE_VENT", "V", "통기 배관"),
		("PIPE_OIL", "OIL", "유류 배관"),
		("PIPE_AIR", "A", "압축 공기 배관"),
		("PIPE_STEAM", "S", "증기 배관"),
		("PIPE_FIRE", "F", "소화 배관"),
		("LINE_ELEC", "E", "전기 배선"),
		("LINE_TEL", "T", "통신 배선"),
	};

	private static IEnumerable<LinetypeDefinition> TextLines()
	{
		const double h = 2.5;
		foreach (var (name, text, desc) in TextLineDefs)
		{
			// 글자 앞뒤 여백 1, 글자 폭은 높이의 0.9배로 어림
			double gap = Math.Round(text.Length * h * 0.9 + 2, 3);
			yield return new LinetypeDefinition(name, $"{desc} ---- {text} ---- {text} ----", new[] { 12.0, -1, -(gap - 1) })
			{
				Elements = new[] { new LinetypeElement(1, true, text, "Standard", h, 0, false, 0, -h / 2) },
			};
		}
	}

	private static readonly LinetypeDefinition[] BuiltIn = BasicLines()
		.Concat(IsoLines())
		.Concat(ParseLin(ShapeLin))
		.Concat(TextLines())
		.ToArray();

	public static LinetypeDefinition? Find(string name) =>
		All.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// 정의로 ACadSharp 선종류를 만든다. 복합 요소는 도면(doc)의 문자 스타일·모양 파일 스타일을 가리킨다
	/// (없으면 Standard / 이름 없는 ltypeshp.shx 모양 스타일을 함께 넣는다).
	/// </summary>
	public static LineType Create(LinetypeDefinition def, CadDocument? doc = null)
	{
		var lt = new LineType(def.Name) { Description = def.Description };
		var segments = def.Pattern.Select(len => new LineType.Segment { Length = len }).ToList();
		foreach (var el in def.Elements)
		{
			if (el.Segment < 0 || el.Segment >= segments.Count)
			{
				continue;
			}
			var s = segments[el.Segment];
			s.Offset = new XY(el.X, el.Y);
			s.Scale = el.Scale;
			s.Rotation = el.Rotation * Math.PI / 180;
			if (el.Absolute)
			{
				s.Flags |= LineTypeShapeFlags.RotationIsAbsolute;
			}
			if (el.IsText)
			{
				s.Flags |= LineTypeShapeFlags.Text;
				s.Text = el.Value;
				s.Style = TextStyleOf(doc, el.Style);
			}
			else
			{
				s.Flags |= LineTypeShapeFlags.Shape;
				s.ShapeNumber = ShapeNumbers[el.Value];
				s.Style = ShapeStyleOf(doc);
			}
		}
		foreach (var s in segments)
		{
			lt.AddSegment(s);
		}
		return lt;
	}

	private static TextStyle TextStyleOf(CadDocument? doc, string name)
	{
		if (doc != null)
		{
			var found = doc.TextStyles.FirstOrDefault(t => !t.IsShapeFile && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
				?? doc.TextStyles.FirstOrDefault(t => string.Equals(t.Name, TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase));
			if (found != null)
			{
				return found;
			}
		}
		return new TextStyle(TextStyle.DefaultName);
	}

	/// <summary>도면의 ltypeshp.shx 모양 스타일 (없으면 새로 만든다. 파일에는 이름 없이 저장된다).</summary>
	public static TextStyle ShapeStyleOf(CadDocument? doc)
	{
		var found = doc?.TextStyles.FirstOrDefault(t => t.IsShapeFile && string.Equals(Path.GetFileName(t.Filename ?? string.Empty), ShapeFile, StringComparison.OrdinalIgnoreCase));
		if (found != null)
		{
			return found;
		}
		var style = new TextStyle(ShapeFile) { Filename = ShapeFile };
		style.Flags |= StyleFlags.IsShape;
		return style;
	}

	/// <summary>
	/// 도면에 선종류를 불러온다 (이미 있으면 건너뜀). 실행 취소 가능. 새로 추가한 개수를 돌려준다.
	/// </summary>
	public static int Load(EditorDocument doc, IEnumerable<string> names)
	{
		CadDocument cad = doc.Document;
		var toAdd = names
			.Where(n => !cad.LineTypes.Contains(n))
			.Select(Find)
			.Where(d => d != null)
			.Select(d => Create(d!, cad))
			.ToList();
		if (toAdd.Count == 0)
		{
			return 0;
		}
		doc.Execute(new DelegateAction(
			"선종류 불러오기",
			() => { foreach (var lt in toAdd) if (!cad.LineTypes.Contains(lt.Name)) cad.LineTypes.Add(lt); },
			() => { foreach (var lt in toAdd) cad.LineTypes.Remove(lt.Name); }));
		return toAdd.Count;
	}
}
