using ACadSharp.Entities;
using CSMath;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 엔티티들에 정방향/역방향 기하 변환(Transform)을 적용하는 액션.
/// </summary>
public sealed class TransformEntitiesAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._entities;

	private readonly List<Entity> _entities;
	private readonly Transform _forward;
	private readonly Transform _backward;

	public string Name { get; }

	public IReadOnlyList<Entity> Entities => this._entities;

	public Transform ForwardTransform => this._forward;

	public Transform BackwardTransform => this._backward;

	public TransformEntitiesAction(IEnumerable<Entity> entities, Transform forward, Transform backward, string name = "변환")
	{
		this._entities = entities.ToList();
		this._forward = forward;
		this._backward = backward;
		this.Name = name;
	}

	public void Do()
	{
		for (int i = 0; i < this._entities.Count; i++)
		{
			this._entities[i].ApplyTransform(this._forward);
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._entities.Count; i++)
		{
			this._entities[i].ApplyTransform(this._backward);
		}
	}
}
