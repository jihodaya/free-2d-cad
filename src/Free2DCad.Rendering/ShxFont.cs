using System.Text;

namespace Free2DCad.Rendering;

/// <summary>
/// 기존 CAD SHX 글꼴 (선 글꼴). 세 가지 형식을 읽는다:
/// "…-86 shapes 1.0/1.1" (일반, 예: txt.shx·romans.shx), "unifont 1.0" (유니코드), "bigfont 1.0" (한글 등 2바이트, 예: whgtxt.shx).
/// 글자 모양은 방향·길이 벡터, 호, 부분 모양 등의 명령 바이트로 되어 있고, 여기서 선분 목록으로 풀어낸다.
/// </summary>
public sealed class ShxFont
{
	public enum ShxKind { Shapes, Unifont, BigFont }

	private readonly Dictionary<int, byte[]> _shapes;
	private readonly Dictionary<int, Glyph?> _glyphCache = new();
	private readonly object _gate = new();

	/// <summary>글자 선(폰트 단위)과 글자를 그린 뒤 펜 위치(다음 글자 시작점).</summary>
	public sealed record Glyph(List<double[]> Strokes, double AdvanceX, double AdvanceY);

	public ShxKind Kind { get; }

	/// <summary>대문자 높이(폰트 단위). 문자 높이 = Above로 맞춘다.</summary>
	public double Above { get; }

	public double Below { get; }

	private ShxFont(ShxKind kind, Dictionary<int, byte[]> shapes, double above, double below)
	{
		this.Kind = kind;
		this._shapes = shapes;
		this.Above = above > 0 ? above : 1;
		this.Below = below;
	}

	public bool Has(int code) => this._shapes.ContainsKey(code);

	private static readonly Dictionary<string, ShxFont?> Cache = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>파일을 읽는다 (같은 경로는 한 번만). 읽을 수 없으면 null.</summary>
	public static ShxFont? Load(string path)
	{
		lock (Cache)
		{
			if (Cache.TryGetValue(path, out var cached))
			{
				return cached;
			}
			ShxFont? font = null;
			try
			{
				font = Parse(File.ReadAllBytes(path));
			}
			catch (Exception)
			{
				font = null;
			}
			Cache[path] = font;
			return font;
		}
	}

	public static ShxFont? Parse(byte[] data)
	{
		int hdrEnd = Array.IndexOf(data, (byte)0x1A);
		if (hdrEnd < 0 || hdrEnd > 40)
		{
			return null;
		}
		string header = Encoding.ASCII.GetString(data, 0, hdrEnd).Trim();
		int p = hdrEnd + 1;
		var shapes = new Dictionary<int, byte[]>();
		if (header.Contains("unifont", StringComparison.OrdinalIgnoreCase))
		{
			// int32 개수, int16 글꼴 정보 길이, 글꼴 정보(이름\0, above, below, modes, encoding, type, 0)
			int count = BitConverter.ToInt32(data, p); p += 4;
			int infoLen = BitConverter.ToUInt16(data, p); p += 2;
			int nameEnd = Array.IndexOf(data, (byte)0, p);
			double above = data[nameEnd + 1], below = data[nameEnd + 2];
			p += infoLen;
			for (int i = 0; i < count - 1 && p + 4 <= data.Length; i++)
			{
				int code = BitConverter.ToUInt16(data, p); p += 2;
				int len = BitConverter.ToUInt16(data, p); p += 2;
				if (p + len > data.Length) break;
				shapes[code] = SkipName(data, p, len);
				p += len;
			}
			return new ShxFont(ShxKind.Unifont, shapes, above, below);
		}
		if (header.Contains("bigfont", StringComparison.OrdinalIgnoreCase))
		{
			// int16 색인 항목 크기(8), int16 항목 수, int16 범위 수, 범위(int16 시작, int16 끝)×n,
			// 색인(uint16 코드, uint16 길이, uint32 파일 위치)×항목 수
			p += 2;
			int count = BitConverter.ToUInt16(data, p); p += 2;
			int ranges = BitConverter.ToUInt16(data, p); p += 2;
			p += ranges * 4;
			double above = 1, below = 0;
			for (int i = 0; i < count && p + 8 <= data.Length; i++)
			{
				int code = BitConverter.ToUInt16(data, p);
				int len = BitConverter.ToUInt16(data, p + 2);
				int off = BitConverter.ToInt32(data, p + 4);
				p += 8;
				if (off <= 0 || off + len > data.Length || len == 0) continue;
				if (code == 0)
				{
					// 글꼴 정보: 이름\0, 높이, 0, modes, 폭, 0
					int ne = Array.IndexOf(data, (byte)0, off);
					if (ne > 0 && ne + 1 < data.Length) above = data[ne + 1];
					continue;
				}
				shapes[code] = SkipName(data, off, len);
			}
			return new ShxFont(ShxKind.BigFont, shapes, above, below);
		}
		if (header.Contains("shapes", StringComparison.OrdinalIgnoreCase))
		{
			// int16 첫 번호, int16 마지막 번호, int16 개수, 색인(uint16 번호, uint16 길이)×개수, 이어서 정의들
			p += 4;
			int count = BitConverter.ToUInt16(data, p); p += 2;
			var index = new List<(int Code, int Len)>();
			for (int i = 0; i < count; i++)
			{
				index.Add((BitConverter.ToUInt16(data, p), BitConverter.ToUInt16(data, p + 2)));
				p += 4;
			}
			double above = 1, below = 0;
			foreach (var (code, len) in index)
			{
				if (p + len > data.Length) break;
				if (code == 0)
				{
					int ne = Array.IndexOf(data, (byte)0, p);
					if (ne > 0 && ne + 2 < data.Length)
					{
						above = data[ne + 1];
						below = data[ne + 2];
					}
				}
				else
				{
					shapes[code] = SkipName(data, p, len);
				}
				p += len;
			}
			return new ShxFont(ShxKind.Shapes, shapes, above, below);
		}
		return null;
	}

	/// <summary>정의 바이트 앞의 이름(\0으로 끝남)을 건너뛴다.</summary>
	private static byte[] SkipName(byte[] data, int start, int len)
	{
		int end = start + len;
		int nul = Array.IndexOf(data, (byte)0, start, len);
		int from = nul >= 0 ? nul + 1 : start;
		return from < end ? data[from..end] : Array.Empty<byte>();
	}

	/// <summary>
	/// 한 줄 문자를 선으로 (미리보기용): 문자 높이 = height, 기준선 y=0, 위쪽 +. 영문은 main, 2바이트 글자는 big(CP949 코드).
	/// 두 글꼴에 없는 글자는 빈칸. 반환: 선 목록과 전체 폭.
	/// </summary>
	public static (List<double[]> Strokes, double Width) LayoutLine(string text, ShxFont? main, ShxFont? big, double height, double widthFactor = 1)
	{
		System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
		var cp949 = System.Text.Encoding.GetEncoding(949);
		var result = new List<double[]>();
		double pen = 0;
		foreach (char ch in text)
		{
			ShxFont? font = null;
			Glyph? g = null;
			if (main != null && main.GetGlyph(ch) is { } mg)
			{
				font = main;
				g = mg;
			}
			else if (big != null && ch > 0x7F)
			{
				byte[] b = cp949.GetBytes(ch.ToString());
				int code = b.Length == 2 ? (b[0] << 8) | b[1] : b[0];
				if (big.GetGlyph(code) is { } bg)
				{
					font = big;
					g = bg;
				}
			}
			if (font == null || g == null)
			{
				pen += height * 0.7 * widthFactor;
				continue;
			}
			double k = height / font.Above;
			foreach (double[] s in g.Strokes)
			{
				var pts = new double[s.Length];
				for (int i = 0; i + 1 < s.Length; i += 2)
				{
					pts[i] = pen + s[i] * k * widthFactor;
					pts[i + 1] = s[i + 1] * k;
				}
				result.Add(pts);
			}
			pen += g.AdvanceX * k * widthFactor;
		}
		return (result, pen);
	}

	/// <summary>글자 모양을 선분으로 (캐시). 없는 글자는 null.</summary>
	public Glyph? GetGlyph(int code)
	{
		lock (this._gate)
		{
			if (this._glyphCache.TryGetValue(code, out var g))
			{
				return g;
			}
			g = null;
			if (this._shapes.TryGetValue(code, out byte[]? bytes))
			{
				try
				{
					var st = new State();
					this.Run(bytes, st, depth: 0);
					st.Flush();
					g = new Glyph(st.Strokes, st.X, st.Y);
				}
				catch (Exception)
				{
					g = null;
				}
			}
			this._glyphCache[code] = g;
			return g;
		}
	}

	private sealed class State
	{
		public double X, Y, Scale = 1;
		public bool PenDown = true;
		public readonly Stack<(double X, double Y)> Stack = new();
		public readonly List<double[]> Strokes = new();
		private List<double>? _cur;

		public void MoveTo(double nx, double ny)
		{
			if (this.PenDown)
			{
				if (this._cur == null)
				{
					this._cur = new List<double> { this.X, this.Y };
				}
				this._cur.Add(nx);
				this._cur.Add(ny);
			}
			else
			{
				this.Flush();
			}
			this.X = nx;
			this.Y = ny;
		}

		public void Flush()
		{
			if (this._cur != null && this._cur.Count >= 4)
			{
				this.Strokes.Add(this._cur.ToArray());
			}
			this._cur = null;
		}
	}

	private static readonly (double X, double Y)[] Dir =
	{
		(1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (-0.5, 1), (-1, 1), (-1, 0.5),
		(-1, 0), (-1, -0.5), (-1, -1), (-0.5, -1), (0, -1), (0.5, -1), (1, -1), (1, -0.5),
	};

	private void Run(byte[] b, State st, int depth)
	{
		if (depth > 6) return;
		int i = 0;
		bool skipNext = false;
		while (i < b.Length)
		{
			int start = i;
			byte c = b[i++];
			bool skip = skipNext;
			skipNext = false;
			var s = skip ? new State { X = st.X, Y = st.Y, Scale = st.Scale, PenDown = false } : st; // 세로 문자 전용 명령은 건너뜀
			if (c > 0x0F)
			{
				int len = c >> 4;
				var d = Dir[c & 0x0F];
				s.MoveTo(s.X + d.X * len * s.Scale, s.Y + d.Y * len * s.Scale);
				continue;
			}
			switch (c)
			{
				case 0:
					return;
				case 1:
					s.PenDown = true;
					break;
				case 2:
					s.Flush();
					s.PenDown = false;
					break;
				case 3:
					if (i < b.Length && b[i] != 0) s.Scale /= b[i];
					i++;
					break;
				case 4:
					if (i < b.Length) s.Scale *= b[i];
					i++;
					break;
				case 5:
					s.Stack.Push((s.X, s.Y));
					break;
				case 6:
					if (s.Stack.Count > 0)
					{
						var (px, py) = s.Stack.Pop();
						s.Flush();
						s.X = px;
						s.Y = py;
					}
					break;
				case 7:
				{
					int sub;
					if (this.Kind == ShxKind.Shapes)
					{
						sub = b[i++];
					}
					else
					{
						sub = (b[i] << 8) | b[i + 1];
						i += 2;
						if (this.Kind == ShxKind.BigFont && sub == 0)
						{
							// 확장 부분 모양: 번호(2), 기준 x, y, 폭, 높이 — 위치·크기 조정은 근사(번호만 사용)
							sub = (b[i] << 8) | b[i + 1];
							i += 6;
						}
					}
					if (!skip && this._shapes.TryGetValue(sub, out byte[]? subBytes))
					{
						s.Flush();
						bool pen = s.PenDown;
						this.Run(subBytes, s, depth + 1);
						s.Flush();
						s.PenDown = pen;
					}
					break;
				}
				case 8:
					s.MoveTo(s.X + (sbyte)b[i] * s.Scale, s.Y + (sbyte)b[i + 1] * s.Scale);
					i += 2;
					break;
				case 9:
					while (i + 1 < b.Length)
					{
						sbyte dx = (sbyte)b[i], dy = (sbyte)b[i + 1];
						i += 2;
						if (dx == 0 && dy == 0) break;
						s.MoveTo(s.X + dx * s.Scale, s.Y + dy * s.Scale);
					}
					break;
				case 0x0A:
				{
					double r = b[i] * s.Scale;
					sbyte oct = (sbyte)b[i + 1];
					i += 2;
					Octant(s, r, oct, 0, 0);
					break;
				}
				case 0x0B:
				{
					int so = b[i], eo = b[i + 1];
					double r = ((b[i + 2] << 8) | b[i + 3]) * s.Scale;
					sbyte oct = (sbyte)b[i + 4];
					i += 5;
					Octant(s, r, oct, so, eo);
					break;
				}
				case 0x0C:
					Bulge(s, (sbyte)b[i] * s.Scale, (sbyte)b[i + 1] * s.Scale, (sbyte)b[i + 2]);
					i += 3;
					break;
				case 0x0D:
					while (i + 1 < b.Length)
					{
						sbyte dx = (sbyte)b[i], dy = (sbyte)b[i + 1];
						if (dx == 0 && dy == 0)
						{
							i += 2;
							break;
						}
						Bulge(s, dx * s.Scale, dy * s.Scale, (sbyte)b[i + 2]);
						i += 3;
					}
					break;
				case 0x0E:
					skipNext = true; // 다음 명령은 세로 쓰기에서만
					break;
			}
			_ = start;
		}
	}

	/// <summary>팔분원 호 (명령 10) / 부분 팔분원 호 (명령 11).</summary>
	private static void Octant(State s, double r, sbyte oct, int startOffset, int endOffset)
	{
		bool cw = oct < 0;
		int v = oct & 0x7F;
		int so = (v >> 4) & 7;
		int n = v & 7;
		if (n == 0) n = 8;
		double a0 = so * 45.0 + startOffset * 45.0 / 256.0;
		double sweep = n * 45.0;
		if (startOffset != 0 || endOffset != 0)
		{
			double a1 = (so + (cw ? -(n - 1) : n - 1)) * 45.0 + (endOffset == 0 ? (cw ? 0 : 45) : endOffset * 45.0 / 256.0 * (cw ? -1 : 1));
			sweep = Math.Abs(a1 - a0);
		}
		double sign = cw ? -1 : 1;
		double rad0 = a0 * Math.PI / 180;
		double cx = s.X - r * Math.Cos(rad0), cy = s.Y - r * Math.Sin(rad0);
		int steps = Math.Max(2, (int)Math.Ceiling(sweep / 11.25));
		for (int k = 1; k <= steps; k++)
		{
			double a = (a0 + sign * sweep * k / steps) * Math.PI / 180;
			s.MoveTo(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
		}
	}

	/// <summary>변위 + 볼록 호 (명령 12/13). bulge 127 = 반원.</summary>
	private static void Bulge(State s, double dx, double dy, sbyte bulge)
	{
		double ex = s.X + dx, ey = s.Y + dy;
		if (bulge == 0)
		{
			s.MoveTo(ex, ey);
			return;
		}
		double bl = bulge / 127.0;
		double theta = 4 * Math.Atan(bl); // 포함각
		double chord = Math.Sqrt(dx * dx + dy * dy);
		if (chord < 1e-12)
		{
			return;
		}
		double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
		double mx = (s.X + ex) / 2, my = (s.Y + ey) / 2;
		double h = r * Math.Cos(theta / 2) * Math.Sign(bl); // 중심까지 거리 (현의 왼쪽이 +)
		double nx = -dy / chord, ny = dx / chord;
		double cx = mx + nx * h, cy = my + ny * h;
		double a0 = Math.Atan2(s.Y - cy, s.X - cx);
		int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 16)));
		for (int k = 1; k <= steps; k++)
		{
			double a = a0 + theta * k / steps;
			s.MoveTo(cx + Math.Abs(r) * Math.Cos(a), cy + Math.Abs(r) * Math.Sin(a));
		}
		s.X = ex;
		s.Y = ey;
	}
}
