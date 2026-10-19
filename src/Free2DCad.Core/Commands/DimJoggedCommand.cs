using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>
/// DIMJOGGED (꺾기 반지름 치수): 호 또는 원 선택 → 중심 위치 재정의 → (치수 텍스트 = …) → 치수선 위치 → 꺾임 위치.
/// 기존 CAD와 같은 LARGE_RADIAL_DIMENSION 객체로 저장한다.
/// </summary>
public sealed class DimJoggedCommand : EditorCommand
{
	private Circle? _circle;
	private XY _override;
	private XY _linePt;
	private int _step;
	private string? _text;
	private double? _textAngle;
	private string? _await;

	public override string Name => "DIMJOGGED";

	public override IEnumerable<Entity> Highlighted => this._circle != null ? new Entity[] { this._circle } : Array.Empty<Entity>();

	public override bool PickingObject => !this.IsFinished && this._step == 0;

	public override string[] Aliases => new[] { "DJO", "JOG" };

	private const string LinePrompt = "[각도(A)/Mtext/텍스트(T)]/<치수선의위치(L)>:";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._circle = null;
		this._step = 0;
		this._text = null;
		this._textAngle = null;
		this._await = null;
		this.Prompt = "호 또는 원 선택:";
	}

	public override void OnText(string text)
	{
		string t = text.Trim();
		if (this._await == "T")
		{
			this._text = t.Length == 0 ? null : t;
			this._await = null;
			this.Prompt = LinePrompt;
			return;
		}
		if (this._await == "A")
		{
			if (double.TryParse(t, out double deg))
			{
				this._textAngle = deg * Math.PI / 180;
				this._await = null;
				this.Prompt = LinePrompt;
			}
			return;
		}
		if (this._step != 2) return;
		switch (t.ToUpperInvariant())
		{
			case "T":
			case "M":
			case "MTEXT":
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
			this._await = null;
			this.Prompt = LinePrompt;
			return;
		}
		if (this._step == 0) this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._await != null) return;
		var p = new XY(x, y);
		switch (this._step)
		{
			case 0:
				switch (this.Ctx.PickAt(x, y))
				{
					case Circle c: // 호(Arc)도 Circle
						this._circle = c;
						break;
					default:
						this.Ctx.Print("호 또는 원을 선택하십시오.");
						return;
				}
				this._step = 1;
				this.Prompt = "중심 위치 재정의 지정:";
				break;
			case 1:
				this._override = p;
				this._step = 2;
				this.Ctx.Print($"치수 텍스트 = {this.Build(p, p)?.GetMeasurementText()}");
				this.Prompt = LinePrompt;
				break;
			case 2:
				this._linePt = p;
				this._step = 3;
				this.Prompt = "꺾임 위치 지정:";
				break;
			case 3:
				{
					var dim = this.Build(this._linePt, p);
					if (dim != null) this.Ctx.Doc.AddEntities(dim);
					this.Finish();
					break;
				}
		}
	}

	/// <summary>치수선 위치(문자 위치)와 꺾임 위치로 치수를 만든다.</summary>
	private DimensionLargeRadial? Build(XY linePick, XY jogPick)
	{
		var c = this._circle!;
		var center = Geom.P(c.Center);
		double r = c.Radius;
		double ang = Math.Atan2(linePick.Y - center.Y, linePick.X - center.X);
		if (c is Arc arc && Geom.Norm(ang - arc.StartAngle) > Geom.Norm(arc.EndAngle - arc.StartAngle))
		{
			// 호 밖 방향이면 가까운 끝점 쪽
			static double D(double a, double b) => Math.Min(Geom.Norm(a - b), Geom.Norm(b - a));
			ang = D(ang, arc.StartAngle) < D(ang, arc.EndAngle) ? arc.StartAngle : arc.EndAngle;
		}
		double ux = Math.Cos(ang), uy = Math.Sin(ang);
		var chord = new XY(center.X + r * ux, center.Y + r * uy);
		var style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document);
		double asz = (style.ArrowSize > 0 ? style.ArrowSize : 2.5) * (style.ScaleFactor > 0 ? style.ScaleFactor : 1);

		// 꺾임 위치: 치수선(중심-원 위 점) 위로 투영, 원 안쪽으로 제한
		double t = (jogPick.X - center.X) * ux + (jogPick.Y - center.Y) * uy;
		t = Math.Clamp(t, 0, Math.Max(0, r - asz * 2));
		var jog = new XY(center.X + ux * t, center.Y + uy * t);
		double along = (linePick.X - center.X) * ux + (linePick.Y - center.Y) * uy;
		var textPt = along > r ? new XY(center.X + ux * along, center.Y + uy * along) : chord;

		var dim = new DimensionLargeRadial
		{
			DefinitionPoint = c.Center,
			ChordPoint = Num.Z(chord.X, chord.Y),
			OverrideCenter = Num.Z(this._override.X, this._override.Y),
			JogPoint = Num.Z(jog.X, jog.Y),
			TextMiddlePoint = Num.Z(textPt.X, textPt.Y),
			IsTextUserDefinedLocation = along > r,
			Style = style,
		};
		if (this._textAngle is double ta) dim.TextRotation = ta;
		if (!string.IsNullOrEmpty(this._text)) dim.Text = this._text;
		try
		{
			dim.UpdateBlock();
		}
		catch (Exception)
		{
			return null;
		}
		return dim;
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._circle == null || this.Ctx == null || this._await != null) yield break;
		var cur = new XY(cursorX, cursorY);
		if (this._step == 1)
		{
			yield return new Line(this._circle.Center, Num.Z(cursorX, cursorY));
		}
		else if (this._step == 2)
		{
			// 꺾임은 치수선 중간쯤으로 가정
			var center = Geom.P(this._circle.Center);
			var mid = new XY((center.X + cursorX) / 2, (center.Y + cursorY) / 2);
			if (this.Build(cur, mid) is { } d) yield return d;
		}
		else if (this._step == 3 && this.Build(this._linePt, cur) is { } d)
		{
			yield return d;
		}
	}
}
