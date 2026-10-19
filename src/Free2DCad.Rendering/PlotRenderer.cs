using SkiaSharp;

namespace Free2DCad.Rendering;

/// <summary>인쇄 색 처리.</summary>
public enum PlotColorMode
{
	/// <summary>화면과 동일 (검은 배경용).</summary>
	None,

	/// <summary>컬러 인쇄: 흰색 계열(7번 색)만 검정으로 (acad.ctb와 같음).</summary>
	Color,

	/// <summary>흑백 인쇄: 모든 선과 문자를 검정으로 (monochrome.ctb와 같음).</summary>
	Monochrome,
}

/// <summary>플롯 설정.</summary>
/// <param name="Area">인쇄 영역. 모형은 월드 좌표, 배치는 도면 공간 좌표.</param>
/// <param name="PaperWidthMm">용지 폭 (방향 반영 후, mm).</param>
/// <param name="PaperHeightMm">용지 높이 (방향 반영 후, mm).</param>
/// <param name="MarginMm">인쇄할 수 없는 여백 (mm).</param>
/// <param name="FitToPaper">true면 용지에 맞춤, false면 <paramref name="MmPerUnit"/> 축척.</param>
/// <param name="MmPerUnit">도면 1단위가 종이에서 몇 mm인지 (1:1 = 1, 1:2 = 0.5, 2:1 = 2).</param>
/// <param name="Center">true면 용지 가운데, false면 여백의 왼쪽 아래에 맞춤.</param>
/// <param name="LineWidthMm">선 굵기 (mm). 0이면 가장 가는 선.</param>
public sealed record PlotOptions(
	Bounds Area,
	double PaperWidthMm,
	double PaperHeightMm,
	double MarginMm = 5,
	bool FitToPaper = true,
	double MmPerUnit = 1,
	bool Center = true,
	PlotColorMode Colors = PlotColorMode.Color,
	double LineWidthMm = 0.18);

/// <summary>
/// 모형 장면 또는 배치를 흰 종이에 그린다. PDF(벡터)와 프린터용 비트맵을 만든다.
/// </summary>
public static class PlotRenderer
{
	private const double PointsPerMm = 72.0 / 25.4;

	/// <summary>실제로 쓰일 축척 (종이 mm / 도면 단위). 용지 맞춤이면 계산값.</summary>
	public static double EffectiveMmPerUnit(PlotOptions o)
	{
		if (!o.FitToPaper)
		{
			return o.MmPerUnit;
		}
		double w = Math.Max(o.PaperWidthMm - 2 * o.MarginMm, 1);
		double h = Math.Max(o.PaperHeightMm - 2 * o.MarginMm, 1);
		return Math.Min(w / Math.Max(o.Area.Width, 1e-9), h / Math.Max(o.Area.Height, 1e-9));
	}

	/// <summary>
	/// 한 페이지를 그린다. 출력 단위는 캔버스 좌표 (PDF: pt, 비트맵: px).
	/// </summary>
	/// <param name="unitsPerMm">출력 단위 / mm (PDF 72/25.4, 비트맵 dpi/25.4).</param>
	/// <param name="layout">배치를 인쇄하면 지정. null이면 모형 장면을 인쇄.</param>
	public static void RenderPage(SKCanvas canvas, float pageWidth, float pageHeight, double unitsPerMm, PlotOptions o, Scene modelScene, LayoutScene? layout)
	{
		canvas.DrawRect(new SKRect(0, 0, pageWidth, pageHeight), new SKPaint { Color = SKColors.White });

		double scale = EffectiveMmPerUnit(o) * unitsPerMm; // 출력 단위 / 도면 단위
		double margin = o.MarginMm * unitsPerMm;
		double cx, cy;
		if (o.Center)
		{
			cx = (o.Area.MinX + o.Area.MaxX) / 2;
			cy = (o.Area.MinY + o.Area.MaxY) / 2;
		}
		else
		{
			// 영역의 왼쪽 아래가 여백 모서리에 오도록
			cx = o.Area.MinX + (pageWidth / 2.0 - margin) / scale;
			cy = o.Area.MinY + (pageHeight / 2.0 - margin) / scale;
		}
		var view = new ViewTransform(cx, cy, scale, (int)Math.Ceiling(pageWidth), (int)Math.Ceiling(pageHeight));
		float lineWidth = (float)(o.LineWidthMm * unitsPerMm);

		if (layout == null)
		{
			// 모형 공간 출력: 종이에 보이는 범위의 도형만
			var (wx0, wy0) = view.ToWorld(0, 0);
			var (wx1, wy1) = view.ToWorld(pageWidth, pageHeight);
			modelScene = Scene.Crop(modelScene, new Bounds(Math.Min(wx0, wx1), Math.Min(wy0, wy1), Math.Max(wx0, wx1), Math.Max(wy0, wy1)));
		}
		using var model = new SkiaSceneRenderer(modelScene)
		{
			Background = SKColors.White,
			PlotColors = o.Colors,
			MinLineWidth = lineWidth,
		};

		// 인쇄 가능 영역 밖은 잘라낸다
		canvas.Save();
		canvas.ClipRect(new SKRect((float)margin, (float)margin, (float)(pageWidth - margin), (float)(pageHeight - margin)));
		if (layout == null)
		{
			model.Render(canvas, view, drawBackground: false);
		}
		else
		{
			using var lr = new LayoutRenderer(layout, model)
			{
				Background = SKColors.White,
				PaperColor = SKColors.White,
				PlotMode = true,
			};
			lr.PaperRenderer.PlotColors = o.Colors;
			lr.PaperRenderer.MinLineWidth = lineWidth;
			lr.Render(canvas, view);
		}
		canvas.Restore();
	}

	/// <summary>벡터 PDF로 저장한다 (한 페이지).</summary>
	public static void ExportPdf(string path, PlotOptions o, Scene modelScene, LayoutScene? layout)
	{
		using var stream = File.Create(path);
		using var doc = SKDocument.CreatePdf(stream, new SKDocumentPdfMetadata
		{
			Title = Path.GetFileNameWithoutExtension(path),
			Creator = "Free 2D CAD",
			RasterDpi = 300,
		});
		float w = (float)(o.PaperWidthMm * PointsPerMm);
		float h = (float)(o.PaperHeightMm * PointsPerMm);
		SKCanvas canvas = doc.BeginPage(w, h);
		RenderPage(canvas, w, h, PointsPerMm, o, modelScene, layout);
		doc.EndPage();
		doc.Close();
	}

	/// <summary>프린터·미리보기용 비트맵. 호출한 쪽에서 Dispose.</summary>
	public static SKBitmap RenderBitmap(PlotOptions o, double dpi, Scene modelScene, LayoutScene? layout)
	{
		double unitsPerMm = dpi / 25.4;
		int w = Math.Max(1, (int)Math.Round(o.PaperWidthMm * unitsPerMm));
		int h = Math.Max(1, (int)Math.Round(o.PaperHeightMm * unitsPerMm));
		var bmp = new SKBitmap(w, h);
		using var canvas = new SKCanvas(bmp);
		RenderPage(canvas, w, h, unitsPerMm, o, modelScene, layout);
		return bmp;
	}

	/// <summary>자주 쓰는 용지 (세로 기준 폭 x 높이, mm).</summary>
	public static readonly IReadOnlyList<(string Name, double Width, double Height)> PaperSizes = new[]
	{
		("A4", 210.0, 297.0),
		("A3", 297.0, 420.0),
		("A2", 420.0, 594.0),
		("A1", 594.0, 841.0),
		("A0", 841.0, 1189.0),
		("B4 (JIS)", 257.0, 364.0),
		("Letter", 215.9, 279.4),
	};
}
