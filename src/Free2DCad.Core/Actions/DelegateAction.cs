namespace Free2DCad.Core.Actions;

/// <summary>
/// 값 바꾸기처럼 "적용/되돌리기" 두 동작으로 표현되는 변경 (문자 스타일, 치수 스타일, 도면 단위 설정 등).
/// 호출하는 쪽에서 이전 값을 캡처해 revert에 넣는다.
/// </summary>
public sealed class DelegateAction : IUndoableAction
{
	private readonly Action _apply;
	private readonly Action _revert;

	public DelegateAction(string name, Action apply, Action revert)
	{
		this.Name = name;
		this._apply = apply ?? throw new ArgumentNullException(nameof(apply));
		this._revert = revert ?? throw new ArgumentNullException(nameof(revert));
	}

	public string Name { get; }

	public void Do() => this._apply();

	public void Undo() => this._revert();
}
