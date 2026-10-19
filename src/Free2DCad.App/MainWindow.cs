using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Color = ACadSharp.Color;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Free2DCad.Core.Commands;
using Free2DCad.Core.Hatching;
using CSMath;
using Free2DCad.Rendering;
using SkiaSharp;
using Point = Avalonia.Point;
using Layout = ACadSharp.Objects.Layout;

using Free2DCad.Core.Tables;
namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 Free 2D CAD 메인 윈도우.
/// 제목 줄 빠른 실행 도구, 클래식 메뉴 막대, 기존 CAD 리본, 좌측 속성 팔레트, 하단 명령줄/상태표시줄을 제공한다.
/// </summary>
public sealed partial class MainWindow : Window
{
	private readonly DrawingCanvas _canvas = new();
	private readonly RibbonControl _ribbon = new();
	private readonly PropertyPanel _propertyPanel = new();
	private readonly GridSplitter _propertySplitter = new();
	private readonly CommandLineControl _cmdLine = new();

	// 상단 제목 표시줄 및 도면 탭
	private readonly TextBlock _titleDocName = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CadTheme.TitleBarFg, IsHitTestVisible = false };
	private readonly ComboBox _titleLayerCombo = new();
	private readonly TextBlock _tabDocName = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Avalonia.Thickness(6, 0) };
	private readonly Border _docTabBorder = new();
	private readonly Border _startPageTabBorder = new();
	private readonly TextBlock _startPageTabText = new() { VerticalAlignment = VerticalAlignment.Center };
	private readonly StartPageControl _startPageControl = new();
	private readonly Grid _centerGrid = new();
	private readonly Canvas _inplaceLayer = new() { ClipToBounds = true };
	private InplaceTextEditor _inplaceEditor = null!;
	private readonly Panel _centerContainer = new();
	private Layer? _previousLayer;
	private bool _isStartPageActive;

	// 하단 상태 표시줄 요소
	private readonly TextBlock _coords = new() { Margin = new Avalonia.Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
	private readonly TextBlock _statusMsg = new() { Margin = new Avalonia.Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
	private readonly Button _btnModel = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnGrid = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnSnapMode = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnOrtho = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnPolar = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnOsnap = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnOtrack = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnLwt = new() { Margin = new Avalonia.Thickness(1, 0) };
	private readonly Button _btnScale = new() { Margin = new Avalonia.Thickness(1, 0), Content = "1:1" };

	private bool _polarTracking;
	private bool _showLineweight;

	private EditorDocument? _doc;
	private bool _forceClose;
	private string? _lastCommandName;

	private StackPanel _layoutTabStack = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
	private readonly Dictionary<string, ViewTransform> _spaceViews = new(StringComparer.OrdinalIgnoreCase);
	private string _activeSpaceName = "Model";
	private Layout? _activeLayout;
	private SkiaSceneRenderer? _modelRenderer;

	private static readonly FilePickerFileType DwgFiles = new("CAD 도면 (*.dwg, *.dxf)") { Patterns = new[] { "*.dwg", "*.dxf" } };

	public MainWindow(string? initialPath)
	{
		CommandLineControl.AddUiCommand("PLOT", "플롯(인쇄)", "PRINT");
		CommandLineControl.AddUiCommand("REGEN", "화면 재생성", "RE");
		CommandLineControl.AddUiCommand("REGENALL", "모든 뷰포트 재생성", "REA");
		CommandLineControl.AddUiCommand("RESETSETTINGS", "설정 초기화");
		CommandLineControl.AddUiCommand("OPTIONS", "옵션 (도면 설정)", "OP");
		CommandLineControl.AddUiCommand("SAVE", "저장", "QSAVE");
		CommandLineControl.AddUiCommand("SAVEAS", "다른 이름으로 저장 (DWG 2000~2018, DXF, DWT)");
		CommandLineControl.AddUiCommand("OPEN", "도면 열기");
		CommandLineControl.AddUiCommand("NEW", "새로 만들기", "QNEW");
		CommandLineControl.AddUiCommand("BLOCKLIB", "블록 라이브러리");
		Free2DCad.Core.UserFolders.EnsureAll();
		CommandLineControl.AddUiCommand("BLOCK", "블록 작성", "B");
		CommandLineControl.AddUiCommand("INSERT", "블록 삽입", "I");
		CommandLineControl.AddUiCommand("EATTEDIT", "속성 편집", "ATE");
		CommandLineControl.AddUiCommand("BEDIT", "블록 편집기");
		CommandLineControl.AddUiCommand("PROPERTIES", "속성 팔레트", "PR", "PROPS", "CH");
		CommandLineControl.AddUiCommand("HATCH", "해치", "H", "BH", "BHATCH");
		CommandLineControl.AddUiCommand("GRADIENT", "그라데이션");
		CommandLineControl.AddUiCommand("DSETTINGS", "도면 설정 대화상자", "DS", "SE");
		CommandLineControl.AddUiCommand("UNITS", "도면 단위 설정", "UN");
		CommandLineControl.AddUiCommand("OSNAP", "객체 스냅 설정", "OS");
		CommandLineControl.AddUiCommand("STYLE", "문자 스타일 관리자", "ST");
		CommandLineControl.AddUiCommand("DIMSTYLE", "치수 스타일 관리자", "D", "DST", "DDIM");
		CommandLineControl.AddUiCommand("LINETYPE", "선종류 관리자", "LT", "LTYPE");

		this.Title = string.Empty;
		this.Width = 1440;
		this.Height = 920;
		this.WindowState = WindowState.Maximized;

		// 기존 CAD 제목 줄: 시스템 제목 줄은 끄고(테두리·크기 조절만 유지) 제목 줄과 창 버튼을 직접 그린다.
		// 시스템/Avalonia 창 버튼은 어두운 테마의 흰 글리프라 밝은 제목 줄에서 보이지 않았음
		this.WindowDecorations = WindowDecorations.BorderOnly;

		this._canvas.Document = null;
		var appSettings = CadAppSettings.Load();
		appSettings.ApplyToCanvas(this._canvas);
		this._polarTracking = this._canvas.PolarTrackingEnabled;
		CadAppSettings.CurrentCommandLineHeight = Math.Clamp(appSettings.CommandLineHeight, 50, 500);
		this._cmdLine.Height = CadAppSettings.CurrentCommandLineHeight;
		this._settingsLoaded = true;

		// 1. 최상단 제목 줄 (높이 약 26px, 배경 #FCFCFC)
		var titleBar = this.BuildTitleBar();

		// 2. 메뉴 막대 (높이 약 24px, 배경 #24282C, 글씨 12px)
		var menu = this.BuildMenuBar();

		// 3. 문서 탭 막대 (높이 약 24px, 배경 #343434)
		var docTabBar = this.BuildDocTabBar();

		// 4. 모형/배치 탭 막대 (캔버스 바로 아래)
		var modelLayoutBar = this.BuildModelLayoutBar();

		// 5. 중앙 작업 영역: 시작 페이지 및 (좌측 도킹 "속성" 팔레트 + 우측 도면 캔버스)
		var canvasDock = new DockPanel();
		DockPanel.SetDock(modelLayoutBar, Dock.Bottom);
		canvasDock.Children.Add(modelLayoutBar);
		// 캔버스 위 겹침 층: 문자 바로 편집 칸 (비어 있는 곳은 클릭이 캔버스로 통과)
		this._inplaceEditor = new InplaceTextEditor(this._inplaceLayer, this._canvas);
		this.SetupTextEditTab();
		// 문자 편집 중 도면 클릭: 그 문자 안이면 커서 이동, 밖이면 확정 (다른 CAD처럼)
		this._canvas.AddHandler(PointerPressedEvent, (_, e) =>
		{
			if (!this._inplaceEditor.IsActive)
			{
				return;
			}
			var pt = e.GetCurrentPoint(this._canvas);
			if (pt.Properties.IsLeftButtonPressed && this._canvas.View is { } view)
			{
				var (wx, wy) = view.ToWorld(pt.Position.X, pt.Position.Y);
				if (this._inplaceEditor.TryPlaceCaret(wx, wy))
				{
					e.Handled = true;
					return;
				}
			}
			// 오른쪽 클릭: 편집 메뉴 (맞춤법 추천 등, 기존 CAD와 같음) — 확정하지 않음
			if (pt.Properties.IsRightButtonPressed && this._canvas.View is { } rview)
			{
				var (rx, ry) = rview.ToWorld(pt.Position.X, pt.Position.Y);
				if (this.OnInplaceRightClick(rx, ry, pt.Position))
				{
					e.Handled = true;
					return;
				}
			}
			this._inplaceEditor.Commit();
		}, Avalonia.Interactivity.RoutingStrategies.Tunnel);
		canvasDock.Children.Add(new Panel { Children = { this._canvas, this._inplaceLayer } });

		this._centerGrid.ColumnDefinitions = new ColumnDefinitions("225,Auto,*");
		Grid.SetColumn(this._propertyPanel, 0);
		this._centerGrid.Children.Add(this._propertyPanel);

		this._propertySplitter.Width = 4;
		this._propertySplitter.Background = CadTheme.PropertyBorder;
		this._propertySplitter.ResizeDirection = GridResizeDirection.Columns;
		Grid.SetColumn(this._propertySplitter, 1);
		this._centerGrid.Children.Add(this._propertySplitter);

		Grid.SetColumn(canvasDock, 2);
		this._centerGrid.Children.Add(canvasDock);

		this._centerContainer.Children.Add(this._centerGrid);
		this._centerContainer.Children.Add(this._startPageControl);
		this._startPageControl.IsVisible = false;

		// 6. 하단 상태 표시줄
		var statusBar = this.BuildStatusBar();

		// 전체 루트 레이아웃 (위 -> 아래)
		var topStack = new StackPanel();
		topStack.Children.Add(titleBar);
		topStack.Children.Add(menu);
		topStack.Children.Add(this._ribbon);
		topStack.Children.Add(docTabBar);

		var root = new DockPanel();
		DockPanel.SetDock(topStack, Dock.Top);
		DockPanel.SetDock(statusBar, Dock.Bottom);
		DockPanel.SetDock(this._cmdLine, Dock.Bottom);
		var cmdSplitter = this.BuildCommandLineSplitter();
		DockPanel.SetDock(cmdSplitter, Dock.Bottom);
		root.Children.Add(topStack);
		root.Children.Add(statusBar);
		root.Children.Add(this._cmdLine);
		root.Children.Add(cmdSplitter);
		root.Children.Add(this._centerContainer);
		this.Content = root;

		// 시작 페이지 이벤트 연동
		this._startPageControl.NewRequested += () =>
		{
			this.NewDocumentWithTemplateChoice();
			this.SwitchToDrawingCanvas();
		};
		this._startPageControl.OpenRequested += () => _ = this.OpenAsync();
		this._startPageControl.FileOpenRequested += async path =>
		{
			await this.LoadAsync(path);
			this.SwitchToDrawingCanvas();
		};

		// 리본 이벤트 연동
		this._ribbon.CommandRequested += cmd => this.OnRibbonCommand(cmd);
		this._ribbon.LayerManagerRequested += () => this.ShowLayerManagerDialog();
		this._ribbon.PropertyPaletteToggleRequested += () => this.TogglePropertyPanel();
		this._ribbon.CommandLineToggleRequested += () => this._cmdLine.IsVisible = !this._cmdLine.IsVisible;
		this._ribbon.ExportPngRequested += () => _ = this.ExportPngAsync();
		this._ribbon.PlotRequested += () => this.ShowPlotDialog();
		this._ribbon.CurrentLayerChanged += layer =>
		{
			if (this._doc != null)
			{
				this._previousLayer = this._doc.CurrentLayer;
				this._doc.CurrentLayer = layer;
				if (this._canvas.SelectedEntities.Count > 0)
				{
					this._doc.SetLayer(this._canvas.SelectedEntities.ToList(), layer);
				}
				this.UpdateTitleLayerCombo();
			}
		};
		this._ribbon.ColorSelected += color =>
		{
			if (this._doc != null && this._canvas.SelectedEntities.Count > 0)
			{
				this._doc.SetColor(this._canvas.SelectedEntities.ToList(), color);
			}
		};
		this._ribbon.LinetypeManagerRequested += () => this.ShowLinetypeManagerDialog();
		this._ribbon.LineTypeSelected += name => this.OnLinetypeSelected(name);

		// 리본 도면층 도구 연동
		this._ribbon.PreviousLayerRequested += () =>
		{
			if (this._doc != null && this._previousLayer != null && this._doc.Document.Layers.Contains(this._previousLayer.Name))
			{
				var prev = this._previousLayer;
				this._previousLayer = this._doc.CurrentLayer;
				this._doc.CurrentLayer = prev;
				this._ribbon.UpdateLayers(this._doc.Document.Layers, prev);
				this.UpdateTitleLayerCombo();
				this._statusMsg.Text = $"도면층 \"{prev.Name}\"(으)로 복원";
			}
		};
		this._ribbon.SetCurrentLayerRequested += () =>
		{
			if (this._doc != null && this._canvas.SelectedEntities.Count > 0)
			{
				var first = this._canvas.SelectedEntities.First();
				if (first.Layer != null)
				{
					this._previousLayer = this._doc.CurrentLayer;
					this._doc.CurrentLayer = first.Layer;
					this._ribbon.UpdateLayers(this._doc.Document.Layers, first.Layer);
					this.UpdateTitleLayerCombo();
					this._statusMsg.Text = $"현재 도면층: {first.Layer.Name}";
				}
			}
		};
		this._ribbon.LayerOffRequested += () =>
		{
			if (this._doc != null)
			{
				this._doc.SetLayerOn(this._doc.CurrentLayer, false);
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};
		this._ribbon.LayerOnRequested += () =>
		{
			if (this._doc != null)
			{
				foreach (var lay in this._doc.Document.Layers)
				{
					if (!lay.IsOn) this._doc.SetLayerOn(lay, true);
				}
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};
		this._ribbon.LayerFreezeRequested += () =>
		{
			if (this._doc != null)
			{
				this._doc.SetLayerFrozen(this._doc.CurrentLayer, true);
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};
		this._ribbon.LayerThawRequested += () =>
		{
			if (this._doc != null)
			{
				foreach (var lay in this._doc.Document.Layers)
				{
					if (lay.Flags.HasFlag(LayerFlags.Frozen)) this._doc.SetLayerFrozen(lay, false);
				}
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};
		this._ribbon.LayerLockRequested += () =>
		{
			if (this._doc != null)
			{
				this._doc.SetLayerLocked(this._doc.CurrentLayer, true);
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
			}
		};
		this._ribbon.LayerUnlockRequested += () =>
		{
			if (this._doc != null)
			{
				this._doc.SetLayerLocked(this._doc.CurrentLayer, false);
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
			}
		};
		this._ribbon.MatchLayerRequested += () => this.StartCommandByName("MATCHPROP");
		this._ribbon.LayerDeleteRequested += () =>
		{
			if (this._doc != null)
			{
				if (this._doc.CanRemoveLayer(this._doc.CurrentLayer))
				{
					var target = this._doc.CurrentLayer;
					this._doc.CurrentLayer = this._doc.Document.Layers["0"] ?? this._doc.Document.Layers.First();
					this._doc.RemoveLayer(target);
					this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
					this.UpdateTitleLayerCombo();
					this.RefreshCurrentSpaceView(keepView: true);
					this._statusMsg.Text = $"도면층 \"{target.Name}\" 삭제 완료";
				}
				else
				{
					this._statusMsg.Text = "현재 도면층 또는 '0' 도면층은 삭제할 수 없습니다.";
				}
			}
		};

		// 속성 팔레트 이벤트 연동
		this._propertyPanel.CloseRequested += () => this.TogglePropertyPanel(false);
		this._propertyPanel.TableCsvRequested += csv => _ = this.SaveTableCsvAsync(csv);
		this._propertyPanel.PickRequested += () =>
		{
			this._canvas.Focus();
			this._cmdLine.SetPrompt("객체 선택:");
		};
		this._propertyPanel.PickAddToggled += () =>
		{
			this._canvas.PickAdd = !this._canvas.PickAdd;
			this._cmdLine.Print($"PICKADD = {(this._canvas.PickAdd ? "1 (추가 모드)" : "0 (단일 모드)")}");
		};
		this._propertyPanel.SelectAllRequested += () => this._canvas.SelectAll();
		this._propertyPanel.ClearSelectionRequested += () => this._canvas.ClearSelection();

		// 캔버스 이벤트 연동
		this._canvas.CursorWorldChanged += (x, y) => this._coords.Text = $"{x:F2},{y:F2},0";
		this._canvas.SelectionChanged += () =>
		{
			this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
		};
		this._canvas.ViewChanged += () => Dispatcher.UIThread.Post(() => this._propertyPanel.RefreshViewProperties());
		this._canvas.ObjectSnapToggled += _ => this.UpdateStatusBarToggles();
		this._canvas.OrthoToggled += _ => this.UpdateStatusBarToggles();
		this._canvas.GridToggled += _ => this.UpdateStatusBarToggles();
		this._canvas.SnapToggled += _ => this.UpdateStatusBarToggles();
		this._canvas.CommandPromptChanged += prompt => this._cmdLine.SetPrompt(prompt);
		this._canvas.CommandFinished += () =>
		{
			this.OnCommandCompleted(this._runningCommand);
			this._canvas.ClearTracking();
			this._cmdLine.SetPrompt("명령어:");
			this._canvas.InvalidateVisual();
		};

		// 캔버스 키 입력을 명령줄로 전달
		this._canvas.CharacterTyped += text => this._cmdLine.AppendText(text);
		this._canvas.EnterOrSpacePressed += space =>
		{
			if (space) this._cmdLine.SpaceOrSubmit();
			else this._cmdLine.AcceptOrSubmit();
		};
		this._cmdLine.SpaceIsText = () => this._canvas.ActiveCommand is { IsFinished: false, SpaceIsText: true };
		this._cmdLine.HistoryRequested += this.ShowTextWindow;
		this._canvas.BackspacePressed += () => this._cmdLine.Backspace();
		this._canvas.CommandLineKey = key => this._cmdLine.HandleListKey(key);
		this._canvas.EscapePressed += () =>
		{
			// 자동완성 목록이 열려 있으면 목록만 닫는다 (기존 CAD와 동일)
			if (!this._cmdLine.CloseSuggestions())
			{
				this.OnCancelRequested();
			}
		};
		this._canvas.RightClickEnterOrRepeat += () => this.OnRightClickCanvas();
		this._canvas.EntityDoubleClicked += ent => _ = this.EditEntityTextAsync(ent);
		this._canvas.BeforeCommandInput += () => this._cmdLine.EchoPrompt(string.Empty);

		// 명령행 입력 처리
		this._cmdLine.CommandSubmitted += this.OnCommandSubmitted;
		this._cmdLine.CancelRequested += this.OnCancelRequested;

		// 안전망: 키보드 포커스가 캔버스에도 명령줄에도 없을 때(예: 커서 옆 자동완성 창이 닫힌 직후)
		// 아무도 처리하지 않은 키를 창 단위에서 받아 명령줄로 넘긴다. 이게 없으면 Esc·글자가 무시됐음
		this.AddHandler(KeyDownEvent, this.OnUnhandledKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
		this.AddHandler(TextInputEvent, this.OnUnhandledTextInput, Avalonia.Interactivity.RoutingStrategies.Bubble);

		// 단축키 설정 (기존 CAD 표준)
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F1"), Command = new RelayCommand(() => { this.ShowHelpDialog(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F2"), Command = new RelayCommand(() => { this.ShowTextWindow(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F3"), Command = new RelayCommand(() => { this._canvas.ObjectSnapEnabled = !this._canvas.ObjectSnapEnabled; this.UpdateStatusBarToggles(); this._canvas.InvalidateVisual(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F7"), Command = new RelayCommand(() => { this._canvas.GridEnabled = !this._canvas.GridEnabled; this.UpdateStatusBarToggles(); this._canvas.InvalidateVisual(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F8"), Command = new RelayCommand(() => { this.ToggleOrtho(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F9"), Command = new RelayCommand(() => { this._canvas.SnapEnabled = !this._canvas.SnapEnabled; this.UpdateStatusBarToggles(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F10"), Command = new RelayCommand(() => { this.TogglePolar(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("F11"), Command = new RelayCommand(() => { this.ToggleOtrack(); return Task.CompletedTask; }) });

		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+N"), Command = new RelayCommand(() => { this.NewDocumentWithTemplateChoice(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+O"), Command = new RelayCommand(this.OpenAsync) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+S"), Command = new RelayCommand(async () => await this.SaveAsync()) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Shift+S"), Command = new RelayCommand(async () => await this.SaveAsAsync()) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Z"), Command = new RelayCommand(() => { if (!this.InEditBox(tb => tb.Undo())) this._doc?.Undo(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+Y"), Command = new RelayCommand(() => { if (!this.InEditBox(tb => tb.Redo())) this._doc?.Redo(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+A"), Command = new RelayCommand(() => { if (!this.InEditBox(tb => tb.SelectAll())) this._canvas.SelectAll(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+D1"), Command = new RelayCommand(() => { this.TogglePropertyPanel(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+C"), Command = new RelayCommand(() => { if (!this.InEditBox(tb => tb.Copy())) this.StartCommandByName("COPYCLIP"); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+V"), Command = new RelayCommand(() => { if (!this.InEditBox(tb => tb.Paste())) this.StartCommandByName("PASTECLIP"); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Ctrl+P"), Command = new RelayCommand(() => { this.ShowPlotDialog(); return Task.CompletedTask; }) });
		this.KeyBindings.Add(new KeyBinding { Gesture = KeyGesture.Parse("Home"), Command = new RelayCommand(() => { this._canvas.ZoomExtents(); return Task.CompletedTask; }) });

		this.UpdateStatusBarToggles();

		if (!string.IsNullOrEmpty(initialPath))
		{
			this.Opened += async (_, _) => await this.LoadAsync(initialPath);
		}
		else
		{
			// 파일을 직접 열지 않고 시작하면 시작 페이지부터 (빈 새 도면은 뒤에 두고, 파일을 열면 그 파일로 대신함)
			this.NewDocument();
			this.SwitchToStartPage();
		}
	}

	/// <summary>배포 버전 (Free2DCad.App.csproj의 Version, 예: "0.3").</summary>
	internal static string AppVersion
	{
		get
		{
			var v = typeof(MainWindow).Assembly.GetName().Version;
			return v == null ? "0.3" : v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
		}
	}

	// 1. 최상단 제목 줄
	private Control BuildTitleBar()
	{
		var titleBar = new Grid
		{
			Height = 26,
			Background = CadTheme.TitleBarBg,
			ColumnDefinitions = new ColumnDefinitions("Auto,*,140"),
		};

		var leftStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Avalonia.Thickness(6, 0, 0, 0),
		};

		// 1) 앱 아이콘 (실행 파일 아이콘과 같은 로고)
		var appIcon = IconHelper.AppLogo(18);
		appIcon.Margin = new Avalonia.Thickness(0, 0, 4, 0);
		leftStack.Children.Add(appIcon);

		// 2) 빠른 실행 도구: 새로 만들기, 열기, 저장, 다른 이름으로 저장, 인쇄 자리 (어두운 아이콘)
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.New, "새로 만들기 (Ctrl+N)", () => this.NewDocumentWithTemplateChoice()));
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.Open, "열기 (Ctrl+O)", () => _ = this.OpenAsync()));
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.Save, "저장 (Ctrl+S)", () => _ = this.SaveAsync()));
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.SaveAs, "다른 이름으로 저장 (Ctrl+Shift+S)", () => _ = this.SaveAsAsync()));
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.Print, "플롯 / 인쇄 (Ctrl+P)", () => this.ShowPlotDialog()));

		leftStack.Children.Add(new Separator { Margin = new Avalonia.Thickness(2, 4), Background = CadTheme.TitleBarSeparator });

		// 3) 명령 취소▾, 명령 복구▾
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.Undo, "명령 취소 (Ctrl+Z)", () => this._doc?.Undo()));
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.Redo, "명령 복구 (Ctrl+Y)", () => this._doc?.Redo()));
		leftStack.Children.Add(new Separator { Margin = new Avalonia.Thickness(2, 4), Background = CadTheme.TitleBarSeparator });
		// 4) 설정 초기화: 무엇을 바꿨는지 기억나지 않을 때 화면·입력 설정을 처음 상태로 (도면 내용은 그대로)
		leftStack.Children.Add(this.CreateQuickButton(IconHelper.ResetSettings, "설정 초기화 (RESETSETTINGS) — 스냅·그리드·직교·객체 스냅·팔레트·명령줄 등을 처음 상태로", () => _ = this.ResetAllSettingsAsync()));

		Grid.SetColumn(leftStack, 0);
		titleBar.Children.Add(leftStack);

		// 가운데: Free 2D CAD - [파일명] (창 전체 너비 기준 중앙 정렬)
		Grid.SetColumn(this._titleDocName, 0);
		Grid.SetColumnSpan(this._titleDocName, 3);
		this._titleDocName.HorizontalAlignment = HorizontalAlignment.Center;
		titleBar.Children.Add(this._titleDocName);

		// 오른쪽: 최소화 / 최대화·복원 / 닫기 (밝은 제목 줄에서도 보이도록 어두운 글리프로 직접 그림)
		var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
		caption.Children.Add(CreateCaptionButton("M 0,5 H 10", "최소화", false, () => this.WindowState = WindowState.Minimized));
		var maxButton = CreateCaptionButton(MaximizeGlyph, "최대화", false, this.ToggleMaximize);
		caption.Children.Add(maxButton);
		caption.Children.Add(CreateCaptionButton("M 0,0 L 10,10 M 10,0 L 0,10", "닫기", true, this.Close));
		Grid.SetColumn(caption, 2);
		titleBar.Children.Add(caption);

		// 최대화 상태에 따라 □ / 겹친 □ 글리프 전환
		this.PropertyChanged += (_, e) =>
		{
			if (e.Property == Window.WindowStateProperty && maxButton.Content is Avalonia.Controls.Shapes.Path glyph)
			{
				bool max = this.WindowState == WindowState.Maximized;
				glyph.Data = StreamGeometry.Parse(max ? RestoreGlyph : MaximizeGlyph);
				ToolTip.SetTip(maxButton, max ? "이전 크기로" : "최대화");
			}
		};

		// 제목 줄을 끌면 창 이동, 더블클릭하면 최대화/복원 (버튼 위에서는 버튼이 먼저 처리)
		titleBar.PointerPressed += (_, e) =>
		{
			if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed || e.Source is Avalonia.Visual v && IsInsideButton(v))
			{
				return;
			}
			if (e.ClickCount == 2)
			{
				this.ToggleMaximize();
			}
			else
			{
				this.BeginMoveDrag(e);
			}
		};

		return titleBar;
	}

	private const string MaximizeGlyph = "M 0.5,0.5 H 9.5 V 9.5 H 0.5 Z";
	private const string RestoreGlyph = "M 2.5,0.5 H 9.5 V 7.5 M 0.5,2.5 H 7.5 V 9.5 H 0.5 Z";

	private void ToggleMaximize() =>
		this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

	private static bool IsInsideButton(Avalonia.Visual v)
	{
		for (Avalonia.Visual? p = v; p != null; p = Avalonia.VisualTree.VisualExtensions.GetVisualParent(p))
		{
			if (p is Button)
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>Windows 11 창 버튼과 같은 크기(46x26). 닫기는 마우스를 올리면 빨간 배경에 흰 글리프.</summary>
	private static Button CreateCaptionButton(string glyphData, string tip, bool isClose, Action onClick)
	{
		var glyph = new Avalonia.Controls.Shapes.Path
		{
			Data = StreamGeometry.Parse(glyphData),
			Stroke = CadTheme.TitleBarFg,
			StrokeThickness = 1,
			Width = 10,
			Height = 10,
			Stretch = Avalonia.Media.Stretch.None,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		var button = new Button
		{
			Width = 46,
			Height = 26,
			Padding = new Avalonia.Thickness(0),
			CornerRadius = new Avalonia.CornerRadius(0),
			BorderThickness = new Avalonia.Thickness(0),
			Background = Brushes.Transparent,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
			Content = glyph,
		};
		ToolTip.SetTip(button, tip);
		IBrush hoverBg = isClose ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE8, 0x11, 0x23)) : new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
		// Fluent 테마의 호버 배경을 덮기 위해 Button 자체 스타일로 지정
		button.Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Button>().Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
		{
			Setters = { new Avalonia.Styling.Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, hoverBg) },
		});
		button.PointerEntered += (_, _) => glyph.Stroke = isClose ? Brushes.White : CadTheme.TitleBarFg;
		button.PointerExited += (_, _) => glyph.Stroke = CadTheme.TitleBarFg;
		button.Click += (_, _) => onClick();
		return button;
	}

	/// <summary>명령을 시작하는 메뉴 항목 (명령 아이콘이 있으면 함께).</summary>
	private MenuItem C(string header, string command)
	{
		var mi = new MenuItem { Header = header, Icon = CommandLineControl.CommandIcon(command.Split('_')[0], 16) };
		mi.Click += (_, _) => this.StartCommandByName(command);
		return mi;
	}

	private static MenuItem Sub(string header, params object[] items) => new() { Header = header, ItemsSource = items };

	// 2. 메뉴 막대 (기존 CAD 클래식 메뉴)
	private Menu BuildMenuBar()
	{
		return new Menu
		{
			Background = CadTheme.MenuBarBg,
			Foreground = CadTheme.MenuBarFg,
			Height = 24,
			FontSize = 12,
			ItemsSource = new[]
			{
				new MenuItem
				{
					Header = "파일(_F)",
					ItemsSource = new object[]
					{
						Item("새로 만들기(_N)", "Ctrl+N", () => { this.NewDocumentWithTemplateChoice(); return Task.CompletedTask; }),
						Item("열기(_O)...", "Ctrl+O", this.OpenAsync),
						this.BuildRecentMenu(),
						Item("저장(_S)", "Ctrl+S", this.SaveAsync),
						Item("다른 이름으로 저장(_A)...", "Ctrl+Shift+S", this.SaveAsAsync),
						Item("플롯(인쇄) (PLOT)(_P)...", "Ctrl+P", () => { this.ShowPlotDialog(); return Task.CompletedTask; }),
						Item("PNG 내보내기...", null, this.ExportPngAsync),
						new Separator(),
						Item("끝내기(_X)", null, () => { this.Close(); return Task.CompletedTask; }),
					},
				},
				new MenuItem
				{
					Header = "편집(_E)",
					ItemsSource = new object[]
					{
						Item("명령 취소(_U)", "Ctrl+Z", () => { this._doc?.Undo(); return Task.CompletedTask; }),
						Item("명령 복구(_R)", "Ctrl+Y", () => { this._doc?.Redo(); return Task.CompletedTask; }),
						new Separator(),
						Item("잘라내기(_T)", "Ctrl+X", () => { this.StartCommandByName("CUTCLIP"); return Task.CompletedTask; }),
						Item("클립보드 복사(_C)", "Ctrl+C", () => { this.StartCommandByName("COPYCLIP"); return Task.CompletedTask; }),
						Item("붙여넣기(_P)", "Ctrl+V", () => { this.StartCommandByName("PASTECLIP"); return Task.CompletedTask; }),
						new Separator(),
						Item("모두 선택(_A)", "Ctrl+A", () => { this._canvas.SelectAll(); return Task.CompletedTask; }),
						Item("찾기 및 바꾸기(_F)... (FIND)", null, () => this.ShowFindDialogAsync()),
						Item("선택 해제", "Escape", () => { this._canvas.ClearSelection(); return Task.CompletedTask; }),
						Item("지우기(_D)", "Delete", () =>
						{
							if (this._canvas.SelectedEntities.Count > 0 && this._doc != null)
							{
								this._doc.Erase(this._canvas.SelectedEntities.ToList());
								this._canvas.ClearSelection();
							}
							return Task.CompletedTask;
						}),
					},
				},
				new MenuItem
				{
					Header = "뷰(_V)",
					ItemsSource = new object[]
					{
						Item("전체 보기(_E)", "Home", () => { this._canvas.ZoomExtents(); return Task.CompletedTask; }),
						Item("확대(_I)", null, () => { this.OnRibbonCommand("ZOOM_IN"); return Task.CompletedTask; }),
						Item("축소(_O)", null, () => { this.OnRibbonCommand("ZOOM_OUT"); return Task.CompletedTask; }),
						Item("초점 이동 (PAN)(_P)", null, () => { this.StartCommandByName("PAN"); return Task.CompletedTask; }),
						Sub("줌(_Z)",
							C("윈도우(_W)", "ZOOM_W"),
							C("이전(_P)", "ZOOM_P"),
							C("중심(_C)", "ZOOM_C"),
							C("객체(_O)", "ZOOM_O"),
							C("확대 2배(_I)", "ZOOM_2X"),
							C("축소 1/2(_O)", "ZOOM_HALF"),
							C("전체(_A)", "ZOOM_A")),
						C("재생성 (REGEN)(_G)", "REGEN"),
						new Separator(),
						Item("도면층 특성 관리자(_L)...", null, () => { this.ShowLayerManagerDialog(); return Task.CompletedTask; }),
						Item("속성 팔레트(_R)", "Ctrl+D1", () => { this.TogglePropertyPanel(); return Task.CompletedTask; }),
						Item("명령줄(_C)", null, () => { this._cmdLine.IsVisible = !this._cmdLine.IsVisible; return Task.CompletedTask; }),
					},
				},
				new MenuItem
				{
					Header = "삽입(_I)",
					ItemsSource = new object[]
					{
						Item("블록 삽입 (INSERT)(_I)...", null, () => { this.StartCommandByName("INSERT"); return Task.CompletedTask; }),
						Item("외부 참조 부착 (XATTACH)(_X)...", null, () => this.XAttachAsync()),
						Item("외부 참조 관리자 (XREF)(_R)...", null, () => this.ShowXrefManagerAsync()),
						Item("블록 라이브러리 (BLOCKLIB)(_B)...", null, () => { _ = this.ShowBlockLibraryAsync(); return Task.CompletedTask; }),
						new Separator(),
						Item("새 배치 (LAYOUT)(_L)...", null, () => { this.StartCommandByName("LAYOUT"); return Task.CompletedTask; }),
					},
				},
				new MenuItem
				{
					Header = "형식(_O)",
					ItemsSource = new object[]
					{
						Item("도면층 (LAYER)(_L)...", null, () => { this.ShowLayerManagerDialog(); return Task.CompletedTask; }),
						Sub("도면층 도구(_O)",
							C("객체의 도면층을 현재로 (LAYMCUR)", "LAYMCUR"),
							C("현재 도면층으로 변경 (LAYCUR)", "LAYCUR"),
							C("이전 도면층 상태 (LAYERP)", "LAYERP"),
							new Separator(),
							C("도면층 일치 (LAYMCH)", "LAYMCH"),
							C("도면층 격리 (LAYISO)", "LAYISO"),
							C("도면층 격리 해제 (LAYUNISO)", "LAYUNISO"),
							C("도면층 끄기 (LAYOFF)", "LAYOFF"),
							C("모든 도면층 켜기 (LAYON)", "LAYON"),
							C("도면층 동결 (LAYFRZ)", "LAYFRZ"),
							C("모든 도면층 동결 해제 (LAYTHW)", "LAYTHW"),
							C("도면층 잠금 (LAYLCK)", "LAYLCK"),
							C("도면층 잠금 해제 (LAYULK)", "LAYULK"),
							C("도면층 삭제 (LAYDEL)", "LAYDEL")),
						C("도면층 상태 관리자 (LAYERSTATE)(_A)...", "LAYERSTATE"),
						Item("색상 (COLOR)(_C)...", null, () => { this.ShowLayerManagerDialog(); return Task.CompletedTask; }),
						new Separator(),
						Item("선종류 (LINETYPE)(_T)...", null, () => { this.ShowLinetypeManagerDialog(); return Task.CompletedTask; }),
						Item("문자 스타일 (STYLE)(_S)...", null, () => { this.ShowTextStyleDialog(); return Task.CompletedTask; }),
						Item("치수 스타일 (DIMSTYLE)(_D)...", null, () => { this.ShowDimStyleDialog(); return Task.CompletedTask; }),
						C("점 스타일 (PTYPE)(_P)...", "PTYPE"),
						C("속성 표시 - 일반 (ATTDISP)", "ATTDISP_N"),
						new Separator(),
						Item("도면 설정 (DSETTINGS)(_E)...", null, () => { this.ShowDrawingSettingsDialog(); return Task.CompletedTask; }),
						Item("도면 단위 (UNITS)(_U)...", null, () => { this.ShowDrawingSettingsDialog(0, 0); return Task.CompletedTask; }),
					},
				},
				new MenuItem
				{
					Header = "도구(_T)",
					ItemsSource = new object[]
					{
						C("빠른 선택 (QSELECT)(_K)...", "QSELECT"),
						Sub("그리기 순서(_D)",
							C("맨 앞으로 가져오기(_F)", "DRAWORDER_F"),
							C("맨 뒤로 보내기(_B)", "DRAWORDER_B"),
							C("객체 앞으로 가져오기(_A)", "DRAWORDER_A"),
							C("객체 뒤로 보내기(_U)", "DRAWORDER_U"),
							new Separator(),
							C("해치를 뒤로 보내기 (HATCHTOBACK)", "HATCHTOBACK"),
							C("문자를 앞으로 가져오기", "TEXTTOFRONT_T"),
							C("치수를 앞으로 가져오기", "TEXTTOFRONT_D"),
							C("모든 주석을 앞으로 가져오기", "TEXTTOFRONT_A")),
						Sub("조회(_Q)",
							C("거리 (DIST)(_D)", "DIST"),
							C("면적 (AREA)(_A)", "AREA"),
							C("정보 목록 (LIST)(_L)", "LIST"),
							C("ID 점 (ID)(_I)", "ID"),
							C("총 길이 (TOTALLENGTH)(_T)", "TOTALLENGTH"),
							C("도면 상태 (STATUS)(_S)", "STATUS")),
						C("유사 선택 (SELECTSIMILAR)", "SELECTSIMILAR"),
						C("속성 추출 (ATTEXT)...", "ATTEXT"),
						Sub("그룹(_G)",
							C("그룹 (GROUP)(_G)", "GROUP"),
							C("빠른 그룹 (QGROUP)(_Q)", "QGROUP"),
							C("그룹 해제 (UNGROUP)(_U)", "UNGROUP"),
							C("그룹 선택 켜기/끄기", "GROUP_SELECT_TOGGLE")),
						C("표 추출(CSV) (TABLEOUT)(_T)...", "TABLEOUT"),
						C("특성 일치 (MATCHPROP)(_M)", "MATCHPROP"),
						new Separator(),
						Item("문자 윈도우 (F2)(_W)", "F2", () => { this.ShowTextWindow(); return Task.CompletedTask; }),
						Item("도움말 (F1)(_H)", "F1", () => { this.ShowHelpDialog(); return Task.CompletedTask; }),
						new Separator(),
						C("제도 설정 (DSETTINGS)(_F)...", "DSETTINGS"),
						Item("옵션 (OPTIONS)(_O)...", null, () => { this.ShowDrawingSettingsDialog(0, 0); return Task.CompletedTask; }),
						Item("설정 초기화 (RESETSETTINGS)...", null, this.ResetAllSettingsAsync),
						Item("사용자 폴더 열기 (글꼴·패턴·템플릿·선종류·블록)", null, () => { this.OpenUserFolder(); return Task.CompletedTask; }),
					},
				},
				new MenuItem
				{
					Header = "그리기(_D)",
					ItemsSource = new object[]
					{
						C("선 (LINE)(_L)", "LINE"),
						C("광선 (RAY)(_R)", "RAY"),
						C("구성선 (XLINE)(_T)", "XLINE"),
						C("다중선 (MLINE)(_U)", "MLINE"),
						new Separator(),
						C("폴리선 (PLINE)(_P)", "PLINE"),
						C("폴리곤 (POLYGON)(_Y)", "POLYGON"),
						C("직사각형 (RECTANG)(_G)", "RECTANG"),
						new Separator(),
						Sub("호(_A)",
							C("3점(_P)", "ARC"),
							new Separator(),
							C("시작점, 중심점, 끝점(_S)", "ARC_SCE"),
							C("시작점, 중심점, 각도(_T)", "ARC_SCA"),
							C("시작점, 중심점, 길이(_A)", "ARC_SCL"),
							new Separator(),
							C("시작점, 끝점, 각도(_N)", "ARC_SEA"),
							C("시작점, 끝점, 방향(_D)", "ARC_SED"),
							C("시작점, 끝점, 반지름(_R)", "ARC_SER"),
							C("시작점, 끝점, 중심점(_E)", "ARC_SEC"),
							new Separator(),
							C("중심점, 시작점, 끝점(_C)", "ARC_CSE"),
							C("중심점, 시작점, 각도(_E)", "ARC_CSA"),
							C("중심점, 시작점, 길이(_L)", "ARC_CSL")),
						Sub("원(_C)",
							C("중심점, 반지름(_R)", "CIRCLE"),
							C("중심점, 지름(_D)", "CIRCLE_D"),
							new Separator(),
							C("2점(_2)", "CIRCLE_2P"),
							C("3점(_3)", "CIRCLE_3P"),
							new Separator(),
							C("접선, 접선, 반지름(_T)", "CIRCLE_TTR"),
							C("접선, 접선, 접선(_A)", "CIRCLE_TTT")),
						C("도넛 (DONUT)(_D)", "DONUT"),
						C("구름형 수정 기호 (REVCLOUD)(_V)", "REVCLOUD"),
						Sub("스플라인(_S)",
							C("맞춤점(_F)", "SPLINE"),
							C("조정점(_C)", "SPLINE_CV")),
						Sub("타원(_E)",
							C("중심(_C)", "ELLIPSE_C"),
							C("축, 끝점(_E)", "ELLIPSE"),
							C("호(_A)", "ELLIPSE_A")),
						Sub("블록(_K)",
							C("만들기 (BLOCK)(_M)...", "BLOCK"),
							C("속성 정의 (ATTDEF)(_D)...", "ATTDEF"),
							C("기준점 (BASE)(_B)", "BASE"),
							C("블록을 파일로 저장 (WBLOCK)(_W)...", "WBLOCK")),
						C("테이블 (TABLE)(_B)...", "TABLE"),
						Sub("점(_O)",
							C("다중 점 (POINT)(_M)", "POINT_M"),
							C("등분할 (DIVIDE)(_D)", "DIVIDE"),
							C("길이 분할 (MEASURE)(_M)", "MEASURE")),
						new Separator(),
						C("해치 (HATCH)(_H)...", "HATCH"),
						C("그라데이션 (GRADIENT)...", "GRADIENT"),
						C("경계 (BOUNDARY)(_B)", "BOUNDARY"),
						C("2D 솔리드 (SOLID)", "SOLID"),
						C("와이프아웃 (WIPEOUT)", "WIPEOUT"),
						new Separator(),
						Sub("문자(_X)",
							C("여러 줄 문자 (MTEXT)(_M)", "MTEXT"),
							C("단일 행 문자 (TEXT)(_S)", "TEXT")),
					},
				},
				new MenuItem
				{
					Header = "치수(_N)",
					ItemsSource = new object[]
					{
						C("빠른 치수 (QDIM)(_Q)", "QDIM"),
						C("공차 (TOLERANCE)(_T)", "TOLERANCE"),
						C("선형 (DIMLINEAR)(_L)", "DIMLINEAR"),
						C("정렬 (DIMALIGNED)(_G)", "DIMALIGNED"),
						C("호 길이 (DIMARC)(_H)", "DIMARC"),
						C("세로좌표 (DIMORDINATE)(_O)", "DIMORDINATE"),
						C("반지름 (DIMRADIUS)(_R)", "DIMRADIUS"),
						C("지름 (DIMDIAMETER)(_D)", "DIMDIAMETER"),
						C("각도 (DIMANGULAR)(_A)", "DIMANGULAR"),
						C("꺾기 (DIMJOGGED)(_J)", "DIMJOGGED"),
						new Separator(),
						C("기준선 (DIMBASELINE)(_B)", "DIMBASELINE"),
						C("연속 (DIMCONTINUE)(_C)", "DIMCONTINUE"),
						new Separator(),
						C("지시선 (MLEADER)(_E)", "MLEADER"),
						C("빠른 지시선 (QLEADER)(_Q)", "QLEADER"),
						C("중심 표시 (DIMCENTER)(_M)", "DIMCENTER"),
						C("중심 표시 - 연장선 (CENTERMARK)", "CENTERMARK"),
						C("중심선 (CENTERLINE)", "CENTERLINE"),
						new Separator(),
						C("치수 간격 조정 (DIMSPACE)(_P)", "DIMSPACE"),
						Sub("치수 편집(_E)",
							C("새 문자 (DIMEDIT N)", "DIMEDIT_N"),
							C("문자 회전 (DIMEDIT R)", "DIMEDIT_R"),
							C("문자 위치 복원 (DIMEDIT H)", "DIMEDIT_H"),
							C("기울기 (DIMEDIT O)", "DIMEDIT_O"),
							C("문자 위치 (DIMTEDIT)", "DIMTEDIT")),
						new Separator(),
						C("치수 스타일 (DIMSTYLE)(_S)...", "DIMSTYLE"),
					},
				},
				new MenuItem
				{
					Header = "수정(_M)",
					ItemsSource = new object[]
					{
						C("특성 일치 (MATCHPROP)(_M)", "MATCHPROP"),
						C("도면층별로 설정 (SETBYLAYER)(_B)", "SETBYLAYER"),
						Sub("객체(_O)",
							C("폴리선 (PEDIT)(_P)", "PEDIT"),
							C("스플라인 (SPLINEDIT)(_S)", "SPLINEDIT"),
							C("배열 (ARRAYEDIT)(_A)", "ARRAYEDIT"),
							C("블록 편집기 (BEDIT)(_B)", "BEDIT"),
							C("속성 - 단일 (EATTEDIT)(_S)...", "EATTEDIT"),
							C("속성 - 전역 (-ATTEDIT)(_G)", "-ATTEDIT"),
							C("속성 동기화 (ATTSYNC)", "ATTSYNC"),
							C("문자 편집 (TEXTEDIT)(_T)", "TEXTEDIT")),
						new Separator(),
						C("지우기 (ERASE)(_E)", "ERASE"),
						C("복사 (COPY)(_Y)", "COPY"),
						C("복제 (ADDSELECTED)", "ADDSELECTED"),
						C("대칭 (MIRROR)(_I)", "MIRROR"),
						C("간격띄우기 (OFFSET)(_S)", "OFFSET"),
						Sub("배열(_A)",
							C("직사각형 배열 (ARRAYRECT)(_R)", "ARRAYRECT"),
							C("원형 배열 (ARRAYPOLAR)(_P)", "ARRAYPOLAR"),
							C("경로 배열 (ARRAYPATH)(_A)", "ARRAYPATH")),
						C("이동 (MOVE)(_V)", "MOVE"),
						C("회전 (ROTATE)(_R)", "ROTATE"),
						C("축척 (SCALE)(_L)", "SCALE"),
						C("신축 (STRETCH)(_H)", "STRETCH"),
						C("길이 조정 (LENGTHEN)(_G)", "LENGTHEN"),
						C("자르기 (TRIM)(_T)", "TRIM"),
						C("연장 (EXTEND)(_D)", "EXTEND"),
						C("끊기 (BREAK)(_K)", "BREAK"),
						C("점에서 끊기 (BREAKATPOINT)", "BREAKATPOINT"),
						C("결합 (JOIN)(_J)", "JOIN"),
						C("모따기 (CHAMFER)(_C)", "CHAMFER"),
						C("모깎기 (FILLET)(_F)", "FILLET"),
						C("반전 (REVERSE)", "REVERSE"),
						C("정렬 (ALIGN)", "ALIGN"),
						new Separator(),
						C("분해 (EXPLODE)(_X)", "EXPLODE"),
						C("문자를 여러 줄 문자로 (TXT2MTXT)", "TXT2MTXT"),
						C("문자 축척 (SCALETEXT)", "SCALETEXT"),
					},
				},
				new MenuItem
				{
					Header = "창(_W)",
					ItemsSource = new object[]
					{
						Item("닫기(_C)", null, async () => { if (this._session != null) await this.CloseSessionAsync(this._session); }),
					},
				},
				new MenuItem
				{
					Header = "도움말(_H)",
					ItemsSource = new object[]
					{
						Item("Free 2D CAD 도움말 (F1)(_H)", "F1", () => { this.ShowHelpDialog(); return Task.CompletedTask; }),
						Item("문자 윈도우 (F2)(_W)", "F2", () => { this.ShowTextWindow(); return Task.CompletedTask; }),
					},
				},
			},
		};
	}

	// 3. 상단 도면 탭 막대 (시작 페이지 + 도면 파일 탭 + +)
	private Control BuildDocTabBar()
	{
		var bar = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Background = CadTheme.DocTabStripBg,
			Height = 24,
		};

		// 1) 시작 페이지 탭
		this._startPageTabBorder.Background = CadTheme.DocTabInactiveBg;
		this._startPageTabBorder.BorderBrush = CadTheme.DropdownBorder;
		this._startPageTabBorder.BorderThickness = new Avalonia.Thickness(1, 1, 1, 0);
		this._startPageTabBorder.Padding = new Avalonia.Thickness(8, 2);
		this._startPageTabBorder.Cursor = new Cursor(StandardCursorType.Hand);
		this._startPageTabText.Text = "시작 페이지";
		this._startPageTabText.FontSize = 11;
		this._startPageTabText.Foreground = CadTheme.DocTabInactiveFg;
		this._startPageTabBorder.Child = this._startPageTabText;
		this._startPageTabBorder.PointerPressed += (_, _) => this.SwitchToStartPage();
		bar.Children.Add(this._startPageTabBorder);

		// 2) 도면 탭들 (여러 도면을 동시에 열기)
		bar.Children.Add(this._docTabsPanel);

		// 3) + 새 도면 탭 버튼
		var btnNewTab = new Button
		{
			Content = "+",
			Background = Brushes.Transparent,
			Foreground = CadTheme.DocTabInactiveFg,
			Padding = new Avalonia.Thickness(6, 1),
			FontSize = 12,
		};
		btnNewTab.Click += (_, _) =>
		{
			this.NewDocumentWithTemplateChoice();
			this.SwitchToDrawingCanvas();
		};
		bar.Children.Add(btnNewTab);

		return bar;
	}

	private void SwitchToStartPage()
	{
		this._isStartPageActive = true;
		this._startPageControl.IsVisible = true;
		this._centerGrid.IsVisible = false;

		this._startPageTabBorder.Background = CadTheme.DocTabActiveBg;
		this._startPageTabText.Foreground = CadTheme.DocTabActiveFg;
		this._startPageTabText.FontWeight = FontWeight.Bold;

		this.RebuildDocTabs();
	}

	private void SwitchToDrawingCanvas()
	{
		this._isStartPageActive = false;
		this._startPageControl.IsVisible = false;
		this._centerGrid.IsVisible = true;

		this._startPageTabBorder.Background = CadTheme.DocTabInactiveBg;
		this._startPageTabText.Foreground = CadTheme.DocTabInactiveFg;
		this._startPageTabText.FontWeight = FontWeight.Normal;

		this.RebuildDocTabs();

		this._canvas.Focus();
		this._canvas.InvalidateVisual();
	}

	// 4. 모형/배치 탭 줄 (|◀ ◀ ▶ ▶| + 이동 버튼과 Model, 배치1, ... 사선 모양 탭)
	private Control BuildModelLayoutBar()
	{
		var bar = new Border
		{
			Background = CadTheme.LayoutTabStripBg,
			BorderBrush = CadTheme.LayoutTabBorder,
			BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
			Height = 22,
		};

		this._layoutTabStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
		bar.Child = this._layoutTabStack;
		this.RebuildLayoutTabBar();
		return bar;
	}

	private void RebuildLayoutTabBar()
	{
		this._layoutTabStack.Children.Clear();

		var paperLayouts = this._doc?.Document.Layouts
			.Where(l => l.IsPaperSpace)
			.OrderBy(l => l.TabOrder)
			.ToList() ?? new List<Layout>();

		// 이동 버튼들 (|◀  ◀  ▶  ▶|  +)
		var btnFirst = CreateNavButton("|◀", "첫 번째 탭 (모형)");
		btnFirst.Click += (_, _) => this.SwitchToModel();
		this._layoutTabStack.Children.Add(btnFirst);

		var btnPrev = CreateNavButton("◀", "이전 탭");
		btnPrev.Click += (_, _) =>
		{
			if (this._activeLayout == null) return;
			int idx = paperLayouts.IndexOf(this._activeLayout);
			if (idx <= 0)
			{
				this.SwitchToModel();
			}
			else
			{
				this.SwitchToLayout(paperLayouts[idx - 1]);
			}
		};
		this._layoutTabStack.Children.Add(btnPrev);

		var btnNext = CreateNavButton("▶", "다음 탭");
		btnNext.Click += (_, _) =>
		{
			if (this._activeLayout == null)
			{
				if (paperLayouts.Count > 0) this.SwitchToLayout(paperLayouts[0]);
			}
			else
			{
				int idx = paperLayouts.IndexOf(this._activeLayout);
				if (idx >= 0 && idx < paperLayouts.Count - 1)
				{
					this.SwitchToLayout(paperLayouts[idx + 1]);
				}
			}
		};
		this._layoutTabStack.Children.Add(btnNext);

		var btnLast = CreateNavButton("▶|", "마지막 탭");
		btnLast.Click += (_, _) =>
		{
			if (paperLayouts.Count > 0)
			{
				this.SwitchToLayout(paperLayouts[^1]);
			}
		};
		this._layoutTabStack.Children.Add(btnLast);

		// 새 배치 추가: 글자 '+'는 작아서 안 보였으므로 굵은 선으로 그린 + 를 흰 네모 버튼에 넣는다
		var btnAddLayout = CreateNavButton("+", "새 배치 추가");
		btnAddLayout.Content = new Avalonia.Controls.Shapes.Path
		{
			Data = Geometry.Parse("M 6,0 V 12 M 0,6 H 12"),
			Stroke = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x20, 0x60, 0xB0)),
			StrokeThickness = 2.4,
			Width = 12,
			Height = 12,
		};
		btnAddLayout.Width = 22;
		btnAddLayout.Height = 18;
		btnAddLayout.Padding = new Avalonia.Thickness(0);
		btnAddLayout.Background = Brushes.White;
		btnAddLayout.BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x80, 0x80, 0x80));
		btnAddLayout.BorderThickness = new Avalonia.Thickness(1);
		btnAddLayout.CornerRadius = new Avalonia.CornerRadius(2);
		btnAddLayout.Margin = new Avalonia.Thickness(4, 0, 2, 0);
		btnAddLayout.VerticalAlignment = VerticalAlignment.Center;
		btnAddLayout.Click += (_, _) => this.AddNewLayout();
		this._layoutTabStack.Children.Add(btnAddLayout);

		this._layoutTabStack.Children.Add(new Separator { Margin = new Avalonia.Thickness(2, 2) });

		// Model 탭
		bool isModelActive = this._activeLayout == null;
		var modelTab = CreateSlantedTab("Model", isModelActive);
		modelTab.Click += (_, _) => this.SwitchToModel();
		var modelMenu = new ContextMenu();
		var mNewFromModel = new MenuItem { Header = "새 배치" };
		mNewFromModel.Click += (_, _) => this.AddNewLayout();
		modelMenu.Items.Add(mNewFromModel);
		modelTab.ContextMenu = modelMenu;
		this._layoutTabStack.Children.Add(modelTab);

		// 배치 탭들
		foreach (var lay in paperLayouts)
		{
			bool isActive = this._activeLayout != null &&
				string.Equals(this._activeLayout.Name, lay.Name, StringComparison.OrdinalIgnoreCase);
			var tab = CreateSlantedTab(lay.Name, isActive);
			var targetLay = lay; // capture
			tab.Click += (_, _) => this.SwitchToLayout(targetLay);
			tab.DoubleTapped += (_, _) => this.ShowRenameLayoutDialog(targetLay);

			var ctxMenu = new ContextMenu();

			var mNew = new MenuItem { Header = "새 배치" };
			mNew.Click += (_, _) => this.AddNewLayout();
			ctxMenu.Items.Add(mNew);

			var mRename = new MenuItem { Header = "이름 바꾸기" };
			mRename.Click += (_, _) => this.ShowRenameLayoutDialog(targetLay);
			ctxMenu.Items.Add(mRename);

			var mDelete = new MenuItem { Header = "삭제" };
			mDelete.Click += (_, _) =>
			{
				if (this._doc == null) return;
				if (paperLayouts.Count <= 1)
				{
					this._statusMsg.Text = "도면에 남은 마지막 배치는 삭제할 수 없습니다.";
					return;
				}
				if (this._activeLayout == targetLay)
				{
					this.SwitchToModel();
				}
				this._doc.RemoveLayout(targetLay);
				this.RebuildLayoutTabBar();
			};
			ctxMenu.Items.Add(mDelete);

			var mMoveLeft = new MenuItem { Header = "왼쪽으로 이동" };
			mMoveLeft.Click += (_, _) =>
			{
				if (this._doc == null) return;
				int idx = paperLayouts.IndexOf(targetLay);
				if (idx > 0)
				{
					var prev = paperLayouts[idx - 1];
					int prevOrder = prev.TabOrder;
					this._doc.MoveLayout(targetLay, prevOrder - 1);
					this.RebuildLayoutTabBar();
				}
			};
			ctxMenu.Items.Add(mMoveLeft);

			var mMoveRight = new MenuItem { Header = "오른쪽으로 이동" };
			mMoveRight.Click += (_, _) =>
			{
				if (this._doc == null) return;
				int idx = paperLayouts.IndexOf(targetLay);
				if (idx < paperLayouts.Count - 1)
				{
					var next = paperLayouts[idx + 1];
					int nextOrder = next.TabOrder;
					this._doc.MoveLayout(targetLay, nextOrder + 1);
					this.RebuildLayoutTabBar();
				}
			};
			ctxMenu.Items.Add(mMoveRight);

			tab.ContextMenu = ctxMenu;
			this._layoutTabStack.Children.Add(tab);
		}
	}

	private void SwitchToModel()
	{
		if (this._doc == null) return;
		if (this._canvas.View != null)
		{
			this._spaceViews[this._activeSpaceName] = this._canvas.View;
		}

		this._activeSpaceName = "Model";
		this._activeLayout = null;
		this._doc.CurrentSpace = this._doc.Document.ModelSpace;

		this.RefreshCurrentSpaceView(keepView: false);
		if (this._spaceViews.TryGetValue("Model", out var savedView))
		{
			this._canvas.View = savedView;
		}
		else
		{
			this._canvas.ZoomExtents();
		}
		this.RebuildLayoutTabBar();
		this._propertyPanel.RefreshViewProperties();
	}

	private void SwitchToLayout(Layout layout)
	{
		if (this._doc == null) return;
		if (this._canvas.View != null)
		{
			this._spaceViews[this._activeSpaceName] = this._canvas.View;
		}

		this._activeSpaceName = layout.Name;
		this._activeLayout = layout;
		if (layout.AssociatedBlock != null)
		{
			this._doc.CurrentSpace = layout.AssociatedBlock;
		}

		this.RefreshCurrentSpaceView(keepView: false);
		if (this._spaceViews.TryGetValue(layout.Name, out var savedView))
		{
			this._canvas.View = savedView;
		}
		else
		{
			this._canvas.ZoomExtents();
		}
		this.RebuildLayoutTabBar();
		this._propertyPanel.RefreshViewProperties();
	}

	private void AddNewLayout()
	{
		if (this._doc == null) return;
		int nextNum = 1;
		while (this._doc.Document.Layouts.ContainsKey($"배치{nextNum}"))
		{
			nextNum++;
		}
		var newLayout = this._doc.AddLayout($"배치{nextNum}");
		this.SwitchToLayout(newLayout);
	}

	private int _sceneVersion;

	/// <summary>
	/// 도면(모형/배치) 화면을 다시 만든다. 장면·검색 색인·렌더러 구성은 백그라운드에서 하고 가장 최신 결과만 적용한다.
	/// 큰 도면(test-3: 선 86,000개)에서 편집할 때마다 UI 스레드가 1초 이상 멈추던 문제 해결.
	/// </summary>
	private void RefreshCurrentSpaceView(bool keepView) => _ = this.RefreshCurrentSpaceViewAsync(keepView);

	private async Task RefreshCurrentSpaceViewAsync(bool keepView)
	{
		if (this._doc == null) return;
		int version = ++this._sceneVersion;
		var doc = this._doc;
		var layout = this._activeLayout;

		(Scene Model, SkiaSceneRenderer Renderer, LayoutScene? LayoutScene, SceneIndex Index)? built = null;
		for (int attempt = 0; attempt < 2 && built == null; attempt++)
		{
			try
			{
				var editBlock = this._beditBlock;
				built = await Task.Run(() => BuildSpace(doc, layout, editBlock));
			}
			catch (Exception ex) when (attempt == 0)
			{
				// 만드는 도중 사용자가 도면을 바꾸면 컬렉션 변경 예외가 날 수 있다 → 한 번 더
				CrashLog.Write("장면 구성 재시도", ex);
			}
		}
		if (built == null)
		{
			return;
		}
		var b = built.Value;

		// 그 사이에 더 새로운 요청이 있었거나 다른 도면으로 바뀌었으면 버림
		if (version != this._sceneVersion || !ReferenceEquals(doc, this._doc) || !ReferenceEquals(layout, this._activeLayout))
		{
			b.Renderer.Dispose();
			return;
		}

		var oldModel = this._modelRenderer;
		this._modelRenderer = b.Renderer;

		if (b.LayoutScene == null)
		{
			this._canvas.SetScene(b.Model, keepView: keepView, renderer: b.Renderer);
			this._canvas.SetSceneIndex(b.Index);
			this.SetStatusBtn(this._btnModel, true, "모델");
		}
		else
		{
			this._canvas.SetLayout(new LayoutRenderer(b.LayoutScene, b.Renderer), keepView: keepView);
			this._canvas.SetSceneIndex(b.Index);
			this.SetStatusBtn(this._btnModel, true, "배치");
		}
		DisposeLater(oldModel);

		// 이 장면(버전 version)은 그 전에 바뀐 것을 모두 담고 있다 → 그 뒤에 바뀐 것만 임시 층에 남긴다
		foreach (var e in this._dirty.Where(kv => kv.Value <= version).Select(kv => kv.Key).ToList())
		{
			this._dirty.Remove(e);
		}
		this.UpdateQuickOverlay();
	}

	// 전체 장면에 아직 반영되지 않은 바뀐 엔티티 → 바뀐 시점의 장면 버전 (그 버전 이상의 장면이 적용되면 반영된 것)
	private readonly Dictionary<Entity, int> _dirty = new();

	/// <summary>
	/// 추가·삭제·이동·속성 변경을 즉시 화면에 보이게: 이전 모양은 배경색으로 덮고 새 모양은 따로 그린다.
	/// 전체 장면 재구성(백그라운드, 큰 도면에서 약 1초)이 끝나면 치운다.
	/// </summary>
	private void UpdateQuickOverlay()
	{
		(SkiaSceneRenderer? Eraser, SkiaSceneRenderer? Fresh) old;
		var shown = this._canvas.CurrentScene;
		if (this._doc == null || this._activeLayout != null || this._dirty.Count == 0 || this._dirty.Count > 5000 || shown == null)
		{
			old = this._canvas.SetQuickOverlay(null, null);
		}
		else
		{
			try
			{
				var keys = new HashSet<Entity>(this._dirty.Keys);
				var eraserScene = Scene.Subset(shown, keys, new Rgb(0, 0, 0));
				var alive = keys.Where(e => e.Document != null && ReferenceEquals(e.Owner, this._doc.Document.ModelSpace)).ToList();
				var freshScene = SceneBuilder.BuildEntities(this._doc.Document, alive);
				// 지운 선 자리에 흐린 테두리가 남지 않도록 덮는 선은 굵게
				var eraser = eraserScene.Polylines.Count + eraserScene.Fills.Count + eraserScene.Texts.Count > 0
					? new SkiaSceneRenderer(eraserScene) { Background = SKColors.Black, MinLineWidth = 3f, OutlineFills = true }
					: null;
				var fresh = new SkiaSceneRenderer(freshScene) { Background = SKColors.Black };
				old = this._canvas.SetQuickOverlay(eraser, fresh);
			}
			catch (Exception ex)
			{
				CrashLog.Write("즉시 반영 층 만들기 실패(무시)", ex);
				old = this._canvas.SetQuickOverlay(null, null);
			}
		}
		DisposeLater(old.Eraser);
		DisposeLater(old.Fresh);
	}

	private static (Scene Model, SkiaSceneRenderer Renderer, LayoutScene? LayoutScene, SceneIndex Index) BuildSpace(EditorDocument doc, Layout? layout, BlockRecord? editBlock = null)
	{
		// 블록 편집기에서는 그 블록 정의의 객체를 그린다
		var model = editBlock != null ? SceneBuilder.BuildBlock(doc.Document, editBlock) : SceneBuilder.Build(doc.Document);
		var renderer = new SkiaSceneRenderer(model) { Background = SKColors.Black };
		if (layout == null)
		{
			return (model, renderer, null, new SceneIndex(model));
		}
		var layoutScene = LayoutScene.Build(doc.Document, layout);
		return (model, renderer, layoutScene, new SceneIndex(layoutScene.PaperScene));
	}

	/// <summary>
	/// 이전 렌더러는 몇 초 뒤에 해제 (렌더 스레드가 그리던 마지막 프레임이 빈 화면으로 깜빡이지 않도록).
	/// </summary>
	private static void DisposeLater(IDisposable? d)
	{
		if (d == null) return;
		_ = Task.Delay(3000).ContinueWith(_ => d.Dispose(), TaskScheduler.Default);
	}

	private async void ShowRenameLayoutDialog(Layout layout)
	{
		if (this._doc == null) return;
		var dialog = new Window
		{
			Title = "배치 이름 바꾸기",
			Width = 320,
			Height = 140,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			CanResize = false,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};

		var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 10 };
		panel.Children.Add(new TextBlock
		{
			Text = "새 배치 이름을 입력하십시오:",
			Foreground = CadTheme.DialogFg,
			FontSize = 12,
		});

		var txtName = new TextBox
		{
			Text = layout.Name,
			FontSize = 12,
		};
		panel.Children.Add(txtName);

		var btnStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Spacing = 8,
		};
		var btnOk = new Button { Content = "확인", Width = 65, IsDefault = true };
		var btnCancel = new Button { Content = "취소", Width = 65, IsCancel = true };

		btnOk.Click += (_, _) =>
		{
			string newName = txtName.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrEmpty(newName) || newName == layout.Name)
			{
				dialog.Close();
				return;
			}
			if (this._doc.Document.Layouts.ContainsKey(newName))
			{
				this._statusMsg.Text = $"배치 \"{newName}\"이(가) 이미 존재합니다.";
				return;
			}
			dialog.Close();
			try
			{
				this._doc.RenameLayout(layout, newName);
				if (this._activeLayout == layout)
				{
					this._activeSpaceName = newName;
				}
				this.RebuildLayoutTabBar();
			}
			catch (Exception ex)
			{
				this._statusMsg.Text = $"이름 변경 실패: {ex.Message}";
			}
		};

		btnCancel.Click += (_, _) => dialog.Close();

		btnStack.Children.Add(btnOk);
		btnStack.Children.Add(btnCancel);
		panel.Children.Add(btnStack);

		dialog.Content = panel;
		txtName.SelectAll();
		await dialog.ShowDialog(this);
	}

	private static Button CreateNavButton(string text, string toolTip) => new()
	{
		Content = text,
		Background = Brushes.Transparent,
		Foreground = CadTheme.LayoutTabInactiveFg,
		Padding = new Avalonia.Thickness(4, 0),
		FontSize = 11,
		Height = 20,
		[ToolTip.TipProperty] = toolTip,
	};

	private sealed class SlantedTab : Decorator
	{
		private readonly TextBlock _textBlock;
		private bool _isHovered;
		private bool _isPressed;

		public bool IsActive { get; }

		public event EventHandler<RoutedEventArgs>? Click;

		public SlantedTab(string text, bool isActive)
		{
			this.IsActive = isActive;
			this.Height = 22;
			this.Cursor = new Cursor(StandardCursorType.Hand);

			this._textBlock = new TextBlock
			{
				Text = text,
				FontSize = 10,
				FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
				Foreground = isActive
					? CadTheme.LayoutTabActiveFg
					: CadTheme.LayoutTabInactiveFg,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Avalonia.Thickness(12, 1, 12, 1),
			};
			this.Child = this._textBlock;
		}

		protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
		{
			if (this.Child != null)
			{
				this.Child.Measure(availableSize);
				return new Avalonia.Size(Math.Max(50, this.Child.DesiredSize.Width + 24), 22);
			}
			return new Avalonia.Size(50, 22);
		}

		public override void Render(DrawingContext context)
		{
			double w = this.Bounds.Width;
			double h = this.Bounds.Height;
			if (w <= 0 || h <= 0) return;

			double slant = 5.0;
			var geom = new StreamGeometry();
			using (var ctx = geom.Open())
			{
				ctx.BeginFigure(new Point(0, h), isFilled: true);
				ctx.LineTo(new Point(slant, 1));
				ctx.LineTo(new Point(w - slant, 1));
				ctx.LineTo(new Point(w, h));
				ctx.EndFigure(isClosed: true);
			}

			IBrush bgBrush = this.IsActive
				? CadTheme.LayoutTabActiveBg
				: (this._isHovered
					? new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD4, 0xD4, 0xD4))
					: CadTheme.LayoutTabInactiveBg);

			IPen borderPen = new Pen(CadTheme.LayoutTabBorder, 1);

			context.DrawGeometry(bgBrush, borderPen, geom);
			// 주의: Render 안에서 속성(Foreground 등)을 바꾸면 "Visual was invalidated during the render pass" 예외로 앱이 종료됨
			base.Render(context);
		}

		private void UpdateForeground()
		{
			this._textBlock.Foreground = this.IsActive
				? CadTheme.LayoutTabActiveFg
				: CadTheme.LayoutTabInactiveFg;
		}

		protected override void OnPointerEntered(PointerEventArgs e)
		{
			base.OnPointerEntered(e);
			this._isHovered = true;
			this.UpdateForeground();
			this.InvalidateVisual();
		}

		protected override void OnPointerExited(PointerEventArgs e)
		{
			base.OnPointerExited(e);
			this._isHovered = false;
			this.UpdateForeground();
			this.InvalidateVisual();
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				this._isPressed = true;
				e.Handled = true;
			}
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (this._isPressed)
			{
				this._isPressed = false;
				var pt = e.GetPosition(this);
				if (new Avalonia.Rect(0, 0, this.Bounds.Width, this.Bounds.Height).Contains(pt))
				{
					this.Click?.Invoke(this, new RoutedEventArgs());
				}
				e.Handled = true;
			}
		}
	}

	private static SlantedTab CreateSlantedTab(string text, bool isActive) => new(text, isActive);

	// 5. 하단 상태 표시줄 (높이 약 22px)
	private Control BuildStatusBar()
	{
		var bar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
			Background = CadTheme.StatusBarBg,
			Height = 26,
		};
		AddStatusButtonStyles(bar);

		// 왼쪽: 좌표 573.44,14.97,0
		var coordStack = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Avalonia.Thickness(8, 0),
		};
		coordStack.Children.Add(new TextBlock
		{
			Text = "┼",
			Foreground = CadTheme.StatusBarFg,
			FontSize = 11,
			VerticalAlignment = VerticalAlignment.Center,
		});
		this._coords.Foreground = CadTheme.StatusBarFg;
		this._coords.FontSize = 12;
		this._coords.FontWeight = FontWeight.SemiBold;
		coordStack.Children.Add(this._coords);

		Grid.SetColumn(coordStack, 0);
		bar.Children.Add(coordStack);

		// 가운데: 상태 메시지
		this._statusMsg.Foreground = CadTheme.StatusBtnInactiveFg;
		this._statusMsg.FontSize = 12;
		Grid.SetColumn(this._statusMsg, 1);
		bar.Children.Add(this._statusMsg);

		// 오른쪽: 토글 버튼들 (16px 아이콘/텍스트, 켜지면 파란 테두리/배경)
		var toggles = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 2,
			Margin = new Avalonia.Thickness(4, 0),
		};

		toggles.Children.Add(this._btnSnapMode);
		toggles.Children.Add(this._btnGrid);
		toggles.Children.Add(this._btnOrtho);
		toggles.Children.Add(this._btnPolar);
		toggles.Children.Add(this._btnOsnap);
		toggles.Children.Add(this._btnOtrack);
		toggles.Children.Add(this._btnLwt);
		toggles.Children.Add(this._btnModel);
		toggles.Children.Add(this._btnScale);

		this._btnSnapMode.Click += (_, _) => { this._canvas.SnapEnabled = !this._canvas.SnapEnabled; this.UpdateStatusBarToggles(); };
		this._btnGrid.Click += (_, _) => { this._canvas.GridEnabled = !this._canvas.GridEnabled; this.UpdateStatusBarToggles(); this._canvas.InvalidateVisual(); };
		this._btnOrtho.Click += (_, _) => this.ToggleOrtho();
		this._btnPolar.Click += (_, _) => this.TogglePolar();
		this._btnOtrack.Click += (_, _) => this.ToggleOtrack();
		this._btnOsnap.Click += (_, _) => { this._canvas.ObjectSnapEnabled = !this._canvas.ObjectSnapEnabled; this.UpdateStatusBarToggles(); this._canvas.InvalidateVisual(); };
		this._btnLwt.Click += (_, _) => { this._showLineweight = !this._showLineweight; this.UpdateStatusBarToggles(); };

		// 상태 표시줄 버튼 오른쪽 클릭 메뉴 (기존 CAD처럼 켜기·옵션·설정)
		this._btnSnapMode.ContextMenu = this.StatusMenu(
			() => this._canvas.SnapEnabled, () => this.ToggleSnapMode(),
			settings: () => this.ShowDrawingSettingsDialog(1, 0));
		this._btnGrid.ContextMenu = this.StatusMenu(
			() => this._canvas.GridEnabled, () => { this._canvas.GridEnabled = !this._canvas.GridEnabled; this.AfterStatusChange(); },
			settings: () => this.ShowDrawingSettingsDialog(1, 0));
		this._btnOrtho.ContextMenu = this.StatusMenu(() => this._canvas.OrthoEnabled, () => this.ToggleOrtho());
		this._btnOtrack.ContextMenu = this.StatusMenu(
			() => this._canvas.ObjectSnapTrackingEnabled, () => this.ToggleOtrack(),
			settings: () => this.ShowDrawingSettingsDialog(1, 1));
		this._btnLwt.ContextMenu = this.StatusMenu(() => this._showLineweight, () => { this._showLineweight = !this._showLineweight; this.UpdateStatusBarToggles(); });

		// 극좌표: 증분 각도 선택
		var polarMenu = new ContextMenu();
		polarMenu.Opening += (_, _) =>
		{
			polarMenu.Items.Clear();
			polarMenu.Items.Add(CheckItem("극좌표 추적 켜기 (F10)", this._polarTracking, () => this.TogglePolar()));
			polarMenu.Items.Add(new Separator());
			foreach (double deg in new[] { 90.0, 45, 30, 22.5, 18, 15, 10, 5 })
			{
				double d = deg;
				polarMenu.Items.Add(CheckItem($"{d:0.#}° 증분", Math.Abs(this._canvas.PolarIncrementDeg - d) < 1e-9, () =>
				{
					this._canvas.PolarIncrementDeg = d;
					if (!this._polarTracking) this.TogglePolar(); // 각도를 고르면 켠다
					this.AfterStatusChange();
				}));
			}
		};
		this._btnPolar.ContextMenu = polarMenu;

		// 객체 스냅: 모드별 켜기/끄기
		var osnapMenu = new ContextMenu();
		osnapMenu.Opening += (_, _) =>
		{
			osnapMenu.Items.Clear();
			osnapMenu.Items.Add(CheckItem("객체 스냅 켜기 (F3)", this._canvas.ObjectSnapEnabled, () => { this._canvas.ObjectSnapEnabled = !this._canvas.ObjectSnapEnabled; this.AfterStatusChange(); }));
			osnapMenu.Items.Add(new Separator());
			foreach (var (mode, name) in new[]
			{
				(SnapModes.Endpoint, "끝점"), (SnapModes.Midpoint, "중간점"), (SnapModes.Center, "중심"),
				(SnapModes.Quadrant, "사분점"), (SnapModes.Intersection, "교차점"), (SnapModes.Insertion, "삽입점"),
				(SnapModes.Node, "노드(점)"), (SnapModes.Nearest, "근처점"),
			})
			{
				var m = mode;
				osnapMenu.Items.Add(CheckItem(name, this._canvas.SnapModes.HasFlag(m), () =>
				{
					this._canvas.SnapModes ^= m;
					this._canvas.ObjectSnapEnabled = true;
					this.AfterStatusChange();
				}));
			}
			osnapMenu.Items.Add(new Separator());
			osnapMenu.Items.Add(PlainItem("모두 선택", () => { this._canvas.SnapModes = SnapModes.Default | SnapModes.Nearest; this.AfterStatusChange(); }));
			osnapMenu.Items.Add(PlainItem("모두 지우기", () => { this._canvas.SnapModes = SnapModes.None; this.AfterStatusChange(); }));
			osnapMenu.Items.Add(new Separator());
			osnapMenu.Items.Add(PlainItem("설정...", () => this.ShowDrawingSettingsDialog(1, 1)));
		};
		this._btnOsnap.ContextMenu = osnapMenu;

		this._btnScale.Click += (_, _) => this.ShowAnnotationScaleMenu();
		this._btnModel.Click += (_, _) =>
		{
			if (this._doc == null) return;
			if (this._activeLayout != null)
			{
				this.SwitchToModel();
			}
			else
			{
				var firstLayout = this._doc.Document.Layouts.Where(l => l.IsPaperSpace).OrderBy(l => l.TabOrder).FirstOrDefault();
				if (firstLayout != null)
				{
					this.SwitchToLayout(firstLayout);
				}
			}
		};

		Grid.SetColumn(toggles, 2);
		bar.Children.Add(toggles);

		return bar;
	}

	/// <summary>
	/// 명령줄 높이 조절 막대: 명령줄 위 경계를 위아래로 끌면 높이가 바뀌고, 놓으면 설정에 저장된다 (50~500px).
	/// </summary>
	private Control BuildCommandLineSplitter()
	{
		var bar = new Border
		{
			Height = 5,
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)),
			Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
			[ToolTip.TipProperty] = "끌어서 명령줄 높이 조절",
		};
		double startY = 0, startH = 0;
		bool dragging = false;
		bar.PointerPressed += (_, e) =>
		{
			dragging = true;
			startY = e.GetPosition(this).Y;
			startH = this._cmdLine.Bounds.Height;
			e.Pointer.Capture(bar);
			e.Handled = true;
		};
		bar.PointerMoved += (_, e) =>
		{
			if (!dragging) return;
			double h = Math.Clamp(startH - (e.GetPosition(this).Y - startY), 50, 500);
			this._cmdLine.Height = h;
		};
		bar.PointerReleased += (_, e) =>
		{
			if (!dragging) return;
			dragging = false;
			e.Pointer.Capture(null);
			CadAppSettings.CurrentCommandLineHeight = this._cmdLine.Height;
			CadAppSettings.FromCanvas(this._canvas).Save();
		};
		bar.PointerEntered += (_, _) => bar.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x8E, 0xE6));
		bar.PointerExited += (_, _) => { if (!dragging) bar.Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x3A, 0x3A, 0x3A)); };
		return bar;
	}

	/// <summary>상태 표시줄 버튼 공통 오른쪽 클릭 메뉴: 켜기(체크) + 설정...</summary>
	private ContextMenu StatusMenu(Func<bool> isOn, Action toggle, Action? settings = null)
	{
		var menu = new ContextMenu();
		menu.Opening += (_, _) =>
		{
			menu.Items.Clear();
			menu.Items.Add(CheckItem("켜기", isOn(), toggle));
			if (settings != null)
			{
				menu.Items.Add(new Separator());
				menu.Items.Add(PlainItem("설정...", settings));
			}
		};
		return menu;
	}

	private static MenuItem CheckItem(string header, bool isChecked, Action onClick)
	{
		var mi = new MenuItem
		{
			Header = header,
			ToggleType = MenuItemToggleType.CheckBox,
			IsChecked = isChecked,
		};
		mi.Click += (_, _) => onClick();
		return mi;
	}

	private static MenuItem PlainItem(string header, Action onClick)
	{
		var mi = new MenuItem { Header = header };
		mi.Click += (_, _) => onClick();
		return mi;
	}

	/// <summary>상태 버튼·메뉴로 바꾼 설정을 표시·저장 (다음 실행에도 유지).</summary>
	private void AfterStatusChange()
	{
		this.UpdateStatusBarToggles(); // 저장 포함
		this._canvas.InvalidateVisual();
	}

	private void ToggleSnapMode()
	{
		this._canvas.SnapEnabled = !this._canvas.SnapEnabled;
		this.AfterStatusChange();
	}

	/// <summary>극좌표 추적 켜기/끄기 (F10). 직교와는 함께 쓸 수 없다 (기존 CAD와 같음).</summary>
	private void TogglePolar()
	{
		this._polarTracking = !this._polarTracking;
		this._canvas.PolarTrackingEnabled = this._polarTracking;
		if (this._polarTracking && this._canvas.OrthoEnabled)
		{
			this._canvas.OrthoEnabled = false;
		}
		this.UpdateStatusBarToggles();
		this._canvas.InvalidateVisual();
	}

	/// <summary>직교 켜기/끄기 (F8). 켜면 극좌표 추적은 끈다.</summary>
	private void ToggleOrtho()
	{
		this._canvas.OrthoEnabled = !this._canvas.OrthoEnabled;
		if (this._canvas.OrthoEnabled && this._polarTracking)
		{
			this._polarTracking = false;
			this._canvas.PolarTrackingEnabled = false;
		}
		this.UpdateStatusBarToggles();
		this._canvas.InvalidateVisual();
	}

	/// <summary>객체 스냅 추적 켜기/끄기 (F11).</summary>
	private void ToggleOtrack()
	{
		this._canvas.ObjectSnapTrackingEnabled = !this._canvas.ObjectSnapTrackingEnabled;
		this._canvas.ClearTracking();
		this.UpdateStatusBarToggles();
		this._canvas.InvalidateVisual();
	}

	private bool _settingsLoaded;

	private void UpdateStatusBarToggles()
	{
		// F키·버튼·메뉴 어느 경로로 바꿔도 다음 실행에 유지되도록 저장 (불러오기 전에는 저장하지 않음)
		if (this._settingsLoaded)
		{
			CadAppSettings.FromCanvas(this._canvas).Save();
		}
		this.SetStatusBtn(this._btnSnapMode, this._canvas.SnapEnabled, "스냅(F9)");
		this.SetStatusBtn(this._btnGrid, this._canvas.GridEnabled, "그리드(F7)");
		this.SetStatusBtn(this._btnOrtho, this._canvas.OrthoEnabled, "직교(F8)");
		this.SetStatusBtn(this._btnPolar, this._polarTracking, "극좌표(F10)");
		this.SetStatusBtn(this._btnOtrack, this._canvas.ObjectSnapTrackingEnabled, "객체 추적(F11)");
		this.SetStatusBtn(this._btnOsnap, this._canvas.ObjectSnapEnabled, "객체 스냅(F3)");
		this.SetStatusBtn(this._btnLwt, this._showLineweight, "선가중치");
		this.SetStatusBtn(this._btnModel, true, this._activeLayout != null ? "배치" : "모델");
		double annoScale = this._doc != null ? Free2DCad.Core.Styles.AnnotationScale.Get(this._doc.Document) : 1.0;
		this.SetStatusBtn(this._btnScale, false, Free2DCad.Core.Styles.AnnotationScale.Label(annoScale));
		ToolTip.SetTip(this._btnScale, "주석 축척: 새 문자·치수·지시선을 이 축척으로 만듭니다 (누르면 축척 목록)");
	}

	/// <summary>주석 축척을 물어야 하는지 (설정 켜짐, 이 도면에서 아직 안 물음, 지금 1:1, 모형 공간, 그린 범위가 용지의 2배 넘음).</summary>
	private bool NeedsAnnotationScalePrompt(out double suggest, out double ratio)
	{
		suggest = 1;
		ratio = 1;
		if (this._doc is not { } doc || !CadAppSettings.CurrentSuggestAnnotationScale || this._annoAsked.TryGetValue(doc, out _))
		{
			return false;
		}
		if (Math.Abs(Free2DCad.Core.Styles.AnnotationScale.Get(doc.Document) - 1) > 1e-9 || this._canvas.ModelExtents is not { } ext)
		{
			return false;
		}
		var h = doc.Document.Header;
		double sw = h.ModelSpaceLimitsMax.X - h.ModelSpaceLimitsMin.X, sh = h.ModelSpaceLimitsMax.Y - h.ModelSpaceLimitsMin.Y;
		if (!(sw > 1e-6) || !(sh > 1e-6))
		{
			(sw, sh) = (Free2DCad.Core.TemplateBuilder.A3Width, Free2DCad.Core.TemplateBuilder.A3Height);
		}
		// 용지가 작게 잡혀 있어도 A3보다 작게 보지는 않는다
		sw = Math.Max(sw, Free2DCad.Core.TemplateBuilder.A3Width);
		sh = Math.Max(sh, Free2DCad.Core.TemplateBuilder.A3Height);
		if (Free2DCad.Core.Styles.AnnotationScale.Suggest(ext.Width, ext.Height, sw, sh) is not { } f)
		{
			return false;
		}
		suggest = f;
		ratio = Math.Max(ext.Width / sw, ext.Height / sh);
		return true;
	}

	/// <summary>"주석 축척" 창: [1:N으로 바꾸기] [1:1 그대로] + 다시 묻지 않음. 고른 뒤 명령을 시작한다.</summary>
	private async Task AskAnnotationScaleThenStartAsync(EditorCommand cmd, double suggest, double ratio)
	{
		var doc = this._doc!;
		this._annoAsked.AddOrUpdate(doc, new object());
		string label = Free2DCad.Core.Styles.AnnotationScale.Label(suggest);
		var dontAsk = new CheckBox { Content = "다시 묻지 않음", FontSize = 12 };
		bool? useSuggested = null;
		var dlg = new Window
		{
			Title = "주석 축척",
			Width = 430,
			SizeToContent = SizeToContent.Height,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var yes = new Button { Content = $"{label} 적용", MinWidth = 110, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		var no = new Button { Content = "1:1 그대로", MinWidth = 90, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
		yes.Click += (_, _) => { useSuggested = true; dlg.Close(); };
		no.Click += (_, _) => { useSuggested = false; dlg.Close(); };
		dlg.Content = new StackPanel
		{
			Margin = new Avalonia.Thickness(16),
			Spacing = 10,
			Children =
			{
				new TextBlock
				{
					Text = $"그린 도면이 용지(A3)보다 약 {ratio:0.#}배 큽니다.\n지금 주석 축척 1:1로는 새 문자·치수·지시선이 도면에 비해 아주 작게 만들어집니다.\n\n주석 축척을 {label}(으)로 바꿀까요? (새 문자·치수·지시선을 {suggest:0.###}배 크기로 만들고, {label}(으)로 출력하면 지정한 크기로 인쇄됩니다.)",
					TextWrapping = TextWrapping.Wrap,
					FontSize = 12,
					Foreground = CadTheme.DialogFg,
				},
				dontAsk,
				new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } },
			},
		};
		await dlg.ShowDialog(this);
		if (dontAsk.IsChecked == true)
		{
			CadAppSettings.CurrentSuggestAnnotationScale = false;
			CadAppSettings.FromCanvas(this._canvas).Save();
		}
		if (useSuggested == true && ReferenceEquals(doc, this._doc))
		{
			Free2DCad.Core.Styles.AnnotationScale.Set(doc.Document, suggest);
			this.UpdateStatusBarToggles();
			this._cmdLine.Print($"주석 축척 {label} — 새 문자·치수·지시선을 {suggest:0.###}배 크기로 만듭니다 (상태 표시줄 {label}에서 바꿀 수 있음).");
		}
		if (ReferenceEquals(doc, this._doc))
		{
			this.StartCommand(cmd);
		}
	}

	/// <summary>주석 축척 목록 (기존 CAD 상태 표시줄의 1:1과 같은 위치·역할).</summary>
	private void ShowAnnotationScaleMenu()
	{
		if (this._doc == null)
		{
			return;
		}
		var doc = this._doc.Document;
		double cur = Free2DCad.Core.Styles.AnnotationScale.Get(doc);
		var menu = new ContextMenu();
		foreach (var (label, factor) in Free2DCad.Core.Styles.AnnotationScale.Standard)
		{
			var mi = new MenuItem
			{
				Header = label,
				Icon = Math.Abs(factor - cur) < 1e-9 ? new TextBlock { Text = "✓", FontWeight = FontWeight.Bold } : null,
			};
			double f = factor;
			mi.Click += (_, _) =>
			{
				Free2DCad.Core.Styles.AnnotationScale.Set(doc, f);
				this.UpdateStatusBarToggles();
				this._cmdLine.Print(Math.Abs(f - 1) < 1e-12
					? "주석 축척 1:1 — 문자·치수를 지정한 크기 그대로 만듭니다."
					: $"주석 축척 {label} — 새 문자·치수·지시선을 {f:0.###}배 크기로 만듭니다 (출력 축척 {label}에서 지정한 크기로 인쇄).");
			};
			menu.Items.Add(mi);
		}
		menu.Open(this._btnScale);
	}

	private void SetStatusBtn(Button btn, bool active, string text)
	{
		btn.Content = text;
		// 가독성: 12px 반굵게, 꺼짐도 흰 글자 (켜짐은 파란 배경으로 구분)
		btn.FontSize = 12;
		btn.FontWeight = FontWeight.SemiBold;
		btn.Height = 21;
		btn.Padding = new Avalonia.Thickness(6, 0);
		btn.Background = active ? CadTheme.StatusBtnActiveBg : Brushes.Transparent;
		btn.Foreground = active ? CadTheme.StatusBtnActiveFg : CadTheme.StatusBtnInactiveFg;
		btn.BorderBrush = active ? CadTheme.StatusBtnActiveBg : new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x5A, 0x5A, 0x5A));
		btn.BorderThickness = new Avalonia.Thickness(1);
		btn.CornerRadius = new Avalonia.CornerRadius(2);
		btn.Classes.Set("stat", true);
		btn.Classes.Set("on", active);
	}

	/// <summary>
	/// 상태 표시줄 토글 버튼의 마우스 올림/누름 색. Fluent 기본 스타일은 올리면 켜짐 파랑을 지워 꺼진 것처럼 보였다.
	/// 켜짐: 올리면 더 진한 파랑, 꺼짐: 올리면 회색 바탕 + 밝은 테두리로 뚜렷하게.
	/// </summary>
	private static void AddStatusButtonStyles(Control host)
	{
		static Style Presenter(Func<Selector?, Selector> sel, byte[] bg, byte[] border) => new(x => sel(x).Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
		{
			Setters =
			{
				new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, new SolidColorBrush(Avalonia.Media.Color.FromRgb(bg[0], bg[1], bg[2]))),
				new Setter(Avalonia.Controls.Presenters.ContentPresenter.BorderBrushProperty, new SolidColorBrush(Avalonia.Media.Color.FromRgb(border[0], border[1], border[2]))),
				new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, Brushes.White),
			},
		};
		host.Styles.Add(Presenter(x => x.OfType<Button>().Class("stat").Class(":pointerover"), new byte[] { 0x5C, 0x63, 0x6E }, new byte[] { 0xA8, 0xC8, 0xF0 }));
		host.Styles.Add(Presenter(x => x.OfType<Button>().Class("stat").Class(":pressed"), new byte[] { 0x44, 0x4B, 0x55 }, new byte[] { 0xA8, 0xC8, 0xF0 }));
		host.Styles.Add(Presenter(x => x.OfType<Button>().Class("stat").Class("on").Class(":pointerover"), new byte[] { 0x1F, 0x5F, 0xAE }, new byte[] { 0xBF, 0xDC, 0xFF }));
		host.Styles.Add(Presenter(x => x.OfType<Button>().Class("stat").Class("on").Class(":pressed"), new byte[] { 0x17, 0x4A, 0x8A }, new byte[] { 0xBF, 0xDC, 0xFF }));
	}

	private Button CreateQuickButton(string iconData, string toolTip, Action action)
	{
		var btn = new Button
		{
			Width = 20,
			Height = 20,
			Padding = new Avalonia.Thickness(2),
			Background = Brushes.Transparent,
			[ToolTip.TipProperty] = toolTip,
			Content = IconHelper.GetIcon(iconData, 14),
		};
		btn.Click += (_, _) => action();
		return btn;
	}

	private void ShowLayerManagerDialog()
	{
		if (this._doc == null) return;
		var dlg = new LayerManagerDialog(this._doc);
		dlg.Show(this);
	}

	private void ShowPlotDialog()
	{
		if (this._doc == null)
		{
			this._cmdLine.Print("열려 있는 도면 문서가 없습니다.");
			return;
		}

		var modelScene = this._beditBlock != null ? SceneBuilder.BuildBlock(this._doc.Document, this._beditBlock) : SceneBuilder.Build(this._doc.Document);
		LayoutScene? layoutScene = null;
		if (this._activeLayout != null)
		{
			layoutScene = LayoutScene.Build(this._doc.Document, this._activeLayout);
		}

		string docName = string.IsNullOrEmpty(this._doc.FilePath)
			? "Drawing1"
			: Path.GetFileNameWithoutExtension(this._doc.FilePath);

		var dlg = new PlotDialog(
			modelScene,
			layoutScene,
			this._canvas.View,
			docName,
			this._activeSpaceName,
			async () => await this._canvas.PickWindowAsync(),
			msg =>
			{
				this._cmdLine.Print(msg.StartsWith("오류") ? msg : $"플롯 완료: {msg}");
				this._statusMsg.Text = "플롯 완료";
			},
			() =>
			{
				this._statusMsg.Text = "플롯 중...";
			});

		dlg.ShowDialog(this);
	}

	/// <summary>
	/// 설정 초기화: 저장된 앱 설정(settings.json)을 지우고 스냅·그리드·직교·극좌표·선가중치·객체 스냅 모드,
	/// 속성 팔레트·명령줄 표시, 리본 펼침, 해치 기본값을 처음 상태로. 도면 내용과 파일은 바뀌지 않는다.
	/// </summary>
	private async Task ResetAllSettingsAsync()
	{
		var dlg = new Window
		{
			Title = "설정 초기화",
			Width = 420,
			Height = 190,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		bool ok = false;
		var panel = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
		panel.Children.Add(new TextBlock
		{
			Text = "화면과 입력 설정을 모두 처음 상태로 되돌립니다.\n(스냅, 그리드, 직교, 극좌표, 객체 스냅 모드, 선가중치 표시,\n속성 팔레트·명령줄 표시, 리본, 해치 기본값)\n\n도면 내용과 파일은 바뀌지 않습니다. 계속할까요?",
			Foreground = CadTheme.DialogFg,
			FontSize = 12,
			TextWrapping = TextWrapping.Wrap,
		});
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
		var btnOk = new Button { Content = "초기화", Width = 80, Height = 26, IsDefault = true };
		var btnCancel = new Button { Content = "취소", Width = 80, Height = 26, IsCancel = true };
		btnOk.Click += (_, _) => { ok = true; dlg.Close(); };
		btnCancel.Click += (_, _) => dlg.Close();
		buttons.Children.Add(btnOk);
		buttons.Children.Add(btnCancel);
		panel.Children.Add(buttons);
		dlg.Content = panel;
		await dlg.ShowDialog(this);
		if (!ok)
		{
			return;
		}

		try
		{
			if (File.Exists(CadAppSettings.SettingsPath))
			{
				File.Delete(CadAppSettings.SettingsPath);
			}
		}
		catch (Exception ex)
		{
			CrashLog.Write("설정 파일 삭제 실패", ex);
		}
		new CadAppSettings().ApplyToCanvas(this._canvas);
		this._polarTracking = false;
		this._canvas.PolarTrackingEnabled = false;
		this._canvas.ObjectSnapTrackingEnabled = true;
		this._canvas.ClearTracking();
		this._showLineweight = false;
		HatchCommand.LastSettings = new Free2DCad.Core.Hatching.HatchSettings();
		this._cmdLine.IsVisible = true;
		this._cmdLine.Height = 92;
		CadAppSettings.CurrentCommandLineHeight = 92;
		this._propertyPanelWidth = 225;
		this._propertyPanel.IsVisible = false; // TogglePropertyPanel이 같은 값이면 무시하므로 먼저 바꿔 둠
		this.TogglePropertyPanel(true);
		this.UpdateStatusBarToggles();
		this._canvas.InvalidateVisual();
		this.RefreshCurrentSpaceView(keepView: true); // 대체 글꼴도 기본값으로 → 문자 다시 그림
		this._cmdLine.Print("설정을 처음 상태로 되돌렸습니다. (도면 내용은 그대로입니다)");
	}

	/// <summary>
	/// 더블클릭 편집: 치수는 문자(측정값 대신 표시할 글자, &lt;&gt; = 측정값), 단일·여러 줄 문자는 내용.
	/// </summary>
	private async Task EditEntityTextAsync(Entity ent)
	{
		if (this._inplaceEditor.IsActive)
		{
			this._inplaceEditor.Commit();
		}
		if (ent is TableEntity table && this._activeLayout == null && this._canvas.LastDoubleClickWorld is { } click
			&& TableEditor.HitCell(table, click.X, click.Y) is { } cell)
		{
			this.BeginTableCellEdit(table, cell.Row, cell.Col);
			return;
		}
		if (this._doc == null)
		{
			return;
		}
		string title;
		string current;
		string hint;
		bool multiline = false;
		switch (ent)
		{
			case Dimension dim:
				title = "치수 문자 편집";
				current = string.IsNullOrEmpty(dim.Text) ? "<>" : Free2DCad.Rendering.MTextTokenizer.PlainText(dim.Text);
				hint = $"측정값: {dim.Measurement:0.###}   (<> = 측정값, 예: <> TYP, %%c<>)";
				break;
			case MText mt:
				title = "여러 줄 문자 편집";
				current = Free2DCad.Rendering.MTextTokenizer.PlainText(mt.Value); // 서식 코드는 숨기고 보이는 글자만
				hint = "여러 줄 문자 내용";
				multiline = true;
				break;
			case TextEntity t:
				title = "문자 편집";
				current = t.Value;
				hint = "문자 내용";
				break;
			case MultiLeader mlText:
				title = "다중지시선 문자 편집";
				current = Free2DCad.Rendering.MTextTokenizer.PlainText(mlText.ContextData.TextLabel ?? string.Empty);
				hint = "지시선 문자";
				multiline = true;
				break;
			default:
				return;
		}

		// 문자·여러 줄 문자: 원래 자리·모양 그대로 두고 바로 반영하며 고친다 (모형 공간에 있을 때)
		if (ent is TextEntity or MText or MultiLeader && this._activeLayout == null && ReferenceEquals(ent.Owner, this._doc.Document.ModelSpace))
		{
			this.BeginLiveTextEdit(ent);
			return;
		}

		// 치수: 치수 문자 그 자리에서 바로 반영하며 고친다
		if (ent is Dimension liveDim && this._activeLayout == null && ReferenceEquals(ent.Owner, this._doc.Document.ModelSpace)
			&& this.BeginLiveDimensionEdit(liveDim))
		{
			return;
		}

		// 그 밖(배치 공간 등): 문자 자리에 입력 칸
		var world = this.InplaceTextArea(ent);
		if (world == null)
		{
			return;
		}
		double textHeight = ent switch
		{
			TextEntity te => te.Height,
			MText me => me.Height,
			Dimension de => de.Style.TextHeight * (de.Style.ScaleFactor > 0 ? de.Style.ScaleFactor : 1),
			MultiLeader mle => mle.ContextData.TextHeight,
			_ => 0,
		};
		this._inplaceEditor.Begin(current, multiline, world.Value, textHeight, $"{title}: {hint}   (Enter{(multiline ? " = 줄 바꿈, Ctrl+Enter" : string.Empty)} = 확정, Esc = 취소)",
			result => this.ApplyEditedText(ent, result));
		await Task.CompletedTask;
	}

	/// <summary>
	/// 문자 바로 편집 (바로 반영): 입력할 때마다 문자 값을 임시로 바꿔 그 자리에서 다시 그리고,
	/// 확정하면 원래 값으로 되돌린 뒤 한 번의 되돌리기 가능한 변경으로 기록한다. 취소하면 원래대로.
	/// </summary>
	/// <summary>
	/// TEXT 한 줄을 그 자리에서 입력 (기존 CAD처럼): Enter로 확정하면 바로 아래 새 줄을 이어 쓰고, 빈 줄에서 Enter면 끝.
	/// </summary>
	private void BeginTextLine(TextEntity line)
	{
		this.BeginLiveTextEdit(line, removeIfEmpty: true, afterCommit: final =>
		{
			if (string.IsNullOrWhiteSpace(final) || this._doc == null)
			{
				return;
			}
			var next = TextCommand.NextLine(line);
			this._doc.AddEntities(next);
			Dispatcher.UIThread.Post(() => this.BeginTextLine(next), DispatcherPriority.Background);
		});
	}

	private void BeginLiveTextEdit(Entity ent, bool removeIfEmpty = false, Action<string>? afterCommit = null)
	{
		if (this._doc == null)
		{
			return;
		}
		var doc = this._doc;
		bool isM = ent is MText or MultiLeader;
		string original = ChangeTextAction.GetTextValue(ent);
		string plain = Free2DCad.Rendering.MTextTokenizer.PlainText(original, isM);

		// 편집 중에는 선택 점선·그립을 숨긴다 (글자 위에 겹쳐 보이지 않게)
		if (this._canvas.SelectedEntities.Count > 0)
		{
			this._canvas.SelectedEntities.Clear();
			this._canvas.InvalidateVisual();
		}

		void ShowNow()
		{
			this._dirty[ent] = this._sceneVersion + 1;
			this.UpdateQuickOverlay();
		}

		// 문자 편집기 탭이 글자별 서식을 넣으면 session.Raw(서식 문자열)가 바뀐다
		this._inplaceEditor.Commit();
		var session = new LiveTextSession(ent, isM, original, ShowNow);
		this._liveText = session;
		this._inplaceEditor.BeginLive(plain, isM,
			preview: shown =>
			{
				ChangeTextAction.SetTextValue(ent, Free2DCad.Rendering.MTextTokenizer.ReplacePlain(session.Raw, shown, isM));
				ShowNow();
				var one = SceneBuilder.BuildEntities(doc.Document, new[] { ent });
				return one.Texts.FirstOrDefault(t => t.Caret != null);
			},
			onCommit: final =>
			{
				ChangeTextAction.SetTextValue(ent, original);
				string value = Free2DCad.Rendering.MTextTokenizer.ReplacePlain(session.Raw, final, isM);
				if (removeIfEmpty && string.IsNullOrWhiteSpace(final))
				{
					doc.Erase(ent); // 새로 만든 문자를 비운 채 끝냄: 만들지 않은 것으로 (기존 CAD와 같음)
				}
				else if (value != original)
				{
					doc.Execute(new ChangeTextAction(ent, value)); // 화면 갱신·되돌리기 기록
				}
				else
				{
					ShowNow();
					this.RefreshCurrentSpaceView(keepView: true);
				}
				this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
				afterCommit?.Invoke(final);
			},
			onCancel: () =>
			{
				ChangeTextAction.SetTextValue(ent, original);
				if (removeIfEmpty && string.IsNullOrEmpty(original))
				{
					doc.Erase(ent);
					return;
				}
				ShowNow();
				this.RefreshCurrentSpaceView(keepView: true);
			});
	}

	/// <summary>
	/// 치수 문자 바로 편집 (기존 CAD처럼 그 자리에서): 화면에 보이는 글(측정값이 들어간 글, 예: Ø4.5)을 고치고,
	/// 측정값 부분이 그대로 남아 있으면 저장할 때 &lt;&gt;로 되돌려 치수와 측정값의 연결을 유지한다.
	/// 치수 문자를 찾지 못하면 false (입력 칸 방식으로).
	/// </summary>
	private bool BeginLiveDimensionEdit(Dimension dim)
	{
		if (this._doc == null)
		{
			return false;
		}
		var doc = this._doc;
		string original = dim.Text ?? string.Empty;
		var first = SceneBuilder.BuildEntities(doc.Document, new[] { dim }).Texts.FirstOrDefault(t => t.Caret != null && t.EditText != null);
		if (first?.EditText is not { } display)
		{
			return false;
		}

		// 원래 문자 = 앞글 + <> + 뒷글 (비어 있으면 측정값만). <>가 없으면 고정 문자.
		int k = original.IndexOf("<>", StringComparison.Ordinal);
		bool associative = original.Length == 0 || k >= 0;
		string rawPre = original.Length == 0 ? string.Empty : k >= 0 ? original[..k] : string.Empty;
		string rawSuf = original.Length == 0 ? string.Empty : k >= 0 ? original[(k + 2)..] : string.Empty;
		string prePlain = Free2DCad.Rendering.MTextTokenizer.PlainText(rawPre);
		string sufPlain = Free2DCad.Rendering.MTextTokenizer.PlainText(rawSuf);
		string meas = associative && display.Length >= prePlain.Length + sufPlain.Length
			? display.Substring(prePlain.Length, display.Length - prePlain.Length - sufPlain.Length)
			: string.Empty;

		string ToValue(string shown)
		{
			if (!associative)
			{
				return Free2DCad.Rendering.MTextTokenizer.ReplacePlain(original, shown);
			}
			int idx = -1;
			if (meas.Length > 0)
			{
				idx = shown.StartsWith(prePlain, StringComparison.Ordinal) && shown.AsSpan(prePlain.Length).StartsWith(meas, StringComparison.Ordinal)
					? prePlain.Length
					: shown.IndexOf(meas, StringComparison.Ordinal);
			}
			if (idx < 0)
			{
				return Free2DCad.Rendering.MTextTokenizer.Escape(shown); // 측정값을 지웠으면 고정 문자
			}
			string newPre = shown[..idx], newSuf = shown[(idx + meas.Length)..];
			string rp = newPre == prePlain ? rawPre
				: prePlain.Length == 0 ? rawPre + Free2DCad.Rendering.MTextTokenizer.Escape(newPre)
				: Free2DCad.Rendering.MTextTokenizer.ReplacePlain(rawPre, newPre);
			string rs = newSuf == sufPlain ? rawSuf
				: sufPlain.Length == 0 ? Free2DCad.Rendering.MTextTokenizer.Escape(newSuf) + rawSuf
				: Free2DCad.Rendering.MTextTokenizer.ReplacePlain(rawSuf, newSuf);
			string v = rp + "<>" + rs;
			return v == "<>" ? string.Empty : v;
		}

		if (this._canvas.SelectedEntities.Count > 0)
		{
			this._canvas.SelectedEntities.Clear();
			this._canvas.InvalidateVisual();
		}

		// 치수 블록을 새로 만들면(UpdateBlock) 치수선 일부와 화살표·문자 색이 빠진다 → 블록 안 문자만 바꾼다
		var blockText = dim.Block?.Entities.OfType<MText>().FirstOrDefault();
		string originalBlockValue = blockText?.Value ?? string.Empty;

		void Set(string value, string shownText)
		{
			dim.Text = value;
			if (blockText != null)
			{
				blockText.Value = Free2DCad.Rendering.MTextTokenizer.ReplacePlain(originalBlockValue, shownText);
			}
			else
			{
				Free2DCad.Core.Editing.GripEditor.RefreshDimension(dim);
			}
		}

		void Show()
		{
			this._dirty[dim] = this._sceneVersion + 1;
			this.UpdateQuickOverlay();
		}

		this._inplaceEditor.BeginLive(display, false,
			preview: shown =>
			{
				Set(ToValue(shown), shown);
				Show();
				return SceneBuilder.BuildEntities(doc.Document, new[] { dim }).Texts.FirstOrDefault(t => t.Caret != null);
			},
			onCommit: final =>
			{
				string value = ToValue(final);
				string newBlockValue = blockText != null ? Free2DCad.Rendering.MTextTokenizer.ReplacePlain(originalBlockValue, final) : string.Empty;
				Set(original, display);
				if (value != original)
				{
					doc.Execute(new EntityEditAction("치수 문자 편집", dim,
						() => { dim.Text = value; if (blockText != null) blockText.Value = newBlockValue; else Free2DCad.Core.Editing.GripEditor.RefreshDimension(dim); },
						() => { dim.Text = original; if (blockText != null) blockText.Value = originalBlockValue; else Free2DCad.Core.Editing.GripEditor.RefreshDimension(dim); }));
				}
				else
				{
					Show();
					this.RefreshCurrentSpaceView(keepView: true);
				}
				this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
			},
			onCancel: () =>
			{
				Set(original, display);
				Show();
				this.RefreshCurrentSpaceView(keepView: true);
			});
		return true;
	}

	/// <summary>문자 바로 편집 칸을 놓을 도면 영역: 단일·여러 줄 문자는 그려진 영역, 치수는 치수 문자 자리.</summary>
	private (double MinX, double MinY, double MaxX, double MaxY)? InplaceTextArea(Entity ent)
	{
		if (ent is Dimension dim)
		{
			double h = dim.Style.TextHeight * (dim.Style.ScaleFactor > 0 ? dim.Style.ScaleFactor : 1);
			if (h <= 0) h = 2.5;
			string shown = string.IsNullOrEmpty(dim.Text) ? $"{dim.Measurement:0.##}" : dim.Text.Replace("<>", $"{dim.Measurement:0.##}");
			double w = Math.Max(shown.Length, 3) * h * 0.8;
			var c = dim.TextMiddlePoint;
			return (c.X - w / 2, c.Y - h / 2, c.X + w / 2, c.Y + h / 2);
		}
		if (ent is MultiLeader ml)
		{
			// 문자 자리: 문자 위치(위쪽 기준)에서 착륙선 방향으로
			var cd = ml.ContextData;
			double h = cd.TextHeight > 0 ? cd.TextHeight : 2.5;
			string shown = Free2DCad.Rendering.MTextTokenizer.PlainText(cd.TextLabel ?? string.Empty);
			int lines = Math.Max(1, shown.Split('\n').Length);
			double w = Math.Max(Math.Max(shown.Split('\n').Max(l => l.Length), 8) * h * 0.8, h * 8);
			var t = cd.TextLocation;
			bool right = cd.TextAlignment == ACadSharp.TextAlignmentType.Right;
			return right ? (t.X - w, t.Y - h * 1.6 * lines, t.X, t.Y) : (t.X, t.Y - h * 1.6 * lines, t.X + w, t.Y);
		}
		return this._canvas.EntityWorldBounds(ent);
	}

	private void ApplyEditedText(Entity ent, string result)
	{
		if (this._doc == null)
		{
			return;
		}
		switch (ent)
		{
			case Dimension dim:
			{
				string oldText = dim.Text ?? string.Empty;
				// 보이는 글자만 고쳤으므로 원래 서식 문자열의 바뀐 자리만 바꾼다 (서식 유지)
				string newText = result.Trim() == "<>" ? string.Empty
					: string.IsNullOrEmpty(oldText) ? Free2DCad.Rendering.MTextTokenizer.Escape(result)
					: Free2DCad.Rendering.MTextTokenizer.ReplacePlain(oldText, result);
				this._doc.Execute(new EntityEditAction("치수 문자 편집", dim,
					() => { dim.Text = newText; Free2DCad.Core.Editing.GripEditor.RefreshDimension(dim); },
					() => { dim.Text = oldText; Free2DCad.Core.Editing.GripEditor.RefreshDimension(dim); }));
				break;
			}
			case MText mt:
				this._doc.Execute(new ChangeTextAction(mt, Free2DCad.Rendering.MTextTokenizer.ReplacePlain(mt.Value, result)));
				break;
			case TextEntity t:
				this._doc.Execute(new ChangeTextAction(t, result));
				break;
			case MultiLeader ml:
			{
				string oldLabel = ml.ContextData.TextLabel ?? string.Empty;
				string newLabel = string.IsNullOrEmpty(oldLabel) ? Free2DCad.Rendering.MTextTokenizer.Escape(result) : Free2DCad.Rendering.MTextTokenizer.ReplacePlain(oldLabel, result);
				this._doc.Execute(new EntityEditAction("다중지시선 문자 편집", ml,
					() => Free2DCad.Core.Commands.MLeaderBuilder.SetText(ml, newLabel),
					() => Free2DCad.Core.Commands.MLeaderBuilder.SetText(ml, oldLabel)));
				break;
			}
		}
		this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
	}

	private void TogglePropertyPanel(bool? visible = null)
	{
		bool target = visible ?? !this._propertyPanel.IsVisible;
		if (target == this._propertyPanel.IsVisible)
		{
			return;
		}
		var col = this._centerGrid.ColumnDefinitions[0];
		if (target)
		{
			// 닫기 전에 쓰던 폭(끌어서 바꾼 폭 포함)으로 되돌림
			col.Width = new GridLength(this._propertyPanelWidth > 0 ? this._propertyPanelWidth : 225);
		}
		else
		{
			// 열 폭을 0으로 해야 도면 영역이 그 자리까지 넓어진다 (숨기기만 하면 225px 빈칸이 남았음)
			this._propertyPanelWidth = col.ActualWidth > 0 ? col.ActualWidth : col.Width.Value;
			col.Width = new GridLength(0);
		}
		this._propertyPanel.IsVisible = target;
		this._propertySplitter.IsVisible = target;
	}

	private double _propertyPanelWidth = 225;

	private void EditSelectedBlockAttributes()
	{
		var insert = this._canvas.SelectedEntities.OfType<Insert>().FirstOrDefault();
		if (insert == null)
		{
			this._cmdLine.Print("속성을 편집할 블록 참조(Insert) 객체를 먼저 선택하세요.");
			return;
		}

		this.TogglePropertyPanel(true);
		var attrCount = insert.Attributes.Count;
		if (attrCount > 0)
		{
			this._cmdLine.Print($"선택된 블록 \"{insert.Block?.Name}\" (속성 {attrCount}개): 좌측 속성 팔레트에서 편집할 수 있습니다.");
		}
		else
		{
			this._cmdLine.Print($"선택된 블록 \"{insert.Block?.Name}\"에 정의된 속성(Attribute)이 없습니다.");
		}
	}

	private void OnRibbonCommand(string cmd)
	{
		if (cmd == "PLOT")
		{
			this.ShowPlotDialog();
			return;
		}
		if (cmd == "ZOOM_EXTENTS")
		{
			this._canvas.ZoomExtents();
			return;
		}
		if (cmd == "ZOOM_IN")
		{
			if (this._canvas.View != null)
			{
				this._canvas.View.ZoomAt(this._canvas.Bounds.Width / 2, this._canvas.Bounds.Height / 2, 1.25);
				this._canvas.InvalidateVisual();
				this._propertyPanel.RefreshViewProperties();
			}
			return;
		}
		if (cmd == "ZOOM_OUT")
		{
			if (this._canvas.View != null)
			{
				this._canvas.View.ZoomAt(this._canvas.Bounds.Width / 2, this._canvas.Bounds.Height / 2, 1.0 / 1.25);
				this._canvas.InvalidateVisual();
				this._propertyPanel.RefreshViewProperties();
			}
			return;
		}
		if (cmd == "SELECT_ALL")
		{
			this._canvas.SelectAll();
			return;
		}
		if (cmd == "EATTEDIT")
		{
			this.EditSelectedBlockAttributes();
			return;
		}

		this.StartCommandByName(cmd);
	}

	private void OnRightClickCanvas()
	{
		if (!string.IsNullOrEmpty(this._lastCommandName))
		{
			this.StartCommandByName(this._lastCommandName);
		}
	}

	/// <summary>화면 처리 중 예외가 복구됐을 때: 진행 중 명령을 안전하게 끝내고 명령줄에 알린다.</summary>
	public void ReportRecoveredError(Exception ex)
	{
		try
		{
			var cmd = this._canvas.ActiveCommand;
			this._canvas.ActiveCommand = null;
			try { cmd?.OnCancel(); } catch (Exception) { }
			this._cmdLine.CloseSuggestions();
			this._cmdLine.Print($"오류가 발생해 명령을 취소했습니다: {ex.Message} (기록: {CrashLog.FilePath})");
			this._cmdLine.SetPrompt("명령어:");
			this._canvas.InvalidateVisual();
		}
		catch (Exception)
		{
			// 알림 중 오류는 무시 (다시 예외를 내면 무한 반복)
		}
	}

	/// <summary>속성 팔레트 등 다른 입력 칸에 포커스가 있으면 그 칸에 단축키 동작(모두 선택·복사·붙여넣기·되돌리기)을 하고 true.</summary>
	private bool InEditBox(Action<TextBox> action)
	{
		if (this.FocusManager?.GetFocusedElement() is TextBox tb && !ReferenceEquals(tb, this._cmdLine.InputBox))
		{
			action(tb);
			return true;
		}
		return false;
	}

	/// <summary>다른 입력 칸(대화상자, 속성 팔레트 편집 칸 등)에 포커스가 있으면 끼어들지 않는다.</summary>
	private bool IsEditingElsewhere(KeyEventArgs? e = null) =>
		this.FocusManager?.GetFocusedElement() is TextBox tb && !ReferenceEquals(tb, this._cmdLine.InputBox);

	private void OnUnhandledKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Handled || e.KeyModifiers != KeyModifiers.None || this.IsEditingElsewhere())
		{
			return;
		}
		switch (e.Key)
		{
			case Key.Escape:
				if (!this._cmdLine.CloseSuggestions())
				{
					this.OnCancelRequested();
				}
				break;
			case Key.Enter:
				this._cmdLine.AcceptOrSubmit();
				break;
			case Key.Space:
				this._cmdLine.SpaceOrSubmit();
				break;
			case Key.Back:
				this._cmdLine.Backspace();
				break;
			case Key.Up:
			case Key.Down:
			case Key.Tab:
				if (!this._cmdLine.HandleListKey(e.Key))
				{
					return;
				}
				break;
			default:
				return;
		}
		e.Handled = true;
		this._canvas.Focus(); // 이후 입력이 다시 캔버스 경로로 오도록
	}

	private void OnUnhandledTextInput(object? sender, TextInputEventArgs e)
	{
		// 공백은 Space 키(=Enter)로 이미 처리됨
		if (e.Handled || string.IsNullOrWhiteSpace(e.Text) || this.IsEditingElsewhere())
		{
			return;
		}
		// 명령줄 입력 칸에 포커스가 있으면 그 칸이 이미 처리함
		if (ReferenceEquals(this.FocusManager?.GetFocusedElement(), this._cmdLine.InputBox))
		{
			return;
		}
		this._cmdLine.AppendText(e.Text);
		e.Handled = true;
		this._canvas.Focus();
	}

	private static bool IsPlainNumber(string text) =>
		double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

	private void OnCancelRequested()
	{
		if (this._canvas.ActiveCommand != null)
		{
			this._cmdLine.EchoPrompt("*취소*");
			this._canvas.ActiveCommand.OnCancel();
			this._canvas.ActiveCommand = null;
			this._cmdLine.SetPrompt("명령어:");
			this._canvas.InvalidateVisual();
		}
		else
		{
			this._canvas.ClearSelection();
		}
	}

	private void OnCommandSubmitted(string text)
	{
		string raw = text;
		text = text.Trim();
		if (this._canvas.ActiveCommand != null && !this._canvas.ActiveCommand.IsFinished)
		{
			this._cmdLine.EchoPrompt(text); // 예: "다음 점 지정: @100,0"
		}

		// 1. 실행 중인 명령이 있는 경우
		if (this._canvas.ActiveCommand != null && !this._canvas.ActiveCommand.IsFinished)
		{
			var running = this._canvas.ActiveCommand;
			if (string.IsNullOrEmpty(text))
			{
				this._canvas.ActiveCommand.OnEnter();
			}
			else if (running.SpaceIsText)
			{
				// 문자 입력 단계: 좌표·숫자로 바꾸지 않고 친 그대로 (앞뒤 공백 포함)
				running.OnText(raw);
			}
			else if (running.WantsSelection && text.ToUpperInvariant() is "ALL" or "전체" or "L" or "LAST" or "P" or "PREVIOUS")
			{
				// 객체 선택 옵션 (기존 CAD와 같음): ALL = 지금 공간의 모든 객체(꺼진·동결 도면층 제외), L = 마지막에 만든 객체, P = 이전 선택
				bool Visible(Entity en) => en.Layer is not { IsOn: false } && !(en.Layer?.Flags.HasFlag(ACadSharp.Tables.LayerFlags.Frozen) ?? false);
				var space = this._doc!.CurrentSpace.Entities;
				List<Entity> found = text.ToUpperInvariant() switch
				{
					"L" or "LAST" => space.Where(Visible).TakeLast(1).ToList(),
					"P" or "PREVIOUS" => this._doc.PreviousSelection.Where(en => en.Owner != null && space.Contains(en)).ToList(),
					_ => space.Where(Visible).ToList(),
				};
				foreach (var en in found) this._canvas.SelectedEntities.Add(en);
				running.OnSelection(found);
				this._cmdLine.Print($"{found.Count}개를 찾음");
			}
			else
			{
				(double X, double Y)? cursorPt = this._canvas.CurrentSnap.HasValue
					? (this._canvas.CurrentSnap.Value.X, this._canvas.CurrentSnap.Value.Y)
					: this._canvas.CursorWorld;
				var ctx = this._canvas.CommandContext;

				// 숫자만 입력: 명령이 값(각도·반지름·개수 등)으로 받으면 그대로, 아니면 직접 거리(커서 방향으로 그 거리의 점)
				bool handled = false;
				if (IsPlainNumber(text))
				{
					var before = (running.Prompt, running.IsFinished, ctx?.PrintCount ?? 0);
					running.OnText(text);
					handled = before != (running.Prompt, running.IsFinished, ctx?.PrintCount ?? 0);
				}
				if (handled)
				{
				}
				else if (CoordinateParser.TryParse(text, ctx?.LastPoint, cursorPt, this._canvas.OrthoEnabled, out var pt))
				{
					this._canvas.ActiveCommand.OnPoint(pt.X, pt.Y);
				}
				else
				{
					this._canvas.ActiveCommand.OnText(text);
				}
			}

			if (this._canvas.ActiveCommand.IsFinished)
			{
				this._canvas.ActiveCommand = null;
				this._cmdLine.SetPrompt("명령어:");
				this.OnCommandCompleted(running);
			}
			else
			{
				this._cmdLine.SetPrompt(this._canvas.ActiveCommand.Prompt);
			}

			this._canvas.InvalidateVisual();
			return;
		}

		// 2. 실행 중인 명령이 없는 경우: 신규 명령 시작 또는 직전 명령 반복
		if (string.IsNullOrEmpty(text))
		{
			if (!string.IsNullOrEmpty(this._lastCommandName))
			{
				this.StartCommandByName(this._lastCommandName);
			}
			return;
		}

		this._cmdLine.Print($"명령어: {text}");
		this.StartCommandByName(text);
	}

	private void StartCommandByName(string nameOrAlias)
	{
		if (this._doc == null)
		{
			this._cmdLine.Print("열려 있는 도면 문서가 없습니다.");
			return;
		}

		if (string.Equals(nameOrAlias, "LAYER", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "LA", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowLayerManagerDialog();
			return;
		}

		// 리본·메뉴의 방식 지정 명령 (원 2점, 호 시작-중심-끝, 맨 앞으로 등)
		if (CommandPresets.Create(nameOrAlias) is { } preset)
		{
			this.StartCommand(preset);
			return;
		}
		switch (nameOrAlias.Trim().ToUpperInvariant())
		{
			case "GROUP_SELECT_TOGGLE":
				this._canvas.GroupSelection = !this._canvas.GroupSelection;
				this._cmdLine.Print(this._canvas.GroupSelection ? "그룹 선택 켜짐 (그룹 구성원을 하나 고르면 그룹 전체가 선택됨)" : "그룹 선택 꺼짐");
				return;
			case "PTYPE":
			case "DDPTYPE":
				_ = this.ShowPointStyleDialogAsync();
				return;
			case "QSELECT":
				_ = this.ShowQuickSelectDialogAsync();
				return;
			case "SELECT_ALL":
				this._canvas.SelectAll();
				return;
			case "BEDIT":
			case "BE":
				_ = this.BeginBlockEditAsync();
				return;
			case "BCLOSE":
				_ = this.CloseBlockEditorAsync();
				return;
			case "BSAVE":
				this._cmdLine.Print(this._beditBlock != null ? "블록 정의를 저장했습니다 (편집 내용은 바로 반영됩니다)." : "블록 편집기가 열려 있지 않습니다.");
				return;
			case "XREF":
			case "XR":
				_ = this.ShowXrefManagerAsync();
				return;
			case "XATTACH":
			case "XA":
				_ = this.XAttachAsync();
				return;
			case "TOLERANCE":
			case "TOL":
				_ = this.ShowToleranceDialogAsync();
				return;
			case "FIND":
				_ = this.ShowFindDialogAsync();
				return;
			case "BLOCK":
			case "B":
			case "BMAKE":
				_ = this.ShowBlockDefineDialogAsync();
				return;
			case "-BLOCK":
			case "-B":
				this.StartCommand(new BlockCommand());
				return;
			case "INSERT":
			case "I":
			case "DDINSERT":
				_ = this.ShowBlockInsertDialogAsync();
				return;
			case "-INSERT":
			case "-I":
				this.StartCommand(new InsertCommand());
				return;
			case "LAYERSTATE":
			case "LAS":
				_ = this.ShowLayerStateManagerAsync();
				return;
			case "ATTEXT":
				_ = this.ExportAttributesAsync();
				return;
			case "HELP":
				this.ShowHelpDialog();
				return;
			case "TEXT_WINDOW":
			case "TEXTSCR":
				this.ShowTextWindow();
				return;
			case "LAYOUT_NEW":
				this.AddNewLayout();
				return;
			case "PDFEXPORT":
			case "EXPORTPDF":
				this.ShowPlotDialog();
				return;
			case "DXFOUT":
			case "EXPORT":
				_ = this.SaveAsAsync();
				return;
			case "PNGOUT":
			case "BMPOUT":
				_ = this.ExportPngAsync();
				return;
		}

		string upperName = nameOrAlias.ToUpperInvariant();
		if (upperName is "REGEN" or "RE" or "REGENALL" or "REA")
		{
			// 화면 재생성: 임시 층을 비우고 도면 전체를 다시 계산해 그린다 (확대 상태 유지)
			this._dirty.Clear();
			this.UpdateQuickOverlay();
			this.RefreshCurrentSpaceView(keepView: true);
			this._cmdLine.Print(upperName.StartsWith("REGENA") || upperName == "REA" ? "모든 뷰포트 재생성 중." : "모형 재생성 중.");
			return;
		}
		if (upperName is "SAVE" or "QSAVE")
		{
			_ = this.SaveAsync();
			return;
		}
		if (upperName is "SAVEAS")
		{
			_ = this.SaveAsAsync();
			return;
		}
		if (upperName is "OPEN")
		{
			_ = this.OpenAsync();
			return;
		}
		if (upperName is "NEW" or "QNEW")
		{
			this.NewDocumentWithTemplateChoice();
			return;
		}
		if (upperName is "OPTIONS" or "OP")
		{
			this.ShowDrawingSettingsDialog(0, 0);
			return;
		}
		if (upperName is "BLOCKLIB")
		{
			_ = this.ShowBlockLibraryAsync();
			return;
		}
		if (upperName is "RESETSETTINGS")
		{
			_ = this.ResetAllSettingsAsync();
			return;
		}

		if (string.Equals(nameOrAlias, "PLOT", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "PRINT", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowPlotDialog();
			return;
		}

		if (string.Equals(nameOrAlias, "EATTEDIT", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "ATE", StringComparison.OrdinalIgnoreCase))
		{
			this.EditSelectedBlockAttributes();
			return;
		}


		if (string.Equals(nameOrAlias, "HATCH", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "H", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "BH", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "BHATCH", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowHatchDialog();
			return;
		}

		if (string.Equals(nameOrAlias, "GRADIENT", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowHatchDialog(new HatchDialogState { ActiveTab = 1 });
			return;
		}

		if (string.Equals(nameOrAlias, "PROPERTIES", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "PR", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "PROPS", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "CH", StringComparison.OrdinalIgnoreCase))
		{
			this.TogglePropertyPanel(true);
			return;
		}

		if (string.Equals(nameOrAlias, "DSETTINGS", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "DS", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "SE", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowDrawingSettingsDialog();
			return;
		}

		if (string.Equals(nameOrAlias, "UNITS", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "UN", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowDrawingSettingsDialog(0, 0);
			return;
		}

		if (string.Equals(nameOrAlias, "OSNAP", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "OS", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowDrawingSettingsDialog(1, 1);
			return;
		}

		if (string.Equals(nameOrAlias, "STYLE", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "ST", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowTextStyleDialog();
			return;
		}

		if (string.Equals(nameOrAlias, "DIMSTYLE", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "D", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "DST", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "DDIM", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowDimStyleDialog();
			return;
		}

		if (string.Equals(nameOrAlias, "LINETYPE", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "LT", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(nameOrAlias, "LTYPE", StringComparison.OrdinalIgnoreCase))
		{
			this.ShowLinetypeManagerDialog();
			return;
		}

		if (upperName is "TABLE" or "TB")
		{
			_ = this.ShowTableInsertDialogAsync();
			return;
		}

		var cmd = CommandRegistry.Create(nameOrAlias);
		if (cmd == null)
		{
			this._cmdLine.Print($"알 수 없는 명령 \"{nameOrAlias}\". 도움말을 보려면 F1 키를 누르십시오.");
			return;
		}

		this.StartCommand(cmd);
	}

	private void ShowDrawingSettingsDialog(int initialTab = 0, int initialSubTab = 0)
	{
		if (this._doc == null) return;
		var dlg = new DrawingSettingsDialog(this._doc, this._canvas, initialTab, initialSubTab);
		dlg.Closed += (_, _) =>
		{
			if (dlg.FontsChanged)
			{
				// 글꼴이 바뀌면 문자를 다시 만든다
				this._dirty.Clear();
				this.UpdateQuickOverlay();
				this.RefreshCurrentSpaceView(keepView: true);
			}
		};
		dlg.ShowDialog(this);
	}

	private void ShowTextStyleDialog()
	{
		if (this._doc == null) return;
		var dlg = new TextStyleDialog(this._doc);
		dlg.ShowDialog(this);
	}

	private void ShowDimStyleDialog()
	{
		if (this._doc == null) return;
		var dlg = new DimStyleManagerDialog(this._doc);
		dlg.ShowDialog(this);
	}

	private void ShowLinetypeManagerDialog()
	{
		if (this._doc == null) return;
		var dlg = new LinetypeManagerDialog(this._doc);
		dlg.Closed += (_, _) =>
		{
			this._ribbon.UpdateLineTypes(this._doc.Document.LineTypes, this._doc.Document.Header.CurrentLineTypeName);
		};
		dlg.ShowDialog(this);
	}

	private void OnLinetypeSelected(string name)
	{
		if (this._doc == null) return;
		this._doc.Document.Header.CurrentLineTypeName = name;
		if (this._canvas.SelectedEntities.Count > 0)
		{
			var lt = this._doc.Document.LineTypes.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
			if (lt != null)
			{
				var targets = this._canvas.SelectedEntities.ToList();
				var oldLts = targets.Select(e => e.LineType).ToList();
				this._doc.Execute(new DelegateAction(
					"선종류 변경",
					() => { foreach (var e in targets) e.LineType = lt; this._canvas.InvalidateVisual(); },
					() => { for (int i = 0; i < targets.Count; i++) targets[i].LineType = oldLts[i]; this._canvas.InvalidateVisual(); }
				));
			}
		}
	}

	/// <summary>주석 축척 제안을 이미 물어본 도면 (도면마다 한 번).</summary>
	private readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorDocument, object> _annoAsked = new();

	/// <summary>문자·치수·지시선을 만드는 명령 (주석 축척 제안 대상).</summary>
	private static readonly HashSet<string> AnnotationCommands = new(StringComparer.OrdinalIgnoreCase)
	{
		"TEXT", "DTEXT", "MTEXT", "MLEADER", "QLEADER", "LEADER", "TOLERANCE", "QDIM",
		"DIMLINEAR", "DIMALIGNED", "DIMANGULAR", "DIMARC", "DIMORDINATE", "DIMRADIUS", "DIMDIAMETER", "DIMJOGGED",
		"DIMBASELINE", "DIMCONTINUE", "DIMCENTER",
	};

	private void StartCommand(EditorCommand cmd)
	{
		if (this._doc == null)
		{
			return;
		}

		// 주석 축척 자동 제안: 1:1인데 그린 도면이 용지보다 훨씬 크면, 문자·치수 명령을 시작할 때 한 번 묻는다
		if (AnnotationCommands.Contains(cmd.Name) && this.NeedsAnnotationScalePrompt(out double suggest, out double ratio))
		{
			_ = this.AskAnnotationScaleThenStartAsync(cmd, suggest, ratio);
			return;
		}

		this._lastCommandName = cmd.Name;
		this._runningCommand = cmd;
		var ctx = new CommandContext(
			this._doc,
			this._canvas.SelectedEntities,
			this._canvas.OrthoEnabled,
			null,
			msg => this._cmdLine.Print(msg));

		ctx.Picker = (px, py) => this._canvas.PickAt(px, py);
		ctx.View = this.ViewController;
		this._canvas.CommandContext = ctx;
		this._canvas.ActiveCommand = cmd;
		this._canvas.ClearTracking();

		if (cmd is TableOutCommand tbo)
		{
			tbo.OnTableExtracted = result => Dispatcher.UIThread.Post(() => this.ShowTablePreviewDialog(result));
		}
		if (cmd is ClassicLeaderCommand leaderCmd)
		{
			this.HookLeaderCommand(leaderCmd);
		}
		if (cmd is PointCommand pointCmd)
		{
			pointCmd.SettingsRequested += () => Dispatcher.UIThread.Post(() => _ = this.ShowPointStyleDialogAsync());
		}

		cmd.Start(ctx);
		// 리본의 옵션이 정해진 버튼 (예: 줌 윈도우 = ZOOM + W)
		if (cmd.AutoInput is { } auto)
		{
			foreach (string input in auto)
			{
				if (cmd.IsFinished) break;
				cmd.OnText(input);
			}
		}

		if (cmd.IsFinished)
		{
			this._canvas.ActiveCommand = null;
			this._cmdLine.SetPrompt("명령어:");
			this.OnCommandCompleted(cmd);
		}
		else
		{
			this._cmdLine.SetPrompt(cmd.Prompt);
		}

		this._canvas.InvalidateVisual();
	}

	private EditorCommand? _runningCommand;
	private Entity? _cloneSource;
	private HashSet<Entity>? _cloneBefore;

	/// <summary>명령이 끝난 뒤 앱이 이어서 할 일: 표를 만들었으면 제목 칸 편집, 표 CSV 내보내기면 저장 창.</summary>
	private void OnCommandCompleted(EditorCommand? cmd)
	{
		// 유사 선택 등: 명령 결과를 화면 선택으로
		if (cmd is ISelectionResultCommand { SelectionResult: { } picked })
		{
			this._canvas.SelectedEntities.Clear();
			this._canvas.SelectedEntities.UnionWith(picked);
			this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
			this._canvas.InvalidateVisual();
			return;
		}
		// 명령이 끝나면 고른 객체 선택을 푼다 (기존 CAD처럼). 해치 경계 고르기처럼 창으로 돌아가는 보조 명령은 제외
		if (cmd is not null and not PickEntityCommand and not HatchPreviewCommand && this._canvas.SelectedEntities.Count > 0)
		{
			this._canvas.SelectedEntities.Clear();
			this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
			this._canvas.InvalidateVisual();
		}
		// 도면층 명령 등으로 바뀐 도면층 상태를 리본·제목 콤보에 반영
		if (this._doc != null)
		{
			this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
			this.UpdateTitleLayerCombo();
		}
		// 복제(ADDSELECTED)로 시작한 그리기 명령이 끝나면 새 객체에 원본 속성을 입힌다
		if (this._cloneSource is { } src && this._cloneBefore != null && cmd is not AddSelectedCommand && this._doc != null)
		{
			var added = this._doc.CurrentSpace.Entities.Where(e => !this._cloneBefore.Contains(e)).ToList();
			this._cloneSource = null;
			this._cloneBefore = null;
			if (added.Count > 0)
			{
				var actions = added.Select(e =>
				{
					var (layer, color, lt, lw, lts) = (e.Layer, e.Color, e.LineType, e.LineWeight, e.LineTypeScale);
					return (IUndoableAction)new EntityEditAction("복제 속성", e,
						() => { e.Layer = src.Layer; e.Color = src.Color; e.LineType = src.LineType; e.LineWeight = src.LineWeight; e.LineTypeScale = src.LineTypeScale; },
						() => { e.Layer = layer; e.Color = color; e.LineType = lt; e.LineWeight = lw; e.LineTypeScale = lts; });
				}).ToList();
				this._doc.Execute(new CompositeAction("복제 속성", actions));
			}
		}
		switch (cmd)
		{
			case AddSelectedCommand { Source: { } source }:
				if (AddSelectedCommand.CommandFor(source) is { } drawCmd && this._doc != null)
				{
					this._cloneSource = source;
					this._cloneBefore = this._doc.CurrentSpace.Entities.ToHashSet();
					Dispatcher.UIThread.Post(() => this.StartCommandByName(drawCmd), DispatcherPriority.Background);
				}
				else
				{
					this._cmdLine.Print("이 객체 종류는 복제할 수 없습니다.");
				}
				break;
			case WblockCommand { Result: { } exported }:
				_ = this.SaveWblockAsync(exported);
				break;
			case TextCommand { Created: { } newText }:
				Dispatcher.UIThread.Post(() => this.BeginTextLine(newText), DispatcherPriority.Background);
				break;
			case MTextCommand { Created: { } newMText }:
				Dispatcher.UIThread.Post(() => this.BeginLiveTextEdit(newMText, removeIfEmpty: true), DispatcherPriority.Background);
				break;
			case MLeaderCommand { Created: { } newMLeader }:
				Dispatcher.UIThread.Post(() => this.BeginLiveTextEdit(newMLeader), DispatcherPriority.Background);
				break;
			case ClassicLeaderCommand { Created: { } classicText }:
				Dispatcher.UIThread.Post(() => this.BeginLiveTextEdit(classicText), DispatcherPriority.Background);
				break;
			case LeaderCommand { Created: { } leaderText }:
				Dispatcher.UIThread.Post(() => this.BeginLiveTextEdit(leaderText), DispatcherPriority.Background);
				break;
			case TextEditCommand { Picked: { } editTarget }:
				Dispatcher.UIThread.Post(() => _ = this.EditEntityTextAsync(editTarget), DispatcherPriority.Background);
				break;
			case TableCommand { Created: { } table }:
				Dispatcher.UIThread.Post(() => this.BeginTableCellEdit(table, 0, 0), DispatcherPriority.Background);
				break;
			case TableExportCommand { Csv: { } csv }:
				_ = this.SaveTableCsvAsync(csv);
				break;
		}
	}

	/// <summary>PTYPE: 점 스타일 창 → PDMODE·PDSIZE (되돌리기 가능).</summary>
	private async Task ShowPointStyleDialogAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		var header = this._doc.Document.Header;
		var dlg = new PointStyleDialog(header.PointDisplayMode, header.PointDisplaySize);
		await dlg.ShowDialog(this);
		if (dlg.Result is { } r)
		{
			var (oldMode, oldSize) = (header.PointDisplayMode, header.PointDisplaySize);
			this._doc.Execute(new DelegateAction("점 스타일",
				() => { header.PointDisplayMode = r.Mode; header.PointDisplaySize = r.Size; },
				() => { header.PointDisplayMode = oldMode; header.PointDisplaySize = oldSize; }));
			this.RefreshCurrentSpaceView(keepView: true);
		}
	}

	/// <summary>QSELECT: 빠른 선택 창 → 조건에 맞는 객체 선택.</summary>
	private async Task ShowQuickSelectDialogAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		var dlg = new QuickSelectDialog(this._doc.CurrentSpace.Entities.ToList());
		await dlg.ShowDialog(this);
		if (dlg.Result is { } picked)
		{
			if (!dlg.Append)
			{
				this._canvas.SelectedEntities.Clear();
			}
			this._canvas.SelectedEntities.UnionWith(picked);
			this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
			this._canvas.InvalidateVisual();
			this._cmdLine.Print($"객체 {picked.Count}개를 선택했습니다.");
		}
	}

	private async Task ShowTableInsertDialogAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		var dlg = new TableInsertDialog();
		await dlg.ShowDialog(this);
		if (dlg.Result is { } spec)
		{
			this._cmdLine.Print("명령어: TABLE");
			this.StartCommand(new TableCommand { Spec = spec, ByWindow = dlg.ByWindow });
		}
	}

	private async Task SaveTableCsvAsync(string csv)
	{
		var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "표를 CSV로 내보내기",
			SuggestedFileName = "table.csv",
			DefaultExtension = "csv",
			FileTypeChoices = new[] { new FilePickerFileType("CSV (쉼표로 구분) (*.csv)") { Patterns = new[] { "*.csv" } } },
		});
		string? path = file?.TryGetLocalPath();
		if (path == null)
		{
			return;
		}
		// 엑셀에서 한글이 깨지지 않도록 BOM 있는 UTF-8
		await File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(true));
		this._cmdLine.Print($"CSV로 내보냈습니다: {path}");
	}

	/// <summary>
	/// 표 칸 바로 편집: 칸 문자가 제자리에서 바로 바뀐다. Enter = 확정, Tab/Shift+Tab = 다음/이전 칸, Esc = 취소.
	/// 확정하면 되돌리기 한 번으로 기록된다.
	/// </summary>
	private void BeginTableCellEdit(TableEntity table, int row, int col)
	{
		if (this._doc == null || table.Document == null)
		{
			return;
		}
		var doc = this._doc;
		string original = TableEditor.GetCellText(table, row, col);
		if (this._canvas.SelectedEntities.Count > 0)
		{
			this._canvas.SelectedEntities.Clear();
			this._canvas.InvalidateVisual();
		}

		void Show()
		{
			this._dirty[table] = this._sceneVersion + 1;
			this.UpdateQuickOverlay();
		}

		// 칸의 도면 좌표 범위 (커서 위치표 고르기·빈 칸 커서)
		var (x0, y0, x1, y1) = TableEditor.CellRect(table, row, col);
		double sx = table.XScale == 0 ? 1 : table.XScale, sy = table.YScale == 0 ? 1 : table.YScale;
		double cos = Math.Cos(table.Rotation), sin = Math.Sin(table.Rotation);
		(double X, double Y) W(double lx, double ly) => (table.InsertPoint.X + lx * sx * cos - ly * sy * sin, table.InsertPoint.Y + lx * sx * sin + ly * sy * cos);
		var (cx, cy) = W((x0 + x1) / 2, (y0 + y1) / 2);
		double textH = TableEditor.GuessTextHeight(table) * sy;

		SceneText? CellScene()
		{
			var one = SceneBuilder.BuildEntities(doc.Document, new[] { table });
			var hit = one.Texts.Where(t => t.Caret != null && t.Bounds.MinX <= cx && t.Bounds.MaxX >= cx && t.Bounds.MinY <= cy && t.Bounds.MaxY >= cy).FirstOrDefault();
			if (hit != null)
			{
				return hit;
			}
			// 빈 칸: 칸 가운데에 커서만
			var b = new Bounds(cx - textH, cy - textH, cx + textH, cy + textH);
			return new SceneText(new[] { string.Empty }, 0, cx, cy, textH, table.Rotation, 1, 0, TextHAlign.Left, TextVAlign.Baseline, 0, string.Empty, Rgb.White, b, table)
			{
				PickOnly = true,
				Caret = new[] { cx, cy - textH / 2, textH, 0 },
				EditText = string.Empty,
			};
		}

		void SetText(string text)
		{
			TableEditor.SetCellText(doc.Document, table, row, col, text);
		}

		this._cmdLine.Print($"표 칸 편집 ({row + 1}행 {col + 1}열): Enter = 확정, Tab = 다음 칸, Esc = 취소");
		this._inplaceEditor.BeginLive(original, false,
			preview: shown =>
			{
				SetText(shown);
				Show();
				return CellScene();
			},
			onCommit: final =>
			{
				SetText(original);
				if (final != original)
				{
					doc.Execute(new EntityEditAction("표 칸 편집", table, () => SetText(final), () => SetText(original)));
				}
				else
				{
					Show();
					this.RefreshCurrentSpaceView(keepView: true);
				}
			},
			onCancel: () =>
			{
				SetText(original);
				Show();
				this.RefreshCurrentSpaceView(keepView: true);
			},
			onTab: back =>
			{
				if (TableEditor.NextCell(table, row, col, back) is { } next)
				{
					Dispatcher.UIThread.Post(() => this.BeginTableCellEdit(table, next.Row, next.Col), DispatcherPriority.Background);
				}
			});
	}

	private async void ShowHatchDialog(HatchDialogState? state = null)
	{
		if (this._doc == null)
		{
			this._cmdLine.Print("열려 있는 도면 문서가 없습니다.");
			return;
		}

		state ??= new HatchDialogState { Settings = HatchCommand.LastSettings.Clone() };

		var dlg = new HatchDialog(this._doc, state);
		await dlg.ShowDialog(this);

		switch (state.Action)
		{
			case HatchDialogAction.PickPoints:
			case HatchDialogAction.SelectObjects:
			case HatchDialogAction.ViewSelection:
			{
				double scale = this._canvas.View?.Scale ?? 1.0;
				if (scale <= 1e-6) scale = 1.0;
				var cmd = new HatchCommand
				{
					CollectOnly = true,
					StartWithObjects = (state.Action == HatchDialogAction.SelectObjects),
					Settings = state.Settings,
					SearchWindow = this._canvas.View?.VisibleWorld,
					PickTolerance = 5.0 / scale,
				};
				cmd.Regions.AddRange(state.Regions);
				cmd.BoundaryObjects.AddRange(state.Objects);
				cmd.Collected += c => Dispatcher.UIThread.Post(() =>
				{
					state.Regions.Clear();
					state.Regions.AddRange(c.Regions);
					state.Objects.Clear();
					state.Objects.AddRange(c.BoundaryObjects);
					this.ShowHatchDialog(state);
				});
				this.StartCommand(cmd);
				break;
			}
			case HatchDialogAction.Preview:
			{
				var hatches = HatchCommand.CreateHatches(state.Settings, state.Regions, state.Objects);
				var previewCmd = new HatchPreviewCommand(hatches);
				previewCmd.Done += () => Dispatcher.UIThread.Post(() => this.ShowHatchDialog(state));
				this.StartCommand(previewCmd);
				break;
			}
			case HatchDialogAction.Inherit:
			{
				var pickCmd = new PickEntityCommand("해치 객체 선택:", e => e is Hatch);
				pickCmd.Picked += entity => Dispatcher.UIThread.Post(() =>
				{
					if (entity is Hatch h)
					{
						state.Settings.PatternName = h.Pattern?.Name ?? (h.IsSolid ? "SOLID" : "ANSI31");
						state.Settings.AngleDegrees = h.PatternAngle * 180.0 / Math.PI;
						state.Settings.Scale = h.PatternScale > 0 ? h.PatternScale : 1.0;
						state.Settings.Color = h.Color;
						if (h.Layer?.Name != null)
						{
							state.Settings.Layer = h.Layer.Name;
						}
					}
					this.ShowHatchDialog(state);
				});
				this.StartCommand(pickCmd);
				break;
			}
			case HatchDialogAction.Ok:
			{
				var hatches = HatchCommand.CreateHatches(state.Settings, state.Regions, state.Objects);
				if (state.RetainBoundaries)
				{
					var polylines = new List<Entity>();
					foreach (var loop in state.Regions.SelectMany(r => r))
					{
						var pl = new LwPolyline { IsClosed = true };
						for (int i = 0; i + 1 < loop.Length; i += 2)
						{
							pl.Vertices.Add(new LwPolyline.Vertex(new XY(loop[i], loop[i + 1])));
						}
						polylines.Add(pl);
					}
					this._doc.BeginTransaction();
					this._doc.AddEntities(hatches);
					this._doc.AddEntities(polylines);
					this._doc.CommitTransaction("HATCH");
				}
				else
				{
					this._doc.AddEntities(hatches);
				}
				HatchCommand.LastSettings = state.Settings.Clone();
				this._canvas.InvalidateVisual();
				this._cmdLine.Print($"해치 {hatches.Count}개 작성됨.");
				break;
			}
		}
	}

	private void ShowTablePreviewDialog(Free2DCad.Core.Tools.TableResult table)
	{
		var dlg = new TablePreviewDialog(table);
		dlg.ShowDialog(this);
	}

	private void ShowHelpDialog()
	{
		var dialog = new Window
		{
			Title = $"Free 2D CAD {AppVersion} 명령 및 단축키 도움말",
			Width = 600,
			Height = 520,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};

		var scroll = new ScrollViewer
		{
			Margin = new Avalonia.Thickness(16),
			Content = new TextBlock
			{
				Foreground = CadTheme.DialogFg,
				FontFamily = "Consolas, 굴림체, monospace",
				FontSize = 12,
				Text =
@"=== Free 2D CAD 주요 명령어 및 별칭 ===

[그리기]
  LINE       (L)      선
  PLINE      (PL)     폴리선
  RECTANG    (REC)    직사각형
  CIRCLE     (C)      원 (반지름 / 지름)
  ARC        (A)      3점 호
  TEXT       (DT)     단일 행 문자
  MTEXT      (MT, T)  여러 줄 문자
  HATCH      (H)      해치

[수정]
  MOVE       (M)      이동
  COPY       (CO, CP) 복사
  ROTATE     (RO)     회전
  SCALE      (SC)     축척
  MIRROR     (MI)     대칭
  OFFSET     (O)      간격띄우기
  EXTEND     (EX)     연장
  TRIM       (TR)     자르기
  FILLET     (F)      모깎기
  CHAMFER    (CHA)    모따기
  STRETCH    (S)      신축
  ARRAY      (AR)     직사각형 배열
  BREAK      (BR)     끊기
  JOIN       (J)      결합
  EXPLODE    (X)      분해
  ERASE      (E)      지우기

[치수 및 계측]
  DIST       (DI)     거리 측정
  DIMLINEAR  (DLI)    선형 치수
  DIMALIGNED (DAL)    정렬 치수
  DIMRADIUS  (DRA)    반지름 치수
  DIMDIAMETER(DDI)    지름 치수
  DIMANGULAR (DAN)    각도 치수
  TABLEOUT   (TBO)    표 추출 (CSV)
  LIST       (LI)     객체 정보 목록
  ID         (ID)     점 좌표
  AREA       (AA)     면적 계산
  MATCHPROP  (MA)     특성 일치",
			}
		};
		dialog.Content = scroll;
		dialog.Show(this);
	}

	private void ShowTextWindow()
	{
		var dialog = new Window
		{
			Title = "Free 2D CAD 텍스트 윈도우",
			Width = 650,
			Height = 400,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};

		var dock = new DockPanel { Margin = new Avalonia.Thickness(8) };
		var inputGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Avalonia.Thickness(0, 4, 0, 0) };
		var prompt = new TextBlock { Text = "명령어: ", Foreground = CadTheme.DialogFg, VerticalAlignment = VerticalAlignment.Center };
		var input = new TextBox { Background = Brushes.Transparent, Foreground = CadTheme.DialogFg, BorderThickness = new Avalonia.Thickness(0) };
		Grid.SetColumn(prompt, 0); inputGrid.Children.Add(prompt);
		Grid.SetColumn(input, 1); inputGrid.Children.Add(input);

		var historyBox = new TextBox
		{
			Text = string.Join(Environment.NewLine, this._cmdLine.GetAllHistory()),
			IsReadOnly = true,
			AcceptsReturn = true,
			Background = Brushes.Transparent,
			Foreground = CadTheme.DialogFg,
			BorderThickness = new Avalonia.Thickness(0),
			FontFamily = "Consolas, 굴림체, monospace",
		};
		var scroll = new ScrollViewer { Content = historyBox };

		DockPanel.SetDock(inputGrid, Dock.Bottom);
		dock.Children.Add(inputGrid);
		dock.Children.Add(scroll);
		dialog.Content = dock;

		input.KeyDown += (_, ke) =>
		{
			if (ke.Key == Key.Enter)
			{
				string t = input.Text ?? string.Empty;
				input.Text = string.Empty;
				this.OnCommandSubmitted(t);
				historyBox.Text = string.Join(Environment.NewLine, this._cmdLine.GetAllHistory());
				historyBox.CaretIndex = historyBox.Text.Length;
				ke.Handled = true;
			}
		};

		dialog.Show(this);
	}

	private void UpdateTitle()
	{
		string fileName = this._session?.FileName ?? (string.IsNullOrEmpty(this._doc?.FilePath) ? "Drawing1.dwg" : Path.GetFileName(this._doc.FilePath));
		string dirty = (this._doc?.IsDirty == true) ? "*" : "";
		this.Title = string.Empty;
		this._titleDocName.Text = $"Free 2D CAD {AppVersion} - [{fileName}{dirty}]";
		this.RebuildDocTabs();
	}

	private void UpdateTitleLayerCombo()
	{
		if (this._doc == null) return;
		var layers = this._doc.Document.Layers.Select(l => l.Name).OrderBy(n => n).ToList();
		this._titleLayerCombo.ItemsSource = layers;
		this._titleLayerCombo.SelectedItem = this._doc.CurrentLayer.Name;
	}

	/// <summary>
	/// 새로 만들기: Templates 폴더에 템플릿(.dwg·.dwt·.dxf, 버전 무관)이 있으면 고르게 하고, 없으면 기본 템플릿으로 바로 만든다.
	/// </summary>
	private async void NewDocumentWithTemplateChoice()
	{
		var templates = Free2DCad.Core.UserFolders.Templates is { } tdir
			? new[] { "*.dwg", "*.dwt", "*.dxf" }.SelectMany(p => Free2DCad.Core.UserFolders.Files(tdir, p))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase).ToList()
			: new List<string>();
		if (templates.Count == 0)
		{
			this.NewDocument();
			return;
		}
		var names = new List<string> { "기본 (Free 2D CAD 내장)" };
		// 같은 이름의 dwg·dxf가 함께 있으면 구별되게 확장자까지 보인다
		names.AddRange(templates.Select(Path.GetFileName).OfType<string>());
		int? pick = await this.PickFromListAsync("새 도면 — 템플릿 선택", "템플릿 (Templates 폴더):", names);
		if (pick is not int idx)
		{
			return;
		}
		if (idx == 0)
		{
			this.NewDocument();
			return;
		}
		try
		{
			this.NewDocument(EditorDocument.NewFromTemplate(templates[idx - 1]));
			this._cmdLine.Print($"템플릿 \"{names[idx]}\"로 새 도면을 만들었습니다. 저장할 때 새 이름을 지정하세요.");
		}
		catch (Exception ex)
		{
			this._cmdLine.Print($"템플릿을 열 수 없습니다: {ex.Message}");
		}
	}

	/// <summary>간단한 목록 선택 창. 고른 번호(취소는 null).</summary>
	private async Task<int?> PickFromListAsync(string title, string label, IReadOnlyList<string> items)
	{
		var dlg = new Window
		{
			Title = title,
			Width = 420,
			Height = 380,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var list = new ListBox { ItemsSource = items, Height = 250, SelectedIndex = 0, Background = Brushes.White, Foreground = Brushes.Black };
		int? result = null;
		var ok = new Button { Content = "확인", Width = 80, Height = 26, IsDefault = true };
		var cancel = new Button { Content = "취소", Width = 80, Height = 26, IsCancel = true };
		ok.Click += (_, _) => { result = list.SelectedIndex >= 0 ? list.SelectedIndex : null; dlg.Close(); };
		cancel.Click += (_, _) => dlg.Close();
		list.DoubleTapped += (_, _) => { result = list.SelectedIndex >= 0 ? list.SelectedIndex : null; dlg.Close(); };
		var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
		buttons.Children.Add(ok);
		buttons.Children.Add(cancel);
		var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };
		panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = CadTheme.DialogFg });
		panel.Children.Add(list);
		panel.Children.Add(buttons);
		dlg.Content = panel;
		await dlg.ShowDialog(this);
		return result;
	}

	/// <summary>블록 라이브러리: Blocks 폴더의 도면을 골라 블록으로 가져온 뒤 바로 삽입 명령.</summary>
	private async Task ShowBlockLibraryAsync()
	{
		if (this._doc == null)
		{
			return;
		}
		var files = Free2DCad.Core.Tools.BlockLibrary.Files;
		if (files.Count == 0)
		{
			this._cmdLine.Print($"블록 라이브러리가 비어 있습니다. 블록으로 쓸 도면(.dwg)을 이 폴더에 넣으세요: {Free2DCad.Core.UserFolders.Blocks}");
			this.OpenUserFolder(Free2DCad.Core.UserFolders.Blocks);
			return;
		}
		var names = files.Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList();
		int? pick = await this.PickFromListAsync("블록 라이브러리", "삽입할 블록 (Blocks 폴더):", names);
		if (pick is not int idx)
		{
			return;
		}
		try
		{
			var r = await Task.Run(() => Free2DCad.Core.Tools.BlockLibrary.Import(this._doc, files[idx]));
			if (!r.AlreadyExisted)
			{
				this._cmdLine.Print($"블록 \"{r.Block.Name}\" 가져옴 (객체 {r.Imported}개{(r.Skipped > 0 ? $", 아직 옮기지 못하는 객체 {r.Skipped}개 제외(중첩 블록·치수 등)" : string.Empty)}).");
			}
			this.StartCommand(new InsertCommand { PreselectedBlock = r.Block });
		}
		catch (Exception ex)
		{
			this._cmdLine.Print($"블록을 가져올 수 없습니다: {ex.Message}");
		}
	}

	/// <summary>사용자 폴더(또는 지정 폴더)를 탐색기로 연다.</summary>
	private void OpenUserFolder(string? folder = null)
	{
		try
		{
			Free2DCad.Core.UserFolders.EnsureAll();
			string dir = folder ?? Free2DCad.Core.UserFolders.Base;
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
		}
		catch (Exception ex)
		{
			this._cmdLine.Print($"폴더를 열 수 없습니다: {ex.Message}");
		}
	}

	/// <summary>파일 메뉴 '최근 파일' (열 때마다 목록을 새로 채움).</summary>
	private MenuItem BuildRecentMenu()
	{
		var menu = new MenuItem { Header = "최근 파일(_R)", Icon = IconHelper.TryGetIcon(IconHelper.Open, 16) };
		void Fill()
		{
			menu.Items.Clear();
			var items = RecentFiles.Items;
			if (items.Count == 0)
			{
				menu.Items.Add(new MenuItem { Header = "(최근 파일 없음)", IsEnabled = false });
				return;
			}
			int n = 1;
			foreach (string path in items)
			{
				string p = path;
				var mi = new MenuItem { Header = $"_{n % 10} {Path.GetFileName(p)}   —   {Path.GetDirectoryName(p)}" };
				mi.Click += async (_, _) => await this.LoadAsync(p);
				menu.Items.Add(mi);
				n++;
			}
			menu.Items.Add(new Separator());
			var clear = new MenuItem { Header = "목록 지우기" };
			clear.Click += (_, _) => RecentFiles.Clear();
			menu.Items.Add(clear);
		}
		Fill();
		menu.SubmenuOpened += (_, _) => Fill();
		return menu;
	}

	private void NewDocument(EditorDocument? fromTemplate = null)
	{
		// 새 탭으로 (기존 CAD처럼 지금 도면은 그대로 열려 있음)
		if (this._session != null && !this.PrepareLeaveSession())
		{
			return;
		}
		this.AddSession(fromTemplate ?? EditorDocument.New());
		this._statusMsg.Text = "새 도면 생성됨";
	}

	private static MenuItem Item(string header, string? gesture, Func<Task> action) => new()
	{
		Header = header,
		InputGesture = gesture != null ? KeyGesture.Parse(gesture) : null,
		Command = new RelayCommand(action),
		Icon = MenuIcon(header),
	};

	/// <summary>
	/// 메뉴 앞 아이콘 (기존 CAD처럼). 머리글의 "(LINE)" 같은 명령 이름으로 찾고, 없으면 글자로 찾는다.
	/// </summary>
	private static Control? MenuIcon(string header)
	{
		// "(CSV) (TABLEOUT)"처럼 괄호가 여러 개일 수 있으므로 모두 확인
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(header, @"\(([A-Z]{2,})\)"))
		{
			if (CommandLineControl.CommandIcon(m.Groups[1].Value, 16) is { } byCommand)
			{
				return byCommand;
			}
		}
		string? key = header switch
		{
			_ when header.StartsWith("새로 만들기") => IconHelper.New,
			_ when header.StartsWith("열기") => IconHelper.Open,
			_ when header.StartsWith("저장") => IconHelper.Save,
			_ when header.StartsWith("다른 이름으로") => IconHelper.SaveAs,
			_ when header.StartsWith("플롯") => IconHelper.Print,
			_ when header.StartsWith("PNG") => IconHelper.Save,
			_ when header.StartsWith("명령 취소") => IconHelper.Undo,
			_ when header.StartsWith("명령 복구") => IconHelper.Redo,
			_ when header.StartsWith("잘라내기") => IconHelper.Cut,
			_ when header.StartsWith("클립보드 복사") => IconHelper.Copy,
			_ when header.StartsWith("붙여넣기") => IconHelper.Paste,
			_ when header.StartsWith("모두 선택") => IconHelper.SelectAll,
			_ when header.StartsWith("선택 해제") => IconHelper.Deselect,
			_ when header.StartsWith("지우기") => IconHelper.Erase,
			_ when header.StartsWith("전체 보기") => IconHelper.ZoomExtents,
			_ when header.StartsWith("확대") => IconHelper.ZoomIn,
			_ when header.StartsWith("축소") => IconHelper.ZoomOut,
			_ when header.StartsWith("도면층") => IconHelper.Layer,
			_ when header.StartsWith("속성 팔레트") => IconHelper.Properties,
			_ when header.StartsWith("색상") => IconHelper.ColorWheel,
			_ when header.StartsWith("끝내기") => IconHelper.Exit,
			_ when header.StartsWith("닫기") => IconHelper.CloseDoc,
			_ when header.StartsWith("명령줄") => IconHelper.CommandLine,
			_ when header.StartsWith("문자 윈도우") => IconHelper.TextWindow,
			_ when header.Contains("도움말") => IconHelper.Help,
			_ when header.StartsWith("사용자 폴더") => IconHelper.Open,
			_ => null,
		};
		return key != null ? IconHelper.TryGetIcon(key, 16) : null;
	}

	private async Task OpenAsync()
	{
		var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "도면 열기",
			AllowMultiple = false,
			FileTypeFilter = new[] { DwgFiles },
		});
		string? path = files.FirstOrDefault()?.TryGetLocalPath();
		if (path != null)
		{
			await this.LoadAsync(path);
		}
	}

	private async Task LoadAsync(string path)
	{
		this._statusMsg.Text = $"여는 중: {path}";
		try
		{
			var sw = Stopwatch.StartNew();
			// 이미 열려 있는 파일이면 그 탭으로
			var open = this._sessions.FirstOrDefault(x => !string.IsNullOrEmpty(x.Doc.FilePath) && string.Equals(Path.GetFullPath(x.Doc.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
			if (open != null)
			{
				this.SwitchSession(open);
				this._statusMsg.Text = $"이미 열려 있는 도면입니다: {Path.GetFileName(path)}";
				return;
			}
			if (this._session != null && !this.PrepareLeaveSession())
			{
				return;
			}
			var doc = await Task.Run(() => EditorDocument.Open(path));

			// 손대지 않은 빈 새 도면 탭은 이 파일로 대신한다
			var pristine = this._sessions.Count == 1 && IsPristine(this._sessions[0]) ? this._sessions[0] : null;
			this.AddSession(doc);
			if (pristine != null)
			{
				this._sessions.Remove(pristine);
				this.RebuildDocTabs();
			}
			this._startPageControl.AddRecentFile(path);
			this.SwitchToDrawingCanvas();
			this._statusMsg.Text = $"{Path.GetFileName(path)} ({doc.Document.Header.Version}) · 객체 {doc.Document.Entities.Count():N0} · {sw.ElapsedMilliseconds} ms";
				this.PrintOpenInfo(doc, path, sw.ElapsedMilliseconds);
		}
		catch (Exception ex)
		{
			this._statusMsg.Text = $"열기 실패: {ex.Message}";
		}
	}

	/// <summary>
	/// 도면을 연 뒤 명령줄에 파일 정보: 경로, 글꼴 대체("[대신] 글꼴을 [원래] 글꼴 대신 사용."), 파일 버전, 객체·도면층·블록 수.
	/// </summary>
	private void PrintOpenInfo(EditorDocument doc, string path, long elapsedMs)
	{
		var d = doc.Document;
		this._cmdLine.Print($"도면 열기: {path}");
		foreach (string line in SceneBuilder.FontSubstitutions(d.TextStyles))
		{
			this._cmdLine.Print(line);
		}
		this._cmdLine.Print($"파일 버전: {VersionName(d.Header.Version)}");
		int blocks = d.BlockRecords.Count(b => !b.Name.StartsWith('*'));
		int layouts = d.Layouts.Count(l => !string.Equals(l.Name, "Model", StringComparison.OrdinalIgnoreCase));
		this._cmdLine.Print($"객체 {d.Entities.Count():N0}개 · 도면층 {d.Layers.Count}개 · 블록 {blocks}개 · 배치 {layouts}개 · 문자 스타일 {d.TextStyles.Count}개 ({elapsedMs:N0} ms)");
	}

	private static string VersionName(ACadSharp.ACadVersion v) => v switch
	{
		ACadSharp.ACadVersion.AC1009 => "R12",
		ACadSharp.ACadVersion.AC1012 => "R13",
		ACadSharp.ACadVersion.AC1014 => "R14",
		ACadSharp.ACadVersion.AC1015 => "R15 (2000)",
		ACadSharp.ACadVersion.AC1018 => "R18 (2004)",
		ACadSharp.ACadVersion.AC1021 => "R21 (2007)",
		ACadSharp.ACadVersion.AC1024 => "R24 (2010)",
		ACadSharp.ACadVersion.AC1027 => "R27 (2013)",
		ACadSharp.ACadVersion.AC1032 => "R32 (2018)",
		_ => v.ToString(),
	};

	private async Task<bool> SaveAsync()
	{
		if (this._doc == null)
		{
			return false;
		}
		if (string.IsNullOrEmpty(this._doc.FilePath))
		{
			return await this.SaveAsAsync();
		}

		try
		{
			await Task.Run(() => this._doc.Save());
			this.UpdateTitle();
			this._statusMsg.Text = $"저장됨: {this._doc.FilePath}";
			return true;
		}
		catch (Exception ex)
		{
			this._statusMsg.Text = $"저장 실패: {ex.Message}";
			return false;
		}
	}

	private async Task<bool> SaveAsAsync()
	{
		if (this._doc == null)
		{
			return false;
		}
		// 저장 형식 (기존 CAD처럼 버전 선택). 2004가 기본 — 국내 협력사 대부분이 여는 버전
		var formats = SaveFormats;
		string baseName = Path.GetFileNameWithoutExtension(this._doc.FilePath ?? "drawing");
		var result = await this.StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions
		{
			Title = "다른 이름으로 도면 저장",
			SuggestedFileName = baseName + ".dwg",
			DefaultExtension = "dwg",
			FileTypeChoices = formats.Select(f => f.Type).ToList(),
			SuggestedFileType = formats[0].Type,
		});
		string? path = result.File?.TryGetLocalPath();
		if (path == null)
		{
			return false;
		}
		var format = formats.FirstOrDefault(f => ReferenceEquals(f.Type, result.SelectedFileType))
			?? formats.FirstOrDefault(f => string.Equals(Path.GetExtension(path), "." + f.Extension, StringComparison.OrdinalIgnoreCase))
			?? formats[0];
		if (!string.Equals(Path.GetExtension(path), "." + format.Extension, StringComparison.OrdinalIgnoreCase))
		{
			path = Path.ChangeExtension(path, format.Extension);
		}

		try
		{
			await Task.Run(() => this._doc.Save(path, format.Version));
			this.UpdateTitle();
			this._statusMsg.Text = $"저장됨: {path} ({format.Type.Name})";
			this._cmdLine.Print($"저장: {path} — {format.Type.Name}");
			return true;
		}
		catch (Exception ex)
		{
			this._statusMsg.Text = $"저장 실패: {ex.Message}";
			return false;
		}
	}

	private sealed record SaveFormat(FilePickerFileType Type, ACadSharp.ACadVersion Version, string Extension);

	private static readonly SaveFormat[] SaveFormats = CreateSaveFormats();

	private static SaveFormat[] CreateSaveFormats()
	{
		SaveFormat F(string name, ACadSharp.ACadVersion v, string ext) =>
			new(new FilePickerFileType(name) { Patterns = new[] { "*." + ext } }, v, ext);
		return new[]
		{
			F("DWG 2004 도면 (*.dwg) — 기본", ACadSharp.ACadVersion.AC1018, "dwg"),
			F("DWG 2018 도면 (*.dwg)", ACadSharp.ACadVersion.AC1032, "dwg"),
			F("DWG 2013 도면 (*.dwg)", ACadSharp.ACadVersion.AC1027, "dwg"),
			F("DWG 2010 도면 (*.dwg)", ACadSharp.ACadVersion.AC1024, "dwg"),
			F("DWG 2000 도면 (*.dwg)", ACadSharp.ACadVersion.AC1015, "dwg"),
			F("DXF 2018 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1032, "dxf"),
			F("DXF 2013 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1027, "dxf"),
			F("DXF 2010 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1024, "dxf"),
			F("DXF 2007 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1021, "dxf"),
			F("DXF 2004 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1018, "dxf"),
			F("DXF 2000 ASCII (*.dxf)", ACadSharp.ACadVersion.AC1015, "dxf"),
			F("도면 템플릿 2004 (*.dwt)", ACadSharp.ACadVersion.AC1018, "dwt"),
		};
	}

	private async Task ExportPngAsync()
	{
		if (this._doc == null || this._canvas.Bounds.Width <= 0 || this._canvas.Bounds.Height <= 0)
		{
			this._statusMsg.Text = "내보낼 도면 화면이 없습니다.";
			return;
		}

		var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "도면 화면 PNG 내보내기",
			SuggestedFileName = Path.GetFileNameWithoutExtension(this._doc.FilePath ?? "drawing") + ".png",
			DefaultExtension = "png",
			FileTypeChoices = new[] { new FilePickerFileType("PNG 이미지 (*.png)") { Patterns = new[] { "*.png" } } },
		});
		string? path = file?.TryGetLocalPath();
		if (path == null)
		{
			return;
		}

		try
		{
			int w = (int)this._canvas.Bounds.Width;
			int h = (int)this._canvas.Bounds.Height;
			using var bitmap = new SKBitmap(w, h);
			using var canvas = new SKCanvas(bitmap);
			canvas.Clear(SKColors.Black);

			if (this._canvas.View != null)
			{
				if (this._activeLayout != null && this._modelRenderer != null)
				{
					var layoutScene = LayoutScene.Build(this._doc.Document, this._activeLayout);
					using var renderer = new LayoutRenderer(layoutScene, this._modelRenderer);
					renderer.Render(canvas, this._canvas.View);
				}
				else
				{
					var scene = this._beditBlock != null ? SceneBuilder.BuildBlock(this._doc.Document, this._beditBlock) : SceneBuilder.Build(this._doc.Document);
					using var renderer = new SkiaSceneRenderer(scene) { Background = SKColors.Black };
					renderer.Render(canvas, this._canvas.View);
				}
			}

			using var img = SKImage.FromBitmap(bitmap);
			using var data = img.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = File.OpenWrite(path);
			data.SaveTo(stream);

			this._statusMsg.Text = $"PNG 내보내기 완료: {path}";
		}
		catch (Exception ex)
		{
			this._statusMsg.Text = $"내보내기 실패: {ex.Message}";
		}
	}

	private void OnDocumentChanged(object? sender, EventArgs e)
	{
		var change = (sender as EditorDocument)?.LastChange ?? DocumentChange.FullChange;
		Dispatcher.UIThread.Post(() =>
		{
			this.UpdateTitle();
			if (this._doc != null)
			{
				if (change.Full)
				{
					this._dirty.Clear();
				}
				else
				{
					// 다음에 시작할 재구성 버전 = 현재 + 1 (RefreshCurrentSpaceView가 올림)
					int next = this._sceneVersion + 1;
					foreach (var ent in change.Entities)
					{
						this._dirty[ent] = next;
					}
				}
				this.UpdateQuickOverlay();
				this.RefreshCurrentSpaceView(keepView: true);
				this._propertyPanel.UpdateSelection(this._canvas.SelectedEntities);
				this._ribbon.UpdateLayers(this._doc.Document.Layers, this._doc.CurrentLayer);
				this.UpdateTitleLayerCombo();
				this.RebuildLayoutTabBar();
			}
		});
	}

	private void OnCurrentSpaceChanged(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(() =>
		{
			if (this._doc == null) return;
			if (this._doc.CurrentSpace == this._doc.Document.ModelSpace ||
				string.Equals(this._doc.CurrentSpace.Name, BlockRecord.ModelSpaceName, StringComparison.OrdinalIgnoreCase))
			{
				if (this._activeLayout != null)
				{
					this.SwitchToModel();
				}
			}
			else
			{
				var matchingLayout = this._doc.Document.Layouts.FirstOrDefault(l => l.AssociatedBlock == this._doc.CurrentSpace ||
					string.Equals(l.AssociatedBlock?.Name, this._doc.CurrentSpace.Name, StringComparison.OrdinalIgnoreCase));
				if (matchingLayout != null && this._activeLayout != matchingLayout)
				{
					this.SwitchToLayout(matchingLayout);
				}
			}
		});
	}

	protected override async void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);
		if (this._forceClose || this._doc == null || !this._sessions.Any(x => x.Doc.IsDirty))
		{
			return;
		}

		e.Cancel = true;

		// 바뀐 도면마다 저장 확인 (취소하면 창을 닫지 않음)
		if (await this.ConfirmCloseAllAsync())
		{
			this._forceClose = true;
			this.Close();
		}
	}
}
