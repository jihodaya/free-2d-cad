using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 원본 엔티티의 특성(도면층, 색상, 선종류)을 대상 엔티티들에 복사하고 Undo 시 이전 상태로 복원하는 액션.
/// </summary>
public sealed class MatchPropertiesAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => this._states.Select(s => s.Entity);

	private sealed class State
	{
		public Entity Entity { get; }
		public Layer OldLayer { get; }
		public Color OldColor { get; }
		public LineType OldLineType { get; }
		public double OldLineTypeScale { get; }

		public State(Entity entity)
		{
			this.Entity = entity;
			this.OldLayer = entity.Layer;
			this.OldColor = entity.Color;
			this.OldLineType = entity.LineType;
			this.OldLineTypeScale = entity.LineTypeScale;
		}
	}

	private readonly List<State> _states;
	private readonly Layer _newLayer;
	private readonly Color _newColor;
	private readonly LineType _newLineType;
	private readonly double _newLineTypeScale;

	public string Name { get; }

	public MatchPropertiesAction(
		IEnumerable<Entity> targets,
		Entity source,
		string name = "특성 일치")
	{
		ArgumentNullException.ThrowIfNull(targets);
		ArgumentNullException.ThrowIfNull(source);

		this.Name = name;
		this._states = targets.Select(t => new State(t)).ToList();
		this._newLayer = source.Layer;
		this._newColor = source.Color;
		this._newLineType = source.LineType;
		this._newLineTypeScale = source.LineTypeScale;
	}

	public void Do()
	{
		for (int i = 0; i < this._states.Count; i++)
		{
			var entity = this._states[i].Entity;
			entity.Layer = this._newLayer;
			entity.Color = this._newColor;
			if (this._newLineType != null)
			{
				entity.LineType = this._newLineType;
			}
			entity.LineTypeScale = this._newLineTypeScale;
		}
	}

	public void Undo()
	{
		for (int i = 0; i < this._states.Count; i++)
		{
			var state = this._states[i];
			state.Entity.Layer = state.OldLayer;
			state.Entity.Color = state.OldColor;
			if (state.OldLineType != null)
			{
				state.Entity.LineType = state.OldLineType;
			}
			state.Entity.LineTypeScale = state.OldLineTypeScale;
		}
	}
}
