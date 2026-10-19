using ACadSharp;
using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Hatching;

/// <summary>기존 CAD '해치 및 그라데이션' 대화상자의 '유형'.</summary>
public enum HatchKind
{
	/// <summary>미리 정의됨 (패턴 목록에서 선택).</summary>
	Predefined,

	/// <summary>사용자 정의 (평행선, 간격·이중).</summary>
	UserDefined,
}

/// <summary>그라데이션 모양 (기존 CAD 그라데이션 탭의 견본 9개 순서).</summary>
public enum GradientShape
{
	Linear,
	Cylinder,
	InvCylinder,
	Spherical,
	Hemispherical,
	Curved,
	InvSpherical,
	InvHemispherical,
	InvCurved,
}

/// <summary>해치 대화상자의 모든 설정. 대화상자와 HATCH 명령이 같이 쓴다.</summary>
public sealed class HatchSettings
{
	public HatchKind Kind { get; set; } = HatchKind.Predefined;

	/// <summary>패턴 이름 (HatchPatternLibrary). 기본 ANSI31 (기존 CAD 기본값).</summary>
	public string PatternName { get; set; } = "ANSI31";

	/// <summary>각도(도).</summary>
	public double AngleDegrees { get; set; }

	/// <summary>축척 (미리 정의 패턴).</summary>
	public double Scale { get; set; } = 1.0;

	/// <summary>간격 (사용자 정의).</summary>
	public double Spacing { get; set; } = 1.0;

	/// <summary>이중 (사용자 정의일 때 직교 선 추가).</summary>
	public bool IsDouble { get; set; }

	/// <summary>연관: 객체 선택으로 만든 경우 경계 객체와 연결.</summary>
	public bool Associative { get; set; } = true;

	/// <summary>별도 해치 생성: 경계마다 해치를 따로 만든다.</summary>
	public bool SeparateHatches { get; set; }

	/// <summary>고립영역 표시 스타일. 기존 CAD 기본은 '외부'.</summary>
	public HatchStyleType IslandStyle { get; set; } = HatchStyleType.Outer;

	/// <summary>고립영역 감지 (끄면 섬을 무시).</summary>
	public bool DetectIslands { get; set; } = true;

	/// <summary>색상. null이면 현재 색 (보통 BYLAYER).</summary>
	public Color? Color { get; set; }

	/// <summary>도면층 이름. null이면 현재 도면층.</summary>
	public string? Layer { get; set; }

	/// <summary>투명도 0~90 (%). null이면 현재 사용(도면층별).</summary>
	public short? Transparency { get; set; }

	/// <summary>해치 원점. null이면 현재 원점(0,0) 사용.</summary>
	public XY? Origin { get; set; }

	/// <summary>그라데이션 탭에서 확인했으면 true.</summary>
	public bool IsGradient { get; set; }

	public GradientShape Gradient { get; set; } = GradientShape.Linear;

	/// <summary>하나의 색상(true) / 두 색상(false).</summary>
	public bool GradientOneColor { get; set; }

	public Color GradientColor1 { get; set; } = new Color(0, 0, 255);

	public Color GradientColor2 { get; set; } = new Color(255, 255, 0);

	/// <summary>하나의 색상일 때 어둡게~밝게 (0~1).</summary>
	public double GradientTint { get; set; } = 1.0;

	/// <summary>방향: 중앙.</summary>
	public bool GradientCentered { get; set; } = true;

	public double GradientAngleDegrees { get; set; }

	public HatchSettings Clone() => (HatchSettings)this.MemberwiseClone();

	public static string GradientName(GradientShape shape) => shape switch
	{
		GradientShape.Linear => "LINEAR",
		GradientShape.Cylinder => "CYLINDER",
		GradientShape.InvCylinder => "INVCYLINDER",
		GradientShape.Spherical => "SPHERICAL",
		GradientShape.Hemispherical => "HEMISPHERICAL",
		GradientShape.Curved => "CURVED",
		GradientShape.InvSpherical => "INVSPHERICAL",
		GradientShape.InvHemispherical => "INVHEMISPHERICAL",
		_ => "INVCURVED",
	};
}

/// <summary>설정과 경계로 Hatch 엔티티를 만든다 (도면에 추가하지는 않음).</summary>
public static class HatchFactory
{
	/// <summary>
	/// 폴리곤 경계로 해치 생성. loops[0]이 바깥 경계, 나머지는 섬(고립영역). 좌표는 x0,y0,x1,y1,...
	/// </summary>
	public static Hatch CreateFromLoops(IReadOnlyList<double[]> loops, HatchSettings s)
	{
		var hatch = CreateEmpty(s);
		for (int i = 0; i < loops.Count; i++)
		{
			double[] pts = loops[i];
			var verts = new List<XYZ>(pts.Length / 2);
			for (int k = 0; k + 1 < pts.Length; k += 2)
			{
				verts.Add(new XYZ(pts[k], pts[k + 1], 0)); // Z는 볼록(bulge) = 0
			}
			var path = new Hatch.BoundaryPath();
			path.Edges.Add(new Hatch.BoundaryPath.Polyline(verts, true));
			path.Flags = i == 0
				? BoundaryPathFlags.External | BoundaryPathFlags.Derived | BoundaryPathFlags.Outermost
				: BoundaryPathFlags.Derived | BoundaryPathFlags.Outermost;
			hatch.Paths.Add(path);
		}
		hatch.IsAssociative = false; // 내부 점으로 찾은 경계는 경계 객체와 연결하지 않음
		return hatch;
	}

	/// <summary>
	/// 선택한 닫힌 객체(원, 닫힌 폴리선, 타원 등)를 경계로 해치 생성. 연관이면 경계 객체와 연결.
	/// </summary>
	public static Hatch CreateFromEntities(IReadOnlyList<Entity> boundaries, HatchSettings s)
	{
		var hatch = CreateEmpty(s);
		foreach (var e in boundaries)
		{
			var path = new Hatch.BoundaryPath(new[] { e });
			if (hatch.Paths.Count == 0)
			{
				path.Flags = BoundaryPathFlags.External | BoundaryPathFlags.Outermost;
			}
			hatch.Paths.Add(path);
		}
		hatch.IsAssociative = s.Associative;
		if (s.Associative)
		{
			// 연관 해치는 경계 객체가 해치를 반응자(reactor)로 가져야 한다. 없으면 기존 CAD AUDIT 오류
			// ("Associative entity ... missing Hatch as reactor")
			foreach (var e in boundaries)
			{
				if (!e.Reactors.Contains(hatch))
				{
					e.AddReactor(hatch);
				}
			}
		}
		return hatch;
	}

	private static Hatch CreateEmpty(HatchSettings s)
	{
		var hatch = new Hatch { Style = s.DetectIslands ? s.IslandStyle : HatchStyleType.Ignore };

		if (s.IsGradient)
		{
			hatch.Pattern = HatchPattern.Solid;
			hatch.IsSolid = true;
			hatch.PatternType = HatchPatternType.SolidFill;
			var g = new HatchGradientPattern(HatchSettings.GradientName(s.Gradient))
			{
				Enabled = true,
				Angle = s.GradientAngleDegrees * Math.PI / 180.0,
				Shift = s.GradientCentered ? 0.0 : 0.25,
				IsSingleColorGradient = s.GradientOneColor,
				ColorTint = s.GradientTint,
			};
			g.Colors.Add(new GradientColor { Value = 0, Color = s.GradientColor1 });
			g.Colors.Add(new GradientColor { Value = 1, Color = s.GradientOneColor ? s.GradientColor1 : s.GradientColor2 });
			hatch.GradientColor = g;
		}
		else if (s.Kind == HatchKind.UserDefined)
		{
			hatch.Pattern = HatchLibraryPattern(HatchPatternLibrary.UserDefined(Math.Max(s.Spacing, 1e-6), s.IsDouble), s, scale: 1.0);
			hatch.IsSolid = false;
			hatch.IsDouble = s.IsDouble;
			hatch.PatternType = (HatchPatternType)0; // DXF 76: 0 = 사용자 정의 (ACadSharp 열거형 이름은 뜻이 다르다)
		}
		else if (string.Equals(s.PatternName, "SOLID", StringComparison.OrdinalIgnoreCase))
		{
			hatch.Pattern = HatchPattern.Solid;
			hatch.IsSolid = true;
			hatch.PatternType = HatchPatternType.SolidFill;
		}
		else
		{
			var pat = HatchPatternLibrary.Get(s.PatternName) ?? HatchPatternLibrary.Get("ANSI31")!;
			hatch.Pattern = HatchLibraryPattern(pat, s, Math.Max(s.Scale, 1e-9));
			hatch.IsSolid = false;
			hatch.PatternType = (HatchPatternType)1; // DXF 76: 1 = 미리 정의 (기존 CAD 도면의 ANSI31 등과 같음)
		}

		if (!hatch.IsSolid)
		{
			StampAngleScale(hatch, s);
		}
		if (s.Color is { } c)
		{
			hatch.Color = c;
		}
		if (s.Transparency is { } t)
		{
			hatch.Transparency = new Transparency(Math.Clamp(t, (short)0, (short)90));
		}
		return hatch;
	}

	/// <summary>
	/// 패턴에 각도·축척·원점을 미리 적용하고 Hatch의 PatternAngle/PatternScale 값만 기록한다.
	/// (Hatch.PatternAngle/PatternScale 세터는 패턴을 다시 변환하므로 세터를 쓰지 않고 패턴을 직접 만든다)
	/// </summary>
	private static HatchPattern HatchLibraryPattern(HatchPattern pat, HatchSettings s, double scale)
	{
		double angle = s.AngleDegrees * Math.PI / 180.0;
		pat.Update(XY.Zero, angle, scale);
		if (s.Origin is { } o)
		{
			foreach (var l in pat.Lines)
			{
				l.BasePoint = new XY(l.BasePoint.X + o.X, l.BasePoint.Y + o.Y);
			}
		}
		return pat;
	}

	/// <summary>Hatch에 각도/축척 값을 기록 (DWG 속성용). 패턴은 이미 변환된 상태여야 한다.</summary>
	private static void StampAngleScale(Hatch hatch, HatchSettings s)
	{
		var field = typeof(Hatch).GetField("_patternAngle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		field?.SetValue(hatch, s.AngleDegrees * Math.PI / 180.0);
		var scaleField = typeof(Hatch).GetField("_patternScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		scaleField?.SetValue(hatch, s.Kind == HatchKind.UserDefined ? 1.0 : s.Scale);
	}
}
