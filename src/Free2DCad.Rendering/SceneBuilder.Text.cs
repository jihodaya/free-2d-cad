using ACadSharp.Tables;

namespace Free2DCad.Rendering;

/// <summary>
/// 문자 배치 엔진 (단일 행 문자·여러 줄 문자 공통).
/// MTEXT 서식(문단 들여쓰기·탭 위치 \p, 폭 비율 \W, 높이 \H, 글꼴 \f·\F, 색 \C, 밑줄 \L, 분수 \S, 자간 \T, 기울기 \Q)을
/// 반영해 줄을 나누고, 글자마다 SHX 선(획) 또는 TrueType 윤곽(채움)으로 장면에 넣는다.
/// 글자가 도형으로 들어가므로 선택·마우스 올림 강조도 글자 하나하나의 모양으로 보인다 (기존 CAD와 같음).
/// 문자 전체를 고르기 쉽도록 둘레 상자는 선택 전용(PickOnly) SceneText로 따로 넣는다.
/// </summary>
public sealed partial class SceneBuilder
{
	/// <summary>배치 요청. 좌표·길이는 모두 도면(월드) 단위.</summary>
	private sealed record TextSpec(
		double X, double Y, double Rotation, double Height, double WidthFactor, double Oblique,
		double WrapWidth, double Ax, TextVAlign V, bool IsMText, double LineSpacingFactor, bool ExactSpacing,
		double UnitScale, TextStyle? Style, Rgb Color)
	{
		/// <summary>단일 행 문자 맞춤(Fit) / 정렬(Aligned): 두 점 사이 폭.</summary>
		public double? FitWidth { get; init; }

		public bool FitKeepsRatio { get; init; }

		/// <summary>여러 줄 문자의 열 (도면 단위로 축척한 값). null = 열 없음.</summary>
		public MTextColumnInfo? Columns { get; init; }

		/// <summary>배경 마스크: 채울 색(null = 도면 배경색)과 테두리 간격띄우기 계수. null = 마스크 없음.</summary>
		public (Rgb? Color, double Scale)? Mask { get; init; }
	}

	/// <summary>문자 스타일에서 정한 글꼴 (SHX 기본·큰 글꼴 또는 TrueType).</summary>
	private sealed record StyleFonts(ShxFont? Main, ShxFont? Big, bool UseShx, string AsciiTtf, string WideTtf, string TtfKey);

	private readonly Dictionary<TextStyle, StyleFonts> _styleFonts = new();
	private static readonly StyleFonts DefaultFonts = new(null, null, false, "malgun.ttf", "malgun.ttf", "malgun.ttf");

	private StyleFonts FontsOf(TextStyle? style)
	{
		if (style == null)
		{
			return DefaultFonts;
		}
		if (this._styleFonts.TryGetValue(style, out var cached))
		{
			return cached;
		}
		StyleFonts result;
		ShxFont? main = ResolveShx(style.Filename, UserFonts.AlternateFont, out bool mainIsShx);
		ShxFont? big = ResolveShx(style.BigFontFilename, UserFonts.AlternateBigFont, out bool bigIsShx);
		if (mainIsShx && !bigIsShx && !MappedToTtf(style.Filename))
		{
			// SHX 기본 글꼴에 큰 글꼴이 없어도 한글은 대체 큰 글꼴(선 글꼴)로
			big = FindShx(UserFonts.AlternateBigFont);
		}
		string styleFlags = (style.TrueType.HasFlag(FontFlags.Bold) ? "|b" : string.Empty) + (style.TrueType.HasFlag(FontFlags.Italic) ? "|i" : string.Empty);
		if ((mainIsShx || bigIsShx) && (main != null || big != null))
		{
			string ascii = FallbackTtfFor(style.Filename);
			result = new StyleFonts(main, big, true, ascii, bigIsShx ? FallbackTtfFor(style.BigFontFilename) : ascii, ascii);
		}
		else if (mainIsShx)
		{
			// SHX를 TrueType으로 보도록 바꾼 경우 (fontmap.txt 또는 대체 글꼴 .ttf)
			string ttf = FallbackTtfFor(style.Filename);
			result = new StyleFonts(null, null, false, ttf, ttf, ttf);
		}
		else
		{
			// TrueType 스타일: 글꼴 패밀리 이름은 확장 데이터(ACAD)에 있다 (예: Standard → GulimChe, 파일 이름은 비어 있음)
			var (family, xBold, xItalic) = TrueTypeFamilyOf(style);
			string file = string.IsNullOrWhiteSpace(style.Filename) ? string.Empty : Path.GetFileName(style.Filename.Trim());
			if (file.Length > 0 && !Path.HasExtension(file))
			{
				file += ".ttf";
			}
			string key;
			if (file.Length > 0 && UserFonts.Find(file) != null)
			{
				key = file + styleFlags;
			}
			else if (family != null)
			{
				key = family + (xBold || style.TrueType.HasFlag(FontFlags.Bold) ? "|b" : string.Empty) + (xItalic || style.TrueType.HasFlag(FontFlags.Italic) ? "|i" : string.Empty);
			}
			else
			{
				key = (file.Length > 0 ? file : "malgun.ttf") + styleFlags;
			}
			result = new StyleFonts(null, null, false, key, key, key);
		}
		this._styleFonts[style] = result;
		return result;
	}

	/// <summary>
	/// 문자 스타일 확장 데이터(앱 ACAD)의 TrueType 글꼴 패밀리 이름과 굵게·기울임 (32비트 값의 0x2000000 = 굵게, 0x1000000 = 기울임).
	/// </summary>
	public static (string? Family, bool Bold, bool Italic) TrueTypeFamilyOf(TextStyle style)
	{
		try
		{
			if (!style.ExtendedData.ContainsKeyName("ACAD"))
			{
				return (null, false, false);
			}
			var records = style.ExtendedData.Get("ACAD").Records;
			string? family = records.OfType<ACadSharp.XData.ExtendedDataString>().Select(r => r.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
			int flags = records.OfType<ACadSharp.XData.ExtendedDataInteger32>().Select(r => r.Value).FirstOrDefault();
			return (family?.Trim(), (flags & 0x2000000) != 0, (flags & 0x1000000) != 0);
		}
		catch (Exception)
		{
			return (null, false, false);
		}
	}

	/// <summary>배치할 한 글자(또는 탭·분수)의 모양과 크기.</summary>
	private sealed class LaidGlyph
	{
		public int Token;
		public MTextToken Tok = null!;
		public double X; // 줄 시작(글상자 왼쪽)에서의 위치
		public double Adv;
		public double H; // 이 글자의 대문자 높이
		public double Wf; // 이 글자의 폭 비율
		public double Shear;
		public ShxChar? Shx;
		public TtfGlyph? Ttf;
		public bool IsSpace;
		public bool IsTab;
		public Rgb Color;
		public List<(double Dx, double Dy, double H, ShxChar? Shx, TtfGlyph? Ttf)>? StackParts;
		public List<double[]>? StackLines;
	}

	private sealed class TextLine
	{
		public readonly List<LaidGlyph> Items = new();
		public double Width;
		public double MaxH;
		public char Align;
		public double Baseline;
		public double Offset;
		public MTextParagraph Para = MTextParagraph.Default;
		public bool ParaFirst;
		public bool ParaLast;
		public int Column;
	}

	/// <summary>한 글자의 SHX/TTF 모양 찾기 (서식의 글꼴 → 스타일 글꼴 → 대체).</summary>
	private (ShxChar? Shx, TtfGlyph? Ttf) GlyphOf(char ch, StyleFonts sf, MTextFormat fmt, double h)
	{
		ShxFont? main = sf.Main, big = sf.Big;
		string asciiTtf = sf.AsciiTtf, wideTtf = sf.WideTtf;
		bool useShx = sf.UseShx;
		string ttfKey = sf.TtfKey;
		if (fmt.Font != null)
		{
			if (fmt.Font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase))
			{
				main = FindShx(fmt.Font) ?? (MappedToTtf(fmt.Font) ? null : FindShx(UserFonts.AlternateFont));
				big ??= FindShx(UserFonts.AlternateBigFont);
				useShx = main != null;
				asciiTtf = wideTtf = FallbackTtfFor(fmt.Font);
			}
			else
			{
				useShx = false;
				ttfKey = fmt.Font + (fmt.Bold ? "|b" : string.Empty) + (fmt.Italic ? "|i" : string.Empty);
			}
		}
		if (useShx)
		{
			if (main != null)
			{
				foreach (int code in Candidates(ch))
				{
					if (main.GetGlyph(code) is { } g)
					{
						return (new ShxChar(main, g, h / main.Above), null);
					}
				}
			}
			if (big != null && ch > 0x7F)
			{
				byte[] bytes = Cp949.GetBytes(ch.ToString());
				int code = bytes.Length == 2 ? (bytes[0] << 8) | bytes[1] : bytes[0];
				if (big.GetGlyph(code) is { } g)
				{
					return (new ShxChar(big, g, h / big.Above), null);
				}
			}
			return (null, GetTtfGlyph(ch > 0x7F ? wideTtf : asciiTtf, ch));
		}
		return (null, GetTtfGlyph(ttfKey, ch));
	}

	private static double AdvanceOf(ShxChar? shx, TtfGlyph? ttf, double h) =>
		shx is { } s ? s.Glyph.AdvanceX * s.Scale : ttf != null ? ttf.Advance * h : h * 0.6;

	/// <summary>문자를 배치해 장면에 넣는다.</summary>
	private void AddFormattedText(List<MTextToken> tokens, TextSpec spec)
	{
		if (spec.Height <= 0)
		{
			return;
		}
		if (tokens.Count == 0 || tokens.All(t => t.Kind == MTextTokenKind.Paragraph || (t.Kind == MTextTokenKind.Char && char.IsWhiteSpace(t.Char))))
		{
			// 그릴 글자가 없어도 (빈 다중지시선·여러 줄 문자를 막 만든 때) 편집용 커서 자리는 둔다 —
			// 전에는 커서·편집 상자가 안 보여 입력할 수 있는지 알 수 없었다 (기존 CAD는 빈 상자와 커서를 보여 줌)
			this.AddEmptyEditText(tokens, spec);
			return;
		}
		var sf = this.FontsOf(spec.Style);
		double baseH = spec.Height;
		double styleOblique = spec.Oblique;

		// 1. 글자 모양·크기
		var glyphs = new List<LaidGlyph>(tokens.Count);
		for (int ti = 0; ti < tokens.Count; ti++)
		{
			var t = tokens[ti];
			var f = t.Format;
			double h = (f.Height is { } abs ? abs * spec.UnitScale : baseH) * f.HeightScale;
			double wf = spec.WidthFactor * f.Width;
			double shear = Math.Tan(f.ObliqueDeg is { } od ? od * Math.PI / 180 : styleOblique);
			var g = new LaidGlyph { Token = ti, Tok = t, H = h, Wf = wf, Shear = shear, Color = f.Color ?? spec.Color };
			switch (t.Kind)
			{
				case MTextTokenKind.Char:
					{
						bool space = t.Char == ' ' || t.Char == ' ' || t.Char == '　';
						var (shx, ttf) = this.GlyphOf(space ? ' ' : t.Char, sf, f, h);
						g.Shx = shx;
						g.Ttf = ttf;
						g.IsSpace = space;
						double adv = AdvanceOf(shx, ttf, h);
						if (space && shx == null && ttf == null)
						{
							adv = h * 0.6;
						}
						g.Adv = adv * wf * f.Tracking;
						break;
					}
				case MTextTokenKind.Tab:
					g.IsTab = true;
					break;
				case MTextTokenKind.Stack:
					this.BuildStack(g, sf, f, h, wf);
					break;
				case MTextTokenKind.Paragraph:
					break;
			}
			glyphs.Add(g);
		}

		// 2. 문단별 줄 나누기 (글상자 폭, 들여쓰기, 탭 위치)
		double wrap = spec.IsMText ? spec.WrapWidth : 0;
		var lines = new List<TextLine>();
		int pStart = 0;
		while (pStart <= glyphs.Count)
		{
			int pEnd = pStart;
			while (pEnd < glyphs.Count && glyphs[pEnd].Tok.Kind != MTextTokenKind.Paragraph)
			{
				pEnd++;
			}
			var para = pEnd > pStart ? glyphs[pStart].Tok.Paragraph : (pEnd < glyphs.Count ? glyphs[pEnd].Tok.Paragraph : MTextParagraph.Default);
			this.LayoutParagraph(glyphs, pStart, pEnd, para, wrap, baseH, spec, lines);
			if (pEnd >= glyphs.Count)
			{
				break;
			}
			pStart = pEnd + 1;
		}
		if (lines.Count == 0)
		{
			return;
		}

		// 3. 줄 간격과 세로 위치 (MTEXT: 글상자 위 = 0에서 아래로, TEXT: 기준선 = 0)
		double spacingUnit = baseH * 5.0 / 3.0 * (spec.LineSpacingFactor > 0 ? spec.LineSpacingFactor : 1);
		double y = 0;
		for (int i = 0; i < lines.Count; i++)
		{
			double lh = lines[i].MaxH > 0 ? lines[i].MaxH : baseH;
			var lp = lines[i].Para;
			if (i == 0)
			{
				y = spec.IsMText ? -lh : 0;
			}
			else if (!lines[i].ParaFirst && lp.SpacingKind != '\0' && spec.IsMText)
			{
				// 문단 줄 간격 (\ps): 배수·정확히·최소
				double natural = Math.Max(baseH * 5.0 / 3.0, lh * 5.0 / 3.0);
				y -= lp.SpacingKind switch
				{
					'm' => natural * (lp.Spacing > 0 ? lp.Spacing : 1),
					'e' => lp.Spacing > 0 ? lp.Spacing : natural,
					_ => Math.Max(lp.Spacing, natural),
				};
			}
			else
			{
				y -= spec.ExactSpacing ? spacingUnit : Math.Max(spacingUnit, lh * 5.0 / 3.0 * (spec.LineSpacingFactor > 0 ? spec.LineSpacingFactor : 1));
			}
			if (i > 0 && spec.IsMText && lines[i].ParaFirst)
			{
				// 문단 앞·뒤 간격 (\pb·\pa, 문자 높이 배수 — 기존 CAD와 같음)
				y -= lp.Before * baseH;
				if (lines[i - 1].ParaLast) y -= lines[i - 1].Para.After * baseH;
			}
			lines[i].Baseline = y;
		}
		// 3-1. 열 (기존 CAD): 열마다 줄 아래(기준선 + 높이/3)가 열 높이를 넘으면 다음 열로, 마지막 열은 나머지 모두
		//      (기존 CAD 화면: 높이 2.5·열 높이 12에 3줄, 높이 25·열 높이 70에 1줄)
		var cols = spec.IsMText ? spec.Columns : null;
		if (cols != null && !cols.Split)
		{
			int c = 0, inCol = 0;
			double shift = 0;
			foreach (var line in lines)
			{
				double depth = -line.Baseline - shift;
				double hc = cols.HeightOf(c);
				// 동적 자동 높이는 필요한 만큼 열을 늘림
				bool more = (cols.Type == 2 && cols.AutoHeight) || c < cols.Columns - 1;
				double below = (line.MaxH > 0 ? line.MaxH : baseH) / 3;
				if (inCol > 0 && more && hc > 0 && depth + below > hc + 1e-9)
				{
					double lh = line.MaxH > 0 ? line.MaxH : baseH;
					c++;
					inCol = 0;
					shift = -line.Baseline - lh;
					depth = lh;
				}
				line.Baseline = -depth;
				line.Column = c;
				inCol++;
			}
		}
		double dy;
		if (spec.IsMText)
		{
			double total = lines.Max(l => -l.Baseline);
			dy = spec.V switch { TextVAlign.Middle => total / 2, TextVAlign.Bottom => total, _ => 0 };
		}
		else
		{
			dy = spec.V switch { TextVAlign.Top => -baseH, TextVAlign.Middle => -baseH / 2, _ => 0 };
		}

		// 4. 가로 정렬
		double maxW = lines.Max(l => l.Width);
		double fitX = 1, fitY = 1;
		if (spec.FitWidth is { } fw && maxW > 1e-9)
		{
			fitX = fw / maxW;
			fitY = spec.FitKeepsRatio ? fitX : 1;
			maxW = fw;
		}
		double boxW = wrap > 0 ? wrap : maxW;
		double boxLeft = -spec.Ax * (cols != null ? cols.TotalWidth : boxW);
		char defAlign = spec.Ax <= 0.01 ? 'l' : spec.Ax >= 0.99 ? 'r' : 'c';
		foreach (var line in lines)
		{
			double lw = line.Width * fitX;
			char a = line.Align is 'l' or 'c' or 'r' ? line.Align : defAlign;
			double colLeft = boxLeft + (cols != null ? line.Column * (cols.Width + cols.Gutter) : 0);
			line.Offset = a switch
			{
				'c' => colLeft + (boxW - lw) / 2,
				'r' => colLeft + boxW - lw,
				_ => colLeft,
			};
			if (!spec.IsMText)
			{
				line.Offset = -spec.Ax * lw;
			}
		}

		// 5. 도형으로 내보내기
		double cos = Math.Cos(spec.Rotation), sin = Math.Sin(spec.Rotation);
		(double X, double Y) W(double lx, double ly) => (spec.X + lx * cos - ly * sin, spec.Y + lx * sin + ly * cos);

		// 배경 마스크 (기존 CAD): 글상자 폭 × 글 높이를 (계수 - 1) × 문자 높이만큼 넓혀 칠하고, 그 뒤 객체를 가린다. 글자는 그 위에.
		if (spec.IsMText && spec.Mask is { } mask)
		{
			double off = Math.Max(0, mask.Scale - 1) * baseH;
			double totalW = cols != null && !cols.Split ? cols.TotalWidth : boxW;
			double depth = lines.Max(l => -l.Baseline) + baseH * 0.07;
			double top = dy, bottom = dy - depth;
			var corners = new[] { W(boxLeft - off, top + off), W(boxLeft + totalW + off, top + off), W(boxLeft + totalW + off, bottom - off), W(boxLeft - off, bottom - off) };
			var loop = corners.SelectMany(c => new[] { c.X, c.Y }).ToArray();
			var mb = Bounds.FromPoints(loop);
			this._scene.Wipeouts.Add(new SceneWipeout(loop, this._order, false, mask.Color ?? default, mb, this._top) { Fill = mask.Color, Background = mask.Color == null });
			this._scene.Include(mb);
			this._order++; // 글자는 마스크보다 나중 (가려지지 않게)
		}
		var fills = new Dictionary<Rgb, List<double[]>>();
		Bounds box = Bounds.Empty;
		this._inShxText = true;
		try
		{
			foreach (var line in lines)
			{
				double baseY = line.Baseline + dy;
				foreach (var g in line.Items)
				{
					double gx = line.Offset + g.X * fitX;
					if (g.StackParts != null)
					{
						foreach (var part in g.StackParts)
						{
							this.EmitGlyph(part.Shx, part.Ttf, gx + part.Dx * fitX, baseY + part.Dy * fitY, part.H * fitY, g.Wf * fitX, g.Shear, g.Color, W, fills, ref box);
						}
						foreach (double[] seg in g.StackLines ?? new List<double[]>())
						{
							var (ax, ay) = W(gx + seg[0] * fitX, baseY + seg[1] * fitY);
							var (bx, by) = W(gx + seg[2] * fitX, baseY + seg[3] * fitY);
							this.AddPolyline(new[] { ax, ay, bx, by }, false, g.Color, null, Affine2.Identity);
						}
						continue;
					}
					if (!g.IsSpace && !g.IsTab)
					{
						this.EmitGlyph(g.Shx, g.Ttf, gx, baseY, g.H * fitY, g.Wf * fitX, g.Shear, g.Color, W, fills, ref box);
					}
				}
				this.EmitDecorations(line, baseY, fitX, W);
				// 선택용 둘레 (글자가 없는 빈 줄 제외)
				if (line.Items.Any(i => !i.IsSpace && !i.IsTab))
				{
					double x0 = line.Offset + line.Items.First(i => !i.IsSpace && !i.IsTab).X * fitX;
					double x1 = line.Offset + line.Width * fitX;
					double h = (line.MaxH > 0 ? line.MaxH : baseH) * fitY;
					foreach (var (px, py) in new[] { W(x0, baseY - h * 0.25), W(x1, baseY - h * 0.25), W(x1, baseY + h), W(x0, baseY + h) })
					{
						box = box.Union(new Bounds(px, py, px, py));
					}
				}
			}
		}
		finally
		{
			this._inShxText = false;
		}
		foreach (var (color, loops) in fills)
		{
			this.AddFill(loops, color, Affine2.Identity);
		}
		if (!box.IsEmpty)
		{
			string[] plain = string.Concat(tokens.Select(t => t.Kind == MTextTokenKind.Paragraph ? "\n" : t.Char.ToString())).Split('\n');
			var text = new SceneText(plain, wrap, spec.X, spec.Y, baseH, spec.Rotation, spec.WidthFactor, spec.Oblique,
				TextHAlign.Left, TextVAlign.Baseline, 0, sf.AsciiTtf, spec.Color, box, this._top)
			{
				PickOnly = true,
				Caret = BuildCaretMap(tokens, lines, dy, fitX, fitY, baseH, W),
				EditText = string.Concat(tokens.Select(MTextTokenizer.PlainOf)),
			};
			this._scene.Texts.Add(text);
			this._scene.Include(box);
		}
	}

	/// <summary>
	/// 문자 바로 편집용 커서 위치표 (편집 글자 경계마다 x, 기준선 y, 줄 높이, 줄 번호 — 도면 좌표).
	/// 줄바꿈에서 빠진 공백·문단 끝은 그 줄 끝에, 분수처럼 여러 글자인 토큰은 폭을 나눠 둔다.
	/// </summary>
	private static double[] BuildCaretMap(List<MTextToken> tokens, List<TextLine> lines, double dy, double fitX, double fitY, double baseH,
		Func<double, double, (double X, double Y)> toWorld)
	{
		var placed = new Dictionary<int, (int Line, LaidGlyph G)>();
		for (int li = 0; li < lines.Count; li++)
		{
			foreach (var g in lines[li].Items)
			{
				placed[g.Token] = (li, g);
			}
		}
		var stops = new List<double>();
		void Stop(int li, double localX)
		{
			var line = lines[li];
			double baseY = line.Baseline + dy;
			var (wx, wy) = toWorld(line.Offset + localX * fitX, baseY);
			stops.Add(wx);
			stops.Add(wy);
			stops.Add((line.MaxH > 0 ? line.MaxH : baseH) * fitY);
			stops.Add(li);
		}
		int curLine = 0;
		double curX = lines.Count > 0 && lines[0].Items.Count > 0 ? lines[0].Items[0].X : 0;
		for (int ti = 0; ti < tokens.Count; ti++)
		{
			int len = MTextTokenizer.PlainOf(tokens[ti]).Length;
			if (placed.TryGetValue(ti, out var p))
			{
				curLine = p.Line;
				for (int k = 0; k < len; k++)
				{
					Stop(curLine, p.G.X + p.G.Adv * k / Math.Max(len, 1));
				}
				curX = p.G.X + p.G.Adv;
			}
			else
			{
				// 배치되지 않은 토큰(문단 끝, 줄바꿈에서 빠진 공백): 지금 줄 끝
				for (int k = 0; k < len; k++)
				{
					Stop(curLine, curX);
				}
				if (tokens[ti].Kind == MTextTokenKind.Paragraph && curLine + 1 < lines.Count)
				{
					curLine++;
					var next = lines[curLine];
					curX = next.Items.Count > 0 ? next.Items[0].X : 0;
				}
			}
		}
		Stop(Math.Min(curLine, lines.Count - 1), curX);
		return stops.ToArray();
	}

	/// <summary>한 문단을 줄로 나눈다: 첫 줄은 (왼쪽+첫 줄) 들여쓰기, 다음 줄은 왼쪽 들여쓰기. 공백에서 줄을 바꾸고 공백이 없으면 글자 경계에서.</summary>
	private void LayoutParagraph(List<LaidGlyph> glyphs, int start, int end, MTextParagraph para, double wrap, double baseH, TextSpec spec, List<TextLine> lines)
	{
		// 들여쓰기·탭 값: \p는 도면 단위, \px는 문자 높이 배수 (기존 CAD: 높이 2.5에서 \pl2; = 2, \pxl2; = 5)
		double k = para.Relative ? baseH : spec.UnitScale;
		double firstX = Math.Max((para.FirstIndent + para.LeftIndent) * k, 0);
		double leftX = Math.Max(para.LeftIndent * k, 0);
		double limit = wrap > 0 ? wrap - para.RightIndent * k : double.MaxValue;
		double[] tabs = para.Tabs.Select(t => t * k).ToArray();
		double defaultTab = baseH * 4;
		int firstLineIndex = lines.Count;

		// 탭 다음 글자 묶음 (다음 탭·문단 끝까지)의 폭, 소수점까지의 폭 — 가운데·오른쪽·소수점 탭 맞춤용
		(double Width, double ToDecimal) Segment(int from)
		{
			double w = 0, dec = -1;
			for (int j = from; j < end && !glyphs[j].IsTab; j++)
			{
				if (dec < 0 && glyphs[j].Tok.Kind == MTextTokenKind.Char && glyphs[j].Tok.Char == para.DecimalChar) dec = w;
				w += glyphs[j].Adv;
			}
			return (w, dec < 0 ? w : dec);
		}

		int idx = start;
		bool firstLine = true;
		do
		{
			var line = new TextLine { Align = para.Align, Para = para, ParaFirst = firstLine };
			double x = firstLine ? firstX : leftX;
			int lastSpace = -1;
			int next = end;
			for (int i = idx; i < end; i++)
			{
				var g = glyphs[i];
				if (g.IsTab)
				{
					int ti = Array.FindIndex(tabs, t => t > x + 1e-9);
					double stop = ti >= 0 ? tabs[ti] : 0;
					if (stop <= 0)
					{
						stop = (Math.Floor(x / defaultTab + 1e-9) + 1) * defaultTab;
					}
					else
					{
						// 가운데·오른쪽·소수점 탭: 다음 글자 묶음의 가운데·끝·소수점을 탭 위치에
						var (segW, toDec) = Segment(i + 1);
						stop = para.TabKind(ti) switch
						{
							'c' => stop - segW / 2,
							'r' => stop - segW,
							'd' => stop - toDec,
							_ => stop,
						};
						stop = Math.Max(stop, x);
					}
					g.Adv = stop - x;
				}
				bool hasInk = line.Items.Any(it => !it.IsSpace && !it.IsTab);
				if (wrap > 0 && x + g.Adv > limit + 1e-9 && hasInk && !g.IsSpace)
				{
					if (lastSpace >= 0)
					{
						next = line.Items[lastSpace].Token + 1;
						line.Items.RemoveRange(lastSpace, line.Items.Count - lastSpace);
					}
					else
					{
						next = i;
					}
					break;
				}
				g.X = x;
				line.Items.Add(g);
				x += g.Adv;
				if (g.IsSpace && g.Tok.Char != ' ')
				{
					lastSpace = line.Items.Count - 1;
				}
			}
			// 끝 공백은 폭에서 뺀다
			var ink = line.Items.Where(it => !it.IsSpace).ToList();
			line.Width = ink.Count > 0 ? ink.Max(it => it.X + it.Adv) : (firstLine ? firstX : leftX);
			line.MaxH = line.Items.Where(it => !it.IsSpace && !it.IsTab).Select(it => it.H).DefaultIfEmpty(baseH).Max();
			lines.Add(line);
			if (next >= end)
			{
				break;
			}
			idx = next;
			while (idx < end && glyphs[idx].IsSpace)
			{
				idx++;
			}
			firstLine = false;
		}
		while (idx < end);
		if (lines.Count > firstLineIndex)
		{
			lines[^1].ParaLast = true;
		}
	}

	/// <summary>
	/// 글자가 없는(빈칸·줄 바꿈뿐인) 문자: 그리지는 않고 편집용 커서 위치표와 작은 선택 둘레만 넣는다.
	/// 첫 줄 기준선은 붙임점에 맞춘 자리 (여러 줄 문자: 위 = 0에서 높이만큼 아래), 빈칸은 높이의 0.35, 줄 바꿈은 5/3 높이.
	/// </summary>
	private void AddEmptyEditText(List<MTextToken> tokens, TextSpec spec)
	{
		double h = spec.Height;
		double cos = Math.Cos(spec.Rotation), sin = Math.Sin(spec.Rotation);
		(double X, double Y) W(double lx, double ly) => (spec.X + lx * cos - ly * sin, spec.Y + lx * sin + ly * cos);
		int lineCount = 1 + tokens.Count(t => t.Kind == MTextTokenKind.Paragraph);
		double step = h * 5.0 / 3.0 * (spec.LineSpacingFactor > 0 ? spec.LineSpacingFactor : 1);
		double firstBase, dy;
		if (spec.IsMText)
		{
			firstBase = -h;
			double total = h + step * (lineCount - 1);
			dy = spec.V switch { TextVAlign.Middle => total / 2, TextVAlign.Bottom => total, _ => 0 };
		}
		else
		{
			firstBase = 0;
			dy = spec.V switch { TextVAlign.Top => -h, TextVAlign.Middle => -h / 2, _ => 0 };
		}
		double boxW = spec.IsMText && spec.WrapWidth > 0 ? spec.WrapWidth : 0;
		double left = -spec.Ax * boxW + (spec.Ax > 0.01 && spec.Ax < 0.99 ? boxW / 2 : spec.Ax >= 0.99 ? boxW : 0);
		var stops = new List<double>();
		double x = left, baseY = firstBase + dy;
		int line = 0;
		void Stop()
		{
			var (wx, wy) = W(x, baseY);
			stops.Add(wx);
			stops.Add(wy);
			stops.Add(h);
			stops.Add(line);
		}
		foreach (var t in tokens)
		{
			foreach (char ch in MTextTokenizer.PlainOf(t))
			{
				Stop();
				if (ch == '\n')
				{
					line++;
					x = left;
					baseY -= step;
				}
				else
				{
					x += h * 0.35;
				}
			}
		}
		Stop();
		// 선택·편집 상자용 둘레: 커서 자리부터 높이만큼 (빈 문자도 화면에서 고를 수 있게)
		Bounds box = Bounds.Empty;
		double maxX = Math.Max(x, left + h * 0.6);
		foreach (var (px, py) in new[] { W(left, firstBase + dy - h * 0.25), W(maxX, firstBase + dy - h * 0.25), W(maxX, firstBase + dy + h), W(left, baseY - h * 0.25) })
		{
			box = box.Union(new Bounds(px, py, px, py));
		}
		var sf = this.FontsOf(spec.Style);
		this._scene.Texts.Add(new SceneText(new[] { string.Empty }, spec.WrapWidth, spec.X, spec.Y, h, spec.Rotation, spec.WidthFactor, spec.Oblique,
			TextHAlign.Left, TextVAlign.Baseline, 0, sf.AsciiTtf, spec.Color, box, this._top)
		{
			PickOnly = true,
			Caret = stops.ToArray(),
			EditText = string.Concat(tokens.Select(MTextTokenizer.PlainOf)),
		});
	}

	/// <summary>분수 (\S): 위·아래 글자를 작게(0.6배) 쌓고, '/'면 가운데 가로선.
	/// 공차 쌓기('^')는 기존 CAD처럼 본 글자 크기로, 본 글자 가운데를 기준으로 위·아래에 둔다.</summary>
	private void BuildStack(LaidGlyph g, StyleFonts sf, MTextFormat f, double h, double wf)
	{
		bool tolerance = g.Tok.StackType == '^';
		double sh = tolerance ? h : h * 0.6;
		var parts = new List<(double, double, double, ShxChar?, TtfGlyph?)>();
		double Measure(string s) => s.Sum(ch => AdvanceOf(this.GlyphOf(ch, sf, f, sh).Shx, this.GlyphOf(ch, sf, f, sh).Ttf, sh)) * wf;
		string top = g.Tok.StackTop ?? string.Empty, bottom = g.Tok.StackBottom ?? string.Empty;
		double wTop = Measure(top), wBottom = Measure(bottom);
		double width = Math.Max(wTop, wBottom);
		void Place(string s, double w, double baseDy)
		{
			double x = (width - w) / 2;
			foreach (char ch in s)
			{
				var (shx, ttf) = this.GlyphOf(ch, sf, f, sh);
				if (ch != ' ')
				{
					parts.Add((x, baseDy, sh, shx, ttf));
				}
				x += AdvanceOf(shx, ttf, sh) * wf;
			}
		}
		if (tolerance)
		{
			Place(top, wTop, h * 0.7);
			Place(bottom, wBottom, -h * 0.7);
		}
		else
		{
			Place(top, wTop, h * 0.6);
			Place(bottom, wBottom, -h * 0.25);
		}
		g.StackParts = parts;
		if (g.Tok.StackType is '/' or '#')
		{
			g.StackLines = new List<double[]> { new[] { 0, h * 0.45, width, h * 0.45 } };
		}
		g.Adv = width + h * 0.1;
		g.H = tolerance ? h : h * 1.2; // 공차 쌓기는 본 글자 가운데에 맞춤 (가운데 정렬 기준)
	}

	/// <summary>글자 하나를 도형으로: SHX는 획(선), TrueType은 윤곽(채움, 색별로 모아서).</summary>
	private void EmitGlyph(ShxChar? shx, TtfGlyph? ttf, double gx, double baseY, double h, double wf, double shear,
		Rgb color, Func<double, double, (double X, double Y)> toWorld, Dictionary<Rgb, List<double[]>> fills, ref Bounds box)
	{
		if (shx is { } v)
		{
			double scale = h / v.Font.Above;
			foreach (double[] s in v.Glyph.Strokes)
			{
				var pts = new double[s.Length];
				for (int k = 0; k + 1 < s.Length; k += 2)
				{
					double ly = s[k + 1] * scale;
					(pts[k], pts[k + 1]) = toWorld(gx + s[k] * scale * wf + ly * shear, baseY + ly);
				}
				this.AddPolyline(pts, false, color, null, Affine2.Identity);
			}
		}
		else if (ttf != null && ttf.Loops.Count > 0)
		{
			if (!fills.TryGetValue(color, out var list))
			{
				fills[color] = list = new List<double[]>();
			}
			foreach (double[] loop in ttf.Loops)
			{
				var pts = new double[loop.Length];
				for (int k = 0; k + 1 < loop.Length; k += 2)
				{
					double ly = loop[k + 1] * h;
					(pts[k], pts[k + 1]) = toWorld(gx + loop[k] * h * wf + ly * shear, baseY + ly);
				}
				list.Add(pts);
			}
		}
	}

	/// <summary>밑줄·윗줄·취소선: 같은 서식이 이어진 글자 구간마다 한 줄.</summary>
	private void EmitDecorations(TextLine line, double baseY, double fitX, Func<double, double, (double X, double Y)> toWorld)
	{
		void Runs(Func<MTextFormat, bool> on, Func<double, double> yOf)
		{
			int i = 0;
			var items = line.Items;
			while (i < items.Count)
			{
				if (!on(items[i].Tok.Format))
				{
					i++;
					continue;
				}
				int j = i;
				while (j + 1 < items.Count && on(items[j + 1].Tok.Format))
				{
					j++;
				}
				// 끝 공백은 빼고
				int last = j;
				while (last > i && items[last].IsSpace) last--;
				double h = items.Skip(i).Take(last - i + 1).Max(it => it.H);
				double x0 = line.Offset + items[i].X * fitX, x1 = line.Offset + (items[last].X + items[last].Adv) * fitX;
				double yy = baseY + yOf(h);
				var (ax, ay) = toWorld(x0, yy);
				var (bx, by) = toWorld(x1, yy);
				this.AddPolyline(new[] { ax, ay, bx, by }, false, items[i].Color, null, Affine2.Identity);
				i = j + 1;
			}
		}
		Runs(f => f.Underline, h => -h * 0.3);
		Runs(f => f.Overline, h => h * 1.3);
		Runs(f => f.Strike, h => h * 0.5);
	}
}
