using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ACadSharp.Entities;
using Free2DCad.Core;
using Free2DCad.Core.Commands;
using Free2DCad.Rendering;
using SkiaSharp;
using Point = Avalonia.Point;

namespace Free2DCad.App;

/// <summary>
/// 도면 뷰포트. 휠: 확대/축소, 가운데 버튼: 이동/전체보기, 왼쪽 버튼: 선택 및 드래그 사각형, 명령 러버밴드 및 오버레이 렌더링.
/// </summary>
public sealed class DrawingCanvas : Control
{
	private SkiaSceneRenderer? _renderer;
	private Scene? _scene;
	private SceneIndex? _sceneIndex;
	private ViewTransform? _view;
	private Point? _panStart;

	private bool _isDraggingBox;
	private (double X, double Y)? _dragStartWorld;
	private (double X, double Y) _currentWorld;
	private SnapPoint? _currentSnap;

	public EditorDocument? Document { get; set; }

	public bool ObjectSnapEnabled { get; set; } = true;

	public bool OrthoEnabled { get; set; } = false;

	public bool GridEnabled { get; set; } = true;

	public bool SnapEnabled { get; set; } = false;

	public double SnapSpacingX { get; set; } = 10.0;

	public double SnapSpacingY { get; set; } = 10.0;

	public double GridSpacingX { get; set; } = 10.0;

	public double GridSpacingY { get; set; } = 10.0;

	public int GridMajorEvery { get; set; } = 5;

	public SnapModes SnapModes { get; set; } = SnapModes.Default;

	public HashSet<Entity> SelectedEntities { get; } = new();

	// 그립 편집 상태: 선택한 객체의 파란 점을 클릭하면 빨간(활성) 점이 되고, 다음 클릭(또는 끌기)으로 반영
	private Entity? _gripEntity;
	private Free2DCad.Core.Editing.Grip? _hotGrip;
	private Point _gripPressPos;
	private bool _gripDragged;
	private const int MaxGripEntities = 200;
	private const double GripHitPx = 7.0;

	/// <summary>그립 편집 중인지 (Esc로 취소).</summary>
	public bool IsGripEditing => this._hotGrip != null;

	private IEnumerable<(Entity Entity, Free2DCad.Core.Editing.Grip Grip)> CurrentGrips() =>
		this.SelectedEntities.Take(MaxGripEntities)
			.SelectMany(en => Free2DCad.Core.Editing.GripEditor.GetGrips(en).Select(g => (en, g)));

	private (Entity Entity, Free2DCad.Core.Editing.Grip Grip)? HitGrip(Point screen)
	{
		if (this._view == null || this.ActiveCommand != null)
		{
			return null;
		}
		(Entity, Free2DCad.Core.Editing.Grip)? best = null;
		double bestD = GripHitPx;
		foreach (var (en, g) in this.CurrentGrips())
		{
			var (sx, sy) = this._view.ToScreen(g.X, g.Y);
			double d = Math.Max(Math.Abs(sx - screen.X), Math.Abs(sy - screen.Y));
			if (d <= bestD)
			{
				bestD = d;
				best = (en, g);
			}
		}
		return best;
	}

	/// <summary>그립이 갈 자리: 객체 스냅 > 그리드 스냅(이미 반영) > 직교(그립 원래 위치 기준).</summary>
	private (double X, double Y) GripTarget()
	{
		double x = this._currentSnap.HasValue ? this._currentSnap.Value.X : (this._tracked?.X ?? this._currentWorld.X);
		double y = this._currentSnap.HasValue ? this._currentSnap.Value.Y : (this._tracked?.Y ?? this._currentWorld.Y);
		if (this.OrthoEnabled && !this._currentSnap.HasValue && this._hotGrip is { } g)
		{
			var o = CoordinateParser.ApplyOrtho((g.X, g.Y), (x, y));
			x = o.X;
			y = o.Y;
		}
		return (x, y);
	}

	private void CommitGrip()
	{
		if (this._hotGrip is { } g && this._gripEntity != null && this.Document != null)
		{
			var (x, y) = this.GripTarget();
			try
			{
				var action = Free2DCad.Core.Editing.GripEditor.CreateEdit(this._gripEntity, g, x, y);
				if (action != null)
				{
					this.Document.Execute(action);
				}
			}
			catch (Exception ex)
			{
				CrashLog.Write("그립 편집 실패", ex);
			}
		}
		this.CancelGrip();
		this.SelectionChanged?.Invoke(); // 속성 팔레트 값 갱신
	}

	/// <summary>그립 편집 취소 (Esc).</summary>
	public void CancelGrip()
	{
		this._hotGrip = null;
		this._gripEntity = null;
		this._gripDragged = false;
		this.InvalidateVisual();
	}

	public SnapPoint? CurrentSnap => this._currentSnap;

	/// <summary>명령이 객체 하나를 클릭으로 고르는 단계인지 (이때는 객체 스냅 안 함).</summary>
	private bool CommandPickingObject => this.ActiveCommand is { IsFinished: false, PickingObject: true };


	public SceneIndex? SceneIndex => this._sceneIndex;

	/// <summary>실행 중인 명령. 바뀌면(시작·끝·취소) 객체 스냅 추적으로 획득한 점(초록 +)을 지운다 — 기존 CAD처럼 명령 안에서만 남는다.</summary>
	public EditorCommand? ActiveCommand
	{
		get => this._activeCommand;
		set
		{
			if (!ReferenceEquals(this._activeCommand, value))
			{
				this._activeCommand = value;
				this.ClearTracking();
				this._currentSnap = null;
			}
		}
	}

	private EditorCommand? _activeCommand;

	/// <summary>
	/// 객체 스냅·추적을 쓰는 때: 명령이 점을 받는 중이거나 그립을 끄는 중. 명령 없이 커서만 움직일 때는 스냅 표시·추적점 획득을 하지 않는다
	/// (전에는 빈 상태에서도 스냅점에 머물면 추적점(초록 +)이 쌓여 객체를 지운 뒤에도 남았다).
	/// </summary>
	private bool SnapWanted => this._hotGrip != null || (this.ActiveCommand is { IsFinished: false } && !this.CommandPickingObject);

	public CommandContext? CommandContext { get; set; }

	private Point? _cursorScreenPos;
	private TaskCompletionSource<Bounds?>? _windowPickTcs;
	private (double X, double Y)? _windowPickFirst;

	public Task<Bounds?> PickWindowAsync()
	{
		this._windowPickTcs?.TrySetResult(null);
		this._windowPickTcs = new TaskCompletionSource<Bounds?>();
		this._windowPickFirst = null;
		this.InvalidateVisual();
		return this._windowPickTcs.Task;
	}

	public void CancelPickWindow()
	{
		this._windowPickFirst = null;
		this._windowPickTcs?.TrySetResult(null);
		this._windowPickTcs = null;
		this.InvalidateVisual();
	}

	public event Action<double, double>? CursorWorldChanged;

	/// <summary>마지막 커서 위치 (도면 좌표, 스냅·추적 반영). 숫자만 입력하면 이 방향으로 거리를 잰다.</summary>
	public (double X, double Y)? CursorWorld { get; private set; }

	public event Action? SelectionChanged;

	/// <summary>명령에 점(클릭)이나 오른쪽 클릭 Enter가 들어가기 직전 (명령줄 기록에 프롬프트를 남기기 위해).</summary>
	public event Action? BeforeCommandInput;

	/// <summary>명령이 없을 때 객체를 더블클릭 (치수·문자 값 편집).</summary>
	public event Action<Entity>? EntityDoubleClicked;

	public event Action? ViewChanged;

	public event Action<bool>? ObjectSnapToggled;

	public event Action<bool>? OrthoToggled;

	public event Action<bool>? GridToggled;
	public event Action<bool>? SnapToggled;

	public event Action<string>? CommandPromptChanged;

	public event Action? CommandFinished;

	public bool PickAdd { get; set; } = true;

	public event Action<string>? TextInputFromCanvas;

	public event Action<bool>? EnterOrSpacePressed;

	public event Action? EscapePressed;

	public event Action? BackspacePressed;

	/// <summary>자동완성 목록용 키(위/아래/Tab)를 명령줄에 넘긴다. 명령줄이 처리했으면 true.</summary>
	public Func<Key, bool>? CommandLineKey { get; set; }

	public event Action<string>? CharacterTyped;

	public event Action? RightClickEnterOrRepeat;

	public DrawingCanvas()
	{
		this.ClipToBounds = true;
		this.Focusable = true;
		this.Cursor = new Cursor(StandardCursorType.None);
	}

	private LayoutRenderer? _layoutRenderer;

	public LayoutRenderer? LayoutRenderer => this._layoutRenderer;

	public ViewTransform? View
	{
		get => this._view;
		set
		{
			this._view = value;
			this.InvalidateVisual();
			this.ViewChanged?.Invoke();
		}
	}

	/// <summary>
	/// 장면 지정. renderer를 주면(백그라운드에서 미리 만든 것) 그대로 쓰고 해제는 만든 쪽이 맡는다.
	/// </summary>
	private bool _ownsRenderer = true;

	// 전체 장면을 다시 만드는 동안(큰 도면에서 약 1초) 바뀐 도형을 즉시 보여 주는 임시 층 (UI 스레드에서만 바꿈)
	// _eraser: 지우거나 옮긴 도형의 이전 모양을 배경색으로 덮음, _fresh: 추가·이동·변경된 도형의 새 모양
	private SkiaSceneRenderer? _eraser;
	private SkiaSceneRenderer? _fresh;

	/// <summary>지금 화면에 쓰는 장면 (임시 층을 만들 때 이전 모양을 찾는 데 씀).</summary>
	public Scene? CurrentScene => this._scene;

	/// <summary>임시 층 교체. 이전 렌더러들을 돌려준다 (해제는 부른 쪽이 늦게).</summary>
	public (SkiaSceneRenderer? Eraser, SkiaSceneRenderer? Fresh) SetQuickOverlay(SkiaSceneRenderer? eraser, SkiaSceneRenderer? fresh)
	{
		var old = (this._eraser, this._fresh);
		this._eraser = eraser;
		this._fresh = fresh;
		this.InvalidateVisual();
		return old;
	}

	public void SetScene(Scene scene, bool keepView = false, SkiaSceneRenderer? renderer = null)
	{
		this._layoutRenderer?.Dispose();
		this._layoutRenderer = null;
		if (this._ownsRenderer)
		{
			this._renderer?.Dispose();
		}
		this._scene = scene;
		this._ownsRenderer = renderer == null;
		this._renderer = renderer ?? new SkiaSceneRenderer(scene)
		{
			Background = SKColors.Black
		};
		if (!keepView)
		{
			int w = (int)this.Bounds.Width;
			int h = (int)this.Bounds.Height;
			// 캔버스가 아직 안 보이면(시작 페이지 뒤) 크기가 거의 0이라 맞추지 않고 처음 그릴 때 맞춘다
			if (w > 20 && h > 20)
			{
				this._view = ViewTransform.Fit(this.FitBounds(scene.Extents), w, h);
				this.ViewChanged?.Invoke();
			}
			else
			{
				this._view = null; // 다음 렌더에서 전체 보기
			}
		}
		// 이미지 캐시는 렌더 스레드 전용: 여기서 해제하면 그리는 중인 이미지를 지워 앱이 종료될 수 있다.
		// 렌더러가 바뀌면 캐시 키가 달라져 렌더 스레드에서 새로 만든다
		this.InvalidateVisual();
	}

	public void SetLayout(LayoutRenderer layoutRenderer, bool keepView = false)
	{
		this._layoutRenderer?.Dispose();
		this._layoutRenderer = layoutRenderer;
		if (this._ownsRenderer)
		{
			this._renderer?.Dispose();
		}
		this._renderer = null;
		this._ownsRenderer = false;
		this._scene = layoutRenderer.Layout.PaperScene;
		if (!keepView)
		{
			int w = (int)this.Bounds.Width;
			int h = (int)this.Bounds.Height;
			if (w > 20 && h > 20)
			{
				this._view = ViewTransform.Fit(layoutRenderer.Layout.Extents, w, h);
				this.ViewChanged?.Invoke();
			}
			else
			{
				this._view = null;
			}
		}
		// 이미지 캐시는 렌더 스레드 전용: 여기서 해제하면 그리는 중인 이미지를 지워 앱이 종료될 수 있다.
		// 렌더러가 바뀌면 캐시 키가 달라져 렌더 스레드에서 새로 만든다
		this.InvalidateVisual();
	}

	public void SetSceneIndex(SceneIndex index)
	{
		this._sceneIndex = index;
		// 도면이 바뀌면(지우기·되돌리기 등) 더 이상 객체 위에 없는 추적점은 버린다
		if (this._acquired.Count > 0 && this._view is { } v)
		{
			double tol = 0.5 / v.Scale;
			this._acquired.RemoveAll(a => SnapEngine.Find(index, a.X, a.Y, tol, this.SnapModes) is not { } s
				|| Math.Abs(s.X - a.X) > tol || Math.Abs(s.Y - a.Y) > tol);
			if (this._acquired.Count == 0)
			{
				this._guides.Clear();
				this._guideLabel = null;
				this._tracked = null;
			}
		}
	}

	/// <summary>그룹 선택 (PICKSTYLE): 켜져 있으면 그룹 구성원 하나를 고를 때 그룹 전체가 선택된다.</summary>
	public bool GroupSelection { get; set; } = true;

	/// <summary>고른 객체 + (그룹 선택이 켜져 있으면) 같은 그룹의 구성원.</summary>
	private IReadOnlyList<Entity> WithGroup(Entity picked)
	{
		if (!this.GroupSelection || picked.Document is not { } doc)
		{
			return new[] { picked };
		}
		var list = new List<Entity> { picked };
		foreach (var g in doc.Groups)
		{
			if (g.Selectable && g.Entities.Contains(picked))
			{
				list.AddRange(g.Entities.Where(m => m.Document == doc && m.Owner == picked.Owner && !list.Contains(m)));
			}
		}
		return list;
	}

	public void ZoomExtents()
	{
		var ext = this._layoutRenderer != null ? this._layoutRenderer.Layout.Extents : this._scene?.Extents;
		if (ext != null && this.Bounds.Width > 20 && this.Bounds.Height > 20)
		{
			this._view = ViewTransform.Fit(this._layoutRenderer != null ? ext.Value : this.FitBounds(ext.Value), (int)this.Bounds.Width, (int)this.Bounds.Height);
			this.InvalidateVisual();
			this.ViewChanged?.Invoke();
		}
	}

	/// <summary>
	/// 전체 보기 범위. 빈 도면은 도면 한계(LIMMIN~LIMMAX, 새 도면 = A3 420×297)를 화면에 맞춘다 —
	/// 전에는 빈 도면이 화면 1픽셀 = 1mm(폭 약 1700mm)로 열려, 마우스로 그린 선이 매우 크고 문자·치수는 작게 보였다.
	/// </summary>
	private Bounds FitBounds(Bounds ext)
	{
		if (!ext.IsEmpty && (ext.Width > 1e-9 || ext.Height > 1e-9))
		{
			return ext;
		}
		if (this.Document?.Document.Header is { } h)
		{
			var a = h.ModelSpaceLimitsMin;
			var b = h.ModelSpaceLimitsMax;
			if (b.X - a.X > 1e-6 && b.Y - a.Y > 1e-6)
			{
				return new Bounds(a.X, a.Y, b.X, b.Y);
			}
		}
		return new Bounds(0, 0, 420, 297);
	}

	public void ClearSelection()
	{
		if (this.SelectedEntities.Count > 0)
		{
			this.SelectedEntities.Clear();
			this.SelectionChanged?.Invoke();
			this.InvalidateVisual();
		}
	}

	public void SelectAll()
	{
		if (this.Document != null)
		{
			this.SelectedEntities.Clear();
			foreach (var e in this.Document.CurrentSpace.Entities)
			{
				this.SelectedEntities.Add(e);
			}
			this.SelectionChanged?.Invoke();
			this.InvalidateVisual();
		}
	}

	public override void Render(DrawingContext context)
	{
		if ((this._renderer == null && this._layoutRenderer == null) || this._scene == null)
		{
			context.FillRectangle(Brushes.Black, new Rect(this.Bounds.Size));
			return;
		}

		// 아직 크기가 정해지지 않은 캔버스(시작 페이지 뒤에서 처음 보일 때)는 보기를 맞추지 않는다 —
		// 1×1 크기로 맞춰 버리면 빈 도면이 아주 멀리 축소된 채로 남았다
		if (this.Bounds.Width < 20 || this.Bounds.Height < 20)
		{
			context.FillRectangle(Brushes.Black, new Rect(this.Bounds.Size));
			return;
		}
		int w = (int)this.Bounds.Width;
		int h = (int)this.Bounds.Height;
		bool viewWasNull = this._view == null;
		if (this._layoutRenderer != null)
		{
			this._view ??= ViewTransform.Fit(this._layoutRenderer.Layout.Extents, w, h);
		}
		else
		{
			this._view ??= ViewTransform.Fit(this.FitBounds(this._scene.Extents), w, h);
		}
		this._view.Width = w;
		this._view.Height = h;
		if (viewWasNull)
		{
			// Render 안에서 다른 컨트롤 속성을 바꾸면 예외가 나므로 렌더가 끝난 뒤에 알린다
			Avalonia.Threading.Dispatcher.UIThread.Post(() => this.ViewChanged?.Invoke());
		}

		var snapshot = new ViewTransform(this._view.CenterX, this._view.CenterY, this._view.Scale, w, h);

		// 커서·선택·명령 미리보기 오버레이는 UI 스레드에서 그림 기록(SKPicture)으로 만들어 넘긴다.
		// 렌더 스레드에서 명령·선택 상태를 직접 읽으면, 그 사이 UI 스레드가 명령을 끝내거나(Space/Enter)
		// 도면을 바꿀 때 서로 엇갈려 앱이 종료됐다
		SKPicture? overlay = null;
		try
		{
			using var recorder = new SKPictureRecorder();
			var rc = recorder.BeginRecording(new SKRect(0, 0, w, h));
			this.DrawOverlay(rc, snapshot, includeGrid: this._layoutRenderer != null);
			overlay = recorder.EndRecording();
		}
		catch (Exception ex)
		{
			CrashLog.Write("오버레이 그리기 예외(무시)", ex);
		}
		SKPicture? grid = null;
		if (this.GridEnabled && this._layoutRenderer == null)
		{
			try
			{
				using var gr = new SKPictureRecorder();
				var gc = gr.BeginRecording(new SKRect(0, 0, w, h));
				this.DrawGrid(gc, snapshot);
				grid = gr.EndRecording();
			}
			catch (Exception)
			{
				grid = null;
			}
		}
		var quick = new[] { this._eraser, this._fresh }.Where(r => r != null).Cast<SkiaSceneRenderer>().ToArray();
		context.Custom(new SceneDrawOperation(this, new Rect(0, 0, w, h), this._renderer, this._layoutRenderer, snapshot, overlay, quick, grid));
	}

	protected override void OnPointerEntered(PointerEventArgs e)
	{
		base.OnPointerEntered(e);
		this._cursorScreenPos = e.GetPosition(this);
		this.InvalidateVisual();
	}

	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);
		this._cursorScreenPos = null;
		this._hoverEntity = null;
		this.InvalidateVisual();
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		if (!string.IsNullOrEmpty(e.Text))
		{
			this.CharacterTyped?.Invoke(e.Text);
			this.TextInputFromCanvas?.Invoke(e.Text);
			e.Handled = true;
		}
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		if (this._view == null) return;
		Point p = e.GetPosition(this);
		this._view.ZoomAt(p.X, p.Y, Math.Pow(1.25, e.Delta.Y));
		this.InvalidateVisual();
		this.ViewChanged?.Invoke();
		e.Handled = true;
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		this.Focus();

		var point = e.GetCurrentPoint(this);
		var props = point.Properties;
		Point p = point.Position;

		if (props.IsMiddleButtonPressed)
		{
			if (e.ClickCount == 2)
			{
				this.ZoomExtents();
				return;
			}
			this._panStart = p;
			e.Pointer.Capture(this);
			e.Handled = true;
			return;
		}

		// 마우스 오른쪽 클릭 = Enter (명령 중일 때) 또는 Enter/반복 (명령 없을 때)
		if (props.IsRightButtonPressed)
		{
			// 문자 바로 편집 중: 편집 메뉴 (맞춤법 추천 등, 기존 CAD와 같음)
			if (this._inplaceCaret != null && this._view != null && this.InplaceRightClick is { } rc)
			{
				var (rwx, rwy) = this._view.ToWorld(p.X, p.Y);
				if (rc(rwx, rwy, p))
				{
					e.Handled = true;
					return;
				}
			}
			if (this.ActiveCommand != null && !this.ActiveCommand.IsFinished)
			{
				this.BeforeCommandInput?.Invoke();
				this.ActiveCommand.OnEnter();
				this.CommandPromptChanged?.Invoke(this.ActiveCommand.Prompt);
				if (this.ActiveCommand.IsFinished)
				{
					this.ActiveCommand = null;
					this.CommandFinished?.Invoke();
				}
				this.InvalidateVisual();
				e.Handled = true;
				return;
			}
			else
			{
				this.RightClickEnterOrRepeat?.Invoke();
				e.Handled = true;
				return;
			}
		}

		if (props.IsLeftButtonPressed && this._view != null)
		{
			this.UpdatePointerState(p);
			var (wx, wy) = this._view.ToWorld(p.X, p.Y);

			// 플롯 윈도우 선택 모드 처리
			if (this._windowPickTcs != null)
			{
				if (this._windowPickFirst == null)
				{
					this._windowPickFirst = (wx, wy);
				}
				else
				{
					var pt1 = this._windowPickFirst.Value;
					(double X, double Y) pt2 = (wx, wy);
					double minX = Math.Min(pt1.X, pt2.X);
					double maxX = Math.Max(pt1.X, pt2.X);
					double minY = Math.Min(pt1.Y, pt2.Y);
					double maxY = Math.Max(pt1.Y, pt2.Y);
					var bounds = new Bounds(minX, minY, maxX, maxY);
					var tcs = this._windowPickTcs;
					this._windowPickTcs = null;
					this._windowPickFirst = null;
					this.InvalidateVisual();
					tcs.TrySetResult(bounds);
				}
				e.Handled = true;
				return;
			}

			// 명령이 객체를 고르는 단계면 ("객체 선택:") 클릭 = 객체 고르기, 빈 곳 끌기 = 창/크로싱 선택
			if (this.ActiveCommand is { IsFinished: false, WantsSelection: true } selCmd)
			{
				var pickedSel = this._sceneIndex?.Pick(wx, wy, this.PickBoxPx / this._view.Scale);
				if (pickedSel != null)
				{
					var members = this.WithGroup(pickedSel);
					if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
					{
						this.SelectedEntities.ExceptWith(members);
					}
					else
					{
						this.SelectedEntities.UnionWith(members);
						selCmd.OnSelection(members);
					}
					this.SelectionChanged?.Invoke();
					this.CommandPromptChanged?.Invoke(selCmd.Prompt);
					if (selCmd.IsFinished)
					{
						this.ActiveCommand = null;
						this.CommandFinished?.Invoke();
					}
				}
				else
				{
					this._isDraggingBox = true;
					this._dragStartWorld = (wx, wy);
					this._currentWorld = (wx, wy);
					e.Pointer.Capture(this);
				}
				this.InvalidateVisual();
				e.Handled = true;
				return;
			}

			// 실행 중인 명령이 있는 경우 명령에 점 전달
			if (this.ActiveCommand != null && !this.ActiveCommand.IsFinished)
			{
				double px = this._currentSnap.HasValue ? this._currentSnap.Value.X : (this._tracked?.X ?? wx);
				double py = this._currentSnap.HasValue ? this._currentSnap.Value.Y : (this._tracked?.Y ?? wy);
				if (this.SnapEnabled && !this._currentSnap.HasValue && this._tracked == null && this.SnapSpacingX > 1e-6 && this.SnapSpacingY > 1e-6)
				{
					px = Math.Round(px / this.SnapSpacingX) * this.SnapSpacingX;
					py = Math.Round(py / this.SnapSpacingY) * this.SnapSpacingY;
				}

				if (this.OrthoEnabled && this.CommandContext?.LastPoint != null)
				{
					var orthoPt = CoordinateParser.ApplyOrtho(this.CommandContext.LastPoint.Value, (px, py));
					px = orthoPt.X;
					py = orthoPt.Y;
				}

				this.BeforeCommandInput?.Invoke();
				if (this.CommandContext != null)
				{
					this.CommandContext.ShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
				}
				this.ActiveCommand.OnPoint(px, py);
				this.CommandPromptChanged?.Invoke(this.ActiveCommand.Prompt);
				if (this.ActiveCommand.IsFinished)
				{
					this.ActiveCommand = null;
					this.CommandFinished?.Invoke();
				}
				this.InvalidateVisual();
				e.Handled = true;
				return;
			}

			// 더블클릭: 치수·문자 편집 (첫 클릭이 그립을 활성화했어도 취소하고 편집 창)
			if (e.ClickCount >= 2 && this.ActiveCommand == null)
			{
				var dbl = this._sceneIndex?.Pick(wx, wy, 6.0 / this._view.Scale);
				if (dbl != null && this.EntityDoubleClicked != null)
				{
					this.CancelGrip();
					e.Pointer.Capture(null);
					this.LastDoubleClickWorld = (wx, wy);
					this.EntityDoubleClicked(dbl);
					e.Handled = true;
					return;
				}
			}

			// 그립 편집: 활성 그립이 있으면 이 클릭으로 반영, 선택 객체의 그립을 누르면 활성화
			if (this._hotGrip != null)
			{
				this.CommitGrip();
				e.Handled = true;
				return;
			}
			if (this.SelectedEntities.Count > 0 && this.HitGrip(p) is { } hit)
			{
				this._gripEntity = hit.Entity;
				this._hotGrip = hit.Grip;
				this._gripPressPos = p;
				this._gripDragged = false;
				e.Pointer.Capture(this);
				this.InvalidateVisual();
				e.Handled = true;
				return;
			}

			// 명령이 없을 때는 선택 모드 동작
			double tol = this.PickBoxPx / this._view.Scale;
			var picked = this._sceneIndex?.Pick(wx, wy, tol);
			bool isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

			if (picked != null)
			{
				var members = this.WithGroup(picked);
				if (isShift)
				{
					if (this.SelectedEntities.Contains(picked))
					{
						this.SelectedEntities.ExceptWith(members);
					}
					else
					{
						this.SelectedEntities.UnionWith(members);
					}
				}
				else if (this.PickAdd)
				{
					this.SelectedEntities.UnionWith(members);
				}
				else
				{
					this.SelectedEntities.Clear();
					this.SelectedEntities.UnionWith(members);
				}
				this.SelectionChanged?.Invoke();
				this.InvalidateVisual();
			}
			else
			{
				// 빈 곳 클릭: 윈도우/크로싱 드래그 사각형 시작
				if (!isShift)
				{
					if (this.SelectedEntities.Count > 0)
					{
						this.SelectedEntities.Clear();
						this.SelectionChanged?.Invoke();
					}
				}

				this._isDraggingBox = true;
				this._dragStartWorld = (wx, wy);
				this._currentWorld = (wx, wy);
				e.Pointer.Capture(this);
				this.InvalidateVisual();
			}
			e.Handled = true;
		}
	}

	// ===== 도움선: 극좌표 추적(F10) / 객체 스냅 추적(F11) =====

	/// <summary>극좌표 추적 (F10): 기준점에서 증분 각도 방향으로 점선 도움선과 점 맞춤.</summary>
	public bool PolarTrackingEnabled { get; set; }

	/// <summary>극좌표 증분 각도(도). 기존 CAD 기본처럼 45도 (0, 45, 90, ...).</summary>
	public double PolarIncrementDeg { get; set; } = 45;

	/// <summary>객체 스냅 추적 (F11): 머문 스냅점에서 수평·수직 정렬 도움선.</summary>
	public bool ObjectSnapTrackingEnabled { get; set; } = true;

	private readonly List<(double X, double Y)> _acquired = new();
	private (double X, double Y)? _hoverSnapPt;
	private DateTime _hoverSnapSince;

	// 이번 커서 위치에서 계산된 도움선 (그리기용): 시작점, 방향(단위벡터), 설명 글자
	private readonly List<((double X, double Y) From, (double X, double Y) Dir)> _guides = new();
	private string? _guideLabel;
	private (double X, double Y)? _tracked; // 도움선에 맞춘 점 (없으면 null)

	/// <summary>명령·그립의 기준점 (극좌표 추적 기준).</summary>
	private (double X, double Y)? TrackingBase()
	{
		if (this._hotGrip is { } g) return (g.X, g.Y);
		if (this.ActiveCommand != null && !this.ActiveCommand.IsFinished) return this.CommandContext?.LastPoint;
		return null;
	}

	/// <summary>명령 시작/끝 등에서 추적점 초기화.</summary>
	public void ClearTracking()
	{
		this._acquired.Clear();
		this._guides.Clear();
		this._guideLabel = null;
		this._tracked = null;
	}

	/// <summary>
	/// 커서(도면 좌표)에 대해 도움선을 계산한다. 객체 스냅이 잡혀 있으면 도움선은 쓰지 않는다.
	/// 우선순위: 추적선 두 개의 교차 > 추적선 하나 > 극좌표 선.
	/// </summary>
	private void ComputeTracking(double wx, double wy)
	{
		this._guides.Clear();
		this._guideLabel = null;
		this._tracked = null;
		if (this._view == null || this._currentSnap.HasValue || this.OrthoEnabled)
		{
			return;
		}
		bool active = this.TrackingBase() != null || this.ActiveCommand != null;
		if (!active)
		{
			return;
		}
		double tolW = 8.0 / this._view.Scale;

		// 객체 스냅 추적: 획득한 점과 수평/수직으로 맞으면 그 선 위로
		var lines = new List<((double X, double Y) From, (double X, double Y) Dir, string Name)>();
		if (this.ObjectSnapTrackingEnabled)
		{
			foreach (var a in this._acquired)
			{
				if (Math.Abs(wy - a.Y) < tolW) lines.Add((a, (wx >= a.X ? 1 : -1, 0), "추적"));
				if (Math.Abs(wx - a.X) < tolW) lines.Add((a, (0, wy >= a.Y ? 1 : -1), "추적"));
			}
		}
		// 극좌표 추적
		var bp = this.TrackingBase();
		if (this.PolarTrackingEnabled && bp is { } b)
		{
			double dx = wx - b.X, dy = wy - b.Y;
			double dist = Math.Sqrt(dx * dx + dy * dy);
			if (dist > tolW)
			{
				double inc = Math.Max(1, this.PolarIncrementDeg) * Math.PI / 180;
				double ang = Math.Atan2(dy, dx);
				double snapAng = Math.Round(ang / inc) * inc;
				double ux = Math.Cos(snapAng), uy = Math.Sin(snapAng);
				double perp = Math.Abs(dx * -uy + dy * ux);
				if (perp < tolW)
				{
					lines.Add((b, (ux, uy), "극좌표"));
				}
			}
		}
		if (lines.Count == 0)
		{
			return;
		}

		// 두 선의 교차점이 가까우면 그 점, 아니면 가장 가까운 선에 투영
		(double X, double Y)? best = null;
		for (int i = 0; i < lines.Count && best == null; i++)
		{
			for (int j = i + 1; j < lines.Count; j++)
			{
				var (p1, d1, _) = lines[i];
				var (p2, d2, _) = lines[j];
				double den = d1.X * d2.Y - d1.Y * d2.X;
				if (Math.Abs(den) < 1e-9) continue;
				double t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / den;
				var ip = (p1.X + d1.X * t, p1.Y + d1.Y * t);
				if (Math.Abs(ip.Item1 - wx) < tolW * 1.5 && Math.Abs(ip.Item2 - wy) < tolW * 1.5)
				{
					best = ip;
					this._guides.Add((p1, d1));
					this._guides.Add((p2, d2));
					this._guideLabel = "교차 추적";
					break;
				}
			}
		}
		if (best == null)
		{
			var (from, dir, name) = lines.OrderBy(l => Math.Abs((wx - l.From.X) * -l.Dir.Y + (wy - l.From.Y) * l.Dir.X)).First();
			double t = (wx - from.X) * dir.X + (wy - from.Y) * dir.Y;
			best = (from.X + dir.X * t, from.Y + dir.Y * t);
			this._guides.Add((from, dir));
			double deg = Math.Atan2(dir.Y, dir.X) * 180 / Math.PI;
			if (deg < 0) deg += 360;
			this._guideLabel = $"{name}: {Math.Abs(t):0.00} < {deg:0}°";
		}
		this._tracked = best;
	}

	/// <summary>스냅점 위에 잠시(0.35초) 머물면 추적점으로 획득 (최대 7개).</summary>
	private void AcquireTrackingPoint()
	{
		if (!this.ObjectSnapTrackingEnabled || !this.SnapWanted || this._currentSnap is not { } sp)
		{
			this._hoverSnapPt = null;
			return;
		}
		var pt = (sp.X, sp.Y);
		if (this._hoverSnapPt != pt)
		{
			this._hoverSnapPt = pt;
			this._hoverSnapSince = DateTime.UtcNow;
			return;
		}
		if ((DateTime.UtcNow - this._hoverSnapSince).TotalMilliseconds >= 350 && !this._acquired.Contains(pt))
		{
			this._acquired.Add(pt);
			if (this._acquired.Count > 7) this._acquired.RemoveAt(0);
		}
	}

	/// <summary>
	/// 화면 좌표로 커서 상태(도면 좌표, 그리드 스냅, 객체 스냅)를 갱신한다.
	/// 클릭에서도 불러, 마우스 이동 이벤트 없이 클릭만 와도(원격 조작·태블릿 등) 정확한 위치를 쓴다.
	/// </summary>
	private void UpdatePointerState(Point p)
	{
		if (this._view == null) return;
		this._cursorScreenPos = p;
		var (wx, wy) = this._view.ToWorld(p.X, p.Y);
		if (this.SnapEnabled && this.SnapSpacingX > 1e-6 && this.SnapSpacingY > 1e-6)
		{
			wx = Math.Round(wx / this.SnapSpacingX) * this.SnapSpacingX;
			wy = Math.Round(wy / this.SnapSpacingY) * this.SnapSpacingY;
		}
		this._currentWorld = (wx, wy);
		if (!this._isDraggingBox && this.ObjectSnapEnabled && this.SnapWanted && this._sceneIndex != null)
		{
			this._currentSnap = SnapEngine.Find(this._sceneIndex, wx, wy, 12.0 / this._view.Scale, this.SnapModes);
		}
		else if (!this.ObjectSnapEnabled || !this.SnapWanted)
		{
			this._currentSnap = null;
		}
		this.AcquireTrackingPoint();
		this.ComputeTracking(wx, wy);
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		if (this._view == null) return;

		Point p = e.GetPosition(this);
		this._cursorScreenPos = p;
		if (this._panStart is Point start)
		{
			this._view.PanPixels(p.X - start.X, p.Y - start.Y);
			this._panStart = p;
			this.InvalidateVisual();
			this.ViewChanged?.Invoke();
		}

		var (wx, wy) = this._view.ToWorld(p.X, p.Y);
		if (this.SnapEnabled && this.SnapSpacingX > 1e-6 && this.SnapSpacingY > 1e-6)
		{
			wx = Math.Round(wx / this.SnapSpacingX) * this.SnapSpacingX;
			wy = Math.Round(wy / this.SnapSpacingY) * this.SnapSpacingY;
		}
		this._currentWorld = (wx, wy);
		if (this._hotGrip != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
			&& Math.Abs(p.X - this._gripPressPos.X) + Math.Abs(p.Y - this._gripPressPos.Y) > 4)
		{
			this._gripDragged = true;
		}

		if (this._isDraggingBox)
		{
			this.InvalidateVisual();
		}
		else
		{
			// 객체 스냅 계산 (명령이 객체를 고르는 중이면 스냅하지 않음 — 기존 CAD와 같음)
			if (this.ObjectSnapEnabled && this.SnapWanted && this._sceneIndex != null)
			{
				double tol = 12.0 / this._view.Scale;
				var snap = SnapEngine.Find(this._sceneIndex, wx, wy, tol, this.SnapModes);
				if (this._currentSnap != snap)
				{
					this._currentSnap = snap;
					this.InvalidateVisual();
				}
			}
			else if (this._currentSnap != null)
			{
				this._currentSnap = null;
				this.InvalidateVisual();
			}
			else if (this.ActiveCommand != null)
			{
				// 미리보기 갱신
				this.InvalidateVisual();
			}
		}

		if (!this._isDraggingBox)
		{
			this.AcquireTrackingPoint();
			this.ComputeTracking(wx, wy);
		}
		this.UpdateHover(p);

		double reportX = this._currentSnap.HasValue ? this._currentSnap.Value.X : (this._tracked?.X ?? wx);
		double reportY = this._currentSnap.HasValue ? this._currentSnap.Value.Y : (this._tracked?.Y ?? wy);
		this.CursorWorld = (reportX, reportY);
		this.CursorWorldChanged?.Invoke(reportX, reportY);

		// 십자 커서와 명령 미리보기는 매번 갱신 (도면은 캐시되어 있으므로 비용이 작음)
		this.InvalidateVisual();
	}

	/// <summary>문자 바로 편집 상태: 커서 위치표(SceneText.Caret), 커서·선택 위치, 깜박임, 편집 영역.</summary>
	public sealed record InplaceCaretState(double[] Stops, double Rotation, int Caret, int SelStart, int SelEnd, bool CaretOn, Bounds Frame);

	private InplaceCaretState? _inplaceCaret;

	/// <summary>문자 바로 편집 중이면 커서와 선택 영역을 문자 위에 직접 그린다 (문자는 원래 자리·모양 그대로).</summary>
	public InplaceCaretState? InplaceCaret
	{
		get => this._inplaceCaret;
		set
		{
			this._inplaceCaret = value;
			this.InvalidateVisual();
		}
	}

	/// <summary>모형 공간을 보고 있으면 그린 도형 범위 (배치면 null).</summary>
	public Bounds? ModelExtents => this._layoutRenderer == null && this._scene is { } sc && !sc.Extents.IsEmpty ? sc.Extents : null;

	/// <summary>문자 편집 선택 영역 색 (기존 CAD "편집기 설정 ▸ 텍스트 강조 색상").</summary>
	public SKColor InplaceSelectionColor { get; set; } = new SKColor(0x33, 0x99, 0xFF, 0x70);

	/// <summary>맞춤법 검사: 틀린 단어 (편집 글자 위치, 길이) — 빨간 물결선으로 그린다.</summary>
	public List<(int Start, int Length)>? InplaceSpellErrors { get; set; }

	/// <summary>문자 바로 편집 중 오른쪽 클릭 (도면 좌표, 화면 좌표). true면 처리함 (Enter로 쓰지 않음).</summary>
	public Func<double, double, Point, bool>? InplaceRightClick { get; set; }

	/// <summary>여러 줄 문자 편집 상자 위의 자 (도면 x 범위). null이면 없음.</summary>
	public (double X0, double X1)? InplaceRuler { get; set; }

	/// <summary>폭이 없는 여러 줄 문자: 자를 문자 범위에 맞춘다.</summary>
	public bool InplaceRulerAuto { get; set; }

	/// <summary>기존 CAD 문자 편집기처럼 편집 상자 위에 눈금 자를 그린다.</summary>
	private void DrawInplaceRuler(SKCanvas canvas, ViewTransform view, InplaceCaretState st)
	{
		var f = st.Frame;
		(double X0, double X1)? span = this.InplaceRuler ?? (this.InplaceRulerAuto ? (f.MinX, f.MaxX) : null);
		if (span is not { } r || Math.Abs(st.Rotation) > 1e-6)
		{
			return;
		}
		var (sx0, sy) = view.ToScreen(r.X0, f.MaxY);
		var (sx1, _) = view.ToScreen(r.X1, f.MaxY);
		float left = (float)Math.Min(sx0, sx1), right = (float)Math.Max(sx0, sx1);
		// 폭 없는 문자(다중지시선·폭 0 MTEXT)가 거의 비어 있으면 기존 CAD처럼 ◁▷ 손잡이만
		bool handleOnly = this.InplaceRuler == null && right - left < 30;
		if (right - left < 20)
		{
			right = left + 20;
		}
		float bottom = (float)sy - 4, top = bottom - 14;
		if (!handleOnly)
		{
			using var bg = new SKPaint { Color = new SKColor(0xF4, 0xF4, 0xF4), Style = SKPaintStyle.Fill };
			using var border = new SKPaint { Color = new SKColor(0x80, 0x80, 0x80), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
			using var tick = new SKPaint { Color = new SKColor(0x40, 0x40, 0x40), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
			canvas.DrawRect(left, top, right - left, bottom - top, bg);
			canvas.DrawRect(left, top, right - left, bottom - top, border);
			// 문자 높이마다 눈금, 다섯 칸마다 긴 눈금
			double unit = Math.Max(f.MaxY - f.MinY, 1e-6) / 2;
			double px = unit * view.Scale;
			while (px < 6) { unit *= 2; px *= 2; }
			int k = 0;
			for (double x = left + px; x < right - 1; x += px, k++)
			{
				float len = (k + 1) % 5 == 0 ? 8 : 4;
				canvas.DrawLine((float)x, bottom - 2, (float)x, bottom - 2 - len, tick);
			}
		}
		// 오른쪽 끝 폭 조절 손잡이: 기존 CAD처럼 ◁▷ 상자 (빈 문자를 막 만든 때도 입력 자리가 보이게)
		using var handleBg = new SKPaint { Color = new SKColor(0xD8, 0xD8, 0xD8), Style = SKPaintStyle.Fill };
		using var handleBorder = new SKPaint { Color = new SKColor(0x70, 0x70, 0x70), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
		using var tri = new SKPaint { Color = new SKColor(0x30, 0x30, 0x30), Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
		float hx = handleOnly ? left : right + 2;
		var hb = new SKRect(hx, top - 1, hx + 24, bottom + 1);
		canvas.DrawRect(hb, handleBg);
		canvas.DrawRect(hb, handleBorder);
		float mid = (hb.Top + hb.Bottom) / 2, hh = (hb.Height - 6) / 2;
		using (var lt = new SKPath())
		{
			lt.MoveTo(hb.Left + 10, mid - hh); lt.LineTo(hb.Left + 4, mid); lt.LineTo(hb.Left + 10, mid + hh); lt.Close();
			canvas.DrawPath(lt, tri);
		}
		using (var rt = new SKPath())
		{
			rt.MoveTo(hb.Right - 10, mid - hh); rt.LineTo(hb.Right - 4, mid); rt.LineTo(hb.Right - 10, mid + hh); rt.Close();
			canvas.DrawPath(rt, tri);
		}
	}

	private void DrawInplaceCaret(SKCanvas canvas, ViewTransform view, InplaceCaretState st)
	{
		this.DrawInplaceRuler(canvas, view, st);
		int n = st.Stops.Length / 4;
		if (n == 0)
		{
			return;
		}
		double ux = -Math.Sin(st.Rotation), uy = Math.Cos(st.Rotation); // 글자 위쪽 방향
		SKPoint S(double wx, double wy)
		{
			var (sx, sy) = view.ToScreen(wx, wy);
			return new SKPoint((float)sx, (float)sy);
		}
		(double X, double Y, double H, int Line) At(int i)
		{
			i = Math.Clamp(i, 0, n - 1);
			return (st.Stops[i * 4], st.Stops[i * 4 + 1], st.Stops[i * 4 + 2], (int)st.Stops[i * 4 + 3]);
		}

		// 편집 중인 영역 표시 (옅은 점선)
		using (var framePaint = new SKPaint { Color = new SKColor(0x9A, 0xC8, 0xFF, 0xA0), StrokeWidth = 1f, Style = SKPaintStyle.Stroke, PathEffect = SKPathEffect.CreateDash(new float[] { 3, 3 }, 0) })
		{
			var a = S(st.Frame.MinX, st.Frame.MaxY);
			var b = S(st.Frame.MaxX, st.Frame.MinY);
			var r = new SKRect(Math.Min(a.X, b.X) - 4, Math.Min(a.Y, b.Y) - 4, Math.Max(a.X, b.X) + 4, Math.Max(a.Y, b.Y) + 4);
			canvas.DrawRect(r, framePaint);
		}

		// 선택 영역: 같은 줄의 이웃 경계 사이를 반투명 파랑으로
		int s0 = Math.Min(st.SelStart, st.SelEnd), s1 = Math.Max(st.SelStart, st.SelEnd);
		if (s1 > s0)
		{
			using var selPaint = new SKPaint { Color = this.InplaceSelectionColor, Style = SKPaintStyle.Fill, IsAntialias = true };
			for (int i = s0; i < s1 && i + 1 < n; i++)
			{
				var p = At(i);
				var q = At(i + 1);
				if (p.Line != q.Line)
				{
					continue;
				}
				double lo = -p.H * 0.3, hi = p.H * 1.15;
				using var path = new SKPath();
				path.MoveTo(S(p.X + ux * lo, p.Y + uy * lo));
				path.LineTo(S(q.X + ux * lo, q.Y + uy * lo));
				path.LineTo(S(q.X + ux * hi, q.Y + uy * hi));
				path.LineTo(S(p.X + ux * hi, p.Y + uy * hi));
				path.Close();
				canvas.DrawPath(path, selPaint);
			}
		}

		// 맞춤법: 틀린 단어 아래 빨간 물결선 (기존 CAD와 같음)
		if (this.InplaceSpellErrors is { Count: > 0 } errs)
		{
			using var wave = new SKPaint { Color = new SKColor(0xFF, 0x30, 0x30), StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true };
			foreach (var (start, len) in errs)
			{
				for (int i = start; i < start + len && i + 1 < n; i++)
				{
					var p = At(i);
					var q = At(i + 1);
					if (p.Line != q.Line)
					{
						continue;
					}
					double d = -p.H * 0.22;
					var a = S(p.X + ux * d, p.Y + uy * d);
					var b = S(q.X + ux * d, q.Y + uy * d);
					float dx = b.X - a.X, dy = b.Y - a.Y;
					float lenPx = MathF.Sqrt(dx * dx + dy * dy);
					if (lenPx < 0.5f) continue;
					float nx = -dy / lenPx * 1.5f, ny = dx / lenPx * 1.5f;
					using var path = new SKPath();
					path.MoveTo(a);
					int steps = Math.Max(2, (int)(lenPx / 2));
					for (int k = 1; k <= steps; k++)
					{
						float t = (float)k / steps;
						float sgn = k % 2 == 0 ? 1 : -1;
						path.LineTo(a.X + dx * t + nx * sgn, a.Y + dy * t + ny * sgn);
					}
					canvas.DrawPath(path, wave);
				}
			}
		}

		// 커서 (깜박임)
		if (st.CaretOn)
		{
			var c = At(st.Caret);
			using var caretPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 1.6f, Style = SKPaintStyle.Stroke, IsAntialias = true };
			canvas.DrawLine(S(c.X - ux * c.H * 0.3, c.Y - uy * c.H * 0.3), S(c.X + ux * c.H * 1.15, c.Y + uy * c.H * 1.15), caretPaint);
		}
	}

	/// <summary>마지막 더블클릭 위치 (도면 좌표) — 표 칸 편집에서 어느 칸인지 찾는 데 쓴다.</summary>
	public (double X, double Y)? LastDoubleClickWorld { get; private set; }

	/// <summary>도면 점 아래 객체 (화면 5px 허용 오차, 지금 장면 색인 사용).</summary>
	public Entity? PickAt(double x, double y) =>
		this._view == null ? null : this._sceneIndex?.Pick(x, y, this.PickBoxPx / this._view.Scale);

	private Entity? _hoverEntity;

	/// <summary>선택 상자 반폭 (픽셀): 평소 5, 고를 수 있는 객체 위에 올라가 있으면 10.</summary>
	private const double PickBoxBasePx = 5, PickBoxHoverPx = 10;

	/// <summary>
	/// 지금 선택 상자 반폭 (픽셀). 객체 위에 올리면 상자가 두 배로 커져 어느 객체를 고르는지 잘 보이고,
	/// 커진 상자 안에 객체가 있는 동안은 그대로 집힌다 (화면에 보이는 상자 = 실제로 고르는 범위).
	/// </summary>
	private double PickBoxPx => this._hoverEntity != null ? PickBoxHoverPx : PickBoxBasePx;

	/// <summary>객체가 화면에 그려진 도면 영역 (문자 바로 편집 칸 위치용). 그려진 것이 없으면 null.</summary>
	public (double MinX, double MinY, double MaxX, double MaxY)? EntityWorldBounds(Entity entity)
	{
		if (this._sceneIndex == null)
		{
			return null;
		}
		var items = this._sceneIndex.ItemsOf(entity);
		double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
		void Add(double x, double y)
		{
			if (double.IsFinite(x) && double.IsFinite(y))
			{
				minX = Math.Min(minX, x); minY = Math.Min(minY, y);
				maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
			}
		}
		foreach (var poly in items.Polylines)
		{
			for (int i = 0; i + 1 < poly.Points.Length; i += 2) Add(poly.Points[i], poly.Points[i + 1]);
		}
		foreach (var fill in items.Fills)
		{
			foreach (double[] loop in fill.Loops)
			{
				for (int i = 0; i + 1 < loop.Length; i += 2) Add(loop[i], loop[i + 1]);
			}
		}
		foreach (var text in items.Texts)
		{
			Add(text.Bounds.MinX, text.Bounds.MinY);
			Add(text.Bounds.MaxX, text.Bounds.MaxY);
		}
		return minX <= maxX ? (minX, minY, maxX, maxY) : null;
	}

	/// <summary>
	/// 커서 아래 객체 (마우스 올림 강조). 명령이 없고, 끌기·그립 편집·화면 이동 중이 아닐 때만.
	/// </summary>
	private void UpdateHover(Point p)
	{
		Entity? hover = null;
		if (this._view != null && this._sceneIndex != null && (this.ActiveCommand == null || this.ActiveCommand is { IsFinished: false, PickingObject: true }) && !this._isDraggingBox
			&& this._hotGrip == null && this._panStart == null && this._windowPickTcs == null)
		{
			var (wx, wy) = this._view.ToWorld(p.X, p.Y);
			hover = this._sceneIndex.Pick(wx, wy, this.PickBoxPx / this._view.Scale);
		}
		this._hoverEntity = hover;
	}

	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		if (this._hotGrip != null)
		{
			e.Pointer.Capture(null);
			if (this._gripDragged)
			{
				// 누른 채 끌어서 놓으면 바로 반영 (클릭-클릭 방식도 지원)
				this.CommitGrip();
			}
			e.Handled = true;
			return;
		}
		if (this._panStart != null)
		{
			this._panStart = null;
			e.Pointer.Capture(null);
			this.ViewChanged?.Invoke();
		}

		if (this._isDraggingBox && this._dragStartWorld is { } start)
		{
			this._isDraggingBox = false;
			e.Pointer.Capture(null);

			double minX = Math.Min(start.X, this._currentWorld.X);
			double maxX = Math.Max(start.X, this._currentWorld.X);
			double minY = Math.Min(start.Y, this._currentWorld.Y);
			double maxY = Math.Max(start.Y, this._currentWorld.Y);

			double tol = (this._view != null) ? 2.0 / this._view.Scale : 0.1;
			if ((maxX - minX) > tol || (maxY - minY) > tol)
			{
				bool crossing = this._currentWorld.X < start.X;
				var rect = new Bounds(minX, minY, maxX, maxY);
				var items = this._sceneIndex?.SelectWindow(rect, crossing);
				if (items != null && this.GroupSelection)
				{
					items = items.SelectMany(this.WithGroup).Distinct().ToList();
				}
				if (items != null)
				{
					bool inCommand = this.ActiveCommand != null && !this.ActiveCommand.IsFinished;
					// 명령 중 선택은 계속 더한다 (기존 CAD처럼), 명령 밖에서는 Shift가 아니면 새로
					if (!inCommand && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
					{
						this.SelectedEntities.Clear();
					}
					foreach (var entity in items)
					{
						this.SelectedEntities.Add(entity);
					}
					this.SelectionChanged?.Invoke();

					if (inCommand && this.ActiveCommand != null)
					{
						this.ActiveCommand.OnSelection(items);
						this.CommandPromptChanged?.Invoke(this.ActiveCommand.Prompt);
						if (this.ActiveCommand.IsFinished)
						{
							this.ActiveCommand = null;
							this.CommandFinished?.Invoke();
						}
					}
				}
			}

			this.InvalidateVisual();
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);

		// 명령 자동완성 목록 이동·완성 키는 명령줄로 넘김 (목록이 열려 있을 때만 처리됨)
		if ((e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Tab) && this.CommandLineKey?.Invoke(e.Key) == true)
		{
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Enter || e.Key == Key.Space)
		{
			this.EnterOrSpacePressed?.Invoke(e.Key == Key.Space);
			e.Handled = true;
			return;
		}
		else if (e.Key == Key.Back)
		{
			this.BackspacePressed?.Invoke();
			e.Handled = true;
			return;
		}
		else if (e.Key == Key.Escape && this._hotGrip != null)
		{
			this.CancelGrip();
			e.Handled = true;
			return;
		}
		else if (e.Key == Key.Escape)
		{
			this.EscapePressed?.Invoke();
			if (this.ActiveCommand != null)
			{
				this.ActiveCommand.OnCancel();
				this.ActiveCommand = null;
				this.CommandFinished?.Invoke();
				this.InvalidateVisual();
			}
			else
			{
				this.ClearSelection();
			}
			e.Handled = true;
			return;
		}
		else if (e.Key == Key.Delete)
		{
			if (this.SelectedEntities.Count > 0 && this.Document != null)
			{
				this.Document.Erase(this.SelectedEntities.ToList());
				this.ClearSelection();
				e.Handled = true;
			}
		}
		else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			this.SelectAll();
			e.Handled = true;
		}
		else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			this.Document?.Undo();
			e.Handled = true;
		}
		else if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			this.Document?.Redo();
			e.Handled = true;
		}
		else if (e.Key == Key.F3)
		{
			this.ObjectSnapEnabled = !this.ObjectSnapEnabled;
			this.ObjectSnapToggled?.Invoke(this.ObjectSnapEnabled);
			this.InvalidateVisual();
			e.Handled = true;
		}
		else if (e.Key == Key.F7)
		{
			this.GridEnabled = !this.GridEnabled;
			this.GridToggled?.Invoke(this.GridEnabled);
			this.InvalidateVisual();
			e.Handled = true;
		}
		else if (e.Key == Key.F8)
		{
			this.OrthoEnabled = !this.OrthoEnabled;
			this.OrthoToggled?.Invoke(this.OrthoEnabled);
			if (this.CommandContext != null)
			{
				this.CommandContext.Ortho = this.OrthoEnabled;
			}
			e.Handled = true;
		}
	}

	// 도면 이미지 캐시 (렌더 스레드 전용). 커서만 움직일 때는 도면을 다시 그리지 않고 오버레이만 갱신한다.
	private SKImage? _sceneCache;
	private (object Renderer, double Cx, double Cy, double Scale, int W, int H, float Dpi)? _sceneCacheKey;

	internal void DrawSceneCached(SKCanvas canvas, GRContext? gr, SkiaSceneRenderer renderer, ViewTransform view)
	{
		float dpi = canvas.TotalMatrix.ScaleX > 0 ? canvas.TotalMatrix.ScaleX : 1f;
		var key = ((object)renderer, view.CenterX, view.CenterY, view.Scale, view.Width, view.Height, dpi);
		if (this._sceneCache == null || !this._sceneCacheKey.HasValue || !this._sceneCacheKey.Value.Equals(key))
		{
			int pw = Math.Max(1, (int)Math.Ceiling(view.Width * dpi));
			int ph = Math.Max(1, (int)Math.Ceiling(view.Height * dpi));
			using var surface = gr != null
				? SKSurface.Create(gr, false, new SKImageInfo(pw, ph))
				: SKSurface.Create(new SKImageInfo(pw, ph));
			if (surface == null)
			{
				renderer.Render(canvas, view, drawBackground: false);
				return;
			}
			surface.Canvas.Clear(SKColors.Transparent);
			surface.Canvas.Scale(dpi);
			renderer.Render(surface.Canvas, view, drawBackground: false); // 배경·그리드는 호출한 쪽에서 먼저 그림
			this._sceneCache?.Dispose();
			this._sceneCache = surface.Snapshot();
			this._sceneCacheKey = key;
		}
		canvas.DrawImage(this._sceneCache, new SKRect(0, 0, view.Width, view.Height));
	}

	internal void DrawSceneCached(SKCanvas canvas, GRContext? gr, LayoutRenderer renderer, ViewTransform view)
	{
		float dpi = canvas.TotalMatrix.ScaleX > 0 ? canvas.TotalMatrix.ScaleX : 1f;
		var key = ((object)renderer, view.CenterX, view.CenterY, view.Scale, view.Width, view.Height, dpi);
		if (this._sceneCache == null || !this._sceneCacheKey.HasValue || !this._sceneCacheKey.Value.Equals(key))
		{
			int pw = Math.Max(1, (int)Math.Ceiling(view.Width * dpi));
			int ph = Math.Max(1, (int)Math.Ceiling(view.Height * dpi));
			using var surface = gr != null
				? SKSurface.Create(gr, false, new SKImageInfo(pw, ph))
				: SKSurface.Create(new SKImageInfo(pw, ph));
			if (surface == null)
			{
				renderer.Render(canvas, view);
				return;
			}
			surface.Canvas.Scale(dpi);
			renderer.Render(surface.Canvas, view);
			this._sceneCache?.Dispose();
			this._sceneCache = surface.Snapshot();
			this._sceneCacheKey = key;
		}
		canvas.DrawImage(this._sceneCache, new SKRect(0, 0, view.Width, view.Height));
	}

	/// <summary>화면 표시용 한글 글꼴 (맑은 고딕, 없으면 한글이 되는 시스템 글꼴).</summary>
	internal static readonly SKTypeface OverlayTypeface =
		SKTypeface.FromFamilyName("Malgun Gothic")
		?? SKFontManager.Default.MatchCharacter('가')
		?? SKTypeface.Default;

	/// <summary>객체의 화면 모양을 주어진 펜으로 다시 그린다 (선택·마우스 올림 강조). 문자는 둘레 사각형.</summary>
	private void DrawEntityOutline(SKCanvas canvas, ViewTransform view, Entity entity, SKPaint paint)
	{
		if (this._sceneIndex == null)
		{
			return;
		}
		var items = this._sceneIndex.ItemsOf(entity);
		foreach (var poly in items.Polylines)
		{
			int count = poly.Points.Length / 2;
			if (count < 2) continue;

			using var path = new SKPath();
			for (int i = 0; i < count; i++)
			{
				var (sx, sy) = view.ToScreen(poly.Points[i * 2], poly.Points[i * 2 + 1]);
				if (i == 0) path.MoveTo((float)sx, (float)sy);
				else path.LineTo((float)sx, (float)sy);
			}
			if (poly.Closed) path.Close();
			canvas.DrawPath(path, paint);
		}

		foreach (var fill in items.Fills)
		{
			for (int l = 0; l < fill.Loops.Count; l++)
			{
				double[] loop = fill.Loops[l];
				int count = loop.Length / 2;
				if (count < 2) continue;

				using var path = new SKPath();
				for (int i = 0; i < count; i++)
				{
					var (sx, sy) = view.ToScreen(loop[i * 2], loop[i * 2 + 1]);
					if (i == 0) path.MoveTo((float)sx, (float)sy);
					else path.LineTo((float)sx, (float)sy);
				}
				path.Close();
				canvas.DrawPath(path, paint);
			}
		}

		foreach (var text in items.Texts)
		{
			if (text.PickOnly)
			{
				continue; // 글자 모양(획·윤곽)으로 이미 강조됨
			}
			var (p1x, p1y) = view.ToScreen(text.Bounds.MinX, text.Bounds.MaxY);
			var (p2x, p2y) = view.ToScreen(text.Bounds.MaxX, text.Bounds.MinY);
			var rect = new SKRect((float)Math.Min(p1x, p2x), (float)Math.Min(p1y, p2y), (float)Math.Max(p1x, p2x), (float)Math.Max(p1y, p2y));
			canvas.DrawRect(rect, paint);
		}
	}

	/// <summary>커서 옆 프롬프트를 켤지 (기존 CAD 동적 입력).</summary>
	public bool DynamicPromptEnabled { get; set; } = true;

	/// <summary>
	/// 커서 옆에 보일 짧은 프롬프트: 끝이 "&lt;기본 동작&gt;:"이면 그 부분만 (기존 CAD와 같음), 아니면 프롬프트 (길면 줄임).
	/// </summary>
	internal static string ShortPrompt(string? prompt)
	{
		if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
		string p = prompt.Trim();
		if (p.EndsWith(':')) p = p[..^1].TrimEnd();
		// "[옵션]/<기본 동작>" → "<기본 동작>" (예: "[각도(A)/문자(T)]/<치수호의위치(L)>" → "<치수호의위치(L)>")
		int slash = p.LastIndexOf("/<", StringComparison.Ordinal);
		if (slash >= 0 && p.EndsWith('>')) return p[(slash + 1)..];
		const int max = 34;
		return p.Length > max ? p[..max] + "..." : p + ":";
	}

	private void DrawCursorPrompt(SKCanvas canvas, Point cursor, string label, (double X, double Y)? coords, (double X, double Y)? from = null)
	{
		using var font = new SKFont(OverlayTypeface, 12f);
		using var textPaint = new SKPaint { Color = new SKColor(0xF0, 0xF0, 0xF0), IsAntialias = true };
		using var boxPaint = new SKPaint { Color = new SKColor(0x3A, 0x3A, 0x3A, 0xE8), Style = SKPaintStyle.Fill };
		using var borderPaint = new SKPaint { Color = new SKColor(0x80, 0x80, 0x80), Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
		using var activePaint = new SKPaint { Color = new SKColor(0x33, 0x99, 0xFF), Style = SKPaintStyle.Fill };
		const float pad = 4f, h = 18f, gap = 6f;
		float x = (float)cursor.X + 16f, y = (float)cursor.Y + 14f;
		float Box(float bx, string text, SKPaint fill)
		{
			float w = font.MeasureText(text) + pad * 2;
			var r = new SKRect(bx, y, bx + w, y + h);
			canvas.DrawRect(r, fill);
			canvas.DrawRect(r, borderPaint);
			canvas.DrawText(text, bx + pad, y + h - 5f, font, textPaint);
			return bx + w + gap;
		}
		// 화면 오른쪽 끝을 넘으면 커서 왼쪽으로
		float total = font.MeasureText(label) + pad * 2 + (coords != null ? 2 * (font.MeasureText("-00000.0000") + pad * 2 + gap) : 0);
		if (x + total > (float)this.Bounds.Width - 4) x = Math.Max(4f, (float)cursor.X - 16f - total);
		if (y + h > (float)this.Bounds.Height - 4) y = (float)cursor.Y - 14f - h;
		x = Box(x, label, boxPaint);
		if (coords is { } c && from is { } f)
		{
			// 앞 점이 있으면 길이와 각도 (기존 CAD 동적 입력처럼) — 마우스로 그려도 실제 크기가 바로 보인다
			double dx = c.X - f.X, dy = c.Y - f.Y;
			double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
			if (ang < 0) ang += 360;
			x = Box(x, Math.Sqrt(dx * dx + dy * dy).ToString("0.####"), activePaint);
			Box(x, "< " + ang.ToString("0.#") + "°", boxPaint);
		}
		else if (coords is { } c2)
		{
			x = Box(x, c2.X.ToString("0.####"), activePaint);
			Box(x, c2.Y.ToString("0.####"), boxPaint);
		}
	}

	/// <summary>
	/// 화면 오른쪽 아래 축척 막대 (0 ── 100 mm, 확대·축소에 따라 1·2·5 × 10ⁿ) + 화면 폭.
	/// 빈 화면이나 크게 축소한 화면에서도 지금 보이는 크기를 가늠할 수 있게.
	/// </summary>
	private void DrawScaleBar(SKCanvas canvas, ViewTransform view)
	{
		if (view.Scale <= 0 || this.Bounds.Width < 200)
		{
			return;
		}
		double target = 110 / view.Scale; // 약 110픽셀
		double p = Math.Pow(10, Math.Floor(Math.Log10(target)));
		double len = target / p >= 5 ? 5 * p : target / p >= 2 ? 2 * p : p;
		float px = (float)(len * view.Scale);
		using var font = new SKFont(OverlayTypeface, 11f);
		string Fmt(double v) => v >= 1 ? v.ToString("#,0.##") : v.ToString("0.####");
		string widthText = "화면 폭 약 " + Fmt(Math.Round(this.Bounds.Width / view.Scale, this.Bounds.Width / view.Scale >= 100 ? 0 : 2)) + " mm";
		// 오른쪽 아래 구석: [막대] [화면 폭 약 N mm]가 오른쪽 끝에서 16픽셀 안쪽에 오도록
		float x0 = (float)this.Bounds.Width - 16 - font.MeasureText(widthText) - 14 - px, y0 = (float)this.Bounds.Height - 16;
		using var line = new SKPaint { Color = new SKColor(0xC8, 0xC8, 0xC8, 0xD0), StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true };
		using var fill = new SKPaint { Color = new SKColor(0xC8, 0xC8, 0xC8, 0xA0), Style = SKPaintStyle.Fill };
		// 반은 채우고 반은 빈 칸 (지도 축척처럼)
		canvas.DrawRect(x0, y0 - 4, px / 2, 4, fill);
		canvas.DrawRect(x0, y0 - 4, px, 4, line);
		canvas.DrawLine(x0, y0 - 8, x0, y0, line);
		canvas.DrawLine(x0 + px, y0 - 8, x0 + px, y0, line);
		using var text = new SKPaint { Color = new SKColor(0xD8, 0xD8, 0xD8), IsAntialias = true };
		canvas.DrawText("0", x0 - 3, y0 - 11, font, text);
		string lenText = Fmt(len) + " mm";
		canvas.DrawText(lenText, x0 + px - font.MeasureText(lenText) / 2, y0 - 11, font, text);
		canvas.DrawText(widthText, x0 + px + 14, y0, font, text);
	}

	internal void DrawOverlay(SKCanvas canvas, ViewTransform view, bool includeGrid = true)
	{
		// 0. 그리드 (F7). 모형 공간은 도면 아래에 따로 그린다(includeGrid=false) — 위에 그리면 객체 선이 끊겨 보였다
		if (this.GridEnabled && includeGrid)
		{
			this.DrawGrid(canvas, view);
		}

		// 0-2. 문자 바로 편집: 커서·선택 영역
		if (this._inplaceCaret is { } caretState)
		{
			this.DrawInplaceCaret(canvas, view, caretState);
		}

		// 0-1. 마우스를 올려 놓은 객체 강조 (기존 CAD처럼 클릭 전에도 선택한 것처럼 점선으로, 커서를 빼면 원래대로)
		// 명령 안에서 이미 고른 객체 (예: 각도 치수의 첫 번째 선) — 기존 CAD처럼 점선
		if (this.ActiveCommand is { IsFinished: false } hlCmd)
		{
			using var pickedPaint = new SKPaint
			{
				Color = new SKColor(0xE8, 0xE8, 0xE8),
				StrokeWidth = 1.5f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true,
				PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0),
			};
			// 원래 실선을 배경색으로 지운 뒤 점선을 그린다 (그냥 덧그리면 실선과 겹쳐 실선으로 보였다)
			using var erasePaint = new SKPaint
			{
				Color = this._renderer?.Background ?? SKColors.Black,
				StrokeWidth = 3f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true,
			};
			foreach (var picked in hlCmd.Highlighted)
			{
				if (!this.SelectedEntities.Contains(picked))
				{
					this.DrawEntityOutline(canvas, view, picked, erasePaint);
					this.DrawEntityOutline(canvas, view, picked, pickedPaint);
				}
			}
			foreach (double[] loop in hlCmd.HighlightedLoops)
			{
				int count = loop.Length / 2;
				if (count < 2) continue;
				using var path = new SKPath();
				for (int i = 0; i < count; i++)
				{
					var (sx, sy) = view.ToScreen(loop[i * 2], loop[i * 2 + 1]);
					if (i == 0) path.MoveTo((float)sx, (float)sy);
					else path.LineTo((float)sx, (float)sy);
				}
				path.Close();
				canvas.DrawPath(path, erasePaint);
				canvas.DrawPath(path, pickedPaint);
			}
		}
		bool commandPicking = this.ActiveCommand is { IsFinished: false, PickingObject: true };
		if (this._hoverEntity is { } hoverEnt && this._inplaceCaret == null && (this.ActiveCommand == null || commandPicking) && this._hotGrip == null && !this.SelectedEntities.Contains(hoverEnt))
		{
			using var hoverPaint = new SKPaint
			{
				Color = new SKColor(0x33, 0x99, 0xFF),
				StrokeWidth = 2f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true,
				PathEffect = SKPathEffect.CreateDash(new float[] { 5, 3 }, 0),
			};
			this.DrawEntityOutline(canvas, view, hoverEnt, hoverPaint);
		}

		// 1. 선택된 엔티티 강조 표시 (#3399FF, 2px 및 그립 사각형)
		if (this.SelectedEntities.Count > 0 && this._sceneIndex != null)
		{
			using var selectPaint = new SKPaint
			{
				Color = new SKColor(0x33, 0x99, 0xFF),
				StrokeWidth = 2f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true,
				PathEffect = SKPathEffect.CreateDash(new float[] { 5, 3 }, 0), // 기존 CAD처럼 선택은 점선
			};
			using var gripFill = new SKPaint
			{
				Color = new SKColor(0x33, 0x99, 0xFF),
				Style = SKPaintStyle.Fill
			};
			using var gripStroke = new SKPaint
			{
				Color = SKColors.White,
				StrokeWidth = 1f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true
			};

			const float gripSize = 6f;

			foreach (var entity in this.SelectedEntities)
			{
				this.DrawEntityOutline(canvas, view, entity, selectPaint);
			}

			// 그립: 객체 종류별 편집점만 (원 = 중심 + 사분점 4개, 선 = 양 끝 + 중간). 활성 그립은 빨강
			if (this.ActiveCommand == null)
			{
				using var hotFill = new SKPaint { Color = new SKColor(0xE8, 0x30, 0x30), Style = SKPaintStyle.Fill };
				var hover = this._hotGrip == null ? (this._cursorScreenPos is Point cp ? this.HitGrip(cp) : null) : null;
				using var hoverFill = new SKPaint { Color = new SKColor(0x2E, 0xCC, 0x71), Style = SKPaintStyle.Fill };
				const float gs = 8f;
				foreach (var (en, g) in this.CurrentGrips())
				{
					var (sx, sy) = view.ToScreen(g.X, g.Y);
					var r = new SKRect((float)sx - gs / 2, (float)sy - gs / 2, (float)sx + gs / 2, (float)sy + gs / 2);
					bool isHot = this._hotGrip is { } hg && ReferenceEquals(en, this._gripEntity) && hg == g;
					bool isHover = hover is { } hv && ReferenceEquals(hv.Entity, en) && hv.Grip == g;
					canvas.DrawRect(r, isHot ? hotFill : isHover ? hoverFill : gripFill);
					canvas.DrawRect(r, gripStroke);
				}
			}
		}

		// 그립 편집 미리보기: 활성 그립을 커서 위치로 옮긴 모양 + 원래 위치에서 이어지는 점선
		if (this._hotGrip is { } hot && this._gripEntity != null && this.Document != null)
		{
			var (tx, ty) = this.GripTarget();
			var (gx, gy) = view.ToScreen(hot.X, hot.Y);
			var (cx2, cy2) = view.ToScreen(tx, ty);
			using var rubber = new SKPaint { Color = new SKColor(255, 200, 100, 200), StrokeWidth = 1f, Style = SKPaintStyle.Stroke, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0) };
			canvas.DrawLine((float)gx, (float)gy, (float)cx2, (float)cy2, rubber);
			var previewEntity = Free2DCad.Core.Editing.GripEditor.Preview(this._gripEntity, hot, tx, ty);
			if (previewEntity != null)
			{
				try
				{
					var ps = SceneBuilder.BuildEntities(this.Document.Document, new[] { previewEntity });
					using var pp = new SKPaint { Color = new SKColor(255, 255, 255, 190), StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };
					foreach (var poly in ps.Polylines)
					{
						int count = poly.Points.Length / 2;
						if (count < 2) continue;
						using var path = new SKPath();
						for (int i = 0; i < count; i++)
						{
							var (sx, sy) = view.ToScreen(poly.Points[i * 2], poly.Points[i * 2 + 1]);
							if (i == 0) path.MoveTo((float)sx, (float)sy);
							else path.LineTo((float)sx, (float)sy);
						}
						if (poly.Closed) path.Close();
						canvas.DrawPath(path, pp);
					}
				}
				catch (Exception)
				{
					// 미리보기 실패는 무시
				}
			}
		}

		// 2. 객체 스냅 표식 (노란색, 한 변 12px) 및 툴팁 글자
		if (this.ObjectSnapEnabled && this._currentSnap is SnapPoint snap)
		{
			var (sx, sy) = view.ToScreen(snap.X, snap.Y);
			float cx = (float)sx;
			float cy = (float)sy;
			const float s = 6f;

			using var snapPaint = new SKPaint
			{
				Color = SKColors.Yellow,
				StrokeWidth = 1.5f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true
			};

			switch (snap.Kind)
			{
				case SnapKind.Endpoint:
					canvas.DrawRect(cx - s, cy - s, 2 * s, 2 * s, snapPaint);
					break;
				case SnapKind.Midpoint:
					using (var p = new SKPath())
					{
						p.MoveTo(cx, cy - s);
						p.LineTo(cx - s, cy + s);
						p.LineTo(cx + s, cy + s);
						p.Close();
						canvas.DrawPath(p, snapPaint);
					}
					break;
				case SnapKind.Center:
					canvas.DrawCircle(cx, cy, s, snapPaint);
					break;
				case SnapKind.Quadrant:
					using (var p = new SKPath())
					{
						p.MoveTo(cx, cy - s);
						p.LineTo(cx + s, cy);
						p.LineTo(cx, cy + s);
						p.LineTo(cx - s, cy);
						p.Close();
						canvas.DrawPath(p, snapPaint);
					}
					break;
				case SnapKind.Intersection:
					canvas.DrawLine(cx - s, cy - s, cx + s, cy + s, snapPaint);
					canvas.DrawLine(cx - s, cy + s, cx + s, cy - s, snapPaint);
					break;
				case SnapKind.Insertion:
					canvas.DrawRect(cx - s, cy - s, 2 * s, 2 * s, snapPaint);
					canvas.DrawRect(cx - s / 2, cy - s / 2, s, s, snapPaint);
					break;
				case SnapKind.Nearest:
					using (var p = new SKPath())
					{
						p.MoveTo(cx - s, cy - s);
						p.LineTo(cx + s, cy - s);
						p.LineTo(cx - s, cy + s);
						p.LineTo(cx + s, cy + s);
						p.Close();
						canvas.DrawPath(p, snapPaint);
					}
					break;
				case SnapKind.Node:
					canvas.DrawCircle(cx, cy, s / 2, snapPaint);
					canvas.DrawLine(cx - s, cy, cx + s, cy, snapPaint);
					canvas.DrawLine(cx, cy - s, cx, cy + s, snapPaint);
					break;
			}

			string snapName = snap.Kind switch
			{
				SnapKind.Endpoint => "끝점",
				SnapKind.Midpoint => "중간점",
				SnapKind.Center => "중심",
				SnapKind.Quadrant => "사분점",
				SnapKind.Intersection => "교차점",
				SnapKind.Insertion => "삽입점",
				SnapKind.Nearest => "근처점",
				SnapKind.Node => "노드",
				_ => snap.Kind.ToString()
			};

			using var textPaint = new SKPaint
			{
				Color = SKColors.Yellow,
				TextSize = 12f,
				IsAntialias = true,
				Typeface = OverlayTypeface, // 기본 글꼴에는 한글이 없어 □□□로 보였다
			};
			canvas.DrawText(snapName, cx + s + 5, cy + 4, textPaint);
		}

		// 2-1. 커서 옆 프롬프트 (기존 CAD 동적 입력처럼: "<치수호의위치(L)>" + 점을 받을 때는 X/Y 좌표 상자)
		if (this.DynamicPromptEnabled && this.ActiveCommand is { IsFinished: false } promptCmd && this._cursorScreenPos is Point cursorPt
			&& !this._isDraggingBox && ShortPrompt(promptCmd.Prompt) is { Length: > 0 } label)
		{
			bool picksPoint = !(promptCmd.PickingObject || promptCmd.WantsSelection);
			this.DrawCursorPrompt(canvas, cursorPt, label, picksPoint ? this.GripTarget() : null, picksPoint ? this.CommandContext?.LastPoint : null);
		}

		// 3. 선택 사각형 (윈도우/크로싱)
		if (this._isDraggingBox && this._dragStartWorld is { } bStart)
		{
			var (p1x, p1y) = view.ToScreen(bStart.X, bStart.Y);
			var (p2x, p2y) = view.ToScreen(this._currentWorld.X, this._currentWorld.Y);

			float left = (float)Math.Min(p1x, p2x);
			float top = (float)Math.Min(p1y, p2y);
			float right = (float)Math.Max(p1x, p2x);
			float bottom = (float)Math.Max(p1y, p2y);
			var box = new SKRect(left, top, right, bottom);

			bool crossing = this._currentWorld.X < bStart.X;
			if (crossing)
			{
				using var fillPaint = new SKPaint
				{
					Color = new SKColor(0x33, 0xCC, 0x33, 0x30),
					Style = SKPaintStyle.Fill
				};
				using var dashEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0);
				using var strokePaint = new SKPaint
				{
					Color = new SKColor(0x33, 0xCC, 0x33),
					StrokeWidth = 1f,
					Style = SKPaintStyle.Stroke,
					PathEffect = dashEffect,
					IsAntialias = true
				};
				canvas.DrawRect(box, fillPaint);
				canvas.DrawRect(box, strokePaint);
			}
			else
			{
				using var fillPaint = new SKPaint
				{
					Color = new SKColor(0x33, 0x99, 0xFF, 0x30),
					Style = SKPaintStyle.Fill
				};
				using var strokePaint = new SKPaint
				{
					Color = new SKColor(0x33, 0x99, 0xFF),
					StrokeWidth = 1f,
					Style = SKPaintStyle.Stroke,
					IsAntialias = true
				};
				canvas.DrawRect(box, fillPaint);
				canvas.DrawRect(box, strokePaint);
			}
		}

		// 플롯 윈도우 선택 러버밴드
		if (this._windowPickFirst is { } wFirst)
		{
			var (sx1, sy1) = view.ToScreen(wFirst.X, wFirst.Y);
			var (sx2, sy2) = view.ToScreen(this._currentWorld.X, this._currentWorld.Y);
			var box = new SKRect((float)Math.Min(sx1, sx2), (float)Math.Min(sy1, sy2), (float)Math.Max(sx1, sx2), (float)Math.Max(sy1, sy2));
			using var strokePaint = new SKPaint
			{
				Color = new SKColor(0xFF, 0x55, 0x55),
				StrokeWidth = 1.5f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = true,
			};
			canvas.DrawRect(box, strokePaint);
		}

		// 4. 러버밴드 및 명령 미리보기 (반투명 흰색)
		if (this.ActiveCommand != null && this.Document != null)
		{
			double targetX = this._currentSnap.HasValue ? this._currentSnap.Value.X : (this._tracked?.X ?? this._currentWorld.X);
			double targetY = this._currentSnap.HasValue ? this._currentSnap.Value.Y : (this._tracked?.Y ?? this._currentWorld.Y);

			if (this.OrthoEnabled && this.CommandContext?.LastPoint != null)
			{
				var orthoPt = CoordinateParser.ApplyOrtho(this.CommandContext.LastPoint.Value, (targetX, targetY));
				targetX = orthoPt.X;
				targetY = orthoPt.Y;
			}

			// 러버밴드: 마지막 점 -> 커서 점선
			if (this.CommandContext?.LastPoint is { } lastPt)
			{
				var (lx, ly) = view.ToScreen(lastPt.X, lastPt.Y);
				var (cx, cy) = view.ToScreen(targetX, targetY);

				using var dashEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
				using var rubberPaint = new SKPaint
				{
					Color = new SKColor(255, 200, 100, 200),
					StrokeWidth = 1f,
					Style = SKPaintStyle.Stroke,
					PathEffect = dashEffect,
					IsAntialias = true,
				};
				canvas.DrawLine((float)lx, (float)ly, (float)cx, (float)cy, rubberPaint);
			}

			// 명령 미리보기 엔티티
			var previews = this.ActiveCommand.Preview(targetX, targetY).ToList();
			if (previews.Count > 0)
			{
				var previewScene = SceneBuilder.BuildEntities(this.Document.Document, previews);
				using var previewPaint = new SKPaint
				{
					Color = new SKColor(255, 255, 255, 180),
					StrokeWidth = 1.5f,
					Style = SKPaintStyle.Stroke,
					IsAntialias = true,
				};

				foreach (var poly in previewScene.Polylines)
				{
					int count = poly.Points.Length / 2;
					if (count < 2) continue;

					using var path = new SKPath();
					for (int i = 0; i < count; i++)
					{
						var (sx, sy) = view.ToScreen(poly.Points[i * 2], poly.Points[i * 2 + 1]);
						if (i == 0) path.MoveTo((float)sx, (float)sy);
						else path.LineTo((float)sx, (float)sy);
					}
					if (poly.Closed) path.Close();
					canvas.DrawPath(path, previewPaint);
				}
			}
		}

		// 4-1. 도움선 (극좌표/객체 스냅 추적): 초록 점선 + 설명 글자, 획득한 추적점은 작은 초록 +
		if (this._acquired.Count > 0 || this._guides.Count > 0)
		{
			using var acqPaint = new SKPaint { Color = new SKColor(0x3C, 0xD2, 0x5A), StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke, IsAntialias = true };
			foreach (var a in this._acquired)
			{
				var (ax, ay) = view.ToScreen(a.X, a.Y);
				canvas.DrawLine((float)ax - 4, (float)ay, (float)ax + 4, (float)ay, acqPaint);
				canvas.DrawLine((float)ax, (float)ay - 4, (float)ax, (float)ay + 4, acqPaint);
			}
			using var guidePaint = new SKPaint { Color = new SKColor(0x3C, 0xD2, 0x5A), StrokeWidth = 1f, Style = SKPaintStyle.Stroke, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 3, 4 }, 0) };
			double far = (view.Width + view.Height) * 2.0 / view.Scale;
			foreach (var (from, dir) in this._guides)
			{
				var (fx, fy) = view.ToScreen(from.X, from.Y);
				var (tx, ty) = view.ToScreen(from.X + dir.X * far, from.Y + dir.Y * far);
				canvas.DrawLine((float)fx, (float)fy, (float)tx, (float)ty, guidePaint);
			}
			if (this._guideLabel != null && this._tracked is { } tp)
			{
				var (lx, ly) = view.ToScreen(tp.X, tp.Y);
				using var tf = new SKPaint { Color = SKColors.White, TextSize = 12f, IsAntialias = true, Typeface = OverlayTypeface };
				float tw = tf.MeasureText(this._guideLabel);
				var box = new SKRect((float)lx + 14, (float)ly + 14, (float)lx + 22 + tw, (float)ly + 32);
				using var bgp = new SKPaint { Color = new SKColor(0x2B, 0x2B, 0x2B, 230), Style = SKPaintStyle.Fill };
				canvas.DrawRect(box, bgp);
				canvas.DrawText(this._guideLabel, box.Left + 4, box.Bottom - 5, tf);
			}
		}

		// 5. UCS 아이콘 표시 (왼쪽 아래)
		this.DrawUcsIcon(canvas, view);
		// 5-1. 축척 막대 (UCS 아이콘 오른쪽)
		this.DrawScaleBar(canvas, view);

		// 6. 십자 커서 및 선택 상자 (화면 전체 십자선)
		this.DrawCrosshair(canvas, view);
	}

	private void DrawGrid(SKCanvas canvas, ViewTransform view)
	{
		var (minWx, maxWy) = view.ToWorld(0, 0);
		var (maxWx, minWy) = view.ToWorld(view.Width, view.Height);
		double worldW = maxWx - minWx;
		double worldH = maxWy - minWy;
		if (worldW <= 0 || worldH <= 0 || double.IsNaN(worldW) || double.IsNaN(worldH)) return;

		double stepX = this.GridSpacingX > 1e-6 ? this.GridSpacingX : 10.0;
		double stepY = this.GridSpacingY > 1e-6 ? this.GridSpacingY : 10.0;
		int majorEvery = Math.Max(1, this.GridMajorEvery);

		double pixelX = stepX * view.Scale;
		double pixelY = stepY * view.Scale;

		bool drawMinor = pixelX >= 4.0 && pixelY >= 4.0;
		double effectiveStepX = drawMinor ? stepX : (stepX * majorEvery);
		double effectiveStepY = drawMinor ? stepY : (stepY * majorEvery);

		if (effectiveStepX * view.Scale < 4.0 || effectiveStepY * view.Scale < 4.0)
		{
			return;
		}

		double startX = Math.Floor(minWx / effectiveStepX) * effectiveStepX;
		double endX = Math.Ceiling(maxWx / effectiveStepX) * effectiveStepX;
		double startY = Math.Floor(minWy / effectiveStepY) * effectiveStepY;
		double endY = Math.Ceiling(maxWy / effectiveStepY) * effectiveStepY;

		int countX = (int)Math.Max(0, (endX - startX) / effectiveStepX);
		int countY = (int)Math.Max(0, (endY - startY) / effectiveStepY);
		if (countX + countY > 1200) return;

		using var minorPaint = new SKPaint
		{
			Color = CadTheme.GridMinorSkia,
			StrokeWidth = 1f,
			IsAntialias = false,
		};
		using var majorPaint = new SKPaint
		{
			Color = CadTheme.GridMajorSkia,
			StrokeWidth = 1f,
			IsAntialias = false,
		};

		// 수직 격자선
		for (double gx = startX; gx <= endX; gx += effectiveStepX)
		{
			var (sx, _) = view.ToScreen(gx, 0);
			bool isMajor = Math.Abs(Math.Round(gx / stepX)) % majorEvery == 0;
			canvas.DrawLine((float)sx, 0, (float)sx, (float)view.Height, isMajor ? majorPaint : minorPaint);
		}

		// 수평 격자선
		for (double gy = startY; gy <= endY; gy += effectiveStepY)
		{
			var (_, sy) = view.ToScreen(0, gy);
			bool isMajor = Math.Abs(Math.Round(gy / stepY)) % majorEvery == 0;
			canvas.DrawLine(0, (float)sy, (float)view.Width, (float)sy, isMajor ? majorPaint : minorPaint);
		}
	}

	private void DrawCrosshair(SKCanvas canvas, ViewTransform view)
	{
		if (this._cursorScreenPos is { } cp)
		{
			float cx = (float)cp.X;
			float cy = (float)cp.Y;
			float armLen = Math.Max(18f, (float)Math.Min(view.Width, view.Height) * 0.05f);

			// 선택 상자: 평소 10×10, 고를 수 있는 객체 위면 20×20 (커진 상자 = 고르는 범위)
			float pb = (float)this.PickBoxPx;

			using var chPaint = new SKPaint
			{
				Color = SKColors.White,
				StrokeWidth = 1f,
				IsAntialias = false,
			};

			// 십자선 (화면 5% 길이, 중앙 선택 상자 안쪽은 띄움)
			canvas.DrawLine(cx - armLen, cy, cx - pb, cy, chPaint);
			canvas.DrawLine(cx + pb, cy, cx + armLen, cy, chPaint);
			canvas.DrawLine(cx, cy - armLen, cx, cy - pb, chPaint);
			canvas.DrawLine(cx, cy + pb, cx, cy + armLen, chPaint);

			// 중앙 pickbox 선택 상자 사각형
			using var pbPaint = new SKPaint
			{
				Color = SKColors.White,
				StrokeWidth = 1f,
				Style = SKPaintStyle.Stroke,
				IsAntialias = false,
			};
			canvas.DrawRect(cx - pb, cy - pb, pb * 2, pb * 2, pbPaint);
		}
	}

	private void DrawUcsIcon(SKCanvas canvas, ViewTransform view)
	{
		float ox = 35f;
		float oy = (float)view.Height - 35f;
		const float len = 25f;

		// 원점 사각형 (흰색 테두리)
		using var boxPaint = new SKPaint
		{
			Color = SKColors.White,
			StrokeWidth = 1.2f,
			Style = SKPaintStyle.Stroke,
			IsAntialias = false,
		};
		canvas.DrawRect(ox - 3, oy - 3, 6, 6, boxPaint);

		using var ucsPaint = new SKPaint
		{
			Color = SKColors.White,
			StrokeWidth = 1.5f,
			IsAntialias = true,
		};

		// X축 (오른쪽 화살표)
		canvas.DrawLine(ox + 3, oy, ox + len, oy, ucsPaint);
		canvas.DrawLine(ox + len, oy, ox + len - 5, oy - 3, ucsPaint);
		canvas.DrawLine(ox + len, oy, ox + len - 5, oy + 3, ucsPaint);

		// Y축 (위쪽 화살표)
		canvas.DrawLine(ox, oy - 3, ox, oy - len, ucsPaint);
		canvas.DrawLine(ox, oy - len, ox - 3, oy - len + 5, ucsPaint);
		canvas.DrawLine(ox, oy - len, ox + 3, oy - len + 5, ucsPaint);

		// 라벨 (흰색)
		using var textPaint = new SKPaint
		{
			Color = SKColors.White,
			TextSize = 10f,
			IsAntialias = true,
		};
		canvas.DrawText("X", ox + len + 4, oy + 4, textPaint);
		canvas.DrawText("Y", ox - 3, oy - len - 4, textPaint);
	}

	/// <summary>Avalonia 렌더 스레드에서 Skia 캔버스에 직접 장면과 오버레이를 그린다.</summary>
	private sealed class SceneDrawOperation : ICustomDrawOperation
	{
		private readonly DrawingCanvas _canvas;
		private readonly SkiaSceneRenderer? _renderer;
		private readonly LayoutRenderer? _layoutRenderer;
		private readonly ViewTransform _view;
		private readonly SKPicture? _overlay;
		private readonly SkiaSceneRenderer[] _pending;
		private readonly SKPicture? _grid;

		public SceneDrawOperation(DrawingCanvas canvas, Rect bounds, SkiaSceneRenderer? renderer, LayoutRenderer? layoutRenderer, ViewTransform view, SKPicture? overlay, SkiaSceneRenderer[] pending, SKPicture? grid)
		{
			this._pending = pending;
			this._grid = grid;
			this._canvas = canvas;
			this.Bounds = bounds;
			this._renderer = renderer;
			this._layoutRenderer = layoutRenderer;
			this._view = view;
			this._overlay = overlay;
		}

		public Rect Bounds { get; }

		public bool HitTest(Point p) => this.Bounds.Contains(p);

		public bool Equals(ICustomDrawOperation? other) => false;

		public void Dispose()
		{
			this._overlay?.Dispose();
			this._grid?.Dispose();
		}

		public void Render(ImmediateDrawingContext context)
		{
			var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
			if (lease == null) return;
			using var api = lease.Lease();
			if (this._layoutRenderer != null)
			{
				this._canvas.DrawSceneCached(api.SkCanvas, api.GrContext, this._layoutRenderer, this._view);
			}
			else if (this._renderer != null)
			{
				// 모형 공간: 배경 → 그리드 → 도면(투명 배경 캐시) 순서로 그려 그리드가 객체 선 아래에 오게 한다
				using (var bg = new SKPaint { Color = this._renderer.Background, Style = SKPaintStyle.Fill })
				{
					api.SkCanvas.DrawRect(new SKRect(0, 0, this._view.Width, this._view.Height), bg);
				}
				if (this._grid != null)
				{
					api.SkCanvas.DrawPicture(this._grid);
				}
				this._canvas.DrawSceneCached(api.SkCanvas, api.GrContext, this._renderer, this._view);
			}
			foreach (var p in this._pending)
			{
				p.Render(api.SkCanvas, this._view, drawBackground: false);
			}
			if (this._overlay != null)
			{
				api.SkCanvas.DrawPicture(this._overlay);
			}
		}
	}
}
