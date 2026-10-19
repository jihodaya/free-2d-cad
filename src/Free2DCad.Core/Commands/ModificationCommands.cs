using ACadSharp.Entities;
using CSMath;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// 선택이 필요한 수정 명령의 공통 기본 클래스.
/// </summary>
public abstract class EntityModificationCommand : EditorCommand
{
	/// <summary>객체 선택 단계</summary>
	public override bool WantsSelection => this.WaitingForSelection;

	protected readonly List<Entity> Targets = new();
	protected bool WaitingForSelection;

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this.Targets.Clear();

		if (ctx.Selection.Count > 0)
		{
			this.Targets.AddRange(ctx.Selection);
			this.WaitingForSelection = false;
			this.RememberSelection();
			this.OnSelectionReady();
		}
		else
		{
			this.WaitingForSelection = true;
			this.Prompt = this.SelectPrompt;
		}
	}

	/// <summary>객체를 고를 때의 문구 (기존 CAD는 명령마다 다름, 예: "Move 객체선택 :").</summary>
	protected virtual string SelectPrompt => "객체 선택:";

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this.WaitingForSelection)
		{
			foreach (var e in entities)
			{
				if (!this.Targets.Contains(e))
				{
					this.Targets.Add(e);
				}
			}
			this.Ctx?.Print($"세트 내의 객체: {this.Targets.Count}");
			this.Prompt = this.SelectPrompt;
		}
	}

	public override void OnEnter()
	{
		if (this.WaitingForSelection)
		{
			this.WaitingForSelection = false;
			if (this.Targets.Count == 0)
			{
				this.OnNothingSelected();
				this.Finish();
				return;
			}
			this.Ctx?.Print($"{this.Targets.Count} 찾음");
			this.RememberSelection();
			this.OnSelectionReady();
		}
	}

	private void RememberSelection()
	{
		var prev = this.Ctx!.Doc.PreviousSelection;
		prev.Clear();
		prev.AddRange(this.Targets);
	}

	/// <summary>아무것도 고르지 않고 Enter (EXPLODE는 "발견된 것이 없습니다.").</summary>
	protected virtual void OnNothingSelected()
	{
	}

	protected abstract void OnSelectionReady();
}

/// <summary>
/// EXPLODE 명령: 블록(Insert) 및 폴리선(LwPolyline)을 분해한다.
/// </summary>
public sealed class ExplodeCommand : EntityModificationCommand
{
	public override string Name => "EXPLODE";
	public override string[] Aliases => new[] { "X" };

	protected override string SelectPrompt => "EXPLODE 객체선택 :";

	protected override void OnNothingSelected() => this.Ctx?.Print("발견된 것이 없습니다.");

	protected override void OnSelectionReady()
	{
		if (this.Ctx == null || this.Targets.Count == 0)
		{
			this.Finish();
			return;
		}

		var toAdd = new List<Entity>();
		var toRemove = new List<Entity>();

		foreach (var entity in this.Targets)
		{
			if (entity is Insert insert)
			{
				toRemove.Add(insert);
				toAdd.AddRange(insert.Explode());
			}
			else if (entity is LwPolyline lw)
			{
				toRemove.Add(lw);
				toAdd.AddRange(ExplodeLwPolyline(lw));
			}
		}

		if (toRemove.Count > 0)
		{
			this.Ctx.Doc.Erase(toRemove);
		}
		if (toAdd.Count > 0)
		{
			this.Ctx.Doc.AddEntities(toAdd);
		}

		this.Finish();
	}

	private static IEnumerable<Entity> ExplodeLwPolyline(LwPolyline lw)
	{
		var verts = lw.Vertices;
		int count = verts.Count;
		if (count < 2) yield break;

		int segCount = lw.IsClosed ? count : count - 1;
		for (int i = 0; i < segCount; i++)
		{
			var v1 = verts[i];
			var v2 = verts[(i + 1) % count];

			if (v1.Bulge == 0)
			{
				yield return new Line(new XYZ(v1.Location.X, v1.Location.Y, 0), new XYZ(v2.Location.X, v2.Location.Y, 0));
			}
			else
			{
				var arc = BulgeToArc(v1.Location.X, v1.Location.Y, v2.Location.X, v2.Location.Y, v1.Bulge);
				if (arc != null)
				{
					yield return arc;
				}
				else
				{
					yield return new Line(new XYZ(v1.Location.X, v1.Location.Y, 0), new XYZ(v2.Location.X, v2.Location.Y, 0));
				}
			}
		}
	}

	private static Arc? BulgeToArc(double x1, double y1, double x2, double y2, double bulge)
	{
		double theta = 4 * Math.Atan(bulge);
		double chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
		if (chord < 1e-9) return null;

		double radius = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
		double mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
		double dist = radius * Math.Cos(Math.Abs(theta) / 2);
		double nx = -(y2 - y1) / chord, ny = (x2 - x1) / chord;
		double sign = bulge > 0 ? 1 : -1;
		double cx = mx + sign * nx * dist;
		double cy = my + sign * ny * dist;

		double start = Math.Atan2(y1 - cy, x1 - cx);
		double end = Math.Atan2(y2 - cy, x2 - cx);
		while (start < 0) start += 2 * Math.PI;
		while (end < 0) end += 2 * Math.PI;

		if (bulge > 0)
		{
			return new Arc { Center = new XYZ(cx, cy, 0), Radius = radius, StartAngle = start, EndAngle = end };
		}
		else
		{
			return new Arc { Center = new XYZ(cx, cy, 0), Radius = radius, StartAngle = end, EndAngle = start };
		}
	}
}
