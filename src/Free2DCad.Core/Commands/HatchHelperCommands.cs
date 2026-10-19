using CSMath;
using ACadSharp.Entities;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 해치 대화상자 '미리보기': 해치를 잠깐 도면에 넣어 보여 주고, Enter·Esc·클릭 중 하나면 되돌린 뒤 Done 호출.
/// 실행 취소 기록은 남지 않는다 (트랜잭션을 되돌림).
/// </summary>
public sealed class HatchPreviewCommand : EditorCommand
{
	private readonly IReadOnlyList<Hatch> _hatches;

	public HatchPreviewCommand(IReadOnlyList<Hatch> hatches) => this._hatches = hatches;

	public override string Name => "HATCHPREVIEW";

	/// <summary>미리보기를 끝내고 대화상자로 돌아갈 때.</summary>
	public event Action? Done;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		ctx.Doc.AddEntities(this._hatches);
		this.Prompt = "Enter 키 또는 오른쪽 클릭으로 대화상자로 돌아갑니다:";
	}

	public override void OnPoint(double x, double y) => this.End();

	public override void OnEnter() => this.End();

	public override void OnText(string text) => this.End();

	public override void OnCancel()
	{
		bool wasFinished = this.IsFinished;
		base.OnCancel();
		if (!wasFinished)
		{
			this.Done?.Invoke();
		}
	}

	private void End()
	{
		// 미리보기로 넣은 해치는 되돌린다
		this.OnCancel();
	}
}

/// <summary>
/// 도면에서 객체 하나를 고른다 (해치 '속성 상속', 선택 보기 등). Picked(null)은 취소.
/// </summary>
public sealed class PickEntityCommand : EditorCommand
{
	private readonly Func<Entity, bool> _filter;
	private SceneIndex? _index;

	public PickEntityCommand(string prompt, Func<Entity, bool>? filter = null)
	{
		this.Prompt = prompt;
		this._filter = filter ?? (_ => true);
	}

	public override string Name => "PICKENTITY";

	/// <summary>객체 선택 허용 거리(도면 단위).</summary>
	public double PickTolerance { get; set; } = 5.0;

	public event Action<Entity?>? Picked;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._index = new SceneIndex(SceneBuilder.Build(ctx.Doc.Document));
	}

	public override void OnPoint(double x, double y)
	{
		var e = this._index?.Pick(x, y, this.PickTolerance);
		if (e == null || !this._filter(e))
		{
			this.Ctx?.Print("객체를 찾을 수 없습니다.");
			return;
		}
		this.Finish();
		this.Picked?.Invoke(e);
	}

	public override void OnEnter()
	{
		this.OnCancel();
	}

	public override void OnCancel()
	{
		bool wasFinished = this.IsFinished;
		base.OnCancel();
		if (!wasFinished)
		{
			this.Picked?.Invoke(null);
		}
	}
}

/// <summary>대화상자에서 부르는 점 하나 고르기 (블록 기준점 등). Picked(null) = 취소.</summary>
public sealed class PickPointCommand : EditorCommand
{
	public PickPointCommand(string prompt) => this.Prompt = prompt;

	public override string Name => "PICKPOINT";

	public event Action<XYZ?>? Picked;

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx != null) this.Ctx.LastPoint = (x, y);
		this.Finish();
		this.Picked?.Invoke(new XYZ(x, y, 0));
	}

	public override void OnText(string text)
	{
		if (this.Ctx != null && CoordinateParser.TryParse(text.Trim(), this.Ctx.LastPoint, null, false, out (double X, double Y) p))
		{
			this.OnPoint(p.X, p.Y);
		}
	}

	public override void OnEnter() => this.OnCancel();

	public override void OnCancel()
	{
		bool wasFinished = this.IsFinished;
		base.OnCancel();
		if (!wasFinished) this.Picked?.Invoke(null);
	}
}

/// <summary>대화상자에서 부르는 객체 선택 (블록 정의의 객체 선택 등). Enter로 끝나면 Selected.</summary>
public sealed class SelectEntitiesCommand : EntityModificationCommand
{
	private readonly string _prompt;

	public SelectEntitiesCommand(string prompt) => this._prompt = prompt;

	public override string Name => "SELECTENTITIES";

	protected override string SelectPrompt => this._prompt;

	public event Action<IReadOnlyList<Entity>>? Selected;

	protected override void OnSelectionReady()
	{
		this.Finish();
		this.Selected?.Invoke(this.Targets.ToList());
	}

	protected override void OnNothingSelected() => this.Selected?.Invoke(Array.Empty<Entity>());
}
