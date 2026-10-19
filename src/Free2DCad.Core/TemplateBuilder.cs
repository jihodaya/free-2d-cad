using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Core;

/// <summary>
/// 새 도면용 빈 템플릿. ACadSharp가 처음부터 만든 문서는 AUDIT 오류(TEXTSIZE 0, 스냅 간격 0, 표 스타일 등)가 있으므로
/// 실무 도면에서 도형과 고유 이름을 비운 파일을 템플릿으로 쓴다. 도면층·문자 스타일·치수 스타일은 그대로 유지된다.
/// </summary>
public static class TemplateBuilder
{
	public const string ResourceName = "Free2DCad.Core.Templates.blank-2004.dwg";

	/// <summary>원본 도면에서 도형과 불필요한 블록을 지워 템플릿 파일을 만든다.</summary>
	public static void CreateFromDrawing(string sourcePath, string outputPath)
	{
		CadDocument doc = DwgReader.Read(sourcePath);

		foreach (Entity e in doc.Entities.ToList())
		{
			doc.Entities.Remove(e);
		}

		// 배치(도면 공간)는 뷰포트만 남긴다
		foreach (BlockRecord br in doc.BlockRecords.Where(b => b.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)))
		{
			foreach (Entity e in br.Entities.Where(x => x is not Viewport).ToList())
			{
				br.Entities.Remove(e);
			}
		}

		// 익명 블록(*D 치수, *U 등)과 사용자 블록 제거. 화살촉 블록(_로 시작)은 치수 스타일이 참조하므로 유지
		foreach (BlockRecord br in doc.BlockRecords.ToList())
		{
			string name = br.Name;
			bool isSpace = name.StartsWith("*Model_Space", StringComparison.OrdinalIgnoreCase)
				|| name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase);
			if (isSpace || name.StartsWith('_'))
			{
				continue;
			}
			try
			{
				doc.BlockRecords.Remove(name);
			}
			catch (Exception)
			{
				// 다른 객체가 참조 중이면 남겨 둔다
			}
		}

		ApplyA3(doc);
		doc.Header.Version = ACadVersion.AC1018;
		DwgWriter.Write(outputPath, doc);
	}

	/// <summary>A3 가로 (420 × 297 mm).</summary>
	public const double A3Width = 420, A3Height = 297;

	/// <summary>
	/// 새 도면을 A3 기준으로: 도면 한계·범위 0,0 ~ 420,297, 처음 보기(*Active 뷰포트) A3 가운데,
	/// 배치(Layout1·2) 용지 ISO A3 mm·1:1과 그 뷰포트. 모형 공간 출력 설정(A3 용지에 맞춤)은 원본 그대로 둔다.
	/// 원본 도면은 A0 크기 도곽(1189 × 841) 기준이라 빈 도면이 A0 범위로 열렸었다.
	/// </summary>
	public static void ApplyA3(CadDocument doc)
	{
		var h = doc.Header;
		var cx = A3Width / 2;
		var cy = A3Height / 2;
		h.ModelSpaceLimitsMin = new XY(0, 0);
		h.ModelSpaceLimitsMax = new XY(A3Width, A3Height);
		h.ModelSpaceExtMin = new XYZ(0, 0, 0);
		h.ModelSpaceExtMax = new XYZ(A3Width, A3Height, 0);
		h.PaperSpaceLimitsMin = new XY(0, 0);
		h.PaperSpaceLimitsMax = new XY(A3Width, A3Height);
		h.PaperSpaceExtMin = new XYZ(0, 0, 0);
		h.PaperSpaceExtMax = new XYZ(A3Width, A3Height, 0);

		foreach (VPort vp in doc.VPorts)
		{
			if (vp.Name.Equals("*Active", StringComparison.OrdinalIgnoreCase))
			{
				vp.Center = new XY(cx, cy);
				vp.ViewHeight = A3Height * 1.06; // 용지 위아래 여백 조금
			}
		}

		foreach (Layout layout in doc.Layouts)
		{
			if (layout.Name.Equals(Layout.ModelLayoutName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			layout.PaperSize = "ISO_A3_(420.00_x_297.00_MM)";
			layout.PaperUnits = PlotPaperUnits.Millimeters;
			layout.PaperWidth = A3Width;
			layout.PaperHeight = A3Height;
			layout.PaperRotation = PlotRotation.NoRotation;
			// 원본(ARCH D) 용지의 그림 원점 이동값을 지운다 (남아 있으면 기존 CAD에서 용지와 뷰포트가 어긋남)
			layout.PaperImageOrigin = XY.Zero;
			layout.PaperImageOriginX = 0;
			layout.PaperImageOriginY = 0;
			layout.NumeratorScale = 1;
			layout.DenominatorScale = 1;
			layout.StandardScale = 1;
			layout.MinLimits = new XY(0, 0);
			layout.MaxLimits = new XY(A3Width, A3Height);
			layout.MinExtents = new XYZ(0, 0, 0);
			layout.MaxExtents = new XYZ(A3Width, A3Height, 0);

			// 배치 뷰포트: 첫 번째(용지 전체)는 용지 크기, 나머지는 용지 안 여백 10mm, 모형의 A3 영역을 보여 줌
			var vports = layout.AssociatedBlock?.Entities.OfType<Viewport>().ToList() ?? new List<Viewport>();
			for (int i = 0; i < vports.Count; i++)
			{
				var v = vports[i];
				v.Center = new XYZ(cx, cy, 0);
				v.Width = i == 0 ? A3Width : A3Width - 20;
				v.Height = i == 0 ? A3Height : A3Height - 20;
				v.ViewCenter = new XY(cx, cy);
				v.ViewTarget = XYZ.Zero; // 보기 가운데는 대상점 기준 — 원본의 대상점이 남으면 모형 A3 영역이 안 보임
				v.ViewHeight = A3Height;
			}
		}
	}

	/// <summary>Core에 포함된 템플릿으로 새 문서를 연다. 템플릿이 없으면 null.</summary>
	internal static CadDocument? LoadEmbedded()
	{
		using Stream? stream = typeof(TemplateBuilder).Assembly.GetManifestResourceStream(ResourceName);
		if (stream == null)
		{
			return null;
		}
		using var ms = new MemoryStream();
		stream.CopyTo(ms);
		ms.Position = 0;
		return DwgReader.Read(ms);
	}
}
