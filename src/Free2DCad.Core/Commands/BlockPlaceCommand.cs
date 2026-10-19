using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Blocks;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 블록 삽입 (INSERT 대화상자 다음, 기존 CAD와 같은 프롬프트): "블록의 삽입점:" → "[구석(C)/XYZ]/X 축척비율 &lt;1.000000&gt;:" →
/// "Y 축척 비율: &lt;X 축척과 같음 (1.000000)&gt;:" → "블록의 회전 각도 &lt;0.0&gt;:" → 속성 값. 대화상자에서 정한 값은 묻지 않는다.
/// 균일한 축척이면 Y를 묻지 않고, 분해면 블록 대신 내용을 넣는다.
/// </summary>
public sealed class BlockPlaceCommand : EditorCommand
{
	private readonly BlockRecord _block;
	private readonly bool _askPoint, _askScale, _askRotation, _uniform, _explode;
	private XYZ _pt;
	private double _sx, _sy, _sz, _rot;
	private int _step;
	private Insert? _pending;
	private int _attIndex;

	public BlockPlaceCommand(BlockRecord block, XYZ? point, XYZ? scale, double? rotationDeg, bool uniform, bool explode)
	{
		this._block = block;
		this._askPoint = point == null;
		this._pt = point ?? XYZ.Zero;
		this._askScale = scale == null;
		this._sx = scale?.X ?? 1;
		this._sy = scale?.Y ?? this._sx;
		this._sz = scale?.Z ?? this._sx;
		this._askRotation = rotationDeg == null;
		this._rot = (rotationDeg ?? 0) * Math.PI / 180;
		this._uniform = uniform;
		this._explode = explode;
	}

	public override string Name => "INSERT";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this.Next();
	}

	private void Next()
	{
		if (this._step == 0 && !this._askPoint) this._step = 1;
		if (this._step == 1 && !this._askScale) this._step = 3;
		if (this._step == 2 && this._uniform)
		{
			this._sy = this._sz = this._sx;
			this._step = 3;
		}
		if (this._step == 3 && !this._askRotation) this._step = 4;
		switch (this._step)
		{
			case 0: this.Prompt = "블록의 삽입점:"; break;
			case 1: this.Prompt = $"[구석(C)/XYZ]/X 축척비율 <{this._sx:0.000000}>:"; break;
			case 2: this.Prompt = $"Y 축척 비율: <X 축척과 같음 ({this._sx:0.000000})>:"; break;
			case 3: this.Prompt = $"블록의 회전 각도 <{this._rot * 180 / Math.PI:0.0}>:"; break;
			default: this.Place(); break;
		}
	}

	private void Place()
	{
		var insert = BlockDefinition.MakeInsert(this._block, this._pt, this._sx, this._sy, this._sz, this._rot);
		if (this._explode)
		{
			this.Ctx!.Doc.AddEntities(insert.Explode());
			this.Finish();
			return;
		}
		this._pending = insert;
		this._attIndex = -1;
		this.NextAttribute(null);
	}

	/// <summary>속성 값 묻기: 상수가 아닌 속성마다 "프롬프트 &lt;기본값&gt;:" (Enter = 기본값).</summary>
	private void NextAttribute(string? value)
	{
		var insert = this._pending!;
		var atts = insert.Attributes.ToList();
		var defs = this._block.AttributeDefinitions.ToList();
		if (this._attIndex >= 0 && value != null)
		{
			atts[this._attIndex].Value = value;
		}
		this._attIndex++;
		while (this._attIndex < atts.Count && this._attIndex < defs.Count && defs[this._attIndex].Flags.HasFlag(ACadSharp.Entities.AttributeFlags.Constant))
		{
			this._attIndex++;
		}
		if (this._attIndex < atts.Count && this._attIndex < defs.Count)
		{
			var def = defs[this._attIndex];
			this._step = 5;
			this.Prompt = $"{(string.IsNullOrEmpty(def.Prompt) ? def.Tag : def.Prompt)} <{def.Value}>:";
			return;
		}
		this.Ctx!.Doc.AddEntities(insert);
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 0:
				this._pt = Num.Z(x, y);
				this.Ctx.LastPoint = (x, y);
				this._step = 1;
				break;
			case 1:
			case 2:
				{
					double d = Math.Sqrt((x - this._pt.X) * (x - this._pt.X) + (y - this._pt.Y) * (y - this._pt.Y));
					if (d > 1e-9)
					{
						if (this._step == 1) this._sx = this._sy = this._sz = d;
						else this._sy = d;
					}
					this._step++;
					break;
				}
			case 3:
				this._rot = Math.Atan2(y - this._pt.Y, x - this._pt.X);
				this._step = 4;
				break;
			default:
				return;
		}
		this.Next();
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		if (this._step == 5)
		{
			this.NextAttribute(text);
			return;
		}
		string t = text.Trim();
		if (this._step == 0)
		{
			if (CoordinateParser.TryParse(t, this.Ctx.LastPoint, null, false, out (double X, double Y) p)) this.OnPoint(p.X, p.Y);
			return;
		}
		if (!Num.TryParse(t, out double v)) return;
		switch (this._step)
		{
			case 1: if (v != 0) this._sx = this._sy = this._sz = v; this._step = 2; break;
			case 2: if (v != 0) this._sy = v; this._step = 3; break;
			case 3: this._rot = v * Math.PI / 180; this._step = 4; break;
		}
		this.Next();
	}

	public override void OnEnter()
	{
		if (this._step == 5)
		{
			this.NextAttribute(null);
			return;
		}
		if (this._step == 0) return;
		this._step++; // 기본값
		this.Next();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this.IsFinished || this._step > 3) yield break;
		var at = this._step == 0 ? Num.Z(cursorX, cursorY) : this._pt;
		yield return BlockDefinition.MakeInsert(this._block, at, this._sx, this._sy, this._sz, this._rot);
	}
}
