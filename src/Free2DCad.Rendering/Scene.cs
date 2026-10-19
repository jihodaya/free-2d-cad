namespace Free2DCad.Rendering;

/// <summary>
/// 렌더링용으로 평탄화한 도면. 모든 좌표는 WCS(월드) 기준이며 블록은 이미 전개되어 있다.
/// </summary>
public sealed class Scene
{
	public List<ScenePolyline> Polylines { get; } = new();

	public List<SceneFill> Fills { get; } = new();

	public List<SceneText> Texts { get; } = new();

	/// <summary>와이프아웃 (그리기 순서대로).</summary>
	public List<SceneWipeout> Wipeouts { get; } = new();

	/// <summary>객체 스냅 후보점 (블록 내부 포함, WCS).</summary>
	public List<SnapPoint> SnapPoints { get; } = new();

	public Bounds Extents { get; private set; } = Bounds.Empty;

	/// <summary>
	/// sources 엔티티에서 나온 도형만 골라 새 장면으로 (color를 주면 그 색으로 칠함 — 지운 도형을 배경색으로 덮을 때).
	/// </summary>
	public static Scene Subset(Scene source, ISet<ACadSharp.Entities.Entity> sources, Rgb? color = null)
	{
		var scene = new Scene();
		foreach (var p in source.Polylines)
		{
			if (p.Source != null && sources.Contains(p.Source))
			{
				scene.Polylines.Add(color is { } c ? p with { Color = c, Dash = null } : p);
				scene.Include(p.Bounds);
			}
		}
		foreach (var f in source.Fills)
		{
			if (f.Source != null && sources.Contains(f.Source))
			{
				scene.Fills.Add(color is { } c ? f with { Color = c, Gradient = null } : f);
				scene.Include(f.Bounds);
			}
		}
		foreach (var t in source.Texts)
		{
			if (t.Source != null && sources.Contains(t.Source))
			{
				scene.Texts.Add(color is { } c ? t with { Color = c } : t);
				scene.Include(t.Bounds);
			}
		}
		return scene;
	}

	/// <summary>범위에 걸친 도형만 (플롯: 영역 밖 도형이 PDF에 들어가 파일이 커지지 않게).</summary>
	public static Scene Crop(Scene source, Bounds area)
	{
		var scene = new Scene();
		foreach (var p in source.Polylines)
		{
			if (p.Bounds.Intersects(area)) { scene.Polylines.Add(p); scene.Include(p.Bounds); }
		}
		foreach (var f in source.Fills)
		{
			if (f.Bounds.Intersects(area)) { scene.Fills.Add(f); scene.Include(f.Bounds); }
		}
		foreach (var t in source.Texts)
		{
			if (t.Bounds.Intersects(area)) { scene.Texts.Add(t); scene.Include(t.Bounds); }
		}
		scene.Wipeouts.AddRange(source.Wipeouts.Where(w => w.Bounds.Intersects(area)));
		scene.SnapPoints.AddRange(source.SnapPoints.Where(sp => sp.X >= area.MinX && sp.X <= area.MaxX && sp.Y >= area.MinY && sp.Y <= area.MaxY));
		return scene;
	}

	internal void Include(Bounds b)
	{
		// NaN이 섞인 범위는 무시 (한 도형의 잘못된 좌표로 전체 화면 좌표계가 깨지지 않게)
		if (double.IsNaN(b.MinX) || double.IsNaN(b.MinY) || double.IsNaN(b.MaxX) || double.IsNaN(b.MaxY))
		{
			return;
		}
		this.Extents = this.Extents.Union(b);
	}
}

/// <summary>ARGB 색상.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
	public static readonly Rgb White = new(255, 255, 255);
}

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
	public static readonly Bounds Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

	public bool IsEmpty => this.MinX > this.MaxX;

	public double Width => this.MaxX - this.MinX;

	public double Height => this.MaxY - this.MinY;

	public Bounds Union(Bounds o) => new(Math.Min(this.MinX, o.MinX), Math.Min(this.MinY, o.MinY), Math.Max(this.MaxX, o.MaxX), Math.Max(this.MaxY, o.MaxY));

	public bool Intersects(Bounds o) => !(o.MinX > this.MaxX || o.MaxX < this.MinX || o.MinY > this.MaxY || o.MaxY < this.MinY);

	public static Bounds FromPoints(double[] xy)
	{
		double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
		for (int i = 0; i < xy.Length; i += 2)
		{
			minX = Math.Min(minX, xy[i]);
			maxX = Math.Max(maxX, xy[i]);
			minY = Math.Min(minY, xy[i + 1]);
			maxY = Math.Max(maxY, xy[i + 1]);
		}
		return new(minX, minY, maxX, maxY);
	}
}

/// <param name="Points">x0,y0,x1,y1,... (WCS)</param>
/// <param name="Dash">선 종류 패턴(월드 단위, 양수=선, 음수=공백). null이면 실선.</param>
/// <param name="Source">이 도형을 만든 모델스페이스 최상위 엔티티 (선택/편집용).</param>
public sealed record ScenePolyline(double[] Points, bool Closed, Rgb Color, double[]? Dash, Bounds Bounds, ACadSharp.Entities.Entity? Source)
{
	/// <summary>SHX 글자 획 (블록 안 문자 포함). 해치 경계 찾기에서 제외한다.</summary>
	public bool IsText { get; init; }

	/// <summary>그리기 순서 (최상위 객체 순번). 와이프아웃이 그보다 앞선 도형만 가린다.</summary>
	public int Order { get; init; }

	/// <summary>선종류 패턴의 시작 위치 (도면 단위, 경로 가운데 기준 대칭 배치).</summary>
	public double DashOffset { get; init; }
}

/// <param name="Loops">각 루프는 x0,y0,x1,y1,... 짝홀(even-odd) 규칙으로 채움.</param>
public sealed record SceneFill(List<double[]> Loops, Rgb Color, Bounds Bounds, ACadSharp.Entities.Entity? Source)
{
	/// <summary>그라데이션 해치면 설정, 아니면 null (단색 Color로 채움).</summary>
	public SceneGradient? Gradient { get; init; }

	/// <summary>그리기 순서 (최상위 객체 순번).</summary>
	public int Order { get; init; }
}

/// <summary>와이프아웃: 이 영역을 배경색으로 칠해 그보다 먼저 그린 도형을 가린다. Frame이면 테두리도.</summary>
public sealed record SceneWipeout(double[] Loop, int Order, bool Frame, Rgb FrameColor, Bounds Bounds, ACadSharp.Entities.Entity? Source)
{
	/// <summary>칠할 색 (MTEXT 배경 마스크). null = 화면 배경색 (와이프아웃).</summary>
	public Rgb? Fill { get; init; }

	/// <summary>배경 마스크의 "도면 배경색 사용" (Fill 없이 배경색).</summary>
	public bool Background { get; init; }
}

/// <summary>그라데이션 채우기. Name은 LINEAR, CYLINDER, INVCYLINDER, SPHERICAL, INVSPHERICAL, HEMISPHERICAL, INVHEMISPHERICAL, CURVED, INVCURVED.</summary>
public sealed record SceneGradient(string Name, Rgb Color1, Rgb Color2, double Angle, bool Centered);

public enum SnapKind { Endpoint, Midpoint, Center, Quadrant, Insertion, Node, Intersection, Nearest }

public readonly record struct SnapPoint(double X, double Y, SnapKind Kind, ACadSharp.Entities.Entity? Source);

public enum TextHAlign { Left, Center, Right }

public enum TextVAlign { Baseline, Bottom, Middle, Top }

/// <summary>
/// 한 개의 문자열 블록. (X,Y)는 정렬 기준점, Height는 대문자 높이(월드 단위).
/// 여러 줄이면 LineSpacing 간격으로 아래로 내려간다. WrapWidth &gt; 0이면 그 폭에서 줄바꿈한다(MTEXT).
/// </summary>
public sealed record SceneText(
	string[] Lines,
	double WrapWidth,
	double X,
	double Y,
	double Height,
	double Rotation,
	double WidthFactor,
	double Oblique,
	TextHAlign HAlign,
	TextVAlign VAlign,
	double LineSpacing,
	string FontFile,
	Rgb Color,
	Bounds Bounds,
	ACadSharp.Entities.Entity? Source)
{
	/// <summary>화면에 그리지 않는 선택용 둘레 (글자는 도형으로 따로 그림).</summary>
	public bool PickOnly { get; init; }

	/// <summary>
	/// 문자 바로 편집용 커서 위치표: 편집 글자(보이는 글자) 경계마다 4개 값 (x, 기준선 y, 줄 높이, 줄 번호), 도면 좌표.
	/// i번째 경계 = i번째 글자 앞. 길이 = (글자 수 + 1) × 4.
	/// </summary>
	public double[]? Caret { get; init; }

	/// <summary>편집 창에서 보이는 글자 (Caret 경계와 같은 순서, 치수는 측정값이 들어간 글).</summary>
	public string? EditText { get; init; }
}
