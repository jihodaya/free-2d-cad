using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Free2DCad.Rendering;

namespace Free2DCad.App;

/// <summary>
/// 작업 화면에서 바로 문자 고치기 (기존 CAD처럼).
/// 문자·여러 줄 문자: 문자가 원래 자리·모양 그대로 있고, 입력한 글자가 도면 문자에 바로 반영된다.
/// 커서와 선택 영역은 문자 위에 직접 그린다. 키 입력(한글 조합 포함)은 보이지 않는 입력 칸이 받는다.
/// 치수 문자처럼 커서 위치를 알 수 없는 경우는 문자 자리에 흰 입력 칸을 띄운다.
/// 단일 행: Enter = 확정. 여러 줄: Enter = 줄 바꿈, Ctrl+Enter = 확정. Esc = 취소. 문자 밖을 클릭해도 확정.
/// </summary>
internal sealed class InplaceTextEditor
{
	private readonly Canvas _layer;
	private readonly DrawingCanvas _canvas;
	private readonly DispatcherTimer _blink;
	private TextBox? _box;
	private Avalonia.Controls.ThemeVariantScope? _host;
	private TextPresenter? _presenter;
	private string _original = string.Empty;
	private Action<string>? _onCommit;
	private Action? _onCancel;
	private Action<bool>? _onTab;
	private bool _multiline;

	// 바로 반영 모드
	private Func<string, SceneText?>? _preview;
	private SceneText? _shown;
	private string? _lastShownText;
	private bool _caretOn = true;

	// 입력 칸 모드 (치수)
	private (double MinX, double MinY, double MaxX, double MaxY) _world;
	private double _textHeight;

	public InplaceTextEditor(Canvas layer, DrawingCanvas canvas)
	{
		this._layer = layer;
		this._canvas = canvas;
		canvas.ViewChanged += this.Reposition;
		this._blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
		this._blink.Tick += (_, _) =>
		{
			this._caretOn = !this._caretOn;
			this.UpdateCaret();
		};
	}

	public bool IsActive => this._box != null;

	/// <summary>편집이 시작·끝날 때 (리본의 문자 편집기 탭을 보이고 숨기는 데 씀).</summary>
	public event Action? Opened;

	public event Action? Closed;

	/// <summary>커서·선택이 바뀔 때 (리본 버튼 눌림 상태 갱신).</summary>
	public event Action? SelectionMoved;

	/// <summary>입력으로 글이 바뀐 뒤 (이전 글, 새 글).</summary>
	public event Action<string, string>? Edited;

	/// <summary>자동 대문자 (기존 CAD 도구 → 자동 대문자): 치는 글자를 대문자로.</summary>
	public bool AutoUpper { get; set; }

	private string _lastText = string.Empty;
	private bool _changingCase;

	/// <summary>커서 위치 (선택이 없을 때).</summary>
	public int CaretIndex => this._box?.CaretIndex ?? 0;

	/// <summary>포커스가 이 컨트롤로 옮겨 가도 확정하지 않을지 (리본 문자 편집기 탭·그 펼침 목록).</summary>
	public Func<IInputElement?, bool>? KeepOpenFor { get; set; }

	/// <summary>바로 반영 모드인지 (글자별 서식을 넣을 수 있음).</summary>
	public bool IsLiveMode => this.IsLive;

	/// <summary>입력 중인 글 (보이는 글자).</summary>
	public string CurrentText => this._box?.Text ?? string.Empty;

	private int _selStart, _selEnd;

	/// <summary>마지막 선택 영역 (리본 버튼을 눌러 포커스가 빠져도 기억).</summary>
	public (int Start, int End) LastSelection => (Math.Min(this._selStart, this._selEnd), Math.Max(this._selStart, this._selEnd));

	/// <summary>입력 칸으로 포커스를 되돌리고 선택 영역 복원.</summary>
	public void Refocus()
	{
		if (this._box is not { } box) return;
		var (a, b) = this.LastSelection;
		box.Focus();
		box.SelectionStart = a;
		box.SelectionEnd = b;
	}

	/// <summary>서식을 바꾼 뒤 도면 문자를 다시 그린다.</summary>
	public void RefreshPreview() => this.Refresh(force: true);

	/// <summary>커서 자리에 글 넣기 (선택 영역은 바뀜).</summary>
	public void InsertAtCaret(string text)
	{
		if (this._box is not { } box) return;
		var (a, b) = this.LastSelection;
		string cur = box.Text ?? string.Empty;
		a = Math.Clamp(a, 0, cur.Length);
		b = Math.Clamp(b, a, cur.Length);
		box.Text = cur[..a] + text + cur[b..];
		this._selStart = this._selEnd = a + text.Length;
		this.Refocus();
	}

	/// <summary>입력 내용 전체 바꾸기 (대·소문자 변환 등).</summary>
	public void ReplaceText(string text)
	{
		if (this._box is not { } box) return;
		var (a, b) = this.LastSelection;
		box.Text = text;
		this._selStart = Math.Min(a, text.Length);
		this._selEnd = Math.Min(b, text.Length);
		this.Refocus();
	}

	public void UndoEdit()
	{
		if (this._box is { } box && box.CanUndo) box.Undo();
		this.Refocus();
	}

	public void RedoEdit()
	{
		if (this._box is { } box && box.CanRedo) box.Redo();
		this.Refocus();
	}

	private bool IsLive => this._preview != null;

	/// <summary>
	/// 바로 반영 모드 시작. preview(글) = 도면 문자에 그 글을 임시로 넣고 다시 그린 뒤 커서 위치표가 든 SceneText를 돌려준다.
	/// commit(최종 글) / cancel() 에서 원래 값 복원과 되돌리기 기록을 한다.
	/// </summary>
	public void BeginLive(string text, bool multiline, Func<string, SceneText?> preview, Action<string> onCommit, Action onCancel, Action<bool>? onTab = null)
	{
		this.Commit();
		this._preview = preview;
		this._onCancel = onCancel;
		this._onTab = onTab;
		this.Start(text, multiline, onCommit, visible: false);
		this.Refresh(force: true);
		this._blink.Start();
	}

	/// <summary>입력 칸 모드 시작 (치수 문자 등). world = 문자가 차지하는 도면 영역, textHeight = 도면 단위 문자 높이.</summary>
	public void Begin(string text, bool multiline, (double MinX, double MinY, double MaxX, double MaxY) world, double textHeight, string? hint, Action<string> onCommit)
	{
		this.Commit();
		this._preview = null;
		this._onCancel = null;
		this._onTab = null;
		this._world = world;
		this._textHeight = textHeight > 0 ? textHeight : Math.Max(world.MaxY - world.MinY, 1);
		this.Start(text, multiline, onCommit, visible: true);
		if (!string.IsNullOrEmpty(hint) && this._box != null)
		{
			ToolTip.SetTip(this._box, hint);
		}
		this.Reposition();
	}

	private void Start(string text, bool multiline, Action<string> onCommit, bool visible)
	{
		this._original = text;
		this._onCommit = onCommit;
		this._multiline = multiline;
		this._shown = null;
		this._lastShownText = null;

		var box = new TextBox
		{
			Text = text,
			AcceptsReturn = multiline,
			AcceptsTab = multiline,
			TextWrapping = TextWrapping.NoWrap,
			Background = Brushes.White,
			Foreground = Brushes.Black,
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF)),
			BorderThickness = new Thickness(1.5),
			CaretBrush = Brushes.Black,
			SelectionBrush = new SolidColorBrush(Color.FromRgb(0x99, 0xC9, 0xFF)),
			SelectionForegroundBrush = Brushes.Black,
			FontFamily = new FontFamily("Malgun Gothic"),
			Padding = new Thickness(3, 1),
			MinHeight = 0,
			CornerRadius = new CornerRadius(0),
		};
		if (!visible)
		{
			// 보이지 않는 입력 칸: 키·한글 조합만 받는다 (한글 조합 창이 커서 근처에 뜨도록 커서 자리에 둔다)
			box.Opacity = 0;
			box.IsHitTestVisible = false;
			box.Width = 240;
			box.FontSize = 14;
		}
		// 여러 줄 입력 칸은 Enter를 스스로 처리(줄 바꿈)하므로 먼저 가로챈다 (Ctrl+Enter = 확정)
		box.AddHandler(InputElement.KeyDownEvent, this.OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		// 포커스를 잃으면 확정. 단, 도면 클릭은 창이 먼저 캔버스로 포커스를 옮긴 뒤 들어오므로
		// 잠시 뒤에도 포커스가 돌아오지 않았을 때만 (문자 안 클릭은 커서 이동 후 포커스를 되돌린다)
		box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
		{
			if (ReferenceEquals(this._box, box) && !box.IsFocused)
			{
				var focused = TopLevel.GetTopLevel(box)?.FocusManager?.GetFocusedElement();
				if (this.KeepOpenFor?.Invoke(focused) == true)
				{
					return; // 리본 문자 편집기 탭을 누른 경우: 편집 계속
				}
				this.Commit();
			}
		}, DispatcherPriority.Background);
		box.TextChanged += (_, _) =>
		{
			string now = box.Text ?? string.Empty;
			if (this.AutoUpper && !this._changingCase && now != now.ToUpperInvariant())
			{
				int ci = box.CaretIndex;
				this._changingCase = true;
				box.Text = now.ToUpperInvariant();
				box.CaretIndex = ci;
				this._changingCase = false;
				return;
			}
			string before = this._lastText;
			this._lastText = box.Text ?? string.Empty;
			this.Refresh(force: false);
			if (!this._changingCase && before != this._lastText) this.Edited?.Invoke(before, this._lastText);
		};
		box.PropertyChanged += (_, e) =>
		{
			if (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
			{
				if (box.IsFocused)
				{
					this._selStart = box.SelectionStart;
					this._selEnd = box.SelectionEnd;
					this.SelectionMoved?.Invoke();
				}
				this._caretOn = true;
				this.UpdateCaret();
			}
		};
		box.TemplateApplied += (_, e) =>
		{
			// 한글 조합 중인 글자(아직 확정 전)도 도면에 바로 보이도록
			this._presenter = e.NameScope.Find<TextPresenter>("PART_TextPresenter");
			if (this._presenter != null)
			{
				this._presenter.PropertyChanged += (_, pe) =>
				{
					if (pe.Property == TextPresenter.PreeditTextProperty || pe.Property == TextPresenter.PreeditTextCursorPositionProperty)
					{
						this.Refresh(force: false);
					}
				};
			}
		};
		this._box = box;
		this._selStart = 0;
		this._selEnd = text.Length;
		this._lastText = text;
		this.AutoUpper = false;
		// 어두운 테마의 입력 칸 상태에서 바탕이 검게 바뀌지 않도록 밝은 테마로 감싼다
		this._host = new Avalonia.Controls.ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light, Child = box };
		this._layer.Children.Add(this._host);
		this.Opened?.Invoke();

		// 더블클릭의 마우스 떼기 처리가 끝난 뒤 포커스를 옮긴다 (바로 옮기면 캔버스가 포커스를 되가져간다)
		Dispatcher.UIThread.Post(() =>
		{
			if (ReferenceEquals(this._box, box))
			{
				box.Focus();
				box.SelectAll();
			}
		}, DispatcherPriority.Input);
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			this.Cancel();
			e.Handled = true;
		}
		else if (e.Key == Key.Enter && (!this._multiline || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
		{
			this.Commit();
			e.Handled = true;
		}
		else if (e.Key == Key.Tab && this._onTab is { } onTab)
		{
			// 표 칸: Tab = 다음 칸, Shift+Tab = 이전 칸
			bool back = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
			e.Handled = true;
			this.Commit();
			onTab(back);
		}
	}

	private string PreeditText => this._presenter?.PreeditText ?? string.Empty;

	/// <summary>입력 내용(조합 중 글자 포함)을 도면 문자에 반영하고 커서를 다시 그린다.</summary>
	private void Refresh(bool force)
	{
		if (this._box is not { } box || this._preview is not { } preview)
		{
			return;
		}
		string text = box.Text ?? string.Empty;
		string pre = this.PreeditText;
		int ci = Math.Clamp(box.CaretIndex, 0, text.Length);
		string shown = pre.Length > 0 ? text.Insert(ci, pre) : text;
		if (force || shown != this._lastShownText)
		{
			this._lastShownText = shown;
			var st = preview(shown);
			if (st?.Caret != null)
			{
				this._shown = st;
			}
			else if (this._shown?.Caret is { Length: >= 4 } old)
			{
				// 글자를 모두 지웠을 때: 첫 글자 자리에 커서만
				this._shown = this._shown with { Caret = old[..4] };
			}
		}
		this._caretOn = true;
		this.UpdateCaret();
	}

	private string? _spellText;

	/// <summary>맞춤법 검사를 다시 (켜고 끌 때).</summary>
	public void RecheckSpelling()
	{
		this._spellText = null;
		this._canvas.InplaceSpellErrors = null;
		this.UpdateCaret();
		this._canvas.InvalidateVisual();
	}

	/// <summary>편집 글자 [start, start+len)을 바꾼다 (맞춤법 추천 고르기).</summary>
	public void ReplaceRange(int start, int len, string with)
	{
		if (this._box is not { } box) return;
		string t = box.Text ?? string.Empty;
		if (start < 0 || start + len > t.Length) return;
		box.Text = t[..start] + with + t[(start + len)..];
		box.CaretIndex = start + with.Length;
		box.SelectionStart = box.SelectionEnd = box.CaretIndex;
		this.Refocus();
	}

	/// <summary>잘라내기·복사·붙여넣기 (편집 메뉴).</summary>
	public void Cut() { this._box?.Cut(); this.Refocus(); }

	public void Copy() { this._box?.Copy(); this.Refocus(); }

	public void Paste() { this._box?.Paste(); this.Refocus(); }

	/// <summary>선택한 글이 있는지.</summary>
	public bool HasSelection => this._box is { } b && b.SelectionStart != b.SelectionEnd;

	/// <summary>모두 선택.</summary>
	public void SelectAll()
	{
		if (this._box is not { } box) return;
		box.SelectionStart = 0;
		box.SelectionEnd = box.Text?.Length ?? 0;
		this.Refocus();
		this.UpdateCaret();
	}

	private void UpdateCaret()
	{
		if (this._box is not { } box || !this.IsLive || this._shown?.Caret is not { } stops)
		{
			return;
		}
		int n = stops.Length / 4;
		string pre = this.PreeditText;
		int caret = box.CaretIndex + (pre.Length > 0 ? (this._presenter?.PreeditTextCursorPosition ?? pre.Length) : 0);
		int selStart = pre.Length > 0 ? caret : box.SelectionStart;
		int selEnd = pre.Length > 0 ? caret : box.SelectionEnd;
		string now = box.Text ?? string.Empty;
		if (!ReferenceEquals(now, this._spellText))
		{
			// 맞춤법 검사 (글이 바뀔 때만, 조합 중인 한글은 제외)
			this._spellText = now;
			this._canvas.InplaceSpellErrors = TextSpellChecker.Check(now);
		}
		this._canvas.InplaceCaret = new DrawingCanvas.InplaceCaretState(stops, this._shown.Rotation,
			Math.Clamp(caret, 0, n - 1), Math.Clamp(selStart, 0, n - 1), Math.Clamp(selEnd, 0, n - 1), this._caretOn, this._shown.Bounds);
		this.Reposition();
	}

	/// <summary>입력 칸 위치: 입력 칸 모드는 문자 자리, 바로 반영 모드는 (보이지 않는 칸을) 커서 자리에.</summary>
	public void Reposition()
	{
		if (this._box is not { } box || this._host == null || this._canvas.View is not { } view)
		{
			return;
		}
		if (this.IsLive)
		{
			if (this._canvas.InplaceCaret is { } st && st.Stops.Length >= 4)
			{
				int i = st.Caret * 4;
				var (sx, sy) = view.ToScreen(st.Stops[i], st.Stops[i + 1]);
				Canvas.SetLeft(this._host, Math.Clamp(sx, 0, Math.Max(this._layer.Bounds.Width - 20, 0)));
				Canvas.SetTop(this._host, Math.Clamp(sy, 0, Math.Max(this._layer.Bounds.Height - 20, 0)));
			}
			return;
		}
		var (x0, y0) = view.ToScreen(this._world.MinX, this._world.MaxY);
		var (x1, y1) = view.ToScreen(this._world.MaxX, this._world.MinY);
		double capPx = this._textHeight * view.Scale;
		box.FontSize = Math.Clamp(capPx / 0.72, 12, 64);
		double w = Math.Abs(x1 - x0);
		double h = Math.Abs(y1 - y0);
		box.MinWidth = Math.Max(w + 16, 160);
		box.MaxWidth = Math.Max(this._layer.Bounds.Width - 8, 200);
		if (this._multiline)
		{
			box.MinHeight = h + 10;
		}
		Canvas.SetLeft(this._host, Math.Clamp(Math.Min(x0, x1) - 5, 0, Math.Max(this._layer.Bounds.Width - box.MinWidth, 0)));
		Canvas.SetTop(this._host, Math.Max(Math.Min(y0, y1) - 5, 0));
	}

	/// <summary>
	/// 편집 중 도면 클릭: 편집 중인 문자 안이면 그 자리로 커서를 옮기고 true, 밖이면 false(호출한 쪽이 확정).
	/// </summary>
	public bool TryPlaceCaret(double wx, double wy)
	{
		if (this._box is not { } box || !this.IsLive || this._shown?.Caret is not { } stops)
		{
			return false;
		}
		var f = this._shown.Bounds;
		double pad = this._shown.Height * 0.5;
		if (wx < f.MinX - pad || wx > f.MaxX + pad || wy < f.MinY - pad || wy > f.MaxY + pad)
		{
			return false;
		}
		// 가장 가까운 커서 경계 (글자 위쪽 방향으로 줄 높이만큼 올라간 선분까지 거리)
		double ux = -Math.Sin(this._shown.Rotation), uy = Math.Cos(this._shown.Rotation);
		int best = 0;
		double bestD = double.MaxValue;
		for (int i = 0; i * 4 + 3 < stops.Length; i++)
		{
			double x = stops[i * 4], y = stops[i * 4 + 1], h = stops[i * 4 + 2];
			double t = Math.Clamp((wx - x) * ux + (wy - y) * uy, -0.3 * h, 1.15 * h);
			double dx = wx - (x + ux * t), dy = wy - (y + uy * t);
			double d = dx * dx + dy * dy;
			if (d < bestD)
			{
				bestD = d;
				best = i;
			}
		}
		int len = box.Text?.Length ?? 0;
		int idx = Math.Clamp(best, 0, len);
		box.CaretIndex = idx;
		box.SelectionStart = idx;
		box.SelectionEnd = idx;
		box.Focus();
		this._caretOn = true;
		this.UpdateCaret();
		return true;
	}

	/// <summary>입력 내용을 반영하고 닫는다.</summary>
	public void Commit()
	{
		if (this._box is not { } box)
		{
			return;
		}
		string text = box.Text ?? string.Empty;
		var onCommit = this._onCommit;
		bool live = this.IsLive;
		this.Close();
		// 바로 반영 모드는 원래 값 복원을 위해 바뀌지 않았어도 알린다
		if (live || text != this._original)
		{
			onCommit?.Invoke(text);
		}
	}

	public void Cancel()
	{
		var onCancel = this._onCancel;
		this.Close();
		onCancel?.Invoke();
	}

	private void Close()
	{
		if (this._box == null)
		{
			return;
		}
		this._blink.Stop();
		this._box = null;
		this._onCommit = null;
		this._onCancel = null;
		this._onTab = null;
		this._preview = null;
		this._presenter = null;
		this._shown = null;
		if (this._host != null)
		{
			this._layer.Children.Remove(this._host);
			this._host.Child = null;
			this._host = null;
		}
		this._canvas.InplaceCaret = null;
		this._canvas.InplaceSpellErrors = null;
		this._spellText = null;
		this._canvas.Focus();
		this._canvas.InvalidateVisual();
		this.Closed?.Invoke();
	}
}
