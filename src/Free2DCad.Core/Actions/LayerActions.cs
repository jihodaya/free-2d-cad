using ACadSharp;
using ACadSharp.Tables;
using Color = ACadSharp.Color;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 도면층 추가 및 실행 취소(Undo) 시 제거 액션.
/// </summary>
public sealed class AddLayerAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly Layer _layer;

	public string Name => "도면층 새로 만들기";

	public AddLayerAction(CadDocument doc, Layer layer)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
	}

	public void Do()
	{
		if (!this._doc.Layers.Contains(this._layer.Name))
		{
			this._doc.Layers.Add(this._layer);
		}
	}

	public void Undo()
	{
		if (this._layer.Name != Layer.DefaultName)
		{
			this._doc.Layers.Remove(this._layer.Name);
		}
	}
}

/// <summary>
/// 도면층 이름 변경 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class RenameLayerAction : IUndoableAction
{
	private readonly Layer _layer;
	private readonly string _oldName;
	private readonly string _newName;

	public string Name => "도면층 이름 바꾸기";

	public RenameLayerAction(Layer layer, string newName)
	{
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
		this._oldName = layer.Name;
		this._newName = newName ?? throw new ArgumentNullException(nameof(newName));
	}

	public void Do()
	{
		this._layer.Name = this._newName;
	}

	public void Undo()
	{
		this._layer.Name = this._oldName;
	}
}

/// <summary>
/// 도면층 색상 변경 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class ChangeLayerColorAction : IUndoableAction
{
	private readonly Layer _layer;
	private readonly Color _oldColor;
	private readonly Color _newColor;

	public string Name => "도면층 색상 변경";

	public ChangeLayerColorAction(Layer layer, Color newColor)
	{
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
		this._oldColor = layer.Color;
		this._newColor = newColor;
	}

	public void Do()
	{
		this._layer.Color = this._newColor;
	}

	public void Undo()
	{
		this._layer.Color = this._oldColor;
	}
}

/// <summary>
/// 도면층 삭제 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class RemoveLayerAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly Layer _layer;

	public string Name => "도면층 삭제";

	public RemoveLayerAction(CadDocument doc, Layer layer)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
	}

	public void Do()
	{
		if (this._layer.Name != Layer.DefaultName)
		{
			this._doc.Layers.Remove(this._layer.Name);
		}
	}

	public void Undo()
	{
		if (!this._doc.Layers.Contains(this._layer.Name))
		{
			this._doc.Layers.Add(this._layer);
		}
	}
}

/// <summary>
/// 도면층 동결/해제 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class ToggleLayerFrozenAction : IUndoableAction
{
	private readonly Layer _layer;
	private readonly bool _targetValue;
	private readonly bool _previousValue;

	public string Name => this._targetValue ? "도면층 동결" : "도면층 동결 해제";

	public ToggleLayerFrozenAction(Layer layer, bool? isFrozen = null)
	{
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
		this._previousValue = layer.Flags.HasFlag(LayerFlags.Frozen);
		this._targetValue = isFrozen ?? !this._previousValue;
	}

	public void Do()
	{
		if (this._targetValue)
		{
			this._layer.Flags |= LayerFlags.Frozen;
		}
		else
		{
			this._layer.Flags &= ~LayerFlags.Frozen;
		}
	}

	public void Undo()
	{
		if (this._previousValue)
		{
			this._layer.Flags |= LayerFlags.Frozen;
		}
		else
		{
			this._layer.Flags &= ~LayerFlags.Frozen;
		}
	}
}

/// <summary>
/// 도면층 잠금/잠금해제 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class ToggleLayerLockedAction : IUndoableAction
{
	private readonly Layer _layer;
	private readonly bool _targetValue;
	private readonly bool _previousValue;

	public string Name => this._targetValue ? "도면층 잠금" : "도면층 잠금 해제";

	public ToggleLayerLockedAction(Layer layer, bool? isLocked = null)
	{
		this._layer = layer ?? throw new ArgumentNullException(nameof(layer));
		this._previousValue = layer.Flags.HasFlag(LayerFlags.Locked);
		this._targetValue = isLocked ?? !this._previousValue;
	}

	public void Do()
	{
		if (this._targetValue)
		{
			this._layer.Flags |= LayerFlags.Locked;
		}
		else
		{
			this._layer.Flags &= ~LayerFlags.Locked;
		}
	}

	public void Undo()
	{
		if (this._previousValue)
		{
			this._layer.Flags |= LayerFlags.Locked;
		}
		else
		{
			this._layer.Flags &= ~LayerFlags.Locked;
		}
	}
}

/// <summary>
/// 객체 속성/형상 변경 및 실행 취소(Undo) 지원 액션.
/// </summary>
public sealed class ModifyEntityPropertyAction : IUndoableAction
{
	private readonly Action _doAction;
	private readonly Action _undoAction;

	public string Name { get; }

	public ModifyEntityPropertyAction(string name, Action doAction, Action undoAction)
	{
		this.Name = name ?? "속성 변경";
		this._doAction = doAction ?? throw new ArgumentNullException(nameof(doAction));
		this._undoAction = undoAction ?? throw new ArgumentNullException(nameof(undoAction));
	}

	public void Do() => this._doAction();

	public void Undo() => this._undoAction();
}
