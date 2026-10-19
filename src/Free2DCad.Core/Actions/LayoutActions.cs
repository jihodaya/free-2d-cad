using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Objects;
using ACadSharp.Tables;
using CSMath;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 새 배치(Layout)를 생성하고 A3 가로 용지 및 뷰포트를 추가하는 실행 취소(Undo) 지원 액션.
/// </summary>
public sealed class AddLayoutAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly string _name;
	private Layout? _layout;
	private BlockRecord? _block;

	public string Name => "배치 새로 만들기";

	public Layout? CreatedLayout => this._layout;

	public AddLayoutAction(CadDocument doc, string name)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._name = name ?? throw new ArgumentNullException(nameof(name));
	}

	public void Do()
	{
		if (this._layout == null)
		{
			// 1. 기존 *Paper_Space 블록과 겹치지 않는 새 블록 이름 생성
			int num = 0;
			string blockName;
			do
			{
				blockName = $"*Paper_Space{num++}";
			} while (this._doc.BlockRecords.Contains(blockName));

			// 2. 용지 A3 가로 (420 x 297 mm) 레이아웃 생성
			var layout = new Layout(this._name, blockName);
			layout.PaperWidth = 420;
			layout.PaperHeight = 297;
			layout.PaperUnits = PlotPaperUnits.Millimeters;
			layout.MinLimits = new XY(0, 0);
			layout.MaxLimits = new XY(420, 297);
			layout.TabOrder = this._doc.Layouts.Any() ? this._doc.Layouts.Max(l => l.TabOrder) + 1 : 1;
			layout.UpdatePaperViewport();
			if (layout.PaperViewport != null)
			{
				// 처음 열었을 때 용지 전체가 보이도록 용지 중앙을 가리킴
				layout.PaperViewport.ViewCenter = new XY(210, 148.5);
				layout.PaperViewport.ViewHeight = 297;
				layout.PaperViewport.SnapSpacing = new XY(10, 10);
				layout.PaperViewport.GridSpacing = new XY(10, 10);
			}

			// 3. 기본 모형 뷰포트 (Id 2): 용지 중앙, 폭·높이 90%
			double vpW = 420 * 0.9;
			double vpH = 297 * 0.9;
			var (modelCx, modelCy, viewH) = CalculateModelView(this._doc, vpW, vpH);

			var modelVp = new Viewport
			{
				Center = new XYZ(210, 148.5, 0),
				Width = vpW,
				Height = vpH,
				ViewCenter = new XY(modelCx, modelCy),
				ViewTarget = XYZ.Zero,
				ViewHeight = viewH,
				// 0이면 AUDIT에서 "Snap X/Y increment 0" 오류
				SnapSpacing = new XY(10, 10),
				GridSpacing = new XY(10, 10),
			};
			layout.AssociatedBlock.Entities.Add(modelVp);

			this._layout = layout;
			this._block = layout.AssociatedBlock;
		}

		if (!this._doc.Layouts.ContainsKey(this._layout!.Name))
		{
			this._doc.Layouts.Add(this._layout);
		}
	}

	public void Undo()
	{
		if (this._layout != null && this._block != null)
		{
			this.RemoveFromDictionary(this._doc, this._layout.Name);
			if (this._doc.BlockRecords.Contains(this._block.Name))
			{
				this._doc.BlockRecords.Remove(this._block.Name);
			}
		}
	}

	private void RemoveFromDictionary(CadDocument doc, string layoutName)
	{
		if (doc.RootDictionary.TryGetEntry<CadDictionary>(CadDictionary.AcadLayout, out var dict))
		{
			dict.Remove(layoutName, out _);
		}
	}

	private static (double Cx, double Cy, double ViewH) CalculateModelView(CadDocument doc, double vpW, double vpH)
	{
		// 화면 표시와 같은 기준(장면 범위)으로 모형 범위를 구한다. 엔티티별 GetBoundingBox는 블록·치수에서 부정확할 수 있음
		Free2DCad.Rendering.Bounds ext = Free2DCad.Rendering.SceneBuilder.Build(doc).Extents;
		if (ext.IsEmpty || ext.Width <= 0 || ext.Height <= 0)
		{
			return (0, 0, vpH);
		}
		double minX = ext.MinX, minY = ext.MinY, maxX = ext.MaxX, maxY = ext.MaxY;

		double mw = maxX - minX;
		double mh = maxY - minY;
		double cx = (minX + maxX) / 2.0;
		double cy = (minY + maxY) / 2.0;

		double vh = (mw / vpW > mh / vpH) ? mw * (vpH / vpW) : mh;
		vh = Math.Max(10.0, vh * 1.1);

		return (cx, cy, vh);
	}
}

/// <summary>
/// 배치 삭제 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class RemoveLayoutAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly Layout _layout;
	private readonly BlockRecord _block;

	public string Name => "배치 삭제";

	public RemoveLayoutAction(CadDocument doc, Layout layout)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
		this._block = layout.AssociatedBlock ?? throw new ArgumentException("레이아웃에 연결된 블록이 없습니다.", nameof(layout));

		if (!this._layout.IsPaperSpace || string.Equals(this._layout.Name, Layout.ModelLayoutName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("모형 공간은 삭제할 수 없습니다.");
		}

		int paperLayoutsCount = this._doc.Layouts.Count(l => l.IsPaperSpace);
		if (paperLayoutsCount <= 1)
		{
			throw new InvalidOperationException("도면에 남은 마지막 배치는 삭제할 수 없습니다.");
		}
	}

	public void Do()
	{
		if (this._doc.RootDictionary.TryGetEntry<CadDictionary>(CadDictionary.AcadLayout, out var dict))
		{
			dict.Remove(this._layout.Name, out _);
		}
		if (this._doc.BlockRecords.Contains(this._block.Name))
		{
			this._doc.BlockRecords.Remove(this._block.Name);
		}
	}

	public void Undo()
	{
		if (!this._doc.Layouts.ContainsKey(this._layout.Name))
		{
			this._doc.Layouts.Add(this._layout);
		}
	}
}

/// <summary>
/// 배치 이름 변경 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class RenameLayoutAction : IUndoableAction
{
	private readonly CadDocument _doc;
	private readonly Layout _layout;
	private readonly string _oldName;
	private readonly string _newName;

	public string Name => "배치 이름 바꾸기";

	public RenameLayoutAction(CadDocument doc, Layout layout, string newName)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));
		this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
		this._oldName = layout.Name;
		this._newName = newName ?? throw new ArgumentNullException(nameof(newName));

		if (string.Equals(this._oldName, Layout.ModelLayoutName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("모형 공간의 이름은 변경할 수 없습니다.");
		}
		if (string.IsNullOrWhiteSpace(newName))
		{
			throw new ArgumentException("새 배치 이름이 비어 있습니다.", nameof(newName));
		}
		if (this._doc.Layouts.ContainsKey(newName))
		{
			throw new InvalidOperationException($"배치 이름 \"{newName}\"이(가) 이미 존재합니다.");
		}
	}

	public void Do()
	{
		this._layout.Name = this._newName;
	}

	public void Undo()
	{
		this._layout.Name = this._oldName;
	}
}

/// <summary>
/// 배치 탭 순서(TabOrder) 변경 및 실행 취소(Undo) 시 복원 액션.
/// </summary>
public sealed class MoveLayoutAction : IUndoableAction
{
	private readonly Layout _layout;
	private readonly int _oldOrder;
	private readonly int _newOrder;

	public string Name => "배치 순서 이동";

	public MoveLayoutAction(Layout layout, int newOrder)
	{
		this._layout = layout ?? throw new ArgumentNullException(nameof(layout));
		this._oldOrder = layout.TabOrder;
		this._newOrder = newOrder;
	}

	public void Do()
	{
		this._layout.TabOrder = this._newOrder;
	}

	public void Undo()
	{
		this._layout.TabOrder = this._oldOrder;
	}
}
