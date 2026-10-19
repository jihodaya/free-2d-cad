using ACadSharp.Objects;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Commands;

/// <summary>
/// LAYOUT 명령 (별칭 LO): 배치 생성(N), 이름 변경(R), 삭제(D), 현재로 설정(S)을 지원한다.
/// </summary>
public sealed class LayoutCommand : EditorCommand
{
	private int _step;
	private string? _subOption;
	private string? _targetLayoutName;

	public override string Name => "LAYOUT";
	public override string[] Aliases => new[] { "LO" };

	public override void Start(CommandContext ctx)
	{
		base.Start(ctx);
		this._step = 0;
		this._subOption = null;
		this._targetLayoutName = null;
		this.Prompt = "배치 옵션 입력 [새로 만들기(N)/이름 바꾸기(R)/삭제(D)/현재로 설정(S)] <새로 만들기>: ";
	}

	public override void OnText(string text)
	{
		string trimmed = text.Trim();
		if (this.Ctx == null)
		{
			this.Finish();
			return;
		}

		if (this._step == 0)
		{
			if (string.IsNullOrEmpty(trimmed) || string.Equals(trimmed, "N", StringComparison.OrdinalIgnoreCase))
			{
				this._subOption = "N";
				this._step = 1;
				int nextNum = 1;
				while (this.Ctx.Doc.Document.Layouts.ContainsKey($"배치{nextNum}"))
				{
					nextNum++;
				}
				this.Prompt = $"새 배치 이름 입력 <배치{nextNum}>: ";
			}
			else if (string.Equals(trimmed, "R", StringComparison.OrdinalIgnoreCase))
			{
				this._subOption = "R";
				this._step = 1;
				this.Prompt = "이름을 바꿀 배치 입력: ";
			}
			else if (string.Equals(trimmed, "D", StringComparison.OrdinalIgnoreCase))
			{
				this._subOption = "D";
				this._step = 1;
				this.Prompt = "삭제할 배치 입력: ";
			}
			else if (string.Equals(trimmed, "S", StringComparison.OrdinalIgnoreCase))
			{
				this._subOption = "S";
				this._step = 1;
				this.Prompt = "현재로 설정할 배치 입력: ";
			}
			else
			{
				this.Ctx.Print("유효하지 않은 옵션입니다.");
				this.Finish();
			}
			return;
		}

		if (this._step == 1)
		{
			switch (this._subOption)
			{
				case "N":
					string newName = trimmed;
					if (string.IsNullOrEmpty(newName))
					{
						int nextNum = 1;
						while (this.Ctx.Doc.Document.Layouts.ContainsKey($"배치{nextNum}"))
						{
							nextNum++;
						}
						newName = $"배치{nextNum}";
					}
					if (this.Ctx.Doc.Document.Layouts.ContainsKey(newName))
					{
						this.Ctx.Print($"배치 \"{newName}\"이(가) 이미 존재합니다.");
					}
					else
					{
						var created = this.Ctx.Doc.AddLayout(newName);
						this.Ctx.Print($"새 배치 \"{created.Name}\"이(가) 생성되었습니다.");
					}
					this.Finish();
					break;

				case "R":
					if (string.IsNullOrEmpty(trimmed))
					{
						this.Finish();
						return;
					}
					if (!this.Ctx.Doc.Document.Layouts.TryGet(trimmed, out var layToRename))
					{
						this.Ctx.Print($"배치 \"{trimmed}\"을(를) 찾을 수 없습니다.");
						this.Finish();
						return;
					}
					this._targetLayoutName = trimmed;
					this._step = 2;
					this.Prompt = "새 배치 이름 입력: ";
					break;

				case "D":
					if (string.IsNullOrEmpty(trimmed))
					{
						this.Finish();
						return;
					}
					if (!this.Ctx.Doc.Document.Layouts.TryGet(trimmed, out var layToDelete))
					{
						this.Ctx.Print($"배치 \"{trimmed}\"을(를) 찾을 수 없습니다.");
						this.Finish();
						return;
					}
					try
					{
						this.Ctx.Doc.RemoveLayout(layToDelete);
						this.Ctx.Print($"배치 \"{trimmed}\"이(가) 삭제되었습니다.");
					}
					catch (Exception ex)
					{
						this.Ctx.Print($"배치 삭제 실패: {ex.Message}");
					}
					this.Finish();
					break;

				case "S":
					if (string.IsNullOrEmpty(trimmed))
					{
						this.Finish();
						return;
					}
					if (string.Equals(trimmed, "Model", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(trimmed, "모형", StringComparison.OrdinalIgnoreCase))
					{
						this.Ctx.Doc.CurrentSpace = this.Ctx.Doc.Document.ModelSpace;
						this.Ctx.Print("현재 공간: 모형");
					}
					else if (this.Ctx.Doc.Document.Layouts.TryGet(trimmed, out var targetLay))
					{
						if (targetLay.AssociatedBlock != null)
						{
							this.Ctx.Doc.CurrentSpace = targetLay.AssociatedBlock;
							this.Ctx.Print($"현재 공간: {targetLay.Name}");
						}
					}
					else
					{
						this.Ctx.Print($"배치 \"{trimmed}\"을(를) 찾을 수 없습니다.");
					}
					this.Finish();
					break;
			}
			return;
		}

		if (this._step == 2 && this._subOption == "R")
		{
			string newName = trimmed;
			if (string.IsNullOrEmpty(newName))
			{
				this.Finish();
				return;
			}
			if (this.Ctx.Doc.Document.Layouts.TryGet(this._targetLayoutName!, out var layToRename))
			{
				try
				{
					this.Ctx.Doc.RenameLayout(layToRename, newName);
					this.Ctx.Print($"배치 이름이 \"{this._targetLayoutName}\"에서 \"{newName}\"(으)로 변경되었습니다.");
				}
				catch (Exception ex)
				{
					this.Ctx.Print($"배치 이름 변경 실패: {ex.Message}");
				}
			}
			this.Finish();
		}
	}

	public override void OnEnter()
	{
		this.OnText(string.Empty);
	}
}
