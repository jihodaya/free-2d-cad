namespace Free2DCad.Core.Actions;

/// <summary>
/// 실행 및 실행 취소(Undo)가 가능한 액션 인터페이스.
/// </summary>
public interface IUndoableAction
{
	/// <summary>
	/// 액션 이름.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// 액션을 실행한다.
	/// </summary>
	void Do();

	/// <summary>
	/// 액션을 되돌린다.
	/// </summary>
	void Undo();
}
