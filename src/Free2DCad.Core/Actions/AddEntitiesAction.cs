using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 작업 공간(CurrentSpace: ModelSpace 또는 PaperSpace)에 엔티티들을 추가하고 Undo 시 제거하는 액션.
/// </summary>
public sealed class AddEntitiesAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._entities;

	private readonly CadDocument? _doc;
	private readonly BlockRecord _targetSpace;
	private readonly List<Entity> _entities;

	public string Name { get; }

	public IReadOnlyList<Entity> Entities => this._entities;

	public BlockRecord TargetSpace => this._targetSpace;

	public AddEntitiesAction(BlockRecord targetSpace, IEnumerable<Entity> entities, string name = "엔티티 추가")
	{
		this._targetSpace = targetSpace ?? throw new ArgumentNullException(nameof(targetSpace));
		this._doc = targetSpace.Document;
		this._entities = entities.ToList();
		this.Name = name;
	}

	public AddEntitiesAction(CadDocument doc, IEnumerable<Entity> entities, string name = "엔티티 추가")
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._targetSpace = doc.ModelSpace ?? doc.BlockRecords[BlockRecord.ModelSpaceName];
		this._entities = entities.ToList();
		this.Name = name;
	}

	public void Do()
	{
		for (int i = 0; i < this._entities.Count; i++)
		{
			Entity entity = this._entities[i];
			if (entity.Owner == null)
			{
				this._targetSpace.Entities.Add(entity);
			}
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._entities.Count; i++)
		{
			Entity entity = this._entities[i];
			this._targetSpace.Entities.Remove(entity);
		}
	}
}
