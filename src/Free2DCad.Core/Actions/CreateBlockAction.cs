using ACadSharp;
using ACadSharp.Tables;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 새 블록 레코드(BlockRecord)를 도면에 추가하고 Undo 시 제거하는 액션.
/// </summary>
public sealed class CreateBlockAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly BlockRecord _blockRecord;

	public string Name => "블록 정의";

	public BlockRecord BlockRecord => this._blockRecord;

	public CreateBlockAction(CadDocument doc, BlockRecord blockRecord)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._blockRecord = blockRecord ?? throw new ArgumentNullException(nameof(blockRecord));
	}

	public void Do()
	{
		if (!this._doc.BlockRecords.Contains(this._blockRecord.Name))
		{
			this._doc.BlockRecords.Add(this._blockRecord);
		}
	}

	public void Undo()
	{
		if (this._doc.BlockRecords.Contains(this._blockRecord.Name))
		{
			this._doc.BlockRecords.Remove(this._blockRecord.Name);
		}
	}
}
