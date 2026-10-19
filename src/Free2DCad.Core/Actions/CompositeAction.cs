namespace Free2DCad.Core.Actions;

/// <summary>
/// 여러 액션을 하나의 작업 단위로 묶는 복합 액션.
/// </summary>
public sealed class CompositeAction : IUndoableAction
{
	private readonly List<IUndoableAction> _actions;

	public string Name { get; }

	public IReadOnlyList<IUndoableAction> Actions => this._actions;

	public CompositeAction(string name, IEnumerable<IUndoableAction> actions)
	{
		this.Name = name;
		this._actions = actions.ToList();
	}

	public void Do()
	{
		for (int i = 0; i < this._actions.Count; i++)
		{
			this._actions[i].Do();
		}
	}

	public void Undo()
	{
		for (int i = this._actions.Count - 1; i >= 0; i--)
		{
			this._actions[i].Undo();
		}
	}
}
