using ACadSharp.Entities;
using CSMath;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 앱 내부 클립보드 복사 및 붙여넣기 명령.
/// </summary>
public static class ClipboardManager
{
	public static List<Entity> ClipboardEntities { get; } = new();

	public static void Copy(IEnumerable<Entity> entities)
	{
		ClipboardEntities.Clear();
		foreach (var e in entities)
		{
			ClipboardEntities.Add((Entity)e.Clone());
		}
	}
}

/// <summary>
/// COPYCLIP 명령: 선택된 객체를 내부 클립보드에 복사한다.
/// </summary>
public sealed class CopyClipCommand : EntityModificationCommand
{
	public override string Name => "COPYCLIP";
	public override string[] Aliases => new[] { "COPYBASE" };

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || !this.WaitingForSelection) return;
		var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
		var index = new SceneIndex(scene);
		var picked = index.Pick(x, y, 5.0);
		if (picked != null && !this.Targets.Contains(picked))
		{
			this.Targets.Add(picked);
			this.Prompt = $"객체 선택: {this.Targets.Count}개 선택됨 <완료는 Enter>:";
		}
	}

	protected override void OnSelectionReady()
	{
		if (this.Ctx != null && this.Targets.Count > 0)
		{
			ClipboardManager.Copy(this.Targets);
			this.Ctx.Print($"{ClipboardManager.ClipboardEntities.Count}개 객체가 클립보드에 복사되었습니다.");
		}
		else
		{
			this.Ctx?.Print("선택된 객체가 없습니다.");
		}
		this.Finish();
	}
}

/// <summary>
/// PASTECLIP 명령: 클립보드 객체를 지정한 삽입점에 붙여넣는다.
/// </summary>
public sealed class PasteClipCommand : EditorCommand
{
	private readonly List<Entity> _clones = new();
	private double _centerX;
	private double _centerY;
	private int _step;

	public override string Name => "PASTECLIP";
	public override string[] Aliases => new[] { "PASTE" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._clones.Clear();

		if (ClipboardManager.ClipboardEntities.Count == 0)
		{
			ctx.Print("클립보드가 비어 있습니다.");
			this.Finish();
			return;
		}

		foreach (var e in ClipboardManager.ClipboardEntities)
		{
			this._clones.Add((Entity)e.Clone());
		}

		// 바운딩 박스 중심 계산
		double minX = double.MaxValue, minY = double.MaxValue;
		double maxX = double.MinValue, maxY = double.MinValue;
		bool hasBounds = false;

		foreach (var e in this._clones)
		{
			var box = e.GetBoundingBox();
			hasBounds = true;
			minX = Math.Min(minX, box.Min.X);
			minY = Math.Min(minY, box.Min.Y);
			maxX = Math.Max(maxX, box.Max.X);
			maxY = Math.Max(maxY, box.Max.Y);
		}

		if (hasBounds)
		{
			this._centerX = (minX + maxX) / 2.0;
			this._centerY = (minY + maxY) / 2.0;
		}
		else
		{
			this._centerX = 0;
			this._centerY = 0;
		}

		this._step = 1;
		this.Prompt = "삽입점 지정:";
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null || this._step != 1) return;

		double dx = x - this._centerX;
		double dy = y - this._centerY;
		var transform = Transform.CreateTranslation(new XYZ(dx, dy, 0));

		foreach (var e in this._clones)
		{
			e.ApplyTransform(transform);
		}

		this.Ctx.Doc.AddEntities(this._clones);
		this.Finish();
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 1)
		{
			double dx = cursorX - this._centerX;
			double dy = cursorY - this._centerY;
			var transform = Transform.CreateTranslation(new XYZ(dx, dy, 0));

			foreach (var e in this._clones)
			{
				var clone = (Entity)e.Clone();
				clone.ApplyTransform(transform);
				yield return clone;
			}
		}
	}
}

/// <summary>CUTCLIP: 선택 객체를 클립보드에 복사하고 도면에서 지운다.</summary>
public sealed class CutClipCommand : EntityModificationCommand
{
	public override string Name => "CUTCLIP";

	protected override void OnSelectionReady()
	{
		if (this.Ctx != null && this.Targets.Count > 0)
		{
			ClipboardManager.Copy(this.Targets);
			this.Ctx.Doc.Erase(this.Targets);
			this.Ctx.Print($"{this.Targets.Count}개 객체를 잘라 클립보드에 넣었습니다.");
		}
		this.Finish();
	}
}
