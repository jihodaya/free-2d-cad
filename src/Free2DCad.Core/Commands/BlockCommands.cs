using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Commands;

/// <summary>
/// BLOCK 명령 (별칭 B): 선택 객체들로 새 BlockRecord를 정의하고, 원본을 Insert(블록 참조)로 치환한다.
/// </summary>
public sealed class BlockCommand : EditorCommand
{
	/// <summary>블록에 넣을 객체 선택 단계</summary>
	public override bool WantsSelection => this._step == 2;

	private int _step;
	private string _blockName = string.Empty;
	private XYZ _basePoint = XYZ.Zero;
	private readonly List<Entity> _targets = new();

	public override string Name => "BLOCK";
	public override string[] Aliases => new[] { "B" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._blockName = string.Empty;
		this._basePoint = XYZ.Zero;
		this._targets.Clear();

		if (ctx.Selection.Count > 0)
		{
			this._targets.AddRange(ctx.Selection);
		}

		this.Prompt = "블록 이름 입력: ";
	}

	public override void OnText(string text)
	{
		if (this.Ctx == null) return;
		string trimmed = text.Trim();

		if (this._step == 0)
		{
			if (string.IsNullOrEmpty(trimmed))
			{
				this.Prompt = "블록 이름 입력: ";
				return;
			}

			if (this.Ctx.Doc.Document.BlockRecords.Contains(trimmed))
			{
				this.Ctx.Print($"블록 \"{trimmed}\"이(가) 이미 있습니다.");
				this.Prompt = "블록이 이미 있습니다. 블록 이름 입력: ";
				return;
			}

			this._blockName = trimmed;
			this._step = 1;
			this.Prompt = "기준점 지정: ";
		}
		else if (this._step == 1)
		{
			if (CoordinateParser.TryParse(trimmed, null, null, false, out var pt))
			{
				this.ApplyBasePoint(pt.X, pt.Y);
			}
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 1)
		{
			this.ApplyBasePoint(x, y);
		}
		else if (this._step == 2)
		{
			var scene = SceneBuilder.Build(this.Ctx.Doc.Document);
			var index = new SceneIndex(scene);
			var picked = index.Pick(x, y, 5.0);
			if (picked != null && !this._targets.Contains(picked))
			{
				this._targets.Add(picked);
				this.Prompt = $"객체 선택: {this._targets.Count}개 선택됨 <완료는 Enter>:";
			}
		}
	}

	public override void OnSelection(IEnumerable<Entity> entities)
	{
		if (this._step == 2)
		{
			foreach (var e in entities)
			{
				if (!this._targets.Contains(e))
				{
					this._targets.Add(e);
				}
			}
			this.Prompt = $"객체 선택: {this._targets.Count}개 선택됨 <완료는 Enter>:";
		}
	}

	public override void OnEnter()
	{
		if (this._step == 2)
		{
			if (this._targets.Count == 0)
			{
				this.Finish();
				return;
			}

			this.CommitBlock();
		}
	}

	private void ApplyBasePoint(double x, double y)
	{
		this._basePoint = new XYZ(x, y, 0);
		this._step = 2;

		if (this._targets.Count > 0)
		{
			this.Prompt = $"객체 선택: {this._targets.Count}개 선택됨 <완료는 Enter>:";
		}
		else
		{
			this.Prompt = "객체 선택:";
		}
	}

	private void CommitBlock()
	{
		if (this.Ctx == null || string.IsNullOrEmpty(this._blockName) || this._targets.Count == 0)
		{
			this.Finish();
			return;
		}

		var blockRecord = new BlockRecord(this._blockName);
		Transform localTr = Transform.CreateTranslation(new XYZ(-this._basePoint.X, -this._basePoint.Y, -this._basePoint.Z));

		foreach (var ent in this._targets)
		{
			var clone = (Entity)ent.Clone();
			clone.ApplyTransform(localTr);
			blockRecord.Entities.Add(clone);
		}

		// 1. 블록 레코드 등록 (Undo 시 삭제)
		this.Ctx.Doc.Execute(new CreateBlockAction(this.Ctx.Doc.Document, blockRecord));

		// 2. 원본 엔티티 제거 (Undo 시 복원)
		this.Ctx.Doc.Erase(this._targets);

		// 3. 같은 위치에 Insert(블록 참조) 추가 (Undo 시 제거)
		var insert = new Insert(blockRecord)
		{
			InsertPoint = this._basePoint,
			Rotation = 0,
			XScale = 1,
			YScale = 1,
			ZScale = 1,
		};
		insert.Attributes.Clear();
		Transform tr = insert.GetTransform();
		foreach (var def in blockRecord.AttributeDefinitions)
		{
			var att = new AttributeEntity(def);
			att.ApplyTransform(tr);
			insert.Attributes.Add(att);
		}

		this.Ctx.Doc.AddEntities(insert);
		this.Finish();
	}
}

/// <summary>
/// INSERT 명령 (별칭 I): 지정한 블록 레코드의 블록 참조(Insert)를 도면에 삽입한다.
/// </summary>
public sealed class InsertCommand : EditorCommand
{
	private int _step;
	private BlockRecord? _targetBlock;
	private XYZ _insertPoint = XYZ.Zero;
	private double _scale = 1.0;

	public override string Name => "INSERT";
	public override string[] Aliases => new[] { "I" };

	public BlockRecord? PreselectedBlock { get; set; }

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._targetBlock = this.PreselectedBlock;
		this._insertPoint = XYZ.Zero;
		this._scale = 1.0;

		if (this._targetBlock != null)
		{
			this._step = 1;
			this.Prompt = "삽입점 지정: ";
		}
		else
		{
			this.Prompt = "블록 이름 입력 [?]: ";
		}
	}

	public override void OnText(string text)
	{
		if (this._step == 4)
		{
			this.NextAttribute(text);
			return;
		}
		if (this.Ctx == null) return;
		string trimmed = text.Trim();

		if (this._step == 0)
		{
			if (trimmed == "?")
			{
				var names = this.Ctx.Doc.Document.BlockRecords
					.Where(b => !b.Name.StartsWith("*") && !b.IsAnonymous)
					.Select(b => b.Name)
					.ToList();

				if (names.Count > 0)
				{
					this.Ctx.Print($"정의된 블록: {string.Join(", ", names)}");
				}
				else
				{
					this.Ctx.Print("정의된 사용자 블록이 없습니다.");
				}
				this.Prompt = "블록 이름 입력: ";
				return;
			}

			if (string.IsNullOrEmpty(trimmed))
			{
				this.Prompt = "블록 이름 입력 [?]: ";
				return;
			}

			if (this.Ctx.Doc.Document.BlockRecords.TryGetValue(trimmed, out var blockRecord))
			{
				this._targetBlock = blockRecord;
				this._step = 1;
				this.Prompt = "삽입점 지정: ";
			}
			else
			{
				this.Ctx.Print($"블록 \"{trimmed}\"을(를) 찾을 수 없습니다.");
				this.Prompt = "블록 이름 입력: ";
			}
		}
		else if (this._step == 1)
		{
			if (CoordinateParser.TryParse(trimmed, null, null, false, out var pt))
			{
				this._insertPoint = new XYZ(pt.X, pt.Y, 0);
				this._step = 2;
				this.Prompt = "축척 비율 지정 <1.0000>: ";
			}
		}
		else if (this._step == 2)
		{
			if (string.IsNullOrEmpty(trimmed))
			{
				this._scale = 1.0;
			}
			else if (double.TryParse(trimmed, out double s) && Math.Abs(s) > 1e-9)
			{
				this._scale = s;
			}
			else
			{
				this._scale = 1.0;
			}

			this._step = 3;
			this.Prompt = "회전 각도 지정 <0>: ";
		}
		else if (this._step == 3)
		{
			double deg = 0.0;
			if (!string.IsNullOrEmpty(trimmed) && double.TryParse(trimmed, out double a))
			{
				deg = a;
			}
			this.CommitInsert(deg);
		}
	}

	public override void OnPoint(double x, double y)
	{
		if (this.Ctx == null) return;

		if (this._step == 1)
		{
			this._insertPoint = new XYZ(x, y, 0);
			this._step = 2;
			this.Prompt = "축척 비율 지정 <1.0000>: ";
		}
		else if (this._step == 2)
		{
			double dx = x - this._insertPoint.X;
			double dy = y - this._insertPoint.Y;
			double dist = Math.Sqrt(dx * dx + dy * dy);
			this._scale = dist > 1e-6 ? dist : 1.0;
			this._step = 3;
			this.Prompt = "회전 각도 지정 <0>: ";
		}
		else if (this._step == 3)
		{
			double dx = x - this._insertPoint.X;
			double dy = y - this._insertPoint.Y;
			double rad = Math.Atan2(dy, dx);
			double deg = rad * 180.0 / Math.PI;
			this.CommitInsert(deg);
		}
	}

	public override void OnEnter()
	{
		if (this._step == 4)
		{
			this.NextAttribute(null);
			return;
		}
		if (this._step == 2)
		{
			this._scale = 1.0;
			this._step = 3;
			this.Prompt = "회전 각도 지정 <0>: ";
		}
		else if (this._step == 3)
		{
			this.CommitInsert(0.0);
		}
	}

	public override IEnumerable<Entity> Preview(double cursorX, double cursorY)
	{
		if (this._step == 1 && this._targetBlock != null)
		{
			var tempInsert = new Insert(this._targetBlock)
			{
				InsertPoint = new XYZ(cursorX, cursorY, 0),
			};
			return tempInsert.Explode();
		}
		return base.Preview(cursorX, cursorY);
	}

	private void CommitInsert(double deg)
	{
		if (this.Ctx == null || this._targetBlock == null)
		{
			this.Finish();
			return;
		}

		double rad = deg * Math.PI / 180.0;
		var insert = new Insert(this._targetBlock)
		{
			InsertPoint = this._insertPoint,
			Rotation = rad,
			XScale = this._scale,
			YScale = this._scale,
			ZScale = this._scale,
		};

		insert.Attributes.Clear();
		Transform tr = insert.GetTransform();
		foreach (var def in this._targetBlock.AttributeDefinitions)
		{
			var att = new AttributeEntity(def);
			att.ApplyTransform(tr);
			insert.Attributes.Add(att);
		}

		this._pending = insert;
		this._attIndex = -1;
		this.NextAttribute(null);
	}

	private Insert? _pending;
	private int _attIndex;

	/// <summary>속성 값 묻기: 상수가 아닌 속성마다 "프롬프트 &lt;기본값&gt;:" (Enter = 기본값).</summary>
	private void NextAttribute(string? value)
	{
		var insert = this._pending!;
		var atts = insert.Attributes.ToList();
		if (this._attIndex >= 0 && value != null)
		{
			atts[this._attIndex].Value = value;
		}
		this._attIndex++;
		var defs = this._targetBlock!.AttributeDefinitions.ToList();
		while (this._attIndex < atts.Count && this._attIndex < defs.Count && defs[this._attIndex].Flags.HasFlag(AttributeFlags.Constant))
		{
			this._attIndex++;
		}
		if (this._attIndex < atts.Count && this._attIndex < defs.Count)
		{
			var def = defs[this._attIndex];
			string prompt = string.IsNullOrEmpty(def.Prompt) ? def.Tag : def.Prompt;
			this._step = 4;
			this.Prompt = $"{prompt} <{def.Value}>:";
			return;
		}
		this.Ctx!.Doc.AddEntities(insert);
		this.Finish();
	}
}
