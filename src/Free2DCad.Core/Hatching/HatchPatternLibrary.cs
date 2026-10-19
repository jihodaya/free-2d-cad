using System.Globalization;
using System.Reflection;
using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Hatching;

/// <summary>패턴 목록 창에 보여 줄 한 항목. Category는 "ANSI", "ISO", "기타".</summary>
public sealed record HatchPatternInfo(string Name, string Category, string Description);

/// <summary>
/// 내장 해치 패턴(mm 기준, Free2DCadPatterns.pat). 기존 CAD가 없는 PC에서도 쓰도록 앱에 포함한다.
/// </summary>
public static class HatchPatternLibrary
{
	private static readonly object Gate = new();
	private static Dictionary<string, (HatchPatternInfo Info, HatchPattern Pattern)>? _patterns;

	/// <summary>
	/// 사용자 패턴 폴더: 실행 파일 폴더 아래 Patterns. 여기에 넣은 *.pat 파일은 '사용자' 탭에 나온다 (회사 표준 패턴 등).
	/// 프로그램 폴더째 복사해 배포하면 패턴도 같이 따라간다.
	/// </summary>
	public static string UserPatternFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Patterns");

	private static Dictionary<string, (HatchPatternInfo Info, HatchPattern Pattern)> Patterns
	{
		get
		{
			lock (Gate)
			{
				return _patterns ??= Load();
			}
		}
	}

	/// <summary>모든 패턴 (파일 순서).</summary>
	public static IReadOnlyList<HatchPatternInfo> All
	{
		get
		{
			lock (Gate)
			{
				return Patterns.Values.Select(v => v.Info).ToList();
			}
		}
	}

	/// <summary>분류별 패턴 ("ANSI", "ISO", "기타").</summary>
	public static IReadOnlyList<HatchPatternInfo> ByCategory(string category) =>
		All.Where(p => p.Category == category)
			.OrderBy(p => p.Name == "SOLID" ? 0 : 1) // 기존 CAD처럼 SOLID 먼저, 나머지는 이름순
			.ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

	public static bool Contains(string name) { lock (Gate) { return Patterns.ContainsKey(name); } }

	/// <summary>축척·각도를 적용하지 않은 패턴 복사본. 없으면 null. SOLID는 선이 없는 패턴.</summary>
	public static HatchPattern? Get(string name)
	{
		lock (Gate)
		{
			return Patterns.TryGetValue(name, out var p) ? p.Pattern.Clone() : null;
		}
	}

	/// <summary>
	/// 열어 둔 도면의 해치에 쓰인 패턴을 목록에 더한다 ('사용자' 탭, 설명 "도면에서 가져옴").
	/// DWG의 해치 패턴은 각도·축척이 적용돼 저장되므로 되돌려서 기본 패턴으로 만든다. 새로 더한 개수를 돌려준다.
	/// </summary>
	public static int RegisterFromDocument(ACadSharp.CadDocument doc)
	{
		int added = 0;
		foreach (var hatch in doc.Entities.OfType<Hatch>())
		{
			var pat = hatch.Pattern;
			if (hatch.IsSolid || pat == null || pat.Lines.Count == 0 || string.IsNullOrWhiteSpace(pat.Name) || pat.Name.StartsWith('_'))
			{
				continue;
			}
			lock (Gate)
			{
				if (Patterns.ContainsKey(pat.Name))
				{
					continue;
				}
				var basePat = pat.Clone();
				try
				{
					basePat.Update(XY.Zero, -hatch.PatternAngle, 1);
					if (Math.Abs(hatch.PatternScale) > 1e-9)
					{
						basePat.Update(XY.Zero, 0, 1.0 / hatch.PatternScale);
					}
				}
				catch (Exception)
				{
					continue;
				}
				Patterns[pat.Name] = (new HatchPatternInfo(pat.Name, "사용자", "도면에서 가져옴"), basePat);
				added++;
			}
		}
		return added;
	}

	/// <summary>
	/// 사용자 정의 패턴(평행선): 간격(spacing)과 이중(교차) 여부. 기존 CAD '유형: 사용자 정의'.
	/// </summary>
	public static HatchPattern UserDefined(double spacing, bool isDouble)
	{
		var pat = new HatchPattern("_USER");
		pat.Lines.Add(new HatchPattern.Line { Angle = 0, BasePoint = XY.Zero, Offset = new XY(0, spacing) });
		if (isDouble)
		{
			pat.Lines.Add(new HatchPattern.Line { Angle = Math.PI / 2, BasePoint = XY.Zero, Offset = new XY(-spacing, 0) });
		}
		return pat;
	}

	/// <summary>
	/// 견본 그림용 선분: (0,0)~(width,height) 사각형 안에 패턴을 전개한다. scale은 패턴 축척 (0 이하면 보기 좋은 축척 자동).
	/// SOLID는 빈 목록 (호출하는 쪽에서 사각형을 채울 것).
	/// </summary>
	public static IReadOnlyList<(double X1, double Y1, double X2, double Y2)> PreviewSegments(string name, double width, double height, double scale = 1, double angleDeg = 0)
	{
		var pattern = Get(name);
		if (pattern == null || pattern.Lines.Count == 0)
		{
			return Array.Empty<(double, double, double, double)>();
		}
		if (scale <= 0)
		{
			// 자동: 가장 촘촘한 선 간격이 견본 크기의 약 1/5이 되도록 (기존 CAD 견본처럼 무늬가 보이게)
			double minGap = pattern.Lines.Select(l => Math.Abs(l.LineOffset)).Where(g => g > 1e-9).DefaultIfEmpty(3.175).Min();
			scale = Math.Min(width, height) / 5.0 / minGap;
		}
		var hatch = HatchFactory.CreateFromLoops(
			new[] { new[] { 0.0, 0.0, width, 0.0, width, height, 0.0, height } },
			new HatchSettings { PatternName = name, Scale = scale, AngleDegrees = angleDeg });
		return hatch.ExplodePattern()
			.Select(l => Math.Abs(l.EndPoint.X - l.StartPoint.X) + Math.Abs(l.EndPoint.Y - l.StartPoint.Y) < 1e-9
				? (l.StartPoint.X - 0.6, l.StartPoint.Y, l.StartPoint.X + 0.6, l.StartPoint.Y) // 점은 짧은 선으로
				: (l.StartPoint.X, l.StartPoint.Y, l.EndPoint.X, l.EndPoint.Y))
			.Take(5000)
			.ToList();
	}

	private static Dictionary<string, (HatchPatternInfo, HatchPattern)> Load()
	{
		var result = new Dictionary<string, (HatchPatternInfo, HatchPattern)>(StringComparer.OrdinalIgnoreCase);
		using (Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Free2DCad.Core.Hatching.Free2DCadPatterns.pat"))
		{
			if (s != null)
			{
				using var reader = new StreamReader(s);
				foreach (var (info, pat) in Parse(reader.ReadToEnd()))
				{
					result[info.Name] = (info, pat);
				}
			}
		}

		// 외부 .pat (실행 파일 폴더\Patterns) → '사용자' 탭. 내장 패턴과 이름이 같으면 사용자 파일의 정의로 바꾼다
		// (예: 다른 CAD 프로그램의 패턴 파일을 넣으면 ANSI31 등이 그 정의로 그려짐, 탭은 원래 자리 그대로)
		foreach (string dir in new[] { UserPatternFolder })
		{
			if (!Directory.Exists(dir))
			{
				continue;
			}
			foreach (string file in Directory.EnumerateFiles(dir, "*.pat").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
			{
				try
				{
					foreach (var (info, pat) in Parse(ReadText(file)))
					{
						string from = $"{info.Description} ({Path.GetFileName(file)})".Trim();
						if (result.TryGetValue(info.Name, out var existing))
						{
							// 같은 이름: 정의는 사용자 파일, 탭은 그대로
							result[info.Name] = (existing.Item1 with { Description = from }, pat);
							continue;
						}
						var entry = info with { Category = "사용자", Description = from };
						result[info.Name] = (entry, pat);
					}
				}
				catch (Exception)
				{
					// 읽을 수 없는 파일은 건너뜀
				}
			}
		}
		return result;
	}

	private static string StandardCategory(string name) =>
		name.StartsWith("ANSI", StringComparison.OrdinalIgnoreCase) ? "ANSI"
		: name.StartsWith("ISO", StringComparison.OrdinalIgnoreCase) ? "ISO"
		: "기타";

	/// <summary>.pat 파일은 UTF-8이 아니라 ANSI(CP949)로 저장된 경우가 많다.</summary>
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

	/// <summary>.pat 형식 텍스트를 읽는다 (ACadSharp HatchPattern.LoadFrom과 같은 해석).</summary>
	public static IEnumerable<(HatchPatternInfo Info, HatchPattern Pattern)> Parse(string text)
	{
		HatchPattern? current = null;
		HatchPatternInfo? info = null;
		foreach (string raw in text.Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0 || line.StartsWith(';'))
			{
				continue;
			}
			if (line.StartsWith('*'))
			{
				if (current != null && info != null)
				{
					yield return (info, current);
				}
				int comma = line.IndexOf(',');
				string name = (comma > 0 ? line[1..comma] : line[1..]).Trim();
				string desc = comma > 0 ? line[(comma + 1)..].Trim() : string.Empty;
				string category = "기타";
				if (desc.StartsWith('['))
				{
					int close = desc.IndexOf(']');
					if (close > 0)
					{
						category = desc[1..close];
						desc = desc[(close + 1)..].Trim();
					}
				}
				current = new HatchPattern(name) { Description = desc };
				info = new HatchPatternInfo(name, category, desc);
				continue;
			}
			if (current == null)
			{
				continue;
			}
			string data = line;
			int semi = data.IndexOf(';');
			if (semi >= 0)
			{
				data = data[..semi]; // 줄 끝 주석
			}
			var parts = data.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			var v = new double[parts.Length];
			bool ok = true;
			for (int k = 0; k < parts.Length && ok; k++)
			{
				ok = double.TryParse(parts[k], NumberStyles.Float, CultureInfo.InvariantCulture, out v[k]);
			}
			if (!ok || v.Length < 5)
			{
				continue;
			}
			double angle = v[0] * Math.PI / 180.0;
			double cos = Math.Cos(angle), sin = Math.Sin(angle);
			var l = new HatchPattern.Line
			{
				Angle = angle,
				BasePoint = new XY(v[1], v[2]),
				// shift/offset은 선 방향 기준 → 도면 좌표로 회전
				Offset = new XY(v[3] * cos - v[4] * sin, v[3] * sin + v[4] * cos),
			};
			l.DashLengths.AddRange(v.Skip(5));
			current.Lines.Add(l);
		}
		if (current != null && info != null)
		{
			yield return (info, current);
		}
	}
}
