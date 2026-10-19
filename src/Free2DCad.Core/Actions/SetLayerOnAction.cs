using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 레이어의 On/Off 상태를 토글(또는 설정)하고 Undo 시 이전 상태로 복원하는 액션.
/// </summary>
public sealed class SetLayerOnAction : IUndoableAction
{
	private readonly Layer _layer;
	private readonly bool _targetValue;
	private readonly bool _previousValue;

	public string Name { get; }

	public Layer Layer => this._layer;

	public bool TargetValue => this._targetValue;

	public bool PreviousValue => this._previousValue;

	public SetLayerOnAction(Layer layer, bool? isOn = null, string? name = null)
	{
		this._layer = layer;
		this._previousValue = layer.IsOn;
		this._targetValue = isOn ?? !layer.IsOn;
		this.Name = name ?? (this._targetValue ? "레이어 켜기" : "레이어 끄기");
	}

	public void Do()
	{
		this._layer.IsOn = this._targetValue;
	}

	public void Undo()
	{
		this._layer.IsOn = this._previousValue;
	}
}
