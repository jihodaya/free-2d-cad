using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using SkiaSharp;

namespace Free2DCad.Rendering;

/// <summary>배치(도면 공간)의 뷰포트: 도면 공간 사각형 안에 모형 공간을 축척으로 보여 준다.</summary>
/// <param name="Rect">도면 공간 좌표의 뷰포트 사각형.</param>
/// <param name="ModelCenterX">뷰포트 중심에 오는 모형 공간 좌표.</param>
/// <param name="Scale">도면 공간 단위 / 모형 공간 단위.</param>
public sealed record LayoutViewport(Bounds Rect, double ModelCenterX, double ModelCenterY, double Scale, Viewport Source);

/// <summary>
/// 배치 한 개의 표시 정보: 용지 범위, 도면 공간 도형, 뷰포트. 뷰 비틀기(TwistAngle)는 아직 무시한다.
/// </summary>
public sealed class LayoutScene
{
	private LayoutScene(Layout layout, Scene paperScene, Bounds paper, List<LayoutViewport> viewports)
	{
		this.Layout = layout;
		this.PaperScene = paperScene;
		this.Paper = paper;
		this.Viewports = viewports;
	}

	public Layout Layout { get; }

	/// <summary>도면 공간에 직접 그린 도형 (표제란, 뷰포트 테두리 등).</summary>
	public Scene PaperScene { get; }

	/// <summary>용지 범위 (도면 공간 좌표).</summary>
	public Bounds Paper { get; }

	public List<LayoutViewport> Viewports { get; }

	/// <summary>범위 줌 대상: 용지와 도면 공간 도형을 합친 범위.</summary>
	public Bounds Extents => this.PaperScene.Extents.IsEmpty ? this.Paper : this.Paper.Union(this.PaperScene.Extents);

	public static LayoutScene Build(CadDocument doc, Layout layout)
	{
		Scene paperScene = layout.AssociatedBlock != null
			? SceneBuilder.BuildBlock(doc, layout.AssociatedBlock)
			: new Scene();

		var viewports = new List<LayoutViewport>();
		foreach (Viewport vp in layout.Viewports ?? Enumerable.Empty<Viewport>())
		{
			// 1번 뷰포트는 용지 자체. 새로 만든 뷰포트는 저장 전까지 Id가 0일 수 있음
			if (vp.Id == 1 || vp.RepresentsPaper || vp.ViewHeight <= 0 || vp.Height <= 0)
			{
				continue;
			}
			var rect = new Bounds(vp.Center.X - vp.Width / 2, vp.Center.Y - vp.Height / 2, vp.Center.X + vp.Width / 2, vp.Center.Y + vp.Height / 2);
			viewports.Add(new LayoutViewport(rect, vp.ViewCenter.X + vp.ViewTarget.X, vp.ViewCenter.Y + vp.ViewTarget.Y, vp.Height / vp.ViewHeight, vp));
		}

		return new LayoutScene(layout, paperScene, PaperBounds(layout), viewports);
	}

	private static Bounds PaperBounds(Layout layout)
	{
		// 배치의 한계(LIMMIN/LIMMAX)가 유효하면 그것이 용지 범위
		if (layout.MaxLimits.X > layout.MinLimits.X && layout.MaxLimits.Y > layout.MinLimits.Y)
		{
			return new Bounds(layout.MinLimits.X, layout.MinLimits.Y, layout.MaxLimits.X, layout.MaxLimits.Y);
		}

		double w = layout.PaperWidth > 0 ? layout.PaperWidth : 420;
		double h = layout.PaperHeight > 0 ? layout.PaperHeight : 297;
		if (layout.PaperUnits == PlotPaperUnits.Inches)
		{
			w /= 25.4;
			h /= 25.4;
		}
		// 용지 크기는 세로 기준으로 저장되므로 회전이 0/180이면 가로로 눕힌다
		bool landscape = layout.PaperRotation is PlotRotation.NoRotation or PlotRotation.Degrees180;
		if (landscape && h > w || !landscape && w > h)
		{
			(w, h) = (h, w);
		}
		return new Bounds(0, 0, w, h);
	}
}

/// <summary>
/// <see cref="LayoutScene"/>을 그린다: 회색 바탕 → 용지 → 뷰포트별 모형 공간(잘라내기) → 도면 공간 도형.
/// </summary>
public sealed class LayoutRenderer : IDisposable
{
	private readonly LayoutScene _layout;
	private readonly SkiaSceneRenderer _paperRenderer;
	private readonly SkiaSceneRenderer _modelRenderer;

	/// <param name="modelRenderer">모형 공간 렌더러 (모형 탭과 공유, 여기서 해제하지 않음).</param>
	public LayoutRenderer(LayoutScene layout, SkiaSceneRenderer modelRenderer)
	{
		this._layout = layout;
		this._paperRenderer = new SkiaSceneRenderer(layout.PaperScene);
		this._modelRenderer = modelRenderer;
	}

	public LayoutScene Layout => this._layout;

	/// <summary>도면 공간 도형 렌더러 (인쇄 색 설정용).</summary>
	public SkiaSceneRenderer PaperRenderer => this._paperRenderer;

	/// <summary>인쇄용: 용지 테두리와 그림자를 그리지 않는다.</summary>
	public bool PlotMode { get; set; }

	public SKColor Background { get; set; } = new SKColor(0x46, 0x4B, 0x52);

	public SKColor PaperColor { get; set; } = SKColors.Black;

	public void Render(SKCanvas canvas, ViewTransform view)
	{
		canvas.Save();
		canvas.ClipRect(new SKRect(0, 0, view.Width, view.Height));

		using (var bg = new SKPaint { Color = this.Background })
		{
			canvas.DrawRect(new SKRect(0, 0, view.Width, view.Height), bg);
		}

		SKRect paper = ToScreen(view, this._layout.Paper);
		using (var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 110) })
		using (var fill = new SKPaint { Color = this.PaperColor })
		using (var edge = new SKPaint { Color = new SKColor(0x90, 0x90, 0x90), Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
		{
			if (!this.PlotMode)
			{
				var sh = paper;
				sh.Offset(5, 5);
				canvas.DrawRect(sh, shadow);
			}
			canvas.DrawRect(paper, fill);
			if (!this.PlotMode)
			{
				canvas.DrawRect(paper, edge);
			}
		}

		foreach (LayoutViewport vp in this._layout.Viewports)
		{
			SKRect r = ToScreen(view, vp.Rect);
			if (r.Right < 0 || r.Left > view.Width || r.Bottom < 0 || r.Top > view.Height)
			{
				continue;
			}
			// 도면 공간 뷰의 중심을 모형 공간으로 옮긴 뷰: 모형점 m → 도면점 = 뷰포트중심 + (m - 모형중심) × 축척
			double cx = vp.ModelCenterX - ((vp.Rect.MinX + vp.Rect.MaxX) / 2 - view.CenterX) / vp.Scale;
			double cy = vp.ModelCenterY - ((vp.Rect.MinY + vp.Rect.MaxY) / 2 - view.CenterY) / vp.Scale;
			var modelView = new ViewTransform(cx, cy, view.Scale * vp.Scale, view.Width, view.Height);

			canvas.Save();
			canvas.ClipRect(r);
			this._modelRenderer.Render(canvas, modelView, drawBackground: false);
			canvas.Restore();
		}

		this._paperRenderer.Render(canvas, view, drawBackground: false);
		canvas.Restore();
	}

	private static SKRect ToScreen(ViewTransform view, Bounds b)
	{
		var (x1, y1) = view.ToScreen(b.MinX, b.MaxY);
		var (x2, y2) = view.ToScreen(b.MaxX, b.MinY);
		return new SKRect((float)x1, (float)y1, (float)x2, (float)y2);
	}

	public void Dispose() => this._paperRenderer.Dispose();
}
