using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Rendering;

/// <summary>
/// ACadSharp 문서의 모델스페이스를 <see cref="Scene"/>으로 평탄화한다.
/// 블록(INSERT)과 치수 블록은 변환을 누적하며 재귀 전개한다.
/// </summary>
public sealed partial class SceneBuilder
{
	private const int MaxDepth = 16;
	private const int CircleSegments = 72;

	private readonly CadDocument _doc;
	private readonly Scene _scene = new();
	private readonly double _ltScale;
	private Entity? _top; // 현재 전개 중인 모델스페이스 최상위 엔티티
	private int _order; // 최상위 객체 순번 (와이프아웃 가림 순서)

	/// <summary>와이프아웃 테두리 표시 (WIPEOUT 프레임 켜기/끄기).</summary>
	public static bool WipeoutFrames { get; set; } = true;
	private bool _skipExtents; // 구성선·광선: 전체 보기 범위에 넣지 않음
	private readonly List<(Entity? Top, double X, double Y, double Dx, double Dy, bool Ray, Rgb Color, double[]? Dash, Affine2 Xf)> _infinite = new();

	private SceneBuilder(CadDocument doc)
	{
		this._doc = doc;
		this._ltScale = doc.Header.LineTypeScale > 0 ? doc.Header.LineTypeScale : 1.0;
	}

	public static Scene Build(CadDocument doc)
	{
		var builder = new SceneBuilder(doc);
		var ctx = new Context(Affine2.Identity, Rgb.White, null, null, 1.0, 0);
		foreach (Entity e in doc.Entities)
		{
			builder._top = e;
			builder._order++;
			builder.AddEntity(e, ctx);
		}
		builder.FlushInfinite();
		return builder._scene;
	}

	/// <summary>블록 레코드(예: 배치의 도면 공간 블록)의 엔티티로 장면을 만든다. 뷰포트는 테두리만 그린다.</summary>
	public static Scene BuildBlock(CadDocument doc, BlockRecord block)
	{
		var builder = new SceneBuilder(doc);
		var ctx = new Context(Affine2.Identity, Rgb.White, null, null, 1.0, 0);
		foreach (Entity e in block.Entities)
		{
			builder._top = e;
			builder._order++;
			if (e is Viewport vp)
			{
				if (vp.Id != 1 && !vp.RepresentsPaper)
				{
					double hw = vp.Width / 2, hh = vp.Height / 2;
					builder.AddPolyline(new[]
					{
						vp.Center.X - hw, vp.Center.Y - hh, vp.Center.X + hw, vp.Center.Y - hh,
						vp.Center.X + hw, vp.Center.Y + hh, vp.Center.X - hw, vp.Center.Y + hh,
					}, true, builder.ResolveColor(vp.Color, vp.Layer ?? Layer.Default, ctx), null, Affine2.Identity);
				}
				continue;
			}
			builder.AddEntity(e, ctx);
		}
		builder.FlushInfinite();
		return builder._scene;
	}

	/// <summary>
	/// 문서에 아직 추가되지 않은 임시 엔티티(명령 미리보기 등)만으로 장면을 만든다.
	/// 레이어가 없는 엔티티는 "0" 레이어 설정으로 그린다.
	/// </summary>
	public static Scene BuildEntities(CadDocument doc, IEnumerable<Entity> entities)
	{
		var builder = new SceneBuilder(doc);
		var ctx = new Context(Affine2.Identity, Rgb.White, null, null, 1.0, 0);
		foreach (Entity e in entities)
		{
			builder._top = e;
			builder._order++;
			builder.AddEntity(e, ctx);
		}
		builder.FlushInfinite();
		return builder._scene;
	}

	/// <summary>
	/// 여러 줄 문자의 실제 크기(폭, 높이) — 치수 문자 배치용 (Dimension.TextMeasurer).
	/// 회전 없이 그려 본 글자 모양의 범위. keepWidth = 글상자 폭으로 줄을 나눈 높이 (열 높이 등).
	/// </summary>
	public static (double Width, double Height)? MeasureText(MText source, CadDocument? doc, bool keepWidth = false)
	{
		var m = (MText)source.Clone();
		m.InsertPoint = XYZ.Zero;
		m.AlignmentPoint = XYZ.AxisX;
		if (!keepWidth)
		{
			m.RectangleWidth = 0;
		}
		var scene = BuildEntities(doc ?? MeasureDoc.Value, new Entity[] { m });
		// 글자 모양(선·채움)만 잰다 — 선택용 둘레(글자 간격 포함)는 빼야 기존 CAD 값과 같다
		var e = Bounds.Empty;
		foreach (var p in scene.Polylines) e = e.Union(p.Bounds);
		foreach (var f in scene.Fills) e = e.Union(f.Bounds);
		if (e.IsEmpty) e = scene.Extents;
		if (e.IsEmpty)
		{
			return null;
		}
		return (e.MaxX - e.MinX, e.MaxY - e.MinY);
	}

	private static readonly Lazy<CadDocument> MeasureDoc = new(() => new CadDocument());

	/// <summary>블록 전개 중 상속되는 상태.</summary>
	/// <param name="BlockColor">BYBLOCK 색상이 가리킬 색.</param>
	/// <param name="BlockLayer">블록 안의 "0" 레이어 엔티티가 따를 레이어.</param>
	/// <param name="BlockLineType">BYBLOCK 선 종류가 가리킬 선 종류.</param>
	/// <param name="LtScale">누적 선 종류 배율 (INSERT 배율 포함).</param>
	private readonly record struct Context(Affine2 Xf, Rgb BlockColor, Layer? BlockLayer, LineType? BlockLineType, double LtScale, int Depth);

	private void AddEntity(Entity e, Context ctx)
	{
		if (e.IsInvisible)
		{
			return;
		}

		this._complexLt = null;
		Layer layer = this.EffectiveLayer(e, ctx);
		if (!layer.IsOn || layer.Flags.HasFlag(LayerFlags.Frozen))
		{
			return;
		}

		Rgb color = this.ResolveColor(e.Color, layer, ctx);
		Affine2 ocs = OcsTransform(e).Then(ctx.Xf);

		switch (e)
		{
			case Line line:
				this.AddPolyline(new[] { line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y }, false, color, this.Dash(e, layer, ctx), ctx.Xf);
				this.Snap(line.StartPoint.X, line.StartPoint.Y, SnapKind.Endpoint, ctx.Xf);
				this.Snap(line.EndPoint.X, line.EndPoint.Y, SnapKind.Endpoint, ctx.Xf);
				this.Snap((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2, SnapKind.Midpoint, ctx.Xf);
				break;
			case Arc arc:
				{
					double sweep = arc.EndAngle - arc.StartAngle;
					if (sweep <= 0) sweep += 2 * Math.PI;
					int n = Math.Max(4, (int)Math.Ceiling(CircleSegments * sweep / (2 * Math.PI)));
					List<XYZ> pts = arc.PolygonalVertexes(n);
					this.AddPolyline(Flatten(pts), false, color, this.Dash(e, layer, ctx), ctx.Xf);
					this.Snap(pts[0].X, pts[0].Y, SnapKind.Endpoint, ctx.Xf);
					this.Snap(pts[^1].X, pts[^1].Y, SnapKind.Endpoint, ctx.Xf);
					double mid = arc.StartAngle + sweep / 2;
					this.Snap(arc.Center.X + arc.Radius * Math.Cos(mid), arc.Center.Y + arc.Radius * Math.Sin(mid), SnapKind.Midpoint, ocs);
					this.Snap(arc.Center.X, arc.Center.Y, SnapKind.Center, ocs);
					break;
				}
			case Circle circle:
				this.AddPolyline(Flatten(circle.PolygonalVertexes(CircleSegments)), true, color, this.Dash(e, layer, ctx), ctx.Xf);
				this.Snap(circle.Center.X, circle.Center.Y, SnapKind.Center, ocs);
				this.Snap(circle.Center.X + circle.Radius, circle.Center.Y, SnapKind.Quadrant, ocs);
				this.Snap(circle.Center.X - circle.Radius, circle.Center.Y, SnapKind.Quadrant, ocs);
				this.Snap(circle.Center.X, circle.Center.Y + circle.Radius, SnapKind.Quadrant, ocs);
				this.Snap(circle.Center.X, circle.Center.Y - circle.Radius, SnapKind.Quadrant, ocs);
				break;
			case Ellipse ellipse:
				this.AddPolyline(Flatten(ellipse.PolygonalVertexes(CircleSegments)), false, color, this.Dash(e, layer, ctx), ctx.Xf);
				this.Snap(ellipse.Center.X, ellipse.Center.Y, SnapKind.Center, ctx.Xf);
				break;
			case LwPolyline lw:
				if (lw.ConstantWidth > 0 || lw.Vertices.Any(v => v.StartWidth > 0 || v.EndWidth > 0))
				{
					this.AddWidePolyline(lw, color, ocs);
				}
				else
				{
					this.AddPolylineByLinetype(lw.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList(), lw.IsClosed,
						lw.Flags.HasFlag(LwPolylineFlags.Plinegen), color, this.Dash(e, layer, ctx), ocs);
				}
				for (int i = 0; i < lw.Vertices.Count; i++)
				{
					var a = lw.Vertices[i];
					this.Snap(a.Location.X, a.Location.Y, SnapKind.Endpoint, ocs);
					bool hasNext = i + 1 < lw.Vertices.Count || lw.IsClosed;
					if (hasNext && a.Bulge == 0)
					{
						var b = lw.Vertices[(i + 1) % lw.Vertices.Count];
						this.Snap((a.Location.X + b.Location.X) / 2, (a.Location.Y + b.Location.Y) / 2, SnapKind.Midpoint, ocs);
					}
				}
				break;
			case Point point:
				this.AddPoint(point, color, ctx.Xf);
				this.Snap(point.Location.X, point.Location.Y, SnapKind.Node, ctx.Xf);
				break;
			case XLine xl:
				this._infinite.Add((this._top, xl.FirstPoint.X, xl.FirstPoint.Y, xl.Direction.X, xl.Direction.Y, false, color, this.Dash(e, layer, ctx), ctx.Xf));
				this.Snap(xl.FirstPoint.X, xl.FirstPoint.Y, SnapKind.Midpoint, ctx.Xf);
				break;
			case Ray ray:
				this._infinite.Add((this._top, ray.StartPoint.X, ray.StartPoint.Y, ray.Direction.X, ray.Direction.Y, true, color, this.Dash(e, layer, ctx), ctx.Xf));
				this.Snap(ray.StartPoint.X, ray.StartPoint.Y, SnapKind.Endpoint, ctx.Xf);
				break;
			case Polyline2D pl2:
				this.AddPolylineByLinetype(pl2.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList(), pl2.IsClosed,
					pl2.Flags.HasFlag(PolylineFlags.ContinuousLinetypePattern), color, this.Dash(e, layer, ctx), ocs);
				break;
			case Polyline3D pl3:
				this.AddPolyline(pl3.Vertices.SelectMany(v => new[] { v.Location.X, v.Location.Y }).ToArray(), pl3.IsClosed, color, this.Dash(e, layer, ctx), ctx.Xf);
				break;
			case Spline spline:
			{
				double[]? spPts = SplinePoints(spline);
				if (spPts != null)
				{
					this.AddPolyline(spPts, false, color, this.Dash(e, layer, ctx), ctx.Xf);
				}
				break;
			}
			case Solid solid: // TRACE 포함 (꼭짓점 순서 1-2-4-3)
				this.AddFill(new List<double[]>
				{
					new[] { solid.FirstCorner.X, solid.FirstCorner.Y, solid.SecondCorner.X, solid.SecondCorner.Y, solid.FourthCorner.X, solid.FourthCorner.Y, solid.ThirdCorner.X, solid.ThirdCorner.Y },
				}, color, ocs);
				break;
			case Hatch hatch:
				this.AddHatch(hatch, color, ocs, ctx);
				break;
			case MText mtext:
				this.AddMText(mtext, color, ocs);
				this.Snap(mtext.InsertPoint.X, mtext.InsertPoint.Y, SnapKind.Insertion, ocs);
				break;
			case AttributeDefinition attDef:
				// 블록 정의 안의 속성 정의는 INSERT의 속성 값으로 대신 표시됨. 도면에 놓인 속성 정의는 태그를 보인다 (ATTDEF 직후)
				if (ctx.Depth == 0)
				{
					string value = attDef.Value;
					attDef.Value = attDef.Tag;
					this.AddText(attDef, color, ocs);
					attDef.Value = value;
					this.Snap(attDef.InsertPoint.X, attDef.InsertPoint.Y, SnapKind.Insertion, ocs);
				}
				break;
			case TextEntity text: // AttributeEntity 포함
				this.AddText(text, color, ocs);
				this.Snap(text.InsertPoint.X, text.InsertPoint.Y, SnapKind.Insertion, ocs);
				break;
			case Insert insert:
				this.AddInsert(insert, color, layer, ctx);
				this.Snap(insert.InsertPoint.X, insert.InsertPoint.Y, SnapKind.Insertion, ocs);
				break;
			case Dimension dim:
				if (dim.Block != null && ctx.Depth < MaxDepth)
				{
					this.AddBlockEntities(dim.Block, ctx with { BlockColor = color, BlockLayer = layer, Depth = ctx.Depth + 1 });
				}
				break;
			case Tolerance tolerance:
				this.AddTolerance(tolerance, color, ctx);
				this.Snap(tolerance.InsertionPoint.X, tolerance.InsertionPoint.Y, SnapKind.Insertion, ctx.Xf);
				break;
			case Wipeout wipeout:
				this.AddWipeout(wipeout, color, ctx.Xf);
				break;
			case MLine mline:
				this.AddMLine(mline, layer, ctx);
				break;
			case Leader leader:
				{
					// 기존 CAD: 지시선 색 = 치수선 색(DIMCLRD, 재정의 포함), 블록별이면 지시선 객체 색. 화살촉 = DIMLDRBLK·DIMASZ
					var ls = leader.GetActiveDimensionStyle();
					Rgb lc = ls == null || ls.DimensionLineColor.IsByBlock ? color : this.ResolveColor(ls.DimensionLineColor, layer, ctx);
					double[] path = LeaderPath(leader);
					this.AddPolyline(path, false, lc, null, ctx.Xf);
					if (leader.ArrowHeadEnabled && path.Length >= 4)
					{
						// 화살촉 = 지시선 화살촉 블록(DIMLDRBLK, 없음·건축 틱·점 등), 방향은 첫 조각(스플라인이면 곡선의 처음)
						double size = (ls?.ArrowSize > 0 ? ls.ArrowSize : 2.5) * (ls?.ScaleFactor > 0 ? ls.ScaleFactor : 1.0);
						var (fx, fy) = ArrowFrom(path, size);
						this.AddLeaderArrow(ls?.LeaderArrow, path[0], path[1], fx, fy, size, lc, layer, ctx);
					}
					break;
				}
			case MultiLeader mleader:
				this.AddMultiLeader(mleader, color, layer, ctx);
				break;
		}
	}

	private void AddInsert(Insert insert, Rgb color, Layer layer, Context ctx)
	{
		if (insert.Block == null || ctx.Depth >= MaxDepth)
		{
			return;
		}

		XYZ basePt = insert.Block.BlockEntity?.BasePoint ?? XYZ.Zero;
		Affine2 ocs = OcsTransform(insert);
		int cols = Math.Max((int)insert.ColumnCount, 1);
		int rows = Math.Max((int)insert.RowCount, 1);
		double ltScale = ctx.LtScale * Math.Max(Math.Abs(insert.XScale), Math.Abs(insert.YScale));

		for (int r = 0; r < rows; r++)
		{
			for (int c = 0; c < cols; c++)
			{
				Affine2 local = Affine2.Translation(-basePt.X, -basePt.Y)
					.Then(Affine2.Scale(insert.XScale, insert.YScale))
					.Then(Affine2.Translation(c * insert.ColumnSpacing, r * insert.RowSpacing))
					.Then(Affine2.Rotation(insert.Rotation))
					.Then(Affine2.Translation(insert.InsertPoint.X, insert.InsertPoint.Y))
					.Then(ocs)
					.Then(ctx.Xf);

				LineType? blockLt = insert.LineType?.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase) == true
					? ctx.BlockLineType
					: insert.LineType?.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase) == true ? layer.LineType : insert.LineType;

				this.AddBlockEntities(insert.Block, new Context(local, color, layer, blockLt, ltScale, ctx.Depth + 1));

				// 표: 칸 안(선에서 먼 빈 곳)을 눌러도 표가 골라지도록 표 전체 둘레를 선택 전용으로
				if (insert is TableEntity table && table.Columns.Count > 0 && table.Rows.Count > 0)
				{
					double w = table.Columns.Sum(col => col.Width), h = table.Rows.Sum(row => row.Height);
					Bounds box = Bounds.Empty;
					foreach (var (lx, ly) in new[] { (0.0, 0.0), (w, 0.0), (w, -h), (0.0, -h) })
					{
						var (px, py) = local.Apply(lx + basePt.X, ly + basePt.Y);
						box = box.Union(new Bounds(px, py, px, py));
					}
					this._scene.Texts.Add(new SceneText(Array.Empty<string>(), 0, box.MinX, box.MinY, 0, 0, 1, 0, TextHAlign.Left, TextVAlign.Baseline, 0,
						string.Empty, color, box, this._top) { PickOnly = true });
				}
			}
		}

		// 속성 값은 이미 WCS(OCS) 좌표로 저장되어 있으므로 부모 변환만 적용
		if (insert.HasAttributes)
		{
			var vis = this._doc.Header.AttributeVisibility; // ATTDISP
			foreach (AttributeEntity att in insert.Attributes)
			{
				if (vis == AttributeVisibilityMode.None || (vis == AttributeVisibilityMode.Normal && att.Flags.HasFlag(AttributeFlags.Hidden)))
				{
					continue;
				}
				this.AddEntity(att, ctx);
			}
		}
	}

	private void AddBlockEntities(BlockRecord block, Context ctx)
	{
		// 외부 참조: 참조 도면의 모형 공간을 그린다 (파일에는 경로만 있음)
		if (XrefLoader.IsXref(block))
		{
			if (XrefLoader.ContentOf(this._doc, block) is { } xdoc)
			{
				foreach (Entity child in xdoc.ModelSpace.Entities)
				{
					this.AddEntity(child, ctx);
				}
			}
			return;
		}
		foreach (Entity child in block.Entities)
		{
			this.AddEntity(child, ctx);
		}
	}

	/// <summary>해치 배경 색상 (없으면 null).</summary>
	public static Color? HatchBackground(Hatch hatch)
	{
		if (!hatch.ExtendedData.TryGet("HATCHBACKGROUNDCOLOR", out var data))
		{
			return null;
		}
		var rec = data.Records.OfType<ACadSharp.XData.ExtendedDataInteger32>().FirstOrDefault();
		if (rec == null)
		{
			return null;
		}
		uint v = unchecked((uint)rec.Value);
		uint kind = v >> 24;
		if (kind == 0xC3)
		{
			short idx = (short)(v & 0xFFFF);
			return idx is > 0 and < 256 ? new Color(idx) : null;
		}
		if (kind == 0xC2)
		{
			return new Color((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
		}
		return null;
	}

	private void AddHatch(Hatch hatch, Rgb color, Affine2 ocs, Context ctx)
	{
		if (hatch.IsSolid)
		{
			var loops = new List<double[]>();
			foreach (Hatch.BoundaryPath path in hatch.Paths)
			{
				double[] loop = BoundaryPoints(path);
				if (loop.Length >= 6)
				{
					loops.Add(loop);
				}
			}
			if (loops.Count > 0)
			{
				this.AddFill(loops, color, ocs, GradientOf(hatch));
			}
			return;
		}

		// 배경 색상 (XData HATCHBACKGROUNDCOLOR, 기존 CAD 형식 1071 = 0xC3000000 | 색상 번호, 0xC2000000 | RGB): 패턴 아래를 채움
		if (HatchBackground(hatch) is { } bg)
		{
			var bgLoops = hatch.Paths.Select(BoundaryPoints).Where(l => l.Length >= 6).ToList();
			if (bgLoops.Count > 0)
			{
				this.AddFill(bgLoops, bg.IsByLayer || bg.IsByBlock ? color : ToRgb(bg), ocs, null);
			}
		}

		IEnumerable<Line> lines;
		try
		{
			lines = hatch.ExplodePattern().ToList();
		}
		catch (Exception)
		{
			return; // 패턴 전개 실패 시 해치는 건너뜀 (경계선은 보통 별도 엔티티로 존재)
		}
		// 점(길이 0 대시)은 그대로면 보이지 않으므로 해치 범위에 비례한 아주 짧은 선으로 그린다
		var box = hatch.GetBoundingBox();
		double dot = Math.Max(box.Max.X - box.Min.X, box.Max.Y - box.Min.Y) * 0.002;
		foreach (Line l in lines)
		{
			double x1 = l.StartPoint.X, y1 = l.StartPoint.Y, x2 = l.EndPoint.X, y2 = l.EndPoint.Y;
			if (Math.Abs(x2 - x1) + Math.Abs(y2 - y1) < 1e-9)
			{
				x1 -= dot / 2;
				x2 += dot / 2;
			}
			this.AddPolyline(new[] { x1, y1, x2, y2 }, false, color, null, ctx.Xf);
		}
	}

	private void AddText(TextEntity text, Rgb color, Affine2 ocs)
	{
		var tokens = MTextTokenizer.Tokenize(text.Value, isMText: false);
		if (tokens.Count == 0)
		{
			return;
		}

		double ax = 0;
		TextVAlign v = TextVAlign.Baseline;
		switch (text.HorizontalAlignment)
		{
			case TextHorizontalAlignment.Center: ax = 0.5; break;
			case TextHorizontalAlignment.Right: ax = 1; break;
			case TextHorizontalAlignment.Middle: ax = 0.5; v = TextVAlign.Middle; break;
		}
		if (text.HorizontalAlignment != TextHorizontalAlignment.Middle)
		{
			v = text.VerticalAlignment switch
			{
				TextVerticalAlignmentType.Bottom => TextVAlign.Bottom,
				TextVerticalAlignmentType.Middle => TextVAlign.Middle,
				TextVerticalAlignmentType.Top => TextVAlign.Top,
				_ => TextVAlign.Baseline,
			};
		}

		// 왼쪽-기준선 정렬이거나 Aligned/Fit이면 삽입점, 그 외에는 정렬점이 기준
		bool fit = text.HorizontalAlignment is TextHorizontalAlignment.Aligned or TextHorizontalAlignment.Fit;
		bool useInsert = (ax == 0 && v == TextVAlign.Baseline) || fit;
		XYZ anchor = useInsert ? text.InsertPoint : text.AlignmentPoint;
		double rotation = text.Rotation;
		double? fitWidth = null;
		if (fit)
		{
			double dx = text.AlignmentPoint.X - text.InsertPoint.X, dy = text.AlignmentPoint.Y - text.InsertPoint.Y;
			double len = Math.Sqrt(dx * dx + dy * dy);
			if (len > 1e-9)
			{
				fitWidth = len;
				rotation = Math.Atan2(dy, dx);
			}
			ax = 0;
		}

		this.AddTextBlock(tokens, anchor, text.Height, rotation, text.WidthFactor, text.ObliqueAngle, 0, ax, v, false, 1, true, text.Style, color, ocs,
			fitWidth, text.HorizontalAlignment == TextHorizontalAlignment.Aligned);
	}

	private void AddMText(MText mtext, Rgb color, Affine2 ocs)
	{
		var tokens = MTextTokenizer.Tokenize(mtext.Value); // 비어 있어도 편집용 커서 자리는 만든다

		int ap = (int)mtext.AttachmentPoint;
		if (ap < 1 || ap > 9) ap = 1;
		double ax = ((ap - 1) % 3) * 0.5;
		TextVAlign v = ((ap - 1) / 3) switch { 1 => TextVAlign.Middle, 2 => TextVAlign.Bottom, _ => TextVAlign.Top };

		// AlignmentPoint는 MTEXT의 X축 방향 벡터
		double rotation = mtext.AlignmentPoint.X != 0 || mtext.AlignmentPoint.Y != 0
			? Math.Atan2(mtext.AlignmentPoint.Y, mtext.AlignmentPoint.X)
			: mtext.Rotation;
		double wf = mtext.Style is { Width: > 0 } st ? st.Width : 1.0;
		double oblique = mtext.Style?.ObliqueAngle ?? 0;

		// 열이 있으면 열 폭으로 줄을 나누고 열마다 높이만큼 담는다
		var columns = MTextColumns.Read(mtext);
		// 배경 마스크 (90: 1 배경 채우기 색, 2 도면 배경색)
		(Rgb? Color, double Scale)? mask = null;
		var bg = mtext.BackgroundFillFlags;
		if (bg.HasFlag(BackgroundFillFlags.UseBackgroundFillColor) || bg.HasFlag(BackgroundFillFlags.UseDrawingWindowColor))
		{
			mask = (bg.HasFlag(BackgroundFillFlags.UseDrawingWindowColor) ? null : ToRgb(mtext.BackgroundColor), mtext.BackgroundScale > 0 ? mtext.BackgroundScale : 1.5);
		}
		this.AddTextBlock(tokens, mtext.InsertPoint, mtext.Height, rotation, wf, oblique, columns?.Width ?? mtext.RectangleWidth, ax, v, true,
			mtext.LineSpacing, mtext.LineSpacingStyle == LineSpacingStyleType.Exact, mtext.Style, color, ocs, columns: columns, mask: mask);
	}

	private void AddMultiLeader(MultiLeader mleader, Rgb color, Layer layer, Context ctx)
	{
		var data = mleader.ContextData;
		if (data == null)
		{
			return;
		}

		// 지시선 색·선종류·화살촉 (기존 CAD 속성창 "지시선 색상/선종류/화살촉 머리"): 블록별 = 객체 값
		var lineColor = mleader.LineColor;
		Rgb lc = lineColor.IsByBlock ? color : this.ResolveColor(lineColor, layer, ctx);
		double[]? dash = this.LeaderDash(mleader, layer, ctx);
		double scale = mleader.ScaleFactor > 0 ? mleader.ScaleFactor : 1.0;
		bool noLine = mleader.PathType == MultiLeaderPathType.Invisible;

		foreach (var root in data.LeaderRoots)
		{
			foreach (var line in root.Lines)
			{
				var pts = line.Points.SelectMany(p => new[] { p.X, p.Y }).Concat(new[] { root.ConnectionPoint.X, root.ConnectionPoint.Y }).ToArray();
				if (!noLine)
				{
					this.AddPolyline(pts, false, lc, dash, ctx.Xf);
				}
				if (pts.Length >= 4 && mleader.ArrowheadSize > 0 && !noLine)
				{
					this.AddLeaderArrow(mleader.Arrowhead, pts[0], pts[1], pts[2], pts[3], mleader.ArrowheadSize * scale, lc, layer, ctx);
				}
			}
			// 연결선(도그레그): 연결점에서 문자 쪽 방향으로
			if (mleader.EnableDogleg && root.LandingDistance > 0)
			{
				var d = root.Direction;
				double len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
				if (len > 1e-12)
				{
					double l = root.LandingDistance * (mleader.ScaleFactor > 0 ? mleader.ScaleFactor : 1.0);
					this.AddPolyline(new[] { root.ConnectionPoint.X, root.ConnectionPoint.Y, root.ConnectionPoint.X + d.X / len * l, root.ConnectionPoint.Y + d.Y / len * l }, false, lc, dash, ctx.Xf);
				}
			}
		}

		// 빈 문자여도 넘긴다 (편집 중 커서 자리) — 그릴 글자가 없으면 배치 엔진이 커서 자리만 둔다
		if (data.HasTextContents)
		{
			double rotation = data.Direction.X != 0 || data.Direction.Y != 0 ? Math.Atan2(data.Direction.Y, data.Direction.X) : data.TextRotation;
			double ax = data.TextAlignment switch
			{
				TextAlignmentType.Center => 0.5,
				TextAlignmentType.Right => 1,
				_ => 0,
			};
			this.AddTextBlock(MTextTokenizer.Tokenize(data.TextLabel), data.TextLocation, data.TextHeight, rotation, 1.0, 0, data.BoundaryWidth, ax, TextVAlign.Top, true,
				data.LineSpacingFactor, false, data.TextStyle, color, ctx.Xf);
		}
	}

	/// <summary>문자 블록: 엔티티 좌표(블록 변환 xf 적용 전) → 도면 좌표로 바꿔 배치 엔진에 넘긴다.</summary>
	private void AddTextBlock(List<MTextToken> tokens, XYZ anchor, double height, double rotation, double widthFactor, double oblique,
		double wrapWidth, double ax, TextVAlign v, bool isMText, double lineSpacing, bool exact, TextStyle? style, Rgb color, Affine2 xf,
		double? fitWidth = null, bool fitKeepsRatio = false, MTextColumnInfo? columns = null, (Rgb? Color, double Scale)? mask = null)
	{
		if (height <= 0)
		{
			return;
		}
		var (x, y) = xf.Apply(anchor.X, anchor.Y);
		var (dx, dy) = xf.Apply(anchor.X + Math.Cos(rotation), anchor.Y + Math.Sin(rotation));
		double worldRotation = Math.Atan2(dy - y, dx - x);
		double scale = xf.ScaleY;
		double wf = widthFactor > 0 ? widthFactor : 1.0;
		this.AddFormattedText(tokens, new TextSpec(x, y, worldRotation, height * scale, wf, oblique, wrapWidth * scale, ax, v, isMText,
			lineSpacing, exact, scale, style, color)
		{
			FitWidth = fitWidth * scale,
			FitKeepsRatio = fitKeepsRatio,
			Columns = columns == null ? null : columns with
			{
				Width = columns.Width * scale,
				Gutter = columns.Gutter * scale,
				Heights = columns.Heights.Select(h => h * scale).ToArray(),
				DefinedHeight = columns.DefinedHeight * scale,
			},
			Mask = mask,
		});
	}

	private void Snap(double x, double y, SnapKind kind, Affine2 xf)
	{
		var (wx, wy) = xf.Apply(x, y);
		this._scene.SnapPoints.Add(new SnapPoint(wx, wy, kind, this._top));
	}

	private void AddPolyline(double[] localPts, bool closed, Rgb color, double[]? dash, Affine2 xf)
	{
		var complex = this._complexLt;
		this._complexLt = null;
		if (localPts.Length < 4)
		{
			return;
		}

		double[] pts = new double[localPts.Length];
		for (int i = 0; i < localPts.Length; i += 2)
		{
			(pts[i], pts[i + 1]) = xf.Apply(localPts[i], localPts[i + 1]);
			// NaN/무한 좌표가 하나라도 있으면 이 도형은 버린다. 장면 범위가 NaN이 되면 화면의 모든 선이 사라졌다
			if (!double.IsFinite(pts[i]) || !double.IsFinite(pts[i + 1]))
			{
				return;
			}
		}

		double[]? worldDash = dash?.Select(d => d * xf.ScaleX).ToArray();
		double dashOffset = 0;
		if (worldDash != null)
		{
			// 패턴 한 주기보다 짧은 선은 실선 (기존 CAD와 같음)
			double? off = CenteredDashOffset(pts, closed, worldDash);
			if (off == null)
			{
				worldDash = null;
				complex = null;
			}
			else
			{
				dashOffset = off.Value;
			}
		}
		var bounds = Bounds.FromPoints(pts);
		this._scene.Polylines.Add(new ScenePolyline(pts, closed, color, worldDash, bounds, this._top) { IsText = this._inShxText, Order = this._order, DashOffset = dashOffset });
		if (!this._skipExtents)
		{
			this._scene.Include(bounds);
		}
		if (complex != null && worldDash != null)
		{
			this.AddLinetypeElements(pts, closed, complex.Value.Lt, complex.Value.Scale * xf.ScaleX, dashOffset, color);
		}
	}

	/// <summary>그라데이션 해치의 두 색. 한 색 그라데이션이면 색조(ColorTint)로 밝게/어둡게 만든 색이 두 번째 색.</summary>
	private static SceneGradient? GradientOf(Hatch hatch)
	{
		var g = hatch.GradientColor;
		if (g == null || !g.Enabled || g.Colors.Count == 0)
		{
			return null;
		}
		Rgb c1 = ToRgb(g.Colors[0].Color);
		Rgb c2;
		if (g.IsSingleColorGradient || g.Colors.Count < 2)
		{
			// 색조 0 = 검정 쪽, 1 = 흰색 쪽 (기존 CAD의 어둡게~밝게)
			double t = Math.Clamp(g.ColorTint, 0, 1);
			byte Mix(byte v) => (byte)(t >= 0.5 ? v + (255 - v) * (t - 0.5) * 2 : v * t * 2);
			c2 = new Rgb(Mix(c1.R), Mix(c1.G), Mix(c1.B));
		}
		else
		{
			c2 = ToRgb(g.Colors[1].Color);
		}
		string name = string.IsNullOrEmpty(g.Name) ? "LINEAR" : g.Name.ToUpperInvariant();
		return new SceneGradient(name, c1, c2, g.Angle, g.Shift == 0);
	}

	private void AddFill(List<double[]> localLoops, Rgb color, Affine2 xf, SceneGradient? gradient = null)
	{
		var loops = new List<double[]>(localLoops.Count);
		Bounds bounds = Bounds.Empty;
		foreach (double[] loop in localLoops)
		{
			double[] pts = new double[loop.Length];
			for (int i = 0; i < loop.Length; i += 2)
			{
				(pts[i], pts[i + 1]) = xf.Apply(loop[i], loop[i + 1]);
			}
			loops.Add(pts);
			bounds = bounds.Union(Bounds.FromPoints(pts));
		}
		this._scene.Fills.Add(new SceneFill(loops, color, bounds, this._top) { Gradient = gradient, Order = this._order });
		this._scene.Include(bounds);
	}

	private Layer EffectiveLayer(Entity e, Context ctx)
	{
		Layer layer = e.Layer ?? Layer.Default;
		if (ctx.BlockLayer != null && layer.Name == Layer.DefaultName)
		{
			return ctx.BlockLayer;
		}
		return layer;
	}

	private Rgb ResolveColor(Color c, Layer layer, Context ctx)
	{
		if (c.IsByLayer)
		{
			c = layer.Color;
		}
		else if (c.IsByBlock)
		{
			return ctx.BlockColor;
		}
		return ToRgb(c);
	}

	internal static Rgb ToRgb(Color c)
	{
		if (c.IsTrueColor)
		{
			ReadOnlySpan<byte> rgb = c.GetTrueColorRgb();
			return new Rgb(rgb[0], rgb[1], rgb[2]);
		}
		if (c.IsByLayer || c.IsByBlock || c.Index == 7)
		{
			return Rgb.White; // 7번(흰/검)은 검은 배경 기준으로 흰색
		}
		return new Rgb(c.R, c.G, c.B);
	}

	/// <summary>엔티티 선 종류의 대시 패턴 (로컬 단위). 실선이면 null.</summary>
	private double[]? Dash(Entity e, Layer layer, Context ctx)
	{
		LineType? lt = e.LineType;
		if (lt == null || lt.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase))
		{
			lt = layer.LineType;
		}
		else if (lt.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase))
		{
			lt = ctx.BlockLineType;
		}

		if (lt == null || lt.Segments.Count() < 2)
		{
			return null;
		}

		double scale = this._ltScale * (e.LineTypeScale > 0 ? e.LineTypeScale : 1.0);
		this._complexLt = IsComplex(lt) ? (lt, scale) : null;
		return lt.Segments.Select(s => s.Length * scale).ToArray();
	}

	/// <summary>법선이 -Z인 엔티티(좌우 반전 복사 등)의 OCS → WCS 변환.</summary>
	private static Affine2 OcsTransform(Entity e)
	{
		XYZ normal = e switch
		{
			LwPolyline lw => lw.Normal,
			Polyline2D pl => pl.Normal,
			TextEntity t => t.Normal,
			MText m => m.Normal,
			Insert i => i.Normal,
			Hatch h => h.Normal,
			Solid s => s.Normal,
			Circle c => c.Normal, // Arc 포함. 다각형은 이미 WCS이므로 중심·사분점 스냅에만 쓰임
			_ => XYZ.AxisZ,
		};
		return normal.Z < 0 ? Affine2.Scale(-1, 1) : Affine2.Identity;
	}

	/// <summary>
	/// 스플라인을 점 배열로. 제어점+매듭이 있으면 직접 NURBS 평가(주기 스플라인 (0,0) 버그 회피, 구간당 3선분이라 가볍다),
	/// 없으면(맞춤점만) ACadSharp 계산으로 대체.
	/// </summary>
	private static double[]? SplinePoints(Spline spline)
	{
		if (spline.ControlPoints.Count >= 2 && spline.Knots.Count == spline.ControlPoints.Count + spline.Degree + 1)
		{
			var ctrl = spline.ControlPoints.Select(c => (c.X, c.Y)).ToList();
			var pts = Nurbs.Tessellate(spline.Degree, spline.Knots, ctrl, spline.Weights.Count == ctrl.Count ? spline.Weights : null);
			if (pts != null)
			{
				var arr = new double[pts.Count * 2];
				for (int i = 0; i < pts.Count; i++)
				{
					arr[2 * i] = pts[i].X;
					arr[2 * i + 1] = pts[i].Y;
				}
				return arr;
			}
		}
		if (spline.TryPolygonalVertexes(CircleSegments, out List<XYZ> sp))
		{
			return Flatten(sp);
		}
		return null;
	}

	/// <summary>지시선 경로: 직선이면 꼭짓점 그대로, 스플라인(점 3개 이상)이면 꼭짓점을 지나는 곡선.</summary>
	private static double[] LeaderPath(Leader leader)
	{
		var v = leader.Vertices;
		if (leader.PathType == LeaderPathType.Spline && v.Count >= 3)
		{
			return FitCurve(v.Select(p => (p.X, p.Y)).ToList(), 16);
		}
		return v.SelectMany(p => new[] { p.X, p.Y }).ToArray();
	}

	/// <summary>점들을 지나는 자연 3차 스플라인 (현 길이 매개변수, 구간당 seg개 선분) — 맞춤점 스플라인 지시선.</summary>
	internal static double[] FitCurve(IReadOnlyList<(double X, double Y)> pts, int seg)
	{
		int n = pts.Count;
		var t = new double[n];
		for (int i = 1; i < n; i++)
		{
			double dx = pts[i].X - pts[i - 1].X, dy = pts[i].Y - pts[i - 1].Y;
			t[i] = t[i - 1] + Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-9);
		}
		// 자연 경계(양끝 2차 미분 0)의 3중 대각 방정식 → 각 점의 2차 미분
		double[] Second(Func<int, double> f)
		{
			var m = new double[n];
			var c = new double[n];
			var r = new double[n];
			for (int i = 1; i < n - 1; i++)
			{
				double h0 = t[i] - t[i - 1], h1 = t[i + 1] - t[i];
				double rhs = 6 * ((f(i + 1) - f(i)) / h1 - (f(i) - f(i - 1)) / h0);
				double denom = 2 * (h0 + h1) - h0 * c[i - 1];
				c[i] = h1 / denom;
				r[i] = (rhs - h0 * r[i - 1]) / denom;
			}
			for (int i = n - 2; i >= 1; i--)
			{
				m[i] = r[i] - c[i] * m[i + 1];
			}
			return m;
		}
		var mx = Second(i => pts[i].X);
		var my = Second(i => pts[i].Y);
		var outPts = new List<double>((n - 1) * seg * 2 + 2) { pts[0].X, pts[0].Y };
		for (int i = 0; i < n - 1; i++)
		{
			double h = t[i + 1] - t[i];
			for (int k = 1; k <= seg; k++)
			{
				double u = h * k / seg, w = h - u;
				double Eval(double y0, double y1, double m0, double m1) =>
					m0 * w * w * w / (6 * h) + m1 * u * u * u / (6 * h) + (y0 / h - m0 * h / 6) * w + (y1 / h - m1 * h / 6) * u;
				outPts.Add(Eval(pts[i].X, pts[i + 1].X, mx[i], mx[i + 1]));
				outPts.Add(Eval(pts[i].Y, pts[i + 1].Y, my[i], my[i + 1]));
			}
		}
		return outPts.ToArray();
	}

	/// <summary>화살촉 방향을 잡을 점: 끝점에서 화살촉 크기만큼 떨어진 경로 위의 첫 점 (없으면 마지막 점).</summary>
	private static (double X, double Y) ArrowFrom(double[] path, double size)
	{
		for (int i = 2; i + 1 < path.Length; i += 2)
		{
			double dx = path[i] - path[0], dy = path[i + 1] - path[1];
			if (path.Length == 4 || dx * dx + dy * dy >= size * size * 0.25)
			{
				return (path[i], path[i + 1]);
			}
		}
		return (path[^2], path[^1]);
	}

	private static double[] Flatten(IEnumerable<XYZ> pts) => pts.SelectMany(p => new[] { p.X, p.Y }).ToArray();

	/// <summary>
	/// 와이프아웃: 경계(픽셀 좌표, 가운데 원점, y는 아래쪽이 +)를 도면 좌표로 바꿔 가림 영역으로.
	/// 도면 좌표 = 삽입점 + U·(x + 0.5) + V·(높이 − (y + 0.5)).
	/// </summary>
	private void AddWipeout(Wipeout w, Rgb color, Affine2 xf)
	{
		var verts = w.ClipBoundaryVertices;
		if (verts.Count < 2)
		{
			return;
		}
		double sy = w.Size.Y > 0 ? w.Size.Y : 1;
		var local = new List<(double X, double Y)>();
		if (verts.Count == 2)
		{
			var a = verts[0];
			var b = verts[1];
			local.AddRange(new[] { (a.X, a.Y), (b.X, a.Y), (b.X, b.Y), (a.X, b.Y) });
		}
		else
		{
			local.AddRange(verts.Select(v => (v.X, v.Y)));
		}
		var pts = new List<double>();
		foreach (var (px, py) in local)
		{
			double u = px + 0.5, v = sy - (py + 0.5);
			double x = w.InsertPoint.X + w.UVector.X * u + w.VVector.X * v;
			double y = w.InsertPoint.Y + w.UVector.Y * u + w.VVector.Y * v;
			var (wx, wy) = xf.Apply(x, y);
			pts.Add(wx);
			pts.Add(wy);
		}
		if (pts.Count >= 6 && Math.Abs(pts[0] - pts[^2]) < 1e-9 && Math.Abs(pts[1] - pts[^1]) < 1e-9)
		{
			pts.RemoveRange(pts.Count - 2, 2);
		}
		var loop = pts.ToArray();
		var bounds = Bounds.FromPoints(loop);
		this._scene.Wipeouts.Add(new SceneWipeout(loop, this._order, WipeoutFrames, color, bounds, this._top));
		this._scene.Include(bounds);
	}

	/// <summary>다중선: 스타일 요소마다 꼭짓점 + 마이터 방향 × 요소 거리로 이은 선 (요소 색·선종류).</summary>
	private void AddMLine(MLine ml, Layer layer, Context ctx)
	{
		if (ml.Vertices.Count < 2)
		{
			return;
		}
		var elements = ml.Style?.Elements.ToList() ?? new List<ACadSharp.Objects.MLineStyle.Element>();
		int lines = ml.Vertices[0].Segments.Count;
		bool closed = ml.Flags.HasFlag(MLineFlags.Closed);
		for (int k = 0; k < lines; k++)
		{
			var pts = new List<double>();
			foreach (var v in ml.Vertices)
			{
				double d = k < v.Segments.Count && v.Segments[k].Parameters.Count > 0 ? v.Segments[k].Parameters[0] : 0;
				pts.Add(v.Position.X + v.Miter.X * d);
				pts.Add(v.Position.Y + v.Miter.Y * d);
			}
			var el = k < elements.Count ? elements[k] : null;
			Rgb color = this.ResolveColor(el != null && !el.Color.IsByLayer ? el.Color : ml.Color, layer, ctx);
			this.AddPolyline(pts.ToArray(), closed, color, this.Dash(ml, layer, ctx), ctx.Xf);
		}
		foreach (var v in ml.Vertices)
		{
			this.Snap(v.Position.X, v.Position.Y, SnapKind.Endpoint, ctx.Xf);
		}
	}

	/// <summary>다중 지시선 선종류: 블록별(또는 없음) = 객체 선종류, 도면층별 = 도면층, 그 밖은 그 선종류.</summary>
	private double[]? LeaderDash(MultiLeader ml, Layer layer, Context ctx)
	{
		var lt = ml.LeaderLineType;
		if (lt == null || lt.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase))
		{
			return this.Dash(ml, layer, ctx);
		}
		if (lt.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase))
		{
			lt = layer.LineType;
		}
		if (lt == null || lt.Segments.Count() < 2)
		{
			return null;
		}
		double s = this._ltScale * (ml.LineTypeScale > 0 ? ml.LineTypeScale : 1.0);
		return lt.Segments.Select(x => x.Length * s).ToArray();
	}

	/// <summary>
	/// 지시선 화살촉: 화살촉 블록(건축 틱·점·없음 등, 치수 화살촉과 같은 블록)이 있으면 끝점에 크기만큼 넣고, 없으면 닫고 채운 화살촉.
	/// </summary>
	private void AddLeaderArrow(BlockRecord? arrow, double tx, double ty, double fx, double fy, double size, Rgb color, Layer layer, Context ctx)
	{
		if (arrow == null)
		{
			this.AddArrowHead(tx, ty, fx, fy, size, color, ctx.Xf);
			return;
		}
		if (arrow.Name.Equals("_None", StringComparison.OrdinalIgnoreCase) || size <= 0)
		{
			return;
		}
		// 화살촉 블록은 끝점이 원점, +X 방향을 가리킨다 (치수 화살촉과 같음)
		var ins = new Insert(arrow)
		{
			InsertPoint = new XYZ(tx, ty, 0),
			Rotation = Math.Atan2(ty - fy, tx - fx),
			XScale = size,
			YScale = size,
			ZScale = size,
			LineType = LineType.Continuous, // 화살촉은 지시선 선종류와 상관없이 실선
		};
		this.AddInsert(ins, color, layer, ctx with { BlockLineType = null });
	}

	/// <summary>닫고 채운 화살촉: 끝(tx,ty), 꼬리 쪽 점(fx,fy) 방향, 길이 size (폭은 길이의 1/3).</summary>
	private void AddArrowHead(double tx, double ty, double fx, double fy, double size, Rgb color, Affine2 xf)
	{
		double dx = fx - tx, dy = fy - ty, len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 1e-12 || size <= 0) return;
		double ux = dx / len, uy = dy / len;
		double bx = tx + ux * size, by = ty + uy * size, hw = size / 6;
		this.AddFill(new List<double[]> { new[] { tx, ty, bx - uy * hw, by + ux * hw, bx + uy * hw, by - ux * hw } }, color, xf);
	}

	/// <summary>구성선·광선: 도면 범위의 몇십 배 길이로 (전체 보기 범위에는 넣지 않음).</summary>
	private void FlushInfinite()
	{
		if (this._infinite.Count == 0)
		{
			return;
		}
		var ext = this._scene.Extents;
		double size = ext.IsEmpty ? 1000 : Math.Max(1000, Math.Max(ext.MaxX - ext.MinX, ext.MaxY - ext.MinY));
		double far = size * 50;
		this._skipExtents = true;
		foreach (var l in this._infinite)
		{
			double len = Math.Sqrt(l.Dx * l.Dx + l.Dy * l.Dy);
			if (len < 1e-12) continue;
			double ux = l.Dx / len, uy = l.Dy / len;
			// 기준점을 화면 범위 가운데에 가장 가까운 점으로 옮겨 양쪽 길이를 고르게
			double cx = ext.IsEmpty ? l.X : (ext.MinX + ext.MaxX) / 2, cy = ext.IsEmpty ? l.Y : (ext.MinY + ext.MaxY) / 2;
			double t0 = (cx - l.X) * ux + (cy - l.Y) * uy;
			double sx, sy;
			if (l.Ray)
			{
				(sx, sy) = (l.X, l.Y);
			}
			else
			{
				(sx, sy) = (l.X + ux * (t0 - far), l.Y + uy * (t0 - far));
			}
			double ex = l.X + ux * (Math.Max(t0, 0) + far), ey = l.Y + uy * (Math.Max(t0, 0) + far);
			this._top = l.Top;
			this.AddPolyline(new[] { sx, sy, ex, ey }, false, l.Color, l.Dash, l.Xf);
		}
		this._skipExtents = false;
		this._infinite.Clear();
	}

	/// <summary>점: 점 스타일(PDMODE) 모양, 크기(PDSIZE: 양수 = 절대, 0·음수 = 도면 범위의 5%·%).</summary>
	private void AddPoint(Point point, Rgb color, Affine2 xf)
	{
		int mode = this._doc.Header.PointDisplayMode;
		double size = this._doc.Header.PointDisplaySize;
		if (size <= 0)
		{
			var mn = this._doc.Header.ModelSpaceExtMin;
			var mx = this._doc.Header.ModelSpaceExtMax;
			double h = mx.Y - mn.Y;
			if (!double.IsFinite(h) || h <= 0 || h > 1e12) h = 100;
			size = h * (size < 0 ? -size : 5) / 100;
		}
		double x = point.Location.X, y = point.Location.Y, r = size / 2;
		switch (mode & 7)
		{
			case 0: // 점
				this.AddPolyline(new[] { x, y, x + size * 1e-3, y }, false, color, null, xf);
				break;
			case 2: // +
				this.AddPolyline(new[] { x - r, y, x + r, y }, false, color, null, xf);
				this.AddPolyline(new[] { x, y - r, x, y + r }, false, color, null, xf);
				break;
			case 3: // X
				this.AddPolyline(new[] { x - r, y - r, x + r, y + r }, false, color, null, xf);
				this.AddPolyline(new[] { x - r, y + r, x + r, y - r }, false, color, null, xf);
				break;
			case 4: // 위로 짧은 선
				this.AddPolyline(new[] { x, y, x, y + r }, false, color, null, xf);
				break;
		}
		if ((mode & 32) != 0)
		{
			var c = new List<double>();
			for (int i = 0; i < 32; i++)
			{
				double a = 2 * Math.PI * i / 32;
				c.Add(x + r * Math.Cos(a));
				c.Add(y + r * Math.Sin(a));
			}
			this.AddPolyline(c.ToArray(), true, color, null, xf);
		}
		if ((mode & 64) != 0)
		{
			this.AddPolyline(new[] { x - r, y - r, x + r, y - r, x + r, y + r, x - r, y + r }, true, color, null, xf);
		}
	}

	/// <summary>폭 있는 폴리선: 조각마다 채운 띠 (호 조각은 잘게 나눠 안쪽·바깥쪽 경계).</summary>
	private void AddWidePolyline(LwPolyline lw, Rgb color, Affine2 xf)
	{
		var v = lw.Vertices;
		int n = v.Count;
		// 꼭짓점 둘인 닫힌 폴리선(ACadSharp 건축 틱 화살촉)은 같은 구간을 되돌아오므로 한 구간만 — 겹친 두 채움이 짝홀 규칙으로 지워져 안 보였다
		int segs = lw.IsClosed && n > 2 ? n : n - 1;
		for (int i = 0; i < segs; i++)
		{
			var a = v[i];
			var b = v[(i + 1) % n];
			double w0 = lw.ConstantWidth > 0 ? lw.ConstantWidth : a.StartWidth;
			double w1 = lw.ConstantWidth > 0 ? lw.ConstantWidth : a.EndWidth;
			var center = new List<double> { a.Location.X, a.Location.Y };
			if (a.Bulge != 0)
			{
				AppendBulge(center, a.Location.X, a.Location.Y, b.Location.X, b.Location.Y, a.Bulge);
			}
			center.Add(b.Location.X);
			center.Add(b.Location.Y);
			int m = center.Count / 2;
			if (w0 <= 0 && w1 <= 0)
			{
				this.AddPolyline(center.ToArray(), false, color, null, xf);
				continue;
			}
			var left = new List<double>();
			var right = new List<double>();
			for (int k = 0; k < m; k++)
			{
				int k0 = Math.Max(0, k - 1), k1 = Math.Min(m - 1, k + 1);
				double dx = center[k1 * 2] - center[k0 * 2], dy = center[k1 * 2 + 1] - center[k0 * 2 + 1];
				double len = Math.Sqrt(dx * dx + dy * dy);
				if (len < 1e-12) { dx = 1; dy = 0; len = 1; }
				double t = m > 1 ? (double)k / (m - 1) : 0;
				double hw = (w0 + (w1 - w0) * t) / 2;
				double nx = -dy / len * hw, ny = dx / len * hw;
				left.Add(center[k * 2] + nx); left.Add(center[k * 2 + 1] + ny);
				right.Add(center[k * 2] - nx); right.Add(center[k * 2 + 1] - ny);
			}
			var loop = new List<double>(left);
			for (int k = m - 1; k >= 0; k--) { loop.Add(right[k * 2]); loop.Add(right[k * 2 + 1]); }
			this.AddFill(new List<double[]> { loop.ToArray() }, color, xf);
		}
	}

	/// <summary>
	/// 폴리선을 선종류 규칙대로 넣는다: 연속 패턴(PLINEGEN)이 아니고 대시가 있으면 꼭짓점 구간마다 패턴을 새로 시작한다 (기존 CAD와 같음).
	/// </summary>
	private void AddPolylineByLinetype(List<(double X, double Y, double Bulge)> v, bool closed, bool continuous, Rgb color, double[]? dash, Affine2 xf)
	{
		int n = v.Count;
		if (dash == null || continuous || n < 3)
		{
			this.AddPolyline(VertexPoints(v, closed), closed, color, dash, xf);
			return;
		}
		var complex = this._complexLt;
		int segs = closed ? n : n - 1;
		for (int i = 0; i < segs; i++)
		{
			var a = v[i];
			var b = v[(i + 1) % n];
			var list = new List<double> { a.X, a.Y };
			if (a.Bulge != 0)
			{
				AppendBulge(list, a.X, a.Y, b.X, b.Y, a.Bulge);
			}
			list.Add(b.X);
			list.Add(b.Y);
			this._complexLt = complex;
			this.AddPolyline(list.ToArray(), false, color, dash, xf);
		}
		this._complexLt = null;
	}

	private static double[] VertexPoints(List<(double X, double Y, double Bulge)> v, bool closed)
	{
		var list = new List<double>();
		int n = v.Count;
		for (int i = 0; i < n; i++)
		{
			var a = v[i];
			list.Add(a.X);
			list.Add(a.Y);
			bool hasNext = i + 1 < n || closed;
			if (hasNext && a.Bulge != 0)
			{
				var b = v[(i + 1) % n];
				AppendBulge(list, a.X, a.Y, b.X, b.Y, a.Bulge);
			}
		}
		return list.ToArray();
	}

	/// <summary>
	/// 기존 CAD처럼 선종류 패턴을 경로 가운데 기준으로 대칭이 되게 놓는다: 첫 대시의 가운데가 양 끝에서 같은 거리.
	/// 돌려주는 값 = 경로 시작이 패턴의 어느 위치에서 시작하는지 (음수면 패턴 앞쪽으로 당김). 경로가 한 주기보다 짧으면 null (실선).
	/// </summary>
	internal static double? CenteredDashOffset(double[] pts, bool closed, double[] dash)
	{
		double period = dash.Sum(Math.Abs);
		if (period <= 0)
		{
			return 0;
		}
		double len = 0;
		for (int i = 2; i < pts.Length; i += 2)
		{
			len += Math.Sqrt((pts[i] - pts[i - 2]) * (pts[i] - pts[i - 2]) + (pts[i + 1] - pts[i - 1]) * (pts[i + 1] - pts[i - 1]));
		}
		if (closed && pts.Length >= 4)
		{
			len += Math.Sqrt((pts[0] - pts[^2]) * (pts[0] - pts[^2]) + (pts[1] - pts[^1]) * (pts[1] - pts[^1]));
		}
		if (len < period)
		{
			return null;
		}
		double first = Math.Max(dash[0], 0);
		double n = Math.Max(0, Math.Round((len - first) / period));
		return (first + n * period - len) / 2;
	}

	private static double[] BoundaryPoints(Hatch.BoundaryPath path)
	{
		var list = new List<double>();
		foreach (Hatch.BoundaryPath.Edge edge in path.Edges)
		{
			switch (edge)
			{
				case Hatch.BoundaryPath.Polyline pl:
					{
						var bulges = pl.Bulges.ToList();
						for (int i = 0; i < pl.Vertices.Count; i++)
						{
							var a = pl.Vertices[i];
							list.Add(a.X);
							list.Add(a.Y);
							double bulge = i < bulges.Count ? bulges[i] : 0;
							bool hasNext = i + 1 < pl.Vertices.Count || pl.IsClosed;
							if (hasNext && bulge != 0)
							{
								var b = pl.Vertices[(i + 1) % pl.Vertices.Count];
								AppendBulge(list, a.X, a.Y, b.X, b.Y, bulge);
							}
						}
						break;
					}
				case Hatch.BoundaryPath.Line l:
					list.Add(l.Start.X);
					list.Add(l.Start.Y);
					list.Add(l.End.X);
					list.Add(l.End.Y);
					break;
				case Hatch.BoundaryPath.Arc a:
					list.AddRange(Flatten(a.PolygonalVertexes(CircleSegments / 2)));
					break;
				case Hatch.BoundaryPath.Ellipse el:
					list.AddRange(Flatten(el.PolygonalVertexes(CircleSegments / 2)));
					break;
				case Hatch.BoundaryPath.Spline s:
					list.AddRange(Flatten(s.PolygonalVertexes(CircleSegments / 2)));
					break;
			}
		}
		return list.ToArray();
	}

	/// <summary>bulge 호의 중간점들을 추가 (끝점 제외).</summary>
	private static void AppendBulge(List<double> list, double x1, double y1, double x2, double y2, double bulge)
	{
		double theta = 4 * Math.Atan(bulge); // 포함각 (부호 = 방향)
		double chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
		if (chord < 1e-12)
		{
			return;
		}
		double radius = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
		double mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
		// 현 중점에서 중심까지 거리. 반원보다 큰 호(|θ| > π)는 cos가 음수가 되어 반대편으로 넘어감
		double dist = radius * Math.Cos(Math.Abs(theta) / 2);
		double nx = -(y2 - y1) / chord, ny = (x2 - x1) / chord; // 현의 왼쪽 법선
		double sign = bulge > 0 ? 1 : -1; // 반시계(양수)면 중심이 왼쪽
		double cx = mx + sign * nx * dist;
		double cy = my + sign * ny * dist;
		double start = Math.Atan2(y1 - cy, x1 - cx);
		int n = Math.Max(2, (int)Math.Ceiling(CircleSegments * Math.Abs(theta) / (2 * Math.PI)));
		for (int k = 1; k < n; k++)
		{
			double t = start + theta * k / n;
			list.Add(cx + radius * Math.Cos(t));
			list.Add(cy + radius * Math.Sin(t));
		}
	}
}
