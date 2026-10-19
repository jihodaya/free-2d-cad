using ACadSharp.Entities;
using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 엔티티들의 레이어를 변경하고 Undo 시 이전 레이어로 복원하는 액션.
/// </summary>
public sealed class ChangeLayerAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._items.Select(i => i.Entity);

	private readonly List<(Entity Entity, Layer OldLayer)> _items;
	private readonly Layer _newLayer;

	public string Name { get; }

	public Layer NewLayer => this._newLayer;

	public ChangeLayerAction(IEnumerable<Entity> entities, Layer newLayer, string name = "레이어 변경")
	{
		this._newLayer = newLayer;
		this.Name = name;
		this._items = entities.Select(e => (e, e.Layer)).ToList();
	}

	public void Do()
	{
		for (int i = 0; i < this._items.Count; i++)
		{
			this._items[i].Entity.Layer = this._newLayer;
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._items.Count; i++)
		{
			this._items[i].Entity.Layer = this._items[i].OldLayer;
		}
	}
}
