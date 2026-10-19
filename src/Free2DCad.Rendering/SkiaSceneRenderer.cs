using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Free2DCad.Rendering;

/// <summary>
/// <see cref="Scene"/>을 SkiaSharp 캔버스에 그린다.
/// 선과 채움은 (색, 선 종류)별로 하나의 월드 좌표 경로로 묶어 캐시하고, 뷰 변환은 캔버스 행렬로 적용한다.
/// </summary>
public sealed class SkiaSceneRenderer : IDisposable
{
	private readonly Scene _scene;
	private readonly FontResolver _fonts = new();
	private readonly List<StrokeBatch> _strokes = new();
	private readonly object _gate = new();
	private bool _disposed;
	private readonly List<(SKPath Path, SKColor Color)> _fills = new();

	/// <summary>그라데이션 해치: 채움 영역, 그라데이션, 범위 (원점 이동 적용된 좌표).</summary>
	private readonly List<(SKPath Path, SceneGradient Gradient, SKRect Box)> _gradients = new();
	private readonly ConditionalWeakTable<SceneText, string[]> _wrapCache = new();

	// float 정밀도 손실을 줄이기 위해 경로는 도면 범위의 최소점을 원점으로 삼는다
	private readonly double _originX;
	private readonly double _originY;

	private sealed record StrokeBatch(SKPath Path, SKColor Color, float[]? Dash, float DashOffset);

	public SkiaSceneRenderer(Scene scene)
	{
		this._scene = scene;
		this._originX = scene.Extents.IsEmpty ? 0 : scene.Extents.MinX;
		this._originY = scene.Extents.IsEmpty ? 0 : scene.Extents.MinY;
		this.BuildBatches();
		this.BuildMasks();
	}

	/// <summary>와이프아웃마다: 가림 영역 경로 + 그 뒤에 그려지는(순서가 늦은) 도형만 담은 덧그림 렌더러.</summary>
	private readonly List<(SKPath Path, SKColor FrameColor, bool Frame, SkiaSceneRenderer? Overlay, Rgb? Fill)> _masks = new();

	private void BuildMasks()
	{
		foreach (var w in this._scene.Wipeouts.OrderBy(x => x.Order))
		{
			var path = new SKPath();
			for (int i = 0; i + 1 < w.Loop.Length; i += 2)
			{
				var p = new SKPoint((float)(w.Loop[i] - this._originX), (float)(w.Loop[i + 1] - this._originY));
				if (i == 0) path.MoveTo(p);
				else path.LineTo(p);
			}
			path.Close();
			var sub = new Scene();
			foreach (var pl in this._scene.Polylines.Where(x => x.Order > w.Order && x.Bounds.Intersects(w.Bounds)))
			{
				sub.Polylines.Add(pl);
				sub.Include(pl.Bounds);
			}
			foreach (var f in this._scene.Fills.Where(x => x.Order > w.Order && x.Bounds.Intersects(w.Bounds)))
			{
				sub.Fills.Add(f);
				sub.Include(f.Bounds);
			}
			bool hasOverlay = sub.Polylines.Count + sub.Fills.Count > 0;
			this._masks.Add((path, ToSk(w.FrameColor), w.Frame, hasOverlay ? new SkiaSceneRenderer(sub) : null, w.Fill));
		}
	}

	private void DrawMasks(SKCanvas canvas, ViewTransform view)
	{
		if (this._masks.Count == 0)
		{
			return;
		}
		var m = SKMatrix.CreateTranslation((float)(this._originX - view.CenterX), (float)(this._originY - view.CenterY));
		m = m.PostConcat(SKMatrix.CreateScale((float)view.Scale, (float)-view.Scale));
		m = m.PostConcat(SKMatrix.CreateTranslation(view.Width / 2f, view.Height / 2f));
		float strokeWidth = this.MinLineWidth > 0 ? (float)(this.MinLineWidth / view.Scale) : 0;
		foreach (var (path, frameColor, frame, overlay, fill) in this._masks)
		{
			canvas.Save();
			canvas.SetMatrix(canvas.TotalMatrix.PreConcat(m));
			var paperColor = this.PlotColors == PlotColorMode.None ? this.Background : SKColors.White;
			using (var bg = new SKPaint { Style = SKPaintStyle.Fill, Color = fill is { } fc ? this.MapColor(ToSk(fc)) : paperColor, IsAntialias = true })
			{
				canvas.DrawPath(path, bg);
			}
			if (frame)
			{
				using var fp = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, Color = this.MapColor(frameColor), IsAntialias = true };
				canvas.DrawPath(path, fp);
			}
			canvas.Restore();
			if (overlay != null)
			{
				overlay.Background = this.Background;
				overlay.PlotColors = this.PlotColors;
				overlay.MinLineWidth = this.MinLineWidth;
				overlay.OutlineFills = this.OutlineFills;
				overlay.RenderCore(canvas, view, false);
			}
		}
	}

	public SKColor Background { get; set; } = SKColors.Black;

	/// <summary>인쇄용 색 처리. None이면 화면과 같다.</summary>
	public PlotColorMode PlotColors { get; set; } = PlotColorMode.None;

	/// <summary>0보다 크면 선 굵기를 화면(출력) 단위로 이 값만큼 준다 (인쇄용). 0이면 머리카락 선.</summary>
	public float MinLineWidth { get; set; }

	/// <summary>
	/// 채움 도형 가장자리도 MinLineWidth 굵기로 칠한다 (지우개 층용: 글자 윤곽 채움의 안티앨리어싱 테두리가
	/// 지워지지 않고 흐리게 남아 원래 글자가 비쳐 보였다).
	/// </summary>
	public bool OutlineFills { get; set; }

	/// <summary>
	/// 기존 CAD 그라데이션 9종을 Skia 셰이더로 근사. box는 채울 영역 범위(월드, 원점 이동).
	/// INV 계열은 두 색을 바꾼다. 중앙이 아니면(Centered=false) 중심을 조금 옮긴다.
	/// </summary>
	private SKShader CreateGradientShader(SceneGradient g, SKRect box)
	{
		SKColor c1 = this.MapColor(ToSk(g.Color1));
		SKColor c2 = this.MapColor(ToSk(g.Color2));
		bool inv = g.Name.StartsWith("INV", StringComparison.Ordinal);
		string kind = inv ? g.Name[3..] : g.Name;
		if (inv)
		{
			(c1, c2) = (c2, c1);
		}
		float cx = box.MidX, cy = box.MidY;
		float half = Math.Max(box.Width, box.Height) / 2f;
		float shift = g.Centered ? 0 : half * 0.5f;
		var rot = SKMatrix.CreateRotation((float)g.Angle, cx, cy);
		switch (kind)
		{
			case "SPHERICAL":
				return SKShader.CreateRadialGradient(new SKPoint(cx - shift, cy + shift), half * 1.2f, new[] { c2, c1 }, null, SKShaderTileMode.Clamp);
			case "HEMISPHERICAL":
				return SKShader.CreateRadialGradient(new SKPoint(cx - shift, box.Top), half * 1.6f, new[] { c2, c1 }, null, SKShaderTileMode.Clamp, rot);
			case "CURVED":
				return SKShader.CreateRadialGradient(new SKPoint(cx - shift, box.Top - half), half * 2.4f, new[] { c2, c1 }, null, SKShaderTileMode.Clamp, rot);
			case "CYLINDER":
				return SKShader.CreateLinearGradient(new SKPoint(box.Left, cy), new SKPoint(box.Right, cy), new[] { c1, c2, c1 }, new[] { 0f, g.Centered ? 0.5f : 0.3f, 1f }, SKShaderTileMode.Clamp, rot);
			default: // LINEAR
				return SKShader.CreateLinearGradient(new SKPoint(box.Left, cy), new SKPoint(box.Right, cy), new[] { c1, c2 }, g.Centered ? null : new[] { 0f, 0.7f }, SKShaderTileMode.Clamp, rot);
		}
	}

	/// <summary>흰 종이에 인쇄할 때: 흰색(7번)과 아주 밝은 색은 검정, 흑백이면 모두 검정.</summary>
	private SKColor MapColor(SKColor c)
	{
		switch (this.PlotColors)
		{
			case PlotColorMode.Monochrome:
				return SKColors.Black;
			case PlotColorMode.Color:
				// 밝기 90% 이상(흰색 계열)은 종이에서 안 보이므로 검정으로
				return c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114 > 230 ? SKColors.Black : c;
			default:
				return c;
		}
	}

	/// <summary>
	/// 캔버스의 현재 변환 기준 (0,0)~(Width,Height) 영역에 그린다. 호스트(UI)의 변환·클립을 유지한다.
	/// </summary>
	public void Render(SKCanvas canvas, ViewTransform view, bool drawBackground = true)
	{
		// 화면 그리기는 렌더 스레드, Dispose는 UI 스레드에서 불린다. 도면이 바뀌어 이전 렌더러를 버리는 순간
		// 렌더 스레드가 아직 그리고 있으면 해제된 SKPath를 읽어 앱이 종료됐다(AccessViolation, 원·선 명령 직후).
		// 잠금으로 둘이 겹치지 않게 하고, 이미 해제됐으면 그리지 않는다.
		lock (this._gate)
		{
			if (this._disposed)
			{
				return;
			}
			this.RenderCore(canvas, view, drawBackground);
		}
	}

	private void RenderCore(SKCanvas canvas, ViewTransform view, bool drawBackground)
	{
		canvas.Save();
		canvas.ClipRect(new SKRect(0, 0, view.Width, view.Height));
		if (drawBackground)
		{
			using var bg = new SKPaint { Color = this.Background, Style = SKPaintStyle.Fill };
			canvas.DrawRect(new SKRect(0, 0, view.Width, view.Height), bg);
		}

		canvas.Save();
		// 월드(원점 이동) → 화면
		var m = SKMatrix.CreateTranslation((float)(this._originX - view.CenterX), (float)(this._originY - view.CenterY));
		m = m.PostConcat(SKMatrix.CreateScale((float)view.Scale, (float)-view.Scale));
		m = m.PostConcat(SKMatrix.CreateTranslation(view.Width / 2f, view.Height / 2f));
		canvas.SetMatrix(canvas.TotalMatrix.PreConcat(m));

		using (var fillPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true })
		{
			float outline = this.OutlineFills && this.MinLineWidth > 0 ? (float)(this.MinLineWidth / view.Scale) : 0;
			using var outlinePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = outline, IsAntialias = true, StrokeJoin = SKStrokeJoin.Round };
			foreach (var (path, color) in this._fills)
			{
				fillPaint.Color = this.MapColor(color);
				canvas.DrawPath(path, fillPaint);
				if (outline > 0)
				{
					outlinePaint.Color = fillPaint.Color;
					canvas.DrawPath(path, outlinePaint);
				}
			}
			fillPaint.Color = SKColors.White;
			foreach (var (path, gradient, box) in this._gradients)
			{
				using var shader = this.CreateGradientShader(gradient, box);
				fillPaint.Shader = shader;
				canvas.DrawPath(path, fillPaint);
				fillPaint.Shader = null;
			}
		}

		// 캔버스 행렬이 월드 단위이므로, 출력 단위 선 굵기는 배율로 나눠 월드 단위로 바꾼다
		float strokeWidth = this.MinLineWidth > 0 ? (float)(this.MinLineWidth / view.Scale) : 0;
		using (var strokePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = strokeWidth, IsAntialias = true, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
		{
			foreach (StrokeBatch batch in this._strokes)
			{
				strokePaint.Color = this.MapColor(batch.Color);
				strokePaint.PathEffect?.Dispose();
				strokePaint.PathEffect = DashEffect(batch.Dash, view.Scale, batch.DashOffset);
				canvas.DrawPath(batch.Path, strokePaint);
			}
			strokePaint.PathEffect?.Dispose();
		}

		canvas.Restore();

		this.DrawTexts(canvas, view);
		this.DrawMasks(canvas, view);
		canvas.Restore();
	}

	/// <summary>
	/// 대시가 화면에서 너무 촘촘하면(1.5px 미만 주기) 실선으로 그린다. 점(길이 0)은 아주 짧은 선으로 대체.
	/// </summary>
	private static SKPathEffect? DashEffect(float[]? dash, double scale, float offset)
	{
		if (dash == null)
		{
			return null;
		}
		double period = dash.Sum(Math.Abs);
		if (period * scale < 3)
		{
			return null;
		}
		var (intervals, phase) = DashIntervals(dash, (float)(0.5 / scale));
		if (intervals == null)
		{
			return null;
		}
		float total = intervals.Sum();
		phase = ((phase + offset) % total + total) % total;
		return SKPathEffect.CreateDash(intervals, phase);
	}

	/// <summary>
	/// 선종류 패턴(+선, -공백, 0점) → Skia 간격(선·공백 번갈아, 선부터)과 시작 위치.
	/// 같은 부호가 이어지면 합치고(복합 선종류의 "25.4,-12.7,-38.1" 등), 공백으로 시작하면 돌려서 phase로 맞춘다.
	/// </summary>
	public static (float[]? Intervals, float Phase) DashIntervals(float[] dash, float minLen)
	{
		var runs = new List<(bool On, float Len)>();
		foreach (float d in dash)
		{
			bool on = d >= 0;
			float len = on ? Math.Max(d, minLen) : -d;
			if (runs.Count > 0 && runs[^1].On == on)
			{
				runs[^1] = (on, runs[^1].Len + len);
			}
			else
			{
				runs.Add((on, len));
			}
		}
		if (runs.Count > 1 && runs[0].On == runs[^1].On)
		{
			// 처음과 끝이 같은 종류면 반복될 때 이어지므로 합친다 (끝 → 앞으로, phase로 보정)
			var last = runs[^1];
			runs.RemoveAt(runs.Count - 1);
			runs[0] = (last.On, last.Len + runs[0].Len);
			if (!runs.Any(r => !r.On) || !runs.Any(r => r.On))
			{
				return runs[0].On ? (null, 0) : (new[] { minLen, runs[0].Len }, 0);
			}
			float total0 = runs.Sum(r => r.Len);
			var (iv, ph) = Rotate(runs);
			return (iv, (ph + last.Len) % total0);
		}
		if (runs.All(r => r.On))
		{
			return (null, 0); // 공백 없음 → 실선
		}
		if (runs.All(r => !r.On))
		{
			return (new[] { minLen, runs.Sum(r => r.Len) }, 0);
		}
		return Rotate(runs);
	}

	/// <summary>번갈아 나오는 구간을 선부터 시작하도록 돌린다. phase = 원래 시작점의 위치.</summary>
	private static (float[] Intervals, float Phase) Rotate(List<(bool On, float Len)> runs)
	{
		float total = runs.Sum(r => r.Len);
		if (runs[0].On)
		{
			return (runs.Select(r => r.Len).ToArray(), 0);
		}
		var rotated = runs.Skip(1).Concat(runs.Take(1)).Select(r => r.Len).ToArray();
		return (rotated, total - runs[0].Len);
	}

	private void BuildBatches()
	{
		var strokeMap = new Dictionary<(Rgb, string), (SKPath Path, float[]? Dash, float Offset)>();
		foreach (ScenePolyline pl in this._scene.Polylines)
		{
			string dashKey = string.Empty;
			float offset = 0;
			if (pl.Dash != null)
			{
				// 패턴 시작 위치가 다르면 따로 묶는다 (주기의 1/1000 단위로 맞춤)
				double period = pl.Dash.Sum(Math.Abs);
				double step = period > 0 ? period / 1000 : 1;
				double norm = period > 0 ? ((pl.DashOffset % period) + period) % period : 0;
				long q = (long)Math.Round(norm / step);
				offset = (float)(q * step);
				dashKey = string.Join(",", pl.Dash.Select(d => d.ToString("G6"))) + "@" + q;
			}
			if (!strokeMap.TryGetValue((pl.Color, dashKey), out var entry))
			{
				entry = (new SKPath(), pl.Dash?.Select(d => (float)d).ToArray(), offset);
				strokeMap[(pl.Color, dashKey)] = entry;
			}
			double[] p = pl.Points;
			entry.Path.MoveTo((float)(p[0] - this._originX), (float)(p[1] - this._originY));
			for (int i = 2; i < p.Length; i += 2)
			{
				entry.Path.LineTo((float)(p[i] - this._originX), (float)(p[i + 1] - this._originY));
			}
			if (pl.Closed)
			{
				entry.Path.Close();
			}
		}
		foreach (var kv in strokeMap)
		{
			this._strokes.Add(new StrokeBatch(kv.Value.Path, ToSk(kv.Key.Item1), kv.Value.Dash, kv.Value.Offset));
		}

		foreach (SceneFill fill in this._scene.Fills.Where(f => f.Gradient != null))
		{
			var path = new SKPath { FillType = SKPathFillType.EvenOdd };
			foreach (double[] loop in fill.Loops)
			{
				path.MoveTo((float)(loop[0] - this._originX), (float)(loop[1] - this._originY));
				for (int i = 2; i < loop.Length; i += 2)
				{
					path.LineTo((float)(loop[i] - this._originX), (float)(loop[i + 1] - this._originY));
				}
				path.Close();
			}
			this._gradients.Add((path, fill.Gradient!, path.Bounds));
		}

		foreach (var group in this._scene.Fills.Where(f => f.Gradient == null).GroupBy(f => f.Color))
		{
			var path = new SKPath { FillType = SKPathFillType.EvenOdd };
			foreach (SceneFill fill in group)
			{
				foreach (double[] loop in fill.Loops)
				{
					path.MoveTo((float)(loop[0] - this._originX), (float)(loop[1] - this._originY));
					for (int i = 2; i < loop.Length; i += 2)
					{
						path.LineTo((float)(loop[i] - this._originX), (float)(loop[i + 1] - this._originY));
					}
					path.Close();
				}
			}
			this._fills.Add((path, ToSk(group.Key)));
		}
	}

	private void DrawTexts(SKCanvas canvas, ViewTransform view)
	{
		Bounds visible = view.VisibleWorld;
		using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

		foreach (SceneText t in this._scene.Texts)
		{
			if (t.PickOnly)
			{
				continue; // 글자는 도형으로 이미 그렸다
			}
			double hPx = t.Height * view.Scale;
			if (hPx < 1.5 || !t.Bounds.Intersects(visible))
			{
				continue;
			}

			FontResolver.Face face = this._fonts.Resolve(t.FontFile);
			FontResolver.Face fb = this._fonts.Fallback;
			using var font = new SKFont(face.Typeface, (float)(hPx / face.CapRatio));
			using var fallbackFont = new SKFont(fb.Typeface, (float)(hPx / fb.CapRatio));
			paint.Color = this.MapColor(ToSk(t.Color));

			string[] lines = t.WrapWidth > 0 ? this.Wrap(t, face) : t.Lines;
			double lsPx = t.LineSpacing * view.Scale;
			double blockH = hPx + Math.Max(lines.Length - 1, 0) * lsPx;
			double b0 = t.VAlign switch
			{
				TextVAlign.Top => hPx,
				TextVAlign.Middle => hPx - blockH / 2,
				TextVAlign.Bottom => hPx - blockH,
				_ => 0,
			};

			var (sx, sy) = view.ToScreen(t.X, t.Y);
			canvas.Save();
			canvas.Translate((float)sx, (float)sy);
			canvas.RotateRadians((float)-t.Rotation);
			canvas.Scale((float)t.WidthFactor, 1f);
			if (t.Oblique != 0)
			{
				canvas.Skew((float)-Math.Tan(t.Oblique), 0);
			}

			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.Length == 0)
				{
					continue;
				}
				var runs = SplitRuns(line, face.Typeface);
				float w = runs.Sum(r => (r.Fallback ? fallbackFont : font).MeasureText(r.Text));
				float x = t.HAlign switch
				{
					TextHAlign.Center => -w / 2,
					TextHAlign.Right => -w,
					_ => 0,
				};
				float baseline = (float)(b0 + i * lsPx);
				foreach (var (text, useFallback) in runs)
				{
					SKFont f = useFallback ? fallbackFont : font;
					if (this.PlotColors != PlotColorMode.None)
					{
						// 인쇄(PDF): 글꼴을 넣지 않고 윤곽선으로 그린다 (파일 크기↓, 받는 PC에 글꼴이 없어도 동일)
						using SKPath glyphs = f.GetTextPath(text, new SKPoint(x, baseline));
						canvas.DrawPath(glyphs, paint);
					}
					else
					{
						canvas.DrawText(text, x, baseline, f, paint);
					}
					x += f.MeasureText(text);
				}
			}
			canvas.Restore();
		}
	}

	/// <summary>기본 글꼴에 글리프가 있는 구간과 없는 구간(대체 글꼴로 그림)으로 나눈다.</summary>
	private static List<(string Text, bool Fallback)> SplitRuns(string line, SKTypeface primary)
	{
		var runs = new List<(string, bool)>();
		int start = 0;
		bool? current = null;
		for (int k = 0; k < line.Length; k++)
		{
			char ch = line[k];
			// 공백은 앞 구간에 붙인다
			bool missing = ch == ' ' ? current ?? false : !primary.ContainsGlyph(ch);
			if (current != null && missing != current)
			{
				runs.Add((line[start..k], current.Value));
				start = k;
			}
			current = missing;
		}
		runs.Add((line[start..], current ?? false));
		return runs;
	}

	/// <summary>MTEXT 참조 폭에 맞춰 줄바꿈 (높이 100 기준으로 측정해 줌과 무관하게 캐시).</summary>
	private string[] Wrap(SceneText t, FontResolver.Face face)
	{
		if (this._wrapCache.TryGetValue(t, out string[]? cached))
		{
			return cached;
		}

		const float refHeight = 100f;
		using var font = new SKFont(face.Typeface, refHeight / (float)face.CapRatio);
		double maxWidth = t.WrapWidth / t.Height * refHeight / t.WidthFactor;
		var result = new List<string>();

		foreach (string para in t.Lines)
		{
			string rest = para;
			while (rest.Length > 0 && font.MeasureText(rest) > maxWidth)
			{
				int fit = 1;
				while (fit < rest.Length && font.MeasureText(rest.AsSpan(0, fit + 1)) <= maxWidth)
				{
					fit++;
				}
				int brk = rest.LastIndexOf(' ', Math.Min(fit, rest.Length - 1));
				int cut = brk > 0 ? brk : fit;
				result.Add(rest[..cut].TrimEnd());
				rest = rest[cut..].TrimStart();
			}
			result.Add(rest);
		}

		string[] lines = result.ToArray();
		this._wrapCache.AddOrUpdate(t, lines);
		return lines;
	}

	private static SKColor ToSk(Rgb c) => new(c.R, c.G, c.B);

	public void Dispose()
	{
		lock (this._gate)
		{
			if (this._disposed)
			{
				return;
			}
			this._disposed = true;
			foreach (var s in this._strokes) s.Path.Dispose();
			foreach (var f in this._fills) f.Path.Dispose();
			foreach (var g in this._gradients) g.Path.Dispose();
			foreach (var m in this._masks)
			{
				m.Path.Dispose();
				m.Overlay?.Dispose();
			}
			this._fonts.Dispose();
		}
	}
}
