using ACadSharp.Entities;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 엔티티 하나의 값 변경 (치수 문자, 그 밖의 속성). 화면 즉시 반영을 위해 바뀐 엔티티를 알린다.
/// </summary>
public sealed class EntityEditAction : IUndoableAction, IEntityChange
{
	private readonly Entity _entity;
	private readonly Action _apply;
	private readonly Action _revert;

	public EntityEditAction(string name, Entity entity, Action apply, Action revert)
	{
		this.Name = name;
		this._entity = entity;
		this._apply = apply;
		this._revert = revert;
	}

	public string Name { get; }

	public IEnumerable<Entity> AffectedEntities => new[] { this._entity };

	public void Do() => this._apply();

	public void Undo() => this._revert();
}
