using ACadSharp.Entities;

namespace Free2DCad.Core.Commands;

/// <summary>
/// CAD 편집 명령 기본 추상 클래스.
/// </summary>
public abstract class EditorCommand
{
	protected CommandContext? Ctx { get; private set; }

	public abstract string Name { get; }

	public virtual string[] Aliases => Array.Empty<string>();

	public string Prompt { get; protected set; } = string.Empty;

	public bool IsFinished { get; protected set; }

	/// <summary>
	/// 지금 객체를 고르는 단계인지 ("객체 선택:"). true면 캔버스가 클릭·창(크로싱) 선택으로 고른 객체를 OnSelection으로 넘긴다
	/// (명령을 먼저 실행한 뒤 객체를 고르는 방식). false면 클릭은 점(OnPoint)으로 간다.
	/// </summary>
	public virtual bool WantsSelection => false;

	/// <summary>문자를 치는 단계인지 (true면 명령줄의 Space가 Enter가 아니라 공백 글자, 입력은 좌표로 바꾸지 않고 그대로 OnText).</summary>
	public virtual bool SpaceIsText => false;

	/// <summary>명령 안에서 이미 고른 객체 (화면에 점선으로 강조, 기존 CAD처럼).</summary>
	public virtual IEnumerable<Entity> Highlighted => Array.Empty<Entity>();

	/// <summary>명령 안에서 고른 영역 (도면 좌표 x0,y0,x1,y1,… 닫힌 고리, 예: 해치 내부 점으로 찾은 경계). Highlighted처럼 점선으로 보인다.</summary>
	public virtual IEnumerable<double[]> HighlightedLoops => Array.Empty<double[]>();

	/// <summary>지금 객체 하나를 클릭으로 고르는 단계인지 (마우스를 올린 객체를 미리 강조).</summary>
	public virtual bool PickingObject => this.WantsSelection;

	/// <summary>시작 직후 명령줄에 차례로 넣을 옵션 (리본의 "줌 윈도우"처럼 옵션이 정해진 버튼).</summary>
	public string[]? AutoInput { get; set; }

	public virtual void Start(CommandContext ctx)
	{
		this.Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
		this.Ctx.Doc.BeginTransaction();
	}

	public virtual void OnPoint(double x, double y)
	{
	}

	public virtual void OnText(string text)
	{
	}

	public virtual void OnEnter()
	{
	}

	public virtual void OnCancel()
	{
		if (!this.IsFinished)
		{
			this.Ctx?.Doc.RollbackTransaction();
			this.IsFinished = true;
		}
	}

	public virtual void OnSelection(IEnumerable<Entity> entities)
	{
	}

	public virtual IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		return Enumerable.Empty<Entity>();
	}

	protected void Finish()
	{
		if (!this.IsFinished)
		{
			this.Ctx?.Doc.CommitTransaction(this.Name);
			this.IsFinished = true;
		}
	}
}
