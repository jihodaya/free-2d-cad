using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// MTEXT (기존 CAD와 같은 순서): 첫 번째 구석 → [정렬(J)/회전각도(R)/텍스트스타일(S)/텍스트높이(H)/너비(W)]/&lt;반대 모서리&gt;
/// → 빈 여러 줄 문자를 만들고 앱이 바로 화면 편집(문자 편집기 탭)을 연다. 명령줄에서 문자를 쳐도 된다.
/// </summary>
public sealed class MTextCommand : EditorCommand
{
	private int _step;
	private (double X, double Y) _p1;
	private (double X, double Y) _p2;
	private string? _await;
	private double? _height;
	private double _rotation;
	private TextStyle? _style;
	private AttachmentPointType _attach = AttachmentPointType.TopLeft;

	public override string Name => "MTEXT";
	public override string[] Aliases => new[] { "MT", "T" };

	/// <summary>문자 없이 만든 여러 줄 문자 (앱이 바로 화면 편집을 시작, 비우고 끝내면 지운다).</summary>
	public MText? Created { get; private set; }

	private const string CornerPrompt = "[정렬(J)/회전각도(R)/텍스트스타일(S)/텍스트높이(H)/너비(W)]/<텍스트 블록의 반대 모서리>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._await = null;
		this.Created = null;
		this.Prompt = "MULTILINE 문자:  문자 블록의 첫번째 구석:";
	}

	private double Height(CommandContext ctx)
	{
		if (this._height is double h) return h;
		var st = this._style ?? ctx.Doc.Document.Header.CurrentTextStyle;
		if (st != null && st.Height > 0) return st.Height;
		return 2.5 * Free2DCad.Core.Styles.AnnotationScale.Get(ctx.Doc.Document);
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim();
		if (this._await != null)
		{
			switch (this._await)
			{
				case "H" when Num.TryParse(t, out double h) && h > 0:
					this._height = h;
					break;
				case "R" when Num.TryParse(t, out double r):
					this._rotation = r * Math.PI / 180;
					break;
				case "W" when Num.TryParse(t, out double w) && w > 0:
					this._p2 = (this._p1.X + w, this._p1.Y - this.Height(this.Ctx));
					this._await = null;
					this.Create(this.Ctx, string.Empty, w);
					return;
				case "S":
					if (this.Ctx.Doc.Document.TextStyles.TryGetValue(t, out var st)) this._style = st;
					else this.Ctx.Print($"\"{t}\" 문자 스타일이 없습니다.");
					break;
				case "J":
					{
						var map = new Dictionary<string, AttachmentPointType>(StringComparer.OrdinalIgnoreCase)
						{
							["TL"] = AttachmentPointType.TopLeft, ["TC"] = AttachmentPointType.TopCenter, ["TR"] = AttachmentPointType.TopRight,
							["ML"] = AttachmentPointType.MiddleLeft, ["MC"] = AttachmentPointType.MiddleCenter, ["MR"] = AttachmentPointType.MiddleRight,
							["BL"] = AttachmentPointType.BottomLeft, ["BC"] = AttachmentPointType.BottomCenter, ["BR"] = AttachmentPointType.BottomRight,
						};
						if (map.TryGetValue(t, out var ap)) this._attach = ap;
						break;
					}
			}
			this._await = null;
			this.Prompt = CornerPrompt;
			return;
		}
		if (this._step == 1)
		{
			switch (t.ToUpperInvariant())
			{
				case "H":
					this._await = "H";
					this.Prompt = $"텍스트 높이 <{this.Height(this.Ctx):0.####}>:";
					return;
				case "R":
					this._await = "R";
					this.Prompt = $"회전 각도 <{this._rotation * 180 / Math.PI:0.####}>:";
					return;
				case "S":
					this._await = "S";
					this.Prompt = $"텍스트 스타일 이름 <{(this._style ?? this.Ctx.Doc.Document.Header.CurrentTextStyle)?.Name}>:";
					return;
				case "W":
					this._await = "W";
					this.Prompt = "너비 지정:";
					return;
				case "J":
					this._await = "J";
					this.Prompt = "[TL/TC/TR/ML/MC/MR/BL/BC/BR] <TL>:";
					return;
			}
		}
		if (this._step == 2)
		{
			this.Create(this.Ctx, text, null);
			return;
		}
		var last = this._step == 1 ? this.Ctx.LastPoint : null;
		if (CoordinateParser.TryParse(t, last, null, false, out (double X, double Y) pt))
		{
			this.OnPoint(pt.X, pt.Y);
		}
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this._await = null;
			this.Prompt = CornerPrompt;
			return;
		}
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._await != null) return;

		if (this._step == 0)
		{
			this._p1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 1;
			this.Prompt = CornerPrompt;
		}
		else if (this._step == 1)
		{
			this._p2 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this.Create(this.Ctx, string.Empty, null);
		}
	}

	private void Create(CommandContext ctx, string text, double? width)
	{
		double minX = Math.Min(this._p1.X, this._p2.X);
		double maxX = Math.Max(this._p1.X, this._p2.X);
		double minY = Math.Min(this._p1.Y, this._p2.Y);
		double maxY = Math.Max(this._p1.Y, this._p2.Y);
		double w = width ?? (maxX - minX);
		if (w < 1e-6) w = 100.0;
		// 자리맞춤에 맞는 상자 위의 삽입점
		double ix = this._attach switch
		{
			AttachmentPointType.TopCenter or AttachmentPointType.MiddleCenter or AttachmentPointType.BottomCenter => (minX + maxX) / 2,
			AttachmentPointType.TopRight or AttachmentPointType.MiddleRight or AttachmentPointType.BottomRight => maxX,
			_ => minX,
		};
		double iy = this._attach switch
		{
			AttachmentPointType.MiddleLeft or AttachmentPointType.MiddleCenter or AttachmentPointType.MiddleRight => (minY + maxY) / 2,
			AttachmentPointType.BottomLeft or AttachmentPointType.BottomCenter or AttachmentPointType.BottomRight => minY,
			_ => maxY,
		};
		if (width != null)
		{
			ix = this._p1.X;
			iy = this._p1.Y;
		}
		var mtext = new MText
		{
			InsertPoint = new XYZ(ix, iy, 0),
			RectangleWidth = w,
			Height = this.Height(ctx),
			Value = text,
			AttachmentPoint = this._attach,
			AlignmentPoint = new XYZ(Math.Cos(this._rotation), Math.Sin(this._rotation), 0),
		};
		var style = this._style ?? ctx.Doc.Document.Header.CurrentTextStyle;
		if (style != null) mtext.Style = style;
		ctx.Doc.AddEntities(mtext);
		if (string.IsNullOrEmpty(text)) this.Created = mtext;
		this.Finish();
	}
}

/// <summary>
/// DIMRADIUS·DIMDIAMETER 공통 (기존 CAD와 같은 흐름): "호 또는 원 선택:" → "치수 텍스트 = 20.0000" →
/// "[각도(A)/Mtext/텍스트(T)]/&lt;치수선의위치(L)&gt;:". 현재 치수 스타일, 고른 원·호는 점선으로, 커서를 따라 미리보기.
/// </summary>
public abstract class RadialDimensionCommandBase : EditorCommand
{
	private int _step;
	private XYZ _center;
	private double _radius;
	private Entity? _picked;
	private string? _await;
	private string? _text;
	private double? _textAngle;

	protected abstract bool Diameter { get; }

	public override bool PickingObject => !this.IsFinished && this._step == 0;

	public override IEnumerable<Entity> Highlighted => this._picked != null ? new[] { this._picked } : Array.Empty<Entity>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._picked = null;
		this._await = null;
		this._text = null;
		this._textAngle = null;
		this.Prompt = "호 또는 원 선택:";
	}

	private void PlacePrompt()
	{
		this._await = null;
		this.Prompt = "[각도(A)/Mtext/텍스트(T)]/<치수선의위치(L)>:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._await != null)
		{
			if (this._await == "T")
			{
				this._text = t;
			}
			else if (Num.TryParse(t, out double a))
			{
				this._textAngle = Num.Rad(a);
			}
			this.PlacePrompt();
			return;
		}
		if (this._step != 1) return;
		switch (t.ToUpperInvariant())
		{
			case "M":
			case "T":
				this._await = "T";
				this.Prompt = "새 치수 문자 <<>>:";
				break;
			case "A":
				this._await = "A";
				this.Prompt = "치수 문자의 각도:";
				break;
		}
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this.PlacePrompt();
			return;
		}
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._await != null) return;
		if (this._step == 0)
		{
			var picked = this.Ctx.PickAt(x, y);
			if (picked is Circle c) // 호(Arc)도 Circle
			{
				this._picked = picked;
				this._center = c.Center;
				this._radius = c.Radius;
				this._step = 1;
				this.Ctx.Print($"치수 텍스트 = {this.Build(x + 1, y).GetMeasurementText()}");
				this.PlacePrompt();
			}
			else if (picked != null)
			{
				this.Ctx.Print("호 또는 원을 선택하십시오.");
			}
			return;
		}
		var dim = this.Build(x, y);
		this.Ctx.Doc.AddEntities(dim);
		DimensionMemory.Remember(this.Ctx.Doc, dim);
		this.Finish();
	}

	private Dimension Build(double x, double y)
	{
		double dx = x - this._center.X, dy = y - this._center.Y;
		double len = Math.Sqrt(dx * dx + dy * dy);
		if (len < 1e-9) { dx = 1; dy = 0; len = 1; }
		double nx = dx / len, ny = dy / len;
		var onCircle = new XYZ(this._center.X + nx * this._radius, this._center.Y + ny * this._radius, 0);
		var style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		// DXF 정의: 반지름 = 10 중심, 15 원 위 점 / 지름 = 15 커서 쪽 원 위 점, 10 반대쪽 점 (QDIM과 같음, 기존 CAD 확인)
		Dimension dim = this.Diameter
			? new DimensionDiameter
			{
				AngleVertex = onCircle,
				DefinitionPoint = new XYZ(this._center.X - nx * this._radius, this._center.Y - ny * this._radius, 0),
				Style = style,
			}
			: new DimensionRadius
			{
				DefinitionPoint = this._center,
				AngleVertex = onCircle,
				Style = style,
			};
		// 문자는 커서 위치 (기존 CAD처럼 치수선을 놓은 곳)
		dim.TextMiddlePoint = new XYZ(x, y, 0);
		dim.IsTextUserDefinedLocation = true;
		DimensionMemory.ApplyText(dim, this._text);
		if (this._textAngle is { } ta) dim.TextRotation = ta;
		try
		{
			dim.UpdateBlock();
		}
		catch (Exception)
		{
			// 블록을 만들 수 없는 경우 (반지름 0 등)
		}
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 1 && this._await == null && this.Ctx != null)
		{
			yield return this.Build(cursorX, cursorY);
		}
	}
}

/// <summary>DIMRADIUS: 원·호의 반지름 치수.</summary>
public sealed class DimRadiusCommand : RadialDimensionCommandBase
{
	public override string Name => "DIMRADIUS";
	public override string[] Aliases => new[] { "DRA" };

	protected override bool Diameter => false;
}

/// <summary>DIMDIAMETER: 원·호의 지름 치수.</summary>
public sealed class DimDiameterCommand : RadialDimensionCommandBase
{
	public override string Name => "DIMDIAMETER";
	public override string[] Aliases => new[] { "DDI" };

	protected override bool Diameter => true;
}

/// <summary>
/// DIMANGULAR 명령 (기존 CAD와 같은 흐름): 호·원·선 선택 또는 Enter로 정점 지정.
/// 선 두 개 → 교점 기준, 치수 호를 놓는 쪽의 각도. 호 → 호의 중심각. 원 → 원 위 두 점 사이 각도. 정점 → 꼭짓점과 두 끝점.
/// </summary>
public sealed class DimAngularCommand : EditorCommand
{
	private enum Mode { Pick, SecondLine, CircleSecond, Vertex, VertexFirst, VertexSecond, Place }

	private Mode _mode;
	private Line? _line1;
	private Line? _line2;
	private XYZ _center;
	private XYZ _p1;
	private XYZ _p2;
	private bool _threePoint;
	private string? _text;

	public override string Name => "DIMANGULAR";

	public override IEnumerable<Entity> Highlighted => new Entity?[] { this._line1, this._line2 }.Where(e => e != null).Select(e => e!);

	public override bool PickingObject => !this.IsFinished && this._mode is Mode.Pick or Mode.SecondLine;

	public override string[] Aliases => new[] { "DAN" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._mode = Mode.Pick;
		this._line1 = this._line2 = null;
		this._text = null;
		this._await = null;
		this._textAngle = null;
		this.Prompt = "각도를 지정하려면 ENTER를 누르십시오/<선, 호 또는 원 선택>:";
	}

	public override void OnEnter()
	{
		if (this._await != null)
		{
			this._await = null;
			this.ToPlace();
			return;
		}
		if (this._mode == Mode.Pick)
		{
			this._mode = Mode.Vertex;
			this.Prompt = "각도의 정점:";
		}
		else
		{
			this.Finish();
		}
	}

	private string? _await;
	private double? _textAngle;

	public override void OnText(string text)
	{
		string t = text.Trim().ToUpperInvariant();
		if (this._mode != Mode.Place) return;
		if (this._await == "T")
		{
			this._text = text.Trim().Length == 0 ? null : text.Trim();
			this._await = null;
			this.ToPlace();
			return;
		}
		if (this._await == "A")
		{
			if (Num.TryParse(t, out double deg)) this._textAngle = Num.Rad(deg);
			this._await = null;
			this.ToPlace();
			return;
		}
		switch (t)
		{
			case "T":
			case "M":
				{
					this._await = "T";
					string cur = this.Build(this._lastCursor)?.GetMeasurementText() ?? "";
					this.Prompt = $"새 치수 문자 <{cur}>:";
					break;
				}
			case "A":
				this._await = "A";
				this.Prompt = "문자 각도:";
				break;
		}
	}

	private XYZ _lastCursor;

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var pt = new XYZ(x, y, 0);
		switch (this._mode)
		{
			case Mode.Pick:
				{
					var picked = this.Ctx.PickAt(x, y);
					switch (picked)
					{
						case Line l:
							this._line1 = l;
							this._mode = Mode.SecondLine;
							this.Prompt = "각도 치수의 다른 선:";
							break;
						case Arc a:
							this._threePoint = true;
							this._center = a.Center;
							this._p1 = new XYZ(a.Center.X + a.Radius * Math.Cos(a.StartAngle), a.Center.Y + a.Radius * Math.Sin(a.StartAngle), 0);
							this._p2 = new XYZ(a.Center.X + a.Radius * Math.Cos(a.EndAngle), a.Center.Y + a.Radius * Math.Sin(a.EndAngle), 0);
							this.ToPlace();
							break;
						case Circle ci:
							this._threePoint = true;
							this._center = ci.Center;
							this._p1 = ProjectOnCircle(ci.Center, ci.Radius, pt);
							this._mode = Mode.CircleSecond;
							this.Prompt = "각도의 다른 변:";
							break;
						default:
							this.Ctx.Print("선, 호 또는 원을 선택하십시오. (Enter = 정점 지정)");
							break;
					}
					break;
				}
			case Mode.SecondLine:
				{
					if (this.Ctx.PickAt(x, y) is Line l2 && !ReferenceEquals(l2, this._line1))
					{
						if (Intersect(this._line1!, l2) is null)
						{
							this.Ctx.Print("두 선이 평행합니다. 다른 선을 선택하십시오.");
							return;
						}
						this._line2 = l2;
						this._threePoint = false;
						this.ToPlace();
					}
					else
					{
						this.Ctx.Print("각도 치수의 다른 선을 선택하십시오.");
					}
					break;
				}
			case Mode.CircleSecond:
				this._p2 = pt;
				this.ToPlace();
				break;
			case Mode.Vertex:
				this._center = pt;
				this._mode = Mode.VertexFirst;
				this.Prompt = "각도의 첫번째 변:";
				this.Ctx.LastPoint = (x, y);
				break;
			case Mode.VertexFirst:
				this._p1 = pt;
				this._mode = Mode.VertexSecond;
				this.Prompt = "각도의 다른 변:";
				break;
			case Mode.VertexSecond:
				this._p2 = pt;
				this._threePoint = true;
				this.ToPlace();
				break;
			case Mode.Place:
				{
					var dim = this.Build(pt);
					if (dim != null)
					{
						this.Ctx.Doc.AddEntities(dim);
						this.Ctx.Print($"치수 텍스트 = {dim.GetMeasurementText()}");
					}
					this.Finish();
					break;
				}
		}
	}

	private void ToPlace()
	{
		this._mode = Mode.Place;
		this.Prompt = "[각도(A)/문자(T)]/<치수호의위치(L)>:";
	}

	private static XYZ ProjectOnCircle(XYZ c, double r, XYZ p)
	{
		double dx = p.X - c.X, dy = p.Y - c.Y, len = Math.Sqrt(dx * dx + dy * dy);
		return len < 1e-12 ? new XYZ(c.X + r, c.Y, 0) : new XYZ(c.X + dx / len * r, c.Y + dy / len * r, 0);
	}

	private static XYZ? Intersect(Line a, Line b)
	{
		double x1 = a.StartPoint.X, y1 = a.StartPoint.Y, x2 = a.EndPoint.X, y2 = a.EndPoint.Y;
		double x3 = b.StartPoint.X, y3 = b.StartPoint.Y, x4 = b.EndPoint.X, y4 = b.EndPoint.Y;
		double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
		if (Math.Abs(den) < 1e-12) return null;
		double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
		return new XYZ(x1 + t * (x2 - x1), y1 + t * (y2 - y1), 0);
	}

	private Dimension? Build(XYZ arcPoint)
	{
		if (this.Ctx == null) return null;
		var style = DimensionStyleHelper.Current(this.Ctx.Doc.Document);
		Dimension dim;
		if (this._threePoint)
		{
			dim = new DimensionAngular3Pt
			{
				AngleVertex = this._center,
				FirstPoint = this._p1,
				SecondPoint = this._p2,
				DefinitionPoint = arcPoint,
				Style = style,
			};
		}
		else
		{
			if (this._line1 == null || this._line2 == null) return null;
			dim = new DimensionAngular2Line
			{
				FirstPoint = this._line1.StartPoint,
				SecondPoint = this._line1.EndPoint,
				AngleVertex = this._line2.StartPoint,
				DefinitionPoint = this._line2.EndPoint,
				DimensionArc = arcPoint,
				Style = style,
			};
		}
		if (!string.IsNullOrEmpty(this._text))
		{
			dim.Text = this._text;
		}
		if (this._textAngle is { } ta)
		{
			dim.TextRotation = ta;
		}
		dim.UpdateBlock();
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._mode == Mode.Place)
		{
			this._lastCursor = new XYZ(cursorX, cursorY, 0);
		}
		if (this._mode == Mode.Place && this._await == null && this.Build(new XYZ(cursorX, cursorY, 0)) is { } dim)
		{
			yield return dim;
		}
		else if (this._mode is Mode.CircleSecond or Mode.VertexSecond)
		{
			yield return new Line(this._center, this._p1);
			yield return new Line(this._center, new XYZ(cursorX, cursorY, 0));
		}
		else if (this._mode == Mode.VertexFirst)
		{
			yield return new Line(this._center, new XYZ(cursorX, cursorY, 0));
		}
	}
}
