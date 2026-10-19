using System.Globalization;
using ACadSharp.Entities;
using CSMath;
using Free2DCad.Core.Geometry;

namespace Free2DCad.Core.Commands;

/// <summary>ERASE: "삭제할 객체 선택:" (기존 CAD와 같은 문구).</summary>
public sealed class EraseCommand : EntityModificationCommand
{
	public override string Name => "ERASE";
	public override string[] Aliases => new[] { "E" };

	protected override string SelectPrompt => "삭제할 객체 선택:";

	protected override void OnSelectionReady()
	{
		if (this.Ctx != null && this.Targets.Count > 0)
		{
			this.Ctx.Doc.Erase(this.Targets);
		}
		this.Finish();
	}
}

/// <summary>이동·복사 공통: 기준점 → 두 번째 점 미리보기.</summary>
public abstract class DisplaceCommandBase : EntityModificationCommand
{
	protected (double X, double Y) BasePt;

	/// <summary>미리보기를 그릴 단계인지 (기준점을 정한 뒤).</summary>
	protected abstract bool PreviewActive { get; }

	protected static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

	protected (double X, double Y) Ortho(double x, double y) =>
		this.Ctx != null && this.Ctx.Ortho ? CoordinateParser.ApplyOrtho(this.BasePt, (x, y)) : (x, y);

	protected virtual IEnumerable<(double Dx, double Dy)> PreviewOffsets(double dx, double dy)
	{
		yield return (dx, dy);
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (!this.PreviewActive || this.Ctx == null) yield break;
		var pt = this.Ortho(cursorX, cursorY);
		foreach (var (dx, dy) in this.PreviewOffsets(pt.X - this.BasePt.X, pt.Y - this.BasePt.Y))
		{
			var transform = Transform.CreateTranslation(new XYZ(dx, dy, 0));
			foreach (var e in this.Targets)
			{
				var clone = (Entity)e.Clone();
				clone.ApplyTransform(transform);
				yield return clone;
			}
		}
	}
}

/// <summary>
/// MOVE (기존 CAD와 같은 문구): "Move 객체선택 :" → "기준점 또는 [거리(D)] &lt;거리(D)&gt;:" →
/// "두번째 지점 또는 &lt;첫번째 지점을 방향으로 사용&gt;:". 거리(D)·Enter: "방향을 지정하세요 &lt;x,y,z&gt;:".
/// </summary>
public sealed class MoveCommand : DisplaceCommandBase
{
	private static (double X, double Y) _lastDisplacement;
	private int _step;

	public override string Name => "MOVE";
	public override string[] Aliases => new[] { "M" };

	protected override string SelectPrompt => "Move 객체선택 :";

	protected override bool PreviewActive => this._step == 2;

	protected override void OnSelectionReady()
	{
		this._step = 1;
		this.Prompt = "기준점 또는 [거리(D)] <거리(D)>:";
	}

	private void AskDisplacement()
	{
		this._step = 3;
		this.Prompt = $"방향을 지정하세요 <{_lastDisplacement.X:0.0000},{_lastDisplacement.Y:0.0000},0.0000>:";
	}

	private void Apply(double dx, double dy)
	{
		_lastDisplacement = (dx, dy);
		this.Ctx!.Doc.Move(this.Targets, dx, dy);
		this.Finish();
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 1:
				this.BasePt = (x, y);
				this.Ctx.LastPoint = (x, y);
				this._step = 2;
				this.Prompt = "두번째 지점 또는 <첫번째 지점을 방향으로 사용>:";
				break;
			case 2:
				var pt = this.Ortho(x, y);
				this.Apply(pt.X - this.BasePt.X, pt.Y - this.BasePt.Y);
				break;
			case 3:
				this.Apply(x, y);
				break;
		}
	}

	public override void OnText(string text)
	{
		if (this._step == 1 && text.Trim().Equals("D", StringComparison.OrdinalIgnoreCase))
		{
			this.AskDisplacement();
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 1: this.AskDisplacement(); return;
			case 2: this.Apply(this.BasePt.X, this.BasePt.Y); return; // 첫 점을 변위로
			case 3: this.Apply(_lastDisplacement.X, _lastDisplacement.Y); return;
		}
		base.OnEnter();
	}
}

/// <summary>
/// COPY (기존 CAD와 같은 문구): "COPY 객체선택:" → "현재 설정: 복사 모드 = 다중" →
/// "COPY 기준점 지정 or [거리(D)/모드(O)] &lt;거리&gt;:" → "두번째 지점 또는 [배열(A)] &lt;첫번째 지점을 방향으로 사용&gt;:" →
/// "두번째 지점 또는 [배열(A)/종료(E)/취소(U)] &lt;종료(E)&gt;:". 배열(A): "배열할 항목의 수를 입력하세요:" → "두번째 지점 또는 [맞춤(F)]:".
/// </summary>
public sealed class CopyCommand : DisplaceCommandBase
{
	private static bool _single;
	private static (double X, double Y) _lastDisplacement;

	private int _step;
	private int _arrayCount;
	private bool _fit;
	private readonly List<IReadOnlyList<Entity>> _made = new();

	public override string Name => "COPY";
	public override string[] Aliases => new[] { "CO", "CP" };

	protected override string SelectPrompt => "COPY 객체선택:";

	protected override bool PreviewActive => this._step is 2 or 6;

	protected override void OnSelectionReady()
	{
		this.Ctx?.Print($"현재 설정: 복사 모드 = {(_single ? "단일" : "다중")}");
		this.AskBase();
	}

	private void AskBase()
	{
		this._step = 1;
		this.Prompt = "COPY 기준점 지정 or [거리(D)/모드(O)] <거리>:";
	}

	private void AskSecond()
	{
		this._step = 2;
		this.Prompt = this._made.Count == 0
			? "두번째 지점 또는 [배열(A)] <첫번째 지점을 방향으로 사용>:"
			: "두번째 지점 또는 [배열(A)/종료(E)/취소(U)] <종료(E)>:";
	}

	private void AskDisplacement()
	{
		this._step = 4;
		this.Prompt = $"방향을 지정하세요 <{_lastDisplacement.X:0.0000},{_lastDisplacement.Y:0.0000},0.0000>:";
	}

	protected override IEnumerable<(double Dx, double Dy)> PreviewOffsets(double dx, double dy)
	{
		if (this._step != 6)
		{
			yield return (dx, dy);
			yield break;
		}
		for (int k = 1; k < this._arrayCount; k++)
		{
			double f = this._fit ? (double)k / (this._arrayCount - 1) : k;
			yield return (dx * f, dy * f);
		}
	}

	private void CopyBy(double dx, double dy)
	{
		_lastDisplacement = (dx, dy);
		this._made.Add(this.Ctx!.Doc.Copy(this.Targets, dx, dy));
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 1:
				this.BasePt = (x, y);
				this.Ctx.LastPoint = (x, y);
				this.AskSecond();
				break;
			case 2:
				{
					var pt = this.Ortho(x, y);
					this.CopyBy(pt.X - this.BasePt.X, pt.Y - this.BasePt.Y);
					if (_single)
					{
						this.Finish();
						return;
					}
					this.AskSecond();
					break;
				}
			case 4:
				this.CopyBy(x, y);
				this.Finish();
				break;
			case 6:
				{
					var pt = this.Ortho(x, y);
					double dx = pt.X - this.BasePt.X, dy = pt.Y - this.BasePt.Y;
					var all = new List<Entity>();
					foreach (var (ox, oy) in this.PreviewOffsets(dx, dy))
					{
						all.AddRange(this.Ctx.Doc.Copy(this.Targets, ox, oy));
					}
					this._made.Add(all);
					_lastDisplacement = (dx, dy);
					this.AskSecond();
					break;
				}
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		switch (this._step)
		{
			case 1 when t == "D":
				this.AskDisplacement();
				return;
			case 1 when t == "O":
				this._step = 3;
				this.Prompt = $"복사 모드 옵션을 입력하세요 [단일(S)/다중(M)] <{(_single ? "단일" : "다중")}>:";
				return;
			case 3:
				if (t == "S") _single = true;
				else if (t == "M") _single = false;
				this.AskBase();
				return;
			case 2 when t == "A":
				this._step = 5;
				this.Prompt = "배열할 항목의 수를 입력하세요:";
				return;
			case 2 when t == "E":
				this.Finish();
				return;
			case 2 when t == "U" && this._made.Count > 0:
				this.Ctx.Doc.Erase(this._made[^1]);
				this._made.RemoveAt(this._made.Count - 1);
				this.AskSecond();
				return;
			case 5:
				if (int.TryParse(t, out int n) && n >= 2)
				{
					this._arrayCount = n;
					this._fit = false;
					this._step = 6;
					this.Prompt = "두번째 지점 또는 [맞춤(F)]:";
				}
				return;
			case 6 when t == "F":
				this._fit = true;
				this.Prompt = "두번째 지점:";
				return;
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 1:
				this.AskDisplacement();
				return;
			case 2:
				if (this._made.Count == 0)
				{
					this.CopyBy(this.BasePt.X, this.BasePt.Y);
				}
				this.Finish();
				return;
			case 3:
				this.AskBase();
				return;
			case 4:
				this.CopyBy(_lastDisplacement.X, _lastDisplacement.Y);
				this.Finish();
				return;
			case 5:
			case 6:
				this.AskSecond();
				return;
		}
		base.OnEnter();
	}
}

/// <summary>
/// ROTATE (기존 CAD와 같은 문구): "&lt;객체선택&gt;:" → "회전 점:" →
/// "회전 각도를 지정하거나 [복사(C)/기준각도(B)] &lt;0&gt;:". 기준각도(B): "기준각도 &lt;0&gt;:" → "[점(P)]/&lt;새 각도 지정&gt;:".
/// </summary>
public sealed class RotateCommand : EntityModificationCommand
{
	private static double _lastAngleDeg;
	private static double _lastRefDeg;

	private int _step;
	private bool _copy;
	private double _refDeg;
	private (double X, double Y) _basePt;
	private (double X, double Y) _p1;

	public override string Name => "ROTATE";
	public override string[] Aliases => new[] { "RO" };

	protected override string SelectPrompt => "<객체선택>:";

	public override void Start(CommandContext ctx)
	{
		ctx.Print("Current positive angle in UCS: ANGDIR=counterclockwise  ANGBASE=0");
		base.Start(ctx);
	}

	protected override void OnSelectionReady()
	{
		this._step = 1;
		this.Prompt = "회전 점:";
	}

	private void AskAngle()
	{
		this._step = 2;
		this.Prompt = $"회전 각도를 지정하거나 [복사(C)/기준각도(B)] <{_lastAngleDeg:0.####}>:";
	}

	private void Apply(double deg)
	{
		_lastAngleDeg = deg;
		var targets = this.Targets;
		if (this._copy)
		{
			targets = this.Ctx!.Doc.Copy(this.Targets, 0, 0).ToList();
		}
		this.Ctx!.Doc.Rotate(targets, this._basePt.X, this._basePt.Y, deg * Math.PI / 180);
		this.Finish();
	}

	private double AngleTo(double x, double y) => Math.Atan2(y - this._basePt.Y, x - this._basePt.X) * 180 / Math.PI;

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 1:
				this._basePt = (x, y);
				this.Ctx.LastPoint = (x, y);
				this.AskAngle();
				break;
			case 2:
				this.Apply(this.AngleTo(x, y));
				break;
			case 3:
				this.OnText(this.AngleTo(x, y).ToString(CultureInfo.InvariantCulture));
				break;
			case 4:
				this.Apply(this.AngleTo(x, y) - this._refDeg);
				break;
			case 5:
				this._p1 = (x, y);
				this._step = 6;
				this.Prompt = "두 번째 점:";
				break;
			case 6:
				this.Apply(Math.Atan2(y - this._p1.Y, x - this._p1.X) * 180 / Math.PI - this._refDeg);
				break;
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
		switch (this._step)
		{
			case 2 when num:
				this.Apply(v);
				return;
			case 2 when t == "C":
				this._copy = true;
				this.Ctx.Print("선택된 객체의 복사본을 회전합니다.");
				this.AskAngle();
				return;
			case 2 when t == "B":
				this._step = 3;
				this.Prompt = $"기준각도 <{_lastRefDeg:0.####}>:";
				return;
			case 3 when num:
				this._refDeg = _lastRefDeg = v;
				this._step = 4;
				this.Prompt = "[점(P)]/<새 각도 지정>:";
				return;
			case 4 when num:
				this.Apply(v - this._refDeg);
				return;
			case 4 when t == "P":
				this._step = 5;
				this.Prompt = "첫 번째 점:";
				return;
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 2:
				this.Apply(_lastAngleDeg);
				return;
			case 3:
				this.OnText(_lastRefDeg.ToString(CultureInfo.InvariantCulture));
				return;
		}
		base.OnEnter();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 2 || this.Ctx == null) yield break;
		double a = this.AngleTo(cursorX, cursorY) * Math.PI / 180;
		var transform = Transform.CreateRotation(new XYZ(0, 0, 1), a);
		foreach (var e in this.Targets)
		{
			var clone = (Entity)e.Clone();
			clone.ApplyTransform(Transform.CreateTranslation(new XYZ(-this._basePt.X, -this._basePt.Y, 0)));
			clone.ApplyTransform(transform);
			clone.ApplyTransform(Transform.CreateTranslation(new XYZ(this._basePt.X, this._basePt.Y, 0)));
			yield return clone;
		}
	}
}

/// <summary>
/// SCALE (기존 CAD와 같은 문구): "SCALE 을 조정할 객체 선택:" → "기준점:" →
/// "축척 비율 [기준점(B)/복사(C)/참조(R)]/&lt;1.0000&gt;:". 참조(R): "기준 축척&lt;1.0000&gt;:" → "[점(P)]/&lt;새 길이 지정 (1.0000)&gt;:".
/// </summary>
public sealed class ScaleCommand : EntityModificationCommand
{
	private static double _lastFactor = 1;
	private static double _lastRef = 1;

	private int _step;
	private bool _copy;
	private double _ref;
	private (double X, double Y) _basePt;
	private (double X, double Y) _p1;

	public override string Name => "SCALE";
	public override string[] Aliases => new[] { "SC" };

	protected override string SelectPrompt => "SCALE 을 조정할 객체 선택:";

	protected override void OnSelectionReady()
	{
		this._step = 1;
		this.Prompt = "기준점:";
	}

	private void AskFactor()
	{
		this._step = 2;
		this.Prompt = $"축척 비율 [기준점(B)/복사(C)/참조(R)]/<{_lastFactor:0.0000}>:";
	}

	private void Apply(double factor)
	{
		if (Math.Abs(factor) < 1e-12) return;
		_lastFactor = factor;
		var targets = this.Targets;
		if (this._copy)
		{
			targets = this.Ctx!.Doc.Copy(this.Targets, 0, 0).ToList();
		}
		this.Ctx!.Doc.Scale(targets, this._basePt.X, this._basePt.Y, factor);
		this.Finish();
	}

	private double DistTo(double x, double y) => Math.Sqrt((x - this._basePt.X) * (x - this._basePt.X) + (y - this._basePt.Y) * (y - this._basePt.Y));

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		switch (this._step)
		{
			case 1:
				this._basePt = (x, y);
				this.Ctx.LastPoint = (x, y);
				this.AskFactor();
				break;
			case 2:
				this.Apply(this.DistTo(x, y));
				break;
			case 3:
				this.OnText(this.DistTo(x, y).ToString(CultureInfo.InvariantCulture));
				break;
			case 4:
				this.Apply(this.DistTo(x, y) / this._ref);
				break;
			case 5:
				this._p1 = (x, y);
				this._step = 6;
				this.Prompt = "두 번째 점:";
				break;
			case 6:
				this.Apply(Math.Sqrt((x - this._p1.X) * (x - this._p1.X) + (y - this._p1.Y) * (y - this._p1.Y)) / this._ref);
				break;
		}
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
		switch (this._step)
		{
			case 2 when num && v > 0:
				this.Apply(v);
				return;
			case 2 when t == "B":
				this._step = 1;
				this.Prompt = "기준점:";
				return;
			case 2 when t == "C":
				this._copy = true;
				this.Ctx.Print("선택된 객체의 복사본의 축척을 조정합니다.");
				this.AskFactor();
				return;
			case 2 when t == "R":
				this._step = 3;
				this.Prompt = $"기준 축척<{_lastRef:0.0000}>:";
				return;
			case 3 when num && v > 0:
				this._ref = _lastRef = v;
				this._step = 4;
				this.Prompt = $"[점(P)]/<새 길이 지정 ({v:0.0000})>:";
				return;
			case 4 when num && v > 0:
				this.Apply(v / this._ref);
				return;
			case 4 when t == "P":
				this._step = 5;
				this.Prompt = "첫 번째 점:";
				return;
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 2:
				this.Apply(_lastFactor);
				return;
			case 3:
				this.OnText(_lastRef.ToString(CultureInfo.InvariantCulture));
				return;
			case 4:
				this.Apply(1);
				return;
		}
		base.OnEnter();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 2 || this.Ctx == null) yield break;
		double f = this.DistTo(cursorX, cursorY);
		if (f < 1e-9) yield break;
		var transform = Transform.CreateScaling(new XYZ(f, f, 1), new XYZ(this._basePt.X, this._basePt.Y, 0));
		foreach (var e in this.Targets)
		{
			var clone = (Entity)e.Clone();
			clone.ApplyTransform(transform);
			yield return clone;
		}
	}
}

/// <summary>
/// MIRROR (기존 CAD와 같은 문구): "Mirror 객체선택:" → "대칭선 시작:" → "대칭선 끝:" →
/// "원래 객체를 삭제합니까? [예(Y)/아니오(N)] &lt;N&gt;".
/// </summary>
public sealed class MirrorCommand : EntityModificationCommand
{
	private int _step;
	private (double X, double Y) _p1;
	private (double X, double Y) _p2;

	public override string Name => "MIRROR";
	public override string[] Aliases => new[] { "MI" };

	protected override string SelectPrompt => "Mirror 객체선택:";

	protected override void OnSelectionReady()
	{
		this._step = 1;
		this.Prompt = "대칭선 시작:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		if (this._step == 1)
		{
			this._p1 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 2;
			this.Prompt = "대칭선 끝:";
		}
		else if (this._step == 2)
		{
			if (this.Ctx.Ortho)
			{
				(x, y) = CoordinateParser.ApplyOrtho(this._p1, (x, y));
			}
			if (Math.Abs(x - this._p1.X) < 1e-9 && Math.Abs(y - this._p1.Y) < 1e-9)
			{
				this.Ctx.Print("대칭선의 두 점은 서로 달라야 합니다. 다른 점을 지정하십시오.");
				return;
			}
			this._p2 = (x, y);
			this.Ctx.LastPoint = (x, y);
			this._step = 3;
			this.Prompt = "원래 객체를 삭제합니까? [예(Y)/아니오(N)] <N>";
		}
	}

	private void Apply(bool deleteOld)
	{
		this.Ctx!.Doc.Mirror(this.Targets, this._p1.X, this._p1.Y, this._p2.X, this._p2.Y, keepOriginal: !deleteOld);
		this.Finish();
	}

	public override void OnText(string text)
	{
		if (this._step != 3 || this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		if (t is "Y" or "N")
		{
			this.Apply(t == "Y");
		}
	}

	public override void OnEnter()
	{
		if (this._step == 3 && this.Ctx != null)
		{
			this.Apply(false);
			return;
		}
		base.OnEnter();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 2 || this.Ctx == null) yield break;
		var (x, y) = this.Ctx.Ortho ? CoordinateParser.ApplyOrtho(this._p1, (cursorX, cursorY)) : (cursorX, cursorY);
		if (Math.Abs(x - this._p1.X) < 1e-9 && Math.Abs(y - this._p1.Y) < 1e-9) yield break;
		foreach (var e in this.Targets)
		{
			var clone = (Entity)e.Clone();
			clone.ApplyTransform(MirrorTransform(this._p1, (x, y)));
			yield return clone;
		}
	}

	private static Transform MirrorTransform((double X, double Y) a, (double X, double Y) b)
	{
		// EditorDocument.Mirror와 같은 행렬: 이동 → 회전 → Y 뒤집기 → 되돌림
		double theta = Math.Atan2(b.Y - a.Y, b.X - a.X);
		Matrix4 m = Matrix4.CreateTranslation(a.X, a.Y, 0) * Matrix4.CreateFromAxisAngle(XYZ.AxisZ, theta)
			* Matrix4.CreateScalingMatrix(1, -1, 1) * Matrix4.CreateFromAxisAngle(XYZ.AxisZ, -theta) * Matrix4.CreateTranslation(-a.X, -a.Y, 0);
		return new Transform(m);
	}
}

/// <summary>
/// OFFSET (기존 CAD와 같은 문구·옵션): "현재 설정: 원본 지우기=아니오(N) 도면층=원본(S) OFFSETGAPTYPE=0" →
/// "간격띄우기 거리 지정 또는 [통과점(T)/지우기(E)/도면층(L)] &lt;거리&gt;:" → "객체선택 [Undo]:" →
/// "평행 복사를 위한 측면 [양쪽(B)]:" (통과점이면 "통과점 지정:") → 다시 "객체선택 [Undo]:".
/// </summary>
public sealed class OffsetCommand : EditorCommand
{
	private static double _lastDistance = -1; // 음수 = 통과점
	private static bool _eraseSource;
	private static bool _currentLayer;

	private int _step; // 0 거리, 1 지우기 옵션, 2 도면층 옵션, 3 객체 고르기, 4 측면
	private bool _through;
	private double _distance;
	private Entity? _source;
	private readonly List<(List<Entity> Made, Entity? Erased)> _history = new();

	public override string Name => "OFFSET";
	public override string[] Aliases => new[] { "O" };

	public override bool PickingObject => !this.IsFinished && this._step == 3;

	public override IEnumerable<Entity> Highlighted => this._step == 4 && this._source != null ? new[] { this._source } : Array.Empty<Entity>();

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		ctx.Print($"현재 설정: 원본 지우기={(_eraseSource ? "예(Y)" : "아니오(N)")} 도면층={(_currentLayer ? "현재(C)" : "원본(S)")} OFFSETGAPTYPE=0");
		this.AskDistance();
	}

	private void AskDistance()
	{
		this._step = 0;
		string def = _lastDistance < 0 ? "통과점" : _lastDistance.ToString("0.0000", CultureInfo.InvariantCulture);
		this.Prompt = $"간격띄우기 거리 지정 또는 [통과점(T)/지우기(E)/도면층(L)] <{def}>:";
	}

	private void AskObject()
	{
		this._step = 3;
		this._source = null;
		this.Prompt = "객체선택 [Undo]:";
	}

	private void SetDistance(double d)
	{
		this._through = false;
		this._distance = _lastDistance = d;
		this.AskObject();
	}

	private void SetThrough()
	{
		this._through = true;
		_lastDistance = -1;
		this.AskObject();
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string t = text.Trim().ToUpperInvariant();
		bool num = double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
		switch (this._step)
		{
			case 0 when num && v > 0:
				this.SetDistance(v);
				return;
			case 0 when t == "T":
				this.SetThrough();
				return;
			case 0 when t == "E":
				this._step = 1;
				this.Prompt = $"간격띄우기 후 원본 객체를 지우시겠습니까? [예(Y)/아니오(N)] <{(_eraseSource ? "예(Y)" : "아니오(N)")}>:";
				return;
			case 0 when t == "L":
				this._step = 2;
				this.Prompt = $"간격띄우기 객체의 도면층 옵션을 입력하세요 [현재(C)/원본(S)] <{(_currentLayer ? "현재(C)" : "원본(S)")}>:";
				return;
			case 1:
				if (t == "Y") _eraseSource = true;
				else if (t == "N") _eraseSource = false;
				this.AskDistance();
				return;
			case 2:
				if (t == "C") _currentLayer = true;
				else if (t == "S") _currentLayer = false;
				this.AskDistance();
				return;
			case 3 when t == "U":
				this.UndoLast();
				return;
			case 3 when t == "E":
				this.Finish();
				return;
			case 4 when t == "B" && !this._through && this._source != null:
				this.MakeBothSides();
				return;
		}
	}

	private void UndoLast()
	{
		if (this._history.Count == 0)
		{
			this.Ctx!.Print("취소할 것이 없습니다.");
			return;
		}
		var (made, erased) = this._history[^1];
		this._history.RemoveAt(this._history.Count - 1);
		this.Ctx!.Doc.Erase(made);
		if (erased != null)
		{
			this.Ctx.Doc.AddEntities(erased);
		}
	}

	private Entity? Make(Entity src, double dist, XY side)
	{
		var e = OffsetGeometry.Offset(src, dist, side);
		if (e != null && _currentLayer)
		{
			e.Layer = this.Ctx!.Doc.Document.Layers.FirstOrDefault(l => l.Name == this.Ctx.Doc.Document.Header.CurrentLayerName) ?? e.Layer;
		}
		return e;
	}

	private void Commit(List<Entity> made)
	{
		var src = this._source!;
		if (made.Count == 0)
		{
			this.Ctx!.Print("객체를 간격띄우기할 수 없습니다.");
			this.AskObject();
			return;
		}
		this.Ctx!.Doc.AddEntities(made);
		Entity? erased = null;
		if (_eraseSource)
		{
			this.Ctx.Doc.Erase(new[] { src });
			erased = src;
		}
		this._history.Add((made, erased));
		this.AskObject();
	}

	private void MakeBothSides()
	{
		var src = this._source!;
		var made = new List<Entity>();
		// 양쪽: 객체 위 한 점에서 법선 양 방향으로
		var segs = Geom.SegmentsOf(src);
		if (segs.Count > 0)
		{
			var s = segs[0];
			var p = s.At(0.5);
			var q = s.At(0.5001);
			double dx = q.X - p.X, dy = q.Y - p.Y, len = Math.Sqrt(dx * dx + dy * dy);
			if (len > 1e-12)
			{
				double nx = -dy / len, ny = dx / len, eps = Math.Max(this._distance, 1) * 1e-4;
				foreach (int sign in new[] { 1, -1 })
				{
					var e = this.Make(src, this._distance, new XY(p.X + nx * eps * sign, p.Y + ny * eps * sign));
					if (e != null) made.Add(e);
				}
			}
		}
		this.Commit(made);
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;
		var p = new XY(x, y);
		switch (this._step)
		{
			case 0:
				if (this.Ctx.LastPoint is { } lp)
				{
					double d = Math.Sqrt((x - lp.X) * (x - lp.X) + (y - lp.Y) * (y - lp.Y));
					if (d > 1e-9) this.SetDistance(d);
				}
				return;
			case 3:
				{
					var picked = this.Ctx.PickAt(x, y);
					if (picked is Line or Arc or Circle or LwPolyline)
					{
						this._source = picked;
						this._step = 4;
						this.Prompt = this._through ? "통과점 지정:" : "평행 복사를 위한 측면 [양쪽(B)]:";
					}
					else if (picked != null)
					{
						this.Ctx.Print("객체를 간격띄우기할 수 없습니다.");
					}
					return;
				}
			case 4:
				{
					var src = this._source!;
					double dist = this._through ? OffsetGeometry.DistanceTo(src, p) : this._distance;
					var made = new List<Entity>();
					if (dist > 1e-9 && this.Make(src, dist, p) is { } e)
					{
						made.Add(e);
					}
					this.Commit(made);
					return;
				}
		}
	}

	public override void OnEnter()
	{
		switch (this._step)
		{
			case 0:
				if (_lastDistance < 0) this.SetThrough();
				else this.SetDistance(_lastDistance);
				return;
			case 1:
			case 2:
				this.AskDistance();
				return;
			case 4:
				this.AskObject();
				return;
		}
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step != 4 || this._source == null) yield break;
		var p = new XY(cursorX, cursorY);
		double dist = this._through ? OffsetGeometry.DistanceTo(this._source, p) : this._distance;
		if (dist > 1e-9 && OffsetGeometry.Offset(this._source, dist, p) is { } e)
		{
			yield return e;
		}
	}
}
