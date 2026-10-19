using ACadSharp.Entities;

namespace Free2DCad.Core.Actions;

/// <summary>
/// TextEntity 또는 MText의 텍스트 내용을 변경하는 되돌리기 가능한 액션.
/// </summary>
public sealed class ChangeTextAction : IUndoableAction, IEntityChange
{
	/// <inheritdoc/>
	public IEnumerable<Entity> AffectedEntities => new[] { this._entity };

	private readonly Entity _entity;
	private readonly string _oldValue;
	private readonly string _newValue;

	public string Name => "문자 변경";

	public ChangeTextAction(Entity entity, string newValue)
	{
		this._entity = entity ?? throw new ArgumentNullException(nameof(entity));
		this._newValue = newValue ?? string.Empty;
		this._oldValue = GetTextValue(entity);
	}

	public void Do()
	{
		SetTextValue(this._entity, this._newValue);
	}

	public void Undo()
	{
		SetTextValue(this._entity, this._oldValue);
	}

	public static string GetTextValue(Entity entity) => entity switch
	{
		TextEntity text => text.Value ?? string.Empty,
		MText mtext => mtext.Value ?? string.Empty,
		MultiLeader ml => ml.ContextData.TextLabel ?? string.Empty,
		_ => string.Empty
	};

	public static void SetTextValue(Entity entity, string value)
	{
		switch (entity)
		{
			case TextEntity text:
				text.Value = value;
				break;
			case MText mtext:
				mtext.Value = value;
				break;
			case MultiLeader ml:
				Free2DCad.Core.Commands.MLeaderBuilder.SetText(ml, value); // 문자 위치·내용 기준점도 다시
				break;
		}
	}
}
