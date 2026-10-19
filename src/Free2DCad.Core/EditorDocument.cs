using ACadSharp;
using ACadSharp.Objects;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;
using System.IO;

namespace Free2DCad.Core;

/// <summary>
/// CAD 도면 문서의 편집 상태 및 되돌리기(Undo/Redo)를 관리하는 에디터 문서.
/// </summary>
public sealed class EditorDocument
{
	static EditorDocument()
	{
		// 치수 문자 배치에 실제 글꼴 크기를 쓴다 (문자와 치수선이 겹치지 않게, 좁은 치수 맞춤)
		Dimension.TextMeasurer ??= (m, doc) => Free2DCad.Rendering.SceneBuilder.MeasureText(m, doc);
	}

	private readonly Stack<IUndoableAction> _undoStack = new();
	private readonly Stack<IUndoableAction> _redoStack = new();
	private List<IUndoableAction>? _currentTransaction;
	private Layer _currentLayer;

	/// <summary>
	/// 원본 CAD 문서.
	/// </summary>
	public CadDocument Document { get; }

	/// <summary>
	/// 파일 경로 (새 도면인 경우 null).
	/// </summary>
	public string? FilePath { get; private set; }

	/// <summary>
	/// 도면 변경 여부.
	/// </summary>
	public bool IsDirty { get; private set; }

	/// <summary>
	/// 현재 작업 레이어 (기본값: "0" 레이어).
	/// </summary>
	public Layer CurrentLayer
	{
		get => this._currentLayer;
		set => this._currentLayer = value ?? throw new ArgumentNullException(nameof(value));
	}

	private BlockRecord _currentSpace;

	/// <summary>
	/// 현재 활성화된 작업 공간 (기본값: 모형 공간 *Model_Space).
	/// </summary>
	public BlockRecord CurrentSpace
	{
		get => this._currentSpace;
		set
		{
			if (this._currentSpace != value)
			{
				this._currentSpace = value ?? throw new ArgumentNullException(nameof(value));
				this.CurrentSpaceChanged?.Invoke(this, EventArgs.Empty);
			}
		}
	}

	/// <summary>
	/// 작업 공간이 모형/배치 등으로 변경될 때 발생하는 이벤트.
	/// </summary>
	public event EventHandler? CurrentSpaceChanged;

	/// <summary>직전 명령이 고른 객체 (객체 선택에서 P = 이전).</summary>
	public List<Entity> PreviousSelection { get; } = new();

	/// <summary>
	/// 도면 내용 또는 편집 상태가 변경될 때 발생하는 이벤트.
	/// </summary>
	public event EventHandler? Changed;

	/// <summary>
	/// 마지막 Changed가 어떤 변경이었는지. Full이면 전체 다시 그리기가 필요, 아니면 Entities만 바뀜(추가·삭제·이동·속성).
	/// </summary>
	public DocumentChange LastChange { get; private set; } = DocumentChange.FullChange;

	private void RaiseChanged(IEnumerable<IUndoableAction> actions)
	{
		this.LastChange = DocumentChange.From(actions);
		this.Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>
	/// 새 엔티티가 추가된 직후 (화면이 전체 장면을 다시 만들기 전에 새 도형만 먼저 보여 주기 위해).
	/// </summary>
	public event Action<IReadOnlyList<Entity>>? EntitiesAdded;

	/// <summary>
	/// 실행 취소(Undo) 가능 여부.
	/// </summary>
	public bool CanUndo => this._undoStack.Count > 0;

	/// <summary>되돌리기 기록 수 (블록 편집기 닫을 때 버리기용 표시).</summary>
	public int UndoCount => this._undoStack.Count;

	/// <summary>
	/// 다시 실행(Redo) 가능 여부.
	/// </summary>
	public bool CanRedo => this._redoStack.Count > 0;

	public EditorDocument(CadDocument doc, string? filePath = null)
	{
		this.Document = doc ?? throw new ArgumentNullException(nameof(doc));
		this.FilePath = filePath;
		Free2DCad.Rendering.XrefLoader.SetHostPath(doc, filePath); // 외부 참조 상대 경로 기준
		this.IsDirty = false;
		this._currentLayer = this.ResolveDefaultLayer(doc);
		this._currentSpace = doc.ModelSpace ?? doc.BlockRecords[BlockRecord.ModelSpaceName];
	}

	/// <summary>
	/// 지정한 파일 경로에서 도면을 연다 (.dwg는 DwgReader, .dxf는 DxfReader).
	/// </summary>
	public static EditorDocument Open(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new ArgumentNullException(nameof(path));
		}

		CadDocument doc;
		if (path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase))
		{
			doc = DxfReader.Read(path);
		}
		else
		{
			doc = DwgReader.Read(path);
		}

		// 도면에 쓰인 해치 패턴을 해치 패턴 목록('사용자' 탭)에 추가 (다른 PC에서 만든 도면의 패턴도 재사용)
		try
		{
			Hatching.HatchPatternLibrary.RegisterFromDocument(doc);
		}
		catch (Exception)
		{
			// 패턴 등록 실패는 도면 열기에 영향 없음
		}

		// 열을 나눠 저장한 여러 줄 문자(R2004): 기존 CAD처럼 첫 문자로 합쳐 한 문자의 열로 다룬다
		try
		{
			Free2DCad.Rendering.MTextColumns.MergeSplit(doc);
		}
		catch (Exception)
		{
			// 합치지 못하면 그대로
		}

		// 맞춤점만 있는 스플라인(DXF 등): 조정점을 계산해 둔다 (기존 CAD처럼 화면에 보이고 편집·스냅 가능)
		foreach (var sp in doc.Entities.OfType<Spline>().Concat(doc.BlockRecords.SelectMany(b => b.Entities.OfType<Spline>())).Distinct())
		{
			if (sp.ControlPoints.Count == 0 && sp.FitPoints.Count >= 2)
			{
				try
				{
					var (ctrl, knots, deg) = Commands.SplineCommand.Interpolate(sp.FitPoints.Select(p => new XY(p.X, p.Y)).ToList());
					sp.Degree = deg;
					foreach (var c in ctrl) sp.ControlPoints.Add(new XYZ(c.X, c.Y, 0));
					sp.Knots.Clear();
					foreach (double k in knots) sp.Knots.Add(k);
				}
				catch (Exception)
				{
					// 계산할 수 없는 곡선은 그대로 둔다
				}
			}
		}

		return new EditorDocument(doc, path);
	}

	/// <summary>
	/// ANSI_949 코드페이지가 지정된 새 도면 문서를 생성한다.
	/// </summary>
	/// <summary>
	/// 템플릿 도면(Templates 폴더의 .dwg·.dwt·.dxf, 버전 무관)으로 새 도면: 도면을 열 때와 같은 처리(해치 패턴 등록·열 문자 합치기·
	/// 맞춤점 스플라인)를 거쳐 도면층·스타일·도곽 등을 그대로 가져오고 파일 경로는 비운다(다른 이름으로 저장).
	/// </summary>
	public static EditorDocument NewFromTemplate(string templatePath)
	{
		var doc = Open(templatePath).Document;
		doc.Header.CodePage = "ANSI_949";
		return new EditorDocument(doc, null);
	}

	public static EditorDocument New()
	{
		// 포함된 템플릿(실무 도면에서 도형과 고유 이름을 비운 파일)을 우선 사용: AUDIT 오류가 없는 기본 객체와 스타일 유지
		CadDocument doc = TemplateBuilder.LoadEmbedded() ?? new CadDocument();
		doc.Header.CodePage = "ANSI_949";
		return new EditorDocument(doc, null);
	}

	/// <summary>
	/// 액션을 실행하고 Undo 스택에 등록한다. 트랜잭션이 활성화된 경우 트랜잭션 내에 누적한다.
	/// </summary>
	public void Execute(IUndoableAction action)
	{
		ArgumentNullException.ThrowIfNull(action);

		action.Do();
		if (this._currentTransaction != null)
		{
			this._currentTransaction.Add(action);
		}
		else
		{
			this._undoStack.Push(action);
			this._redoStack.Clear();
		}
		this.IsDirty = true;
		this.RaiseChanged(new[] { action });
	}

	/// <summary>
	/// 여러 액션을 하나의 CompositeAction으로 묶기 위한 트랜잭션을 시작한다.
	/// </summary>
	public void BeginTransaction()
	{
		this._currentTransaction = new List<IUndoableAction>();
	}

	/// <summary>
	/// 현재 트랜잭션에 누적된 액션들을 하나의 CompositeAction으로 묶어 Undo 스택에 커밋한다.
	/// </summary>
	public void CommitTransaction(string name = "Command")
	{
		if (this._currentTransaction != null)
		{
			var actions = this._currentTransaction;
			this._currentTransaction = null;
			if (actions.Count > 0)
			{
				var composite = new CompositeAction(name, actions);
				this._undoStack.Push(composite);
				this._redoStack.Clear();
				this.IsDirty = true;
				this.RaiseChanged(actions);
			}
		}
	}

	/// <summary>
	/// 명령 안의 "명령 취소(U)": 실행 중인 명령(트랜잭션)의 마지막 변경 하나만 되돌린다. 트랜잭션 밖이면 Undo와 같다.
	/// </summary>
	public bool UndoLastStep()
	{
		if (this._currentTransaction == null)
		{
			bool can = this.CanUndo;
			this.Undo();
			return can;
		}
		if (this._currentTransaction.Count == 0)
		{
			return false;
		}
		var action = this._currentTransaction[^1];
		this._currentTransaction.RemoveAt(this._currentTransaction.Count - 1);
		action.Undo();
		this.RaiseChanged(new[] { action });
		return true;
	}

	/// <summary>
	/// 현재 트랜잭션에 누적된 액션들을 역순으로 취소하고 트랜잭션을 취소한다.
	/// </summary>
	public void RollbackTransaction()
	{
		if (this._currentTransaction != null)
		{
			var actions = this._currentTransaction;
			this._currentTransaction = null;
			for (int i = actions.Count - 1; i >= 0; i--)
			{
				actions[i].Undo();
			}
			this.RaiseChanged(actions);
		}
	}

	/// <summary>
	/// 마지막 작업을 실행 취소(Undo)한다.
	/// </summary>
	public void Undo()
	{
		if (!this.CanUndo)
		{
			return;
		}

		IUndoableAction action = this._undoStack.Pop();
		action.Undo();
		this._redoStack.Push(action);
		this.IsDirty = true;
		this.RaiseChanged(new[] { action });
	}

	/// <summary>
	/// 취소된 마지막 작업을 다시 실행(Redo)한다.
	/// </summary>
	public void Redo()
	{
		if (!this.CanRedo)
		{
			return;
		}

		IUndoableAction action = this._redoStack.Pop();
		action.Do();
		this._undoStack.Push(action);
		this.IsDirty = true;
		this.RaiseChanged(new[] { action });
	}

	/// <summary>
	/// 엔티티들을 모델스페이스에 추가한다. 레이어가 지정되지 않은 엔티티에는 CurrentLayer를 지정한다.
	/// </summary>
	public void AddEntities(params Entity[] entities)
	{
		this.AddEntities((IEnumerable<Entity>)entities);
	}

	/// <summary>
	/// 엔티티들을 모델스페이스에 추가한다. 레이어가 지정되지 않은 엔티티에는 CurrentLayer를 지정한다.
	/// </summary>
	public void AddEntities(IEnumerable<Entity> entities)
	{
		List<Entity> list = entities.ToList();
		for (int i = 0; i < list.Count; i++)
		{
			Entity entity = list[i];
			if (entity.Layer == null ||
				entity.Layer == Layer.Default ||
				(entity.Document == null && entity.Layer.Document == null && entity.Layer.Name == Layer.DefaultName))
			{
				entity.Layer = this.CurrentLayer;
			}
		}

		this.Execute(new AddEntitiesAction(this.CurrentSpace, list));
		this.EntitiesAdded?.Invoke(list);
	}

	/// <summary>
	/// 엔티티들을 도면에서 삭제한다.
	/// </summary>
	/// <summary>
	/// 객체를 다른 객체들로 바꾼다 (자르기·끊기 등): 지운 것과 새로 넣은 것을 한 번의 되돌리기로. 새 객체는 지운 객체가 있던 공간에.
	/// </summary>
	public void Replace(IEnumerable<Entity> remove, IEnumerable<Entity> add, string name)
	{
		var r = remove.ToList();
		var a = add.ToList();
		var space = r.Select(e => e.Owner).OfType<BlockRecord>().FirstOrDefault() ?? this.CurrentSpace;
		var actions = new List<IUndoableAction>();
		if (r.Count > 0) actions.Add(new RemoveEntitiesAction(r, name));
		if (a.Count > 0) actions.Add(new AddEntitiesAction(space, a, name));
		if (actions.Count > 0)
		{
			this.Execute(new CompositeAction(name, actions));
		}
	}

	public void Erase(params Entity[] entities)
	{
		this.Erase((IEnumerable<Entity>)entities);
	}

	/// <summary>
	/// 엔티티들을 도면에서 삭제한다.
	/// </summary>
	public void Erase(IEnumerable<Entity> entities)
	{
		this.Execute(new RemoveEntitiesAction(entities, "Erase"));
	}

	/// <summary>
	/// 엔티티들을 지정한 거리(dx, dy)만큼 이동한다.
	/// </summary>
	public void Move(IEnumerable<Entity> entities, double dx, double dy)
	{
		Transform forward = Transform.CreateTranslation(new XYZ(dx, dy, 0));
		Transform backward = Transform.CreateTranslation(new XYZ(-dx, -dy, 0));
		this.Execute(new TransformEntitiesAction(entities, forward, backward, "Move"));
	}

	/// <summary>
	/// 엔티티들을 복제하여 이동한 뒤 도면에 추가한다. 새로 만든 엔티티 목록을 반환한다.
	/// </summary>
	public IReadOnlyList<Entity> Copy(IEnumerable<Entity> entities, double dx, double dy)
	{
		List<Entity> clones = new();
		Transform moveTransform = Transform.CreateTranslation(new XYZ(dx, dy, 0));

		foreach (Entity e in entities)
		{
			Entity clone = (Entity)e.Clone();
			clone.ApplyTransform(moveTransform);
			clones.Add(clone);
		}

		this.Execute(new AddEntitiesAction(this.CurrentSpace, clones, "Copy"));
		return clones;
	}

	/// <summary>
	/// 엔티티들을 기준점(baseX, baseY)을 중심으로 회전한다 (radians 단위).
	/// </summary>
	public void Rotate(IEnumerable<Entity> entities, double baseX, double baseY, double radians)
	{
		Matrix4 t = Matrix4.CreateTranslation(baseX, baseY, 0);
		Matrix4 tInv = Matrix4.CreateTranslation(-baseX, -baseY, 0);
		Matrix4 r = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, radians);
		Matrix4 rInv = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, -radians);

		Matrix4 forwardMat = t * r * tInv;
		Matrix4 backwardMat = t * rInv * tInv;

		Transform forward = new Transform(forwardMat);
		Transform backward = new Transform(backwardMat);

		this.Execute(new TransformEntitiesAction(entities, forward, backward, "Rotate"));
	}

	/// <summary>
	/// 엔티티들을 기준점(baseX, baseY)을 중심으로 배율(factor)만큼 확대/축소한다.
	/// </summary>
	public void Scale(IEnumerable<Entity> entities, double baseX, double baseY, double factor)
	{
		if (Math.Abs(factor) < 1e-12)
		{
			throw new ArgumentException("확대/축소 배율은 0이 될 수 없습니다.", nameof(factor));
		}

		Matrix4 forwardMat = Matrix4.CreateScale(new XYZ(factor, factor, 1), new XYZ(baseX, baseY, 0));
		Matrix4 backwardMat = Matrix4.CreateScale(new XYZ(1.0 / factor, 1.0 / factor, 1), new XYZ(baseX, baseY, 0));

		Transform forward = new Transform(forwardMat);
		Transform backward = new Transform(backwardMat);

		this.Execute(new TransformEntitiesAction(entities, forward, backward, "Scale"));
	}

	/// <summary>
	/// 두 점(x1, y1)과 (x2, y2)를 잇는 직선을 축으로 대칭 변환한다.
	/// keepOriginal이 true이면 복사 후 대칭하여 새 엔티티 목록을 반환하고, false이면 기존 엔티티를 대칭 이동한다.
	/// </summary>
	public IReadOnlyList<Entity> Mirror(IEnumerable<Entity> entities, double x1, double y1, double x2, double y2, bool keepOriginal = false)
	{
		double dx = x2 - x1;
		double dy = y2 - y1;
		if (Math.Abs(dx) < 1e-12 && Math.Abs(dy) < 1e-12)
		{
			throw new ArgumentException("대칭축의 두 점은 서로 달라야 합니다.");
		}

		double theta = Math.Atan2(dy, dx);
		Matrix4 t = Matrix4.CreateTranslation(x1, y1, 0);
		Matrix4 tInv = Matrix4.CreateTranslation(-x1, -y1, 0);
		Matrix4 r = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, theta);
		Matrix4 rInv = Matrix4.CreateFromAxisAngle(XYZ.AxisZ, -theta);
		Matrix4 scale = Matrix4.CreateScalingMatrix(1, -1, 1);

		Matrix4 mirrorMat = t * r * scale * rInv * tInv;
		Transform mirrorTransform = new Transform(mirrorMat);

		if (keepOriginal)
		{
			List<Entity> clones = new();
			foreach (Entity e in entities)
			{
				Entity clone = (Entity)e.Clone();
				clone.ApplyTransform(mirrorTransform);
				clones.Add(clone);
			}

			this.Execute(new AddEntitiesAction(this.CurrentSpace, clones, "Mirror Copy"));
			return clones;
		}
		else
		{
			List<Entity> list = entities.ToList();
			this.Execute(new TransformEntitiesAction(list, mirrorTransform, mirrorTransform, "Mirror"));
			return list;
		}
	}

	/// <summary>
	/// 엔티티들의 레이어를 변경한다.
	/// </summary>
	public void SetLayer(IEnumerable<Entity> entities, Layer layer)
	{
		this.Execute(new ChangeLayerAction(entities, layer));
	}

	/// <summary>
	/// 엔티티들의 색상을 변경한다.
	/// </summary>
	public void SetColor(IEnumerable<Entity> entities, Color color)
	{
		this.Execute(new ChangeColorAction(entities, color));
	}

	/// <summary>
	/// 레이어의 On/Off 상태를 토글 또는 설정한다.
	/// </summary>
	public void SetLayerOn(Layer layer, bool? isOn = null)
	{
		this.Execute(new SetLayerOnAction(layer, isOn));
	}

	/// <summary>
	/// 텍스트 엔티티의 문자열 내용을 변경한다.
	/// </summary>
	public void ChangeText(Entity entity, string newText)
	{
		this.Execute(new ChangeTextAction(entity, newText));
	}

	/// <summary>
	/// 새 도면층을 추가한다 (실행 취소 가능).
	/// </summary>
	public Layer AddLayer(string name, Color? color = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("도면층 이름이 비어 있습니다.", nameof(name));
		}
		if (this.Document.Layers.TryGetValue(name, out var existing))
		{
			return existing;
		}

		var layer = new Layer(name) { Color = color ?? new Color(7) };
		this.Execute(new AddLayerAction(this.Document, layer));
		return layer;
	}

	/// <summary>
	/// 도면층 이름을 변경한다 (실행 취소 가능).
	/// </summary>
	public void RenameLayer(Layer layer, string newName)
	{
		this.Execute(new RenameLayerAction(layer, newName));
	}

	/// <summary>
	/// 도면층 색상을 변경한다 (실행 취소 가능).
	/// </summary>
	public void SetLayerColor(Layer layer, Color newColor)
	{
		this.Execute(new ChangeLayerColorAction(layer, newColor));
	}

	/// <summary>
	/// 도면층 동결/해제 상태를 변경한다 (실행 취소 가능).
	/// </summary>
	public void SetLayerFrozen(Layer layer, bool? isFrozen = null)
	{
		this.Execute(new ToggleLayerFrozenAction(layer, isFrozen));
	}

	/// <summary>
	/// 도면층 잠금/해제 상태를 변경한다 (실행 취소 가능).
	/// </summary>
	public void SetLayerLocked(Layer layer, bool? isLocked = null)
	{
		this.Execute(new ToggleLayerLockedAction(layer, isLocked));
	}

	/// <summary>
	/// 도면층을 삭제할 수 있는지 확인한다 (0 레이어, 현재 레이어, 사용 중인 레이어는 삭제 불가).
	/// </summary>
	public bool CanRemoveLayer(Layer layer)
	{
		if (layer == null) return false;
		if (string.Equals(layer.Name, "0", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(layer.Name, Layer.DefaultName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (layer == this.CurrentLayer || string.Equals(layer.Name, this.CurrentLayer?.Name, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		foreach (BlockRecord block in this.Document.BlockRecords)
		{
			foreach (Entity entity in block.Entities)
			{
				if (entity.Layer == layer || string.Equals(entity.Layer?.Name, layer.Name, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
		}

		return true;
	}

	/// <summary>
	/// 도면층을 도면에서 삭제한다 (실행 취소 가능).
	/// </summary>
	public void RemoveLayer(Layer layer)
	{
		if (!this.CanRemoveLayer(layer))
		{
			throw new InvalidOperationException($"도면층 \"{layer.Name}\"은(는) 삭제할 수 없습니다. (0 도면층, 현재 도면층 또는 객체가 포함된 도면층)");
		}

		this.Execute(new RemoveLayerAction(this.Document, layer));
	}

	/// <summary>
	/// 새 배치(Layout)를 추가한다 (실행 취소 가능).
	/// </summary>
	public Layout AddLayout(string name)
	{
		var action = new AddLayoutAction(this.Document, name);
		this.Execute(action);
		return action.CreatedLayout!;
	}

	/// <summary>
	/// 배치를 삭제한다 (실행 취소 가능).
	/// </summary>
	public void RemoveLayout(Layout layout)
	{
		this.Execute(new RemoveLayoutAction(this.Document, layout));
	}

	/// <summary>
	/// 배치 이름을 변경한다 (실행 취소 가능).
	/// </summary>
	public void RenameLayout(Layout layout, string newName)
	{
		this.Execute(new RenameLayoutAction(this.Document, layout, newName));
	}

	/// <summary>
	/// 배치 순서(TabOrder)를 변경한다 (실행 취소 가능).
	/// </summary>
	public void MoveLayout(Layout layout, int newOrder)
	{
		this.Execute(new MoveLayoutAction(layout, newOrder));
	}

	/// <summary>
	/// 저장할 파일 버전 (기본 AC1018 = 2004, 국내 협력사 호환). 다른 이름으로 저장에서 고른 버전이 이후 저장에도 쓰인다.
	/// DWG는 R14·2000·2004·2010·2013·2018, DXF는 R2000~2018 (DWG 2007은 쓰기 미지원).
	/// </summary>
	public ACadVersion SaveVersion { get; set; } = ACadVersion.AC1018;

	/// <summary>
	/// 도면을 파일로 저장한다. 확장자가 .dxf이면 DxfWriter, 그 외(.dwg, .dwt)에는 DwgWriter로 저장한다.
	/// version을 주면 그 버전으로 (이후 저장 기본값도 바뀜). 대상 파일이 이미 있으면 같은 이름의 .bak 파일로 복사한다.
	/// </summary>
	public void Save(string? path = null, ACadVersion? version = null)
	{
		string targetPath = path ?? this.FilePath ?? throw new InvalidOperationException("저장할 파일 경로가 지정되지 않았습니다.");

		if (File.Exists(targetPath))
		{
			string bakPath = Path.ChangeExtension(targetPath, ".bak");
			File.Copy(targetPath, bakPath, overwrite: true);
		}

		if (version is { } v)
		{
			this.SaveVersion = v;
		}
		bool isDxf = string.Equals(Path.GetExtension(targetPath), ".dxf", StringComparison.OrdinalIgnoreCase);
		if (!isDxf && this.SaveVersion == ACadVersion.AC1021)
		{
			this.SaveVersion = ACadVersion.AC1018; // DWG 2007은 쓰기 미지원 → 2004
		}
		this.Document.Header.Version = this.SaveVersion;
		// 지운 객체가 그룹에 남아 있으면 없는 핸들을 쓰게 된다 → 그룹에서 빼고, 빈 그룹은 지운다
		foreach (var g in this.Document.Groups.ToList())
		{
			foreach (var e in g.Entities.Where(e => e.Document != this.Document).ToList())
			{
				g.Remove(e);
			}
			if (!g.Entities.Any())
			{
				this.Document.Groups.Remove(g.Name);
			}
		}
		// 현재 선가중치(CELWEIGHT)가 표준 값이 아니면(예: 29) 기존 CAD가 헤더 오류로 본다 → 도면층별
		if (!ValidLineWeights.Contains((short)this.Document.Header.CurrentEntityLineWeight))
		{
			this.Document.Header.CurrentEntityLineWeight = LineWeightType.ByLayer;
		}
		if (this.SaveVersion >= ACadVersion.AC1021)
		{
			// 2007 이상에만 있는 시스템 변수: 2004 이하 도면을 올려 저장하면 0으로 남아 기존 CAD AUDIT에서 오류 (범위 밖)
			var h = this.Document.Header;
			if (!(h.SweptSolidWidth > 0)) h.SweptSolidWidth = 5;
			if (!(h.SweptSolidHeight > 0)) h.SweptSolidHeight = 80;
			if (!(h.LensLength > 0)) h.LensLength = 50;
			if (!(h.Dw3DPrecision >= 1 && h.Dw3DPrecision <= 6)) h.Dw3DPrecision = 2;
		}

		string codePage = this.Document.Header.CodePage;
		if (string.IsNullOrEmpty(codePage) ||
			(string.Equals(codePage, "ANSI_1252", StringComparison.OrdinalIgnoreCase) && this.HasKoreanText()))
		{
			this.Document.Header.CodePage = "ANSI_949";
		}

		string ext = Path.GetExtension(targetPath);
		if (string.Equals(ext, ".dxf", StringComparison.OrdinalIgnoreCase))
		{
			DxfWriter.Write(targetPath, this.Document);
		}
		else
		{
			DwgWriter.Write(targetPath, this.Document);
		}

		this.FilePath = targetPath;
		Free2DCad.Rendering.XrefLoader.SetHostPath(this.Document, targetPath);
		this.IsDirty = false;
	}

	private static readonly HashSet<short> ValidLineWeights = new()
	{
		-3, -2, -1, 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211,
	};

	private bool HasKoreanText()
	{
		foreach (BlockRecord blockRecord in this.Document.BlockRecords)
		{
			foreach (Entity e in blockRecord.Entities)
			{
				if (CheckEntityKorean(e))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool CheckEntityKorean(Entity e)
	{
		switch (e)
		{
			case TextEntity text: // AttributeEntity, AttributeDefinition 포함
				if (ContainsKorean(text.Value)) return true;
				break;
			case MText mText:
				if (ContainsKorean(mText.Value)) return true;
				break;
			case Dimension dim:
				if (ContainsKorean(dim.Text)) return true;
				break;
		}
		return false;
	}

	private static bool ContainsKorean(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if ((c >= 0xAC00 && c <= 0xD7AF) ||
				(c >= 0x1100 && c <= 0x11FF) ||
				(c >= 0x3130 && c <= 0x318F))
			{
				return true;
			}
		}
		return false;
	}

	private Layer ResolveDefaultLayer(CadDocument doc)
	{
		if (doc.Layers.TryGetValue("0", out Layer? layer0))
		{
			return layer0;
		}
		if (doc.Layers.Any())
		{
			return doc.Layers.First();
		}

		Layer fallback = new Layer("0");
		doc.Layers.Add(fallback);
		return fallback;
	}
}

/// <summary>문서 변경 내용: 전체(Full) 또는 특정 엔티티들.</summary>
public sealed record DocumentChange(bool Full, IReadOnlyCollection<Entity> Entities)
{
	public static readonly DocumentChange FullChange = new(true, Array.Empty<Entity>());

	public static DocumentChange From(IEnumerable<IUndoableAction> actions)
	{
		var set = new HashSet<Entity>();
		bool full = false;
		void Visit(IUndoableAction a)
		{
			switch (a)
			{
				case CompositeAction c:
					foreach (var child in c.Actions) Visit(child);
					break;
				case IEntityChange ec:
					foreach (var e in ec.AffectedEntities) set.Add(e);
					break;
				default:
					full = true;
					break;
			}
		}
		foreach (var a in actions) Visit(a);
		return full ? FullChange : new DocumentChange(false, set);
	}
}
