using ACadSharp;
using ACadSharp.Entities;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 엔티티들의 색상을 변경하고 Undo 시 이전 색상으로 복원하는 액션.
/// </summary>
public sealed class ChangeColorAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._items.Select(i => i.Entity);

	private readonly List<(Entity Entity, Color OldColor)> _items;
	private readonly Color _newColor;

	public string Name { get; }

	public Color NewColor => this._newColor;

	public ChangeColorAction(IEnumerable<Entity> entities, Color newColor, string name = "색상 변경")
	{
		this._newColor = newColor;
		this.Name = name;
		this._items = entities.Select(e => (e, e.Color)).ToList();
	}

	public void Do()
	{
		for (int i = 0; i < this._items.Count; i++)
		{
			this._items[i].Entity.Color = this._newColor;
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._items.Count; i++)
		{
			this._items[i].Entity.Color = this._items[i].OldColor;
		}
	}
}
