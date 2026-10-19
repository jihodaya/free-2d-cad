using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Commands;

/// <summary>
/// TOLERANCE (기하 공차): 앱의 "기하학적 공차" 대화상자에서 만든 문자열로, "공차 위치 선택:"에서 고른 점에 공차 틀을 놓는다 (기존 CAD와 같음).
/// </summary>
public sealed class ToleranceCommand : EditorCommand
{
	private readonly string _text;

	public ToleranceCommand() : this(string.Empty)
	{
	}

	public ToleranceCommand(string text) => this._text = text;

	public override string Name => "TOLERANCE";

	public override string[] Aliases => new[] { "TOL" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		if (string.IsNullOrEmpty(this._text))
		{
			ctx.Print("기하학적 공차 대화상자에서 내용을 지정하십시오.");
			this.Finish();
			return;
		}
		this.Prompt = "공차 위치 선택:";
	}

	private Tolerance Make(double x, double y) => new()
	{
		Text = this._text,
		InsertionPoint = Num.Z(x, y),
		Direction = XYZ.AxisX,
		Style = DimensionStyleHelper.Current(this.Ctx!.Doc.Document),
	};

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		this.Ctx.Doc.AddEntities(this.Make(x, y));
		this.Finish();
	}

	public override void OnText(string text)
	{
		if (this.Ctx != null && CoordinateParser.TryParse(text.Trim(), this.Ctx.LastPoint, null, false, out (double X, double Y) p))
		{
			this.OnPoint(p.X, p.Y);
		}
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this.Ctx == null || this.IsFinished) yield break;
		yield return this.Make(cursorX, cursorY);
	}
}
