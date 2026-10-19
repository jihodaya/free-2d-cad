namespace Free2DCad.Rendering;

/// <summary>
/// 월드(WCS, Y 위쪽) ↔ 화면(픽셀, Y 아래쪽) 변환. 화면 중심이 월드 (CenterX, CenterY)를 가리킨다.
/// </summary>
public sealed class ViewTransform
{
	public ViewTransform(double centerX, double centerY, double scale, int width, int height)
	{
		this.CenterX = centerX;
		this.CenterY = centerY;
		this.Scale = scale;
		this.Width = width;
		this.Height = height;
	}

	public double CenterX { get; set; }

	public double CenterY { get; set; }

	/// <summary>월드 1단위당 픽셀 수.</summary>
	public double Scale { get; set; }

	public int Width { get; set; }

	public int Height { get; set; }

	public (double X, double Y) ToScreen(double x, double y) =>
		((x - this.CenterX) * this.Scale + this.Width / 2.0, this.Height / 2.0 - (y - this.CenterY) * this.Scale);

	public (double X, double Y) ToWorld(double sx, double sy) =>
		((sx - this.Width / 2.0) / this.Scale + this.CenterX, (this.Height / 2.0 - sy) / this.Scale + this.CenterY);

	public Bounds VisibleWorld
	{
		get
		{
			double hw = this.Width / 2.0 / this.Scale, hh = this.Height / 2.0 / this.Scale;
			return new Bounds(this.CenterX - hw, this.CenterY - hh, this.CenterX + hw, this.CenterY + hh);
		}
	}

	/// <summary>범위 전체가 보이도록 맞춘 뷰 (margin: 여백 비율).</summary>
	public static ViewTransform Fit(Bounds extents, int width, int height, double margin = 0.03)
	{
		if (extents.IsEmpty || extents.Width <= 0 && extents.Height <= 0)
		{
			return new ViewTransform(0, 0, 1, width, height);
		}
		double sx = width / Math.Max(extents.Width, 1e-9);
		double sy = height / Math.Max(extents.Height, 1e-9);
		double scale = Math.Min(sx, sy) * (1 - 2 * margin);
		return new ViewTransform((extents.MinX + extents.MaxX) / 2, (extents.MinY + extents.MaxY) / 2, scale, width, height);
	}

	/// <summary>화면 좌표 (sx, sy) 지점을 고정한 채로 배율을 factor만큼 바꾼다.</summary>
	public void ZoomAt(double sx, double sy, double factor)
	{
		var (wx, wy) = this.ToWorld(sx, sy);
		this.Scale *= factor;
		var (nx, ny) = this.ToWorld(sx, sy);
		this.CenterX += wx - nx;
		this.CenterY += wy - ny;
	}

	public void PanPixels(double dx, double dy)
	{
		this.CenterX -= dx / this.Scale;
		this.CenterY += dy / this.Scale;
	}
}
