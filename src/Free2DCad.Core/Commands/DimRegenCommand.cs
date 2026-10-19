using ACadSharp.Entities;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Commands;

/// <summary>
/// DIMREGEN: 도면의 모든 치수 블록을 현재 치수 스타일·재정의로 다시 그린다 (예전 버전에서 만든 치수를 새 배치 규칙으로).
/// 한 번에 되돌릴 수 있다.
/// </summary>
public sealed class DimRegenCommand : EditorCommand
{
	public override string Name => "DIMREGEN";

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		var dims = ctx.Doc.Document.BlockRecords
			.Where(b => !b.Name.StartsWith("*D", StringComparison.OrdinalIgnoreCase))
			.SelectMany(b => b.Entities.OfType<Dimension>())
			.Where(d => d.Block != null)
			.Distinct()
			.ToList();
		var actions = new List<IUndoableAction>();
		foreach (var dim in dims)
		{
			var old = dim.Block.Entities.ToList();
			List<Entity>? regenerated = null;
			actions.Add(new EntityEditAction("DIMREGEN", dim,
				() =>
				{
					if (regenerated == null)
					{
						dim.UpdateBlock();
						regenerated = dim.Block.Entities.ToList();
					}
					else
					{
						Swap(dim, regenerated);
					}
				},
				() => Swap(dim, old)));
		}
		if (actions.Count > 0)
		{
			ctx.Doc.Execute(new CompositeAction("DIMREGEN", actions));
		}
		ctx.Print($"치수 {dims.Count}개를 다시 그렸습니다.");
		this.Finish();
	}

	private static void Swap(Dimension dim, List<Entity> entities)
	{
		var block = dim.Block;
		foreach (var e in block.Entities.ToList())
		{
			block.Entities.Remove(e);
		}
		foreach (var e in entities)
		{
			block.Entities.Add(e);
		}
	}
}
