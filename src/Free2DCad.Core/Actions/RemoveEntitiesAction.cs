using ACadSharp.Entities;
using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 엔티티를 소유한 블록 레코드(Owner)에서 제거하고 Undo 시 같은 소유자에 다시 추가하는 액션.
/// </summary>
public sealed class RemoveEntitiesAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._records.Select(r => r.Entity);

	private readonly List<(Entity Entity, BlockRecord Owner)> _records = new();

	// 되돌릴 때 원래 그리기 순서로 (소유자별 지우기 전 순서)
	private readonly Dictionary<BlockRecord, List<Entity>> _orders = new();

	public string Name { get; }

	public IReadOnlyList<Entity> Entities => this._records.Select(r => r.Entity).ToList();

	public RemoveEntitiesAction(IEnumerable<Entity> entities, string name = "엔티티 제거")
	{
		this.Name = name;
		foreach (Entity entity in entities)
		{
			if (entity.Owner is BlockRecord blockRecord)
			{
				this._records.Add((entity, blockRecord));
			}
		}
	}

	public void Do()
	{
		this._orders.Clear();
		foreach (var owner in this._records.Select(r => r.Owner).Distinct())
		{
			this._orders[owner] = owner.Entities.ToList();
		}
		for (int i = 0; i < this._records.Count; i++)
		{
			var (entity, owner) = this._records[i];
			owner.Entities.Remove(entity);
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._records.Count; i++)
		{
			var (entity, owner) = this._records[i];
			if (entity.Owner == null)
			{
				owner.Entities.Add(entity);
			}
		}
		foreach (var (owner, order) in this._orders)
		{
			owner.Entities.Reorder(order);
		}
	}
}
