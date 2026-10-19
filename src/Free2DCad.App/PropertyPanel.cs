using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using Free2DCad.Rendering;
using Color = ACadSharp.Color;
using Point = Avalonia.Point;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 실측 기반 좌측 도킹 속성 팔레트.
/// 좌측 12px 세로 띠와 닫기 버튼, "속성" 볼드 헤더, 5개 도구 버튼,
/// 17px 행 높이의 2열 표, 12px 들여쓰기 회색 띠, 1px 테두리,
/// 선택 없음 시 [일반] [3D 시각화] [플롯 스타일] [View] [시각 스타일] 전체 분류 제공,
/// 객체 선택 시 [일반] [3D 시각화] [형상] [문자] [기타] 제공.
/// </summary>
public sealed partial class PropertyPanel : Border
{
	private EditorDocument? _doc;
	private Func<ViewTransform?>? _viewProvider;
	private IReadOnlyCollection<Entity> _selected = Array.Empty<Entity>();
	private bool _updatingUi;

	private readonly ComboBox _selectionTypeCombo = new();

	/// <summary>위쪽 객체 종류 목록 (첫 항목은 전체 = null). 종류를 고르면 그 종류만 속성을 보인다 (기존 CAD와 같음).</summary>
	private List<string?> _typeKeys = new();
	private string? _typeFilter;
	private readonly StackPanel _categoriesPanel = new() { Spacing = 1 };

	private TextBlock? _txtViewCenterX;
	private TextBlock? _txtViewCenterY;
	private TextBlock? _txtViewWidth;
	private TextBlock? _txtViewHeight;

	// 외부 연동 이벤트
	public event Action? CloseRequested;
	public event Action? PickRequested;
	public event Action? PickAddToggled;
	public event Action? SelectAllRequested;
	public event Action? ClearSelectionRequested;

	/// <summary>표 CSV 내보내기 요청 (저장 창은 앱이 띄운다).</summary>
	public event Action<string>? TableCsvRequested;

	private record ColorOption(string Name, Color Color, IBrush Brush);

	private static readonly ColorOption[] ColorOptions = new[]
	{
		new ColorOption("BYLAYER", Color.ByLayer, Brushes.White),
		new ColorOption("BYBLOCK", Color.ByBlock, Brushes.White),
		new ColorOption("1 빨강", new Color(1), Brushes.Red),
		new ColorOption("2 노랑", new Color(2), Brushes.Yellow),
		new ColorOption("3 초록", new Color(3), Brushes.LimeGreen),
		new ColorOption("4 하늘", new Color(4), Brushes.Cyan),
		new ColorOption("5 파랑", new Color(5), Brushes.DodgerBlue),
		new ColorOption("6 자홍", new Color(6), Brushes.Magenta),
		new ColorOption("7 흰색", new Color(7), Brushes.White),
	};

	public PropertyPanel()
	{
		this.MinWidth = 180;
		this.HorizontalAlignment = HorizontalAlignment.Stretch;
		this.Background = CadTheme.PropertyEmptyBg;
		this.BorderBrush = CadTheme.PropertyBorder;
		this.BorderThickness = new Thickness(0, 0, 1, 0);

		// 루트 레이아웃: 왼쪽 12px 세로 띠 + 오른쪽 메인 영역
		var rootGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("12,*"),
		};

		// 1. 왼쪽 12px 세로 띠 (기존 CAD 스타일 그립 띠)
		var leftStrip = new Border
		{
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD8, 0xD8, 0xD8)),
			BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
			BorderThickness = new Thickness(0, 0, 1, 0),
		};

		var stripStack = new StackPanel
		{
			Orientation = Orientation.Vertical,
			HorizontalAlignment = HorizontalAlignment.Center,
			Spacing = 2,
			Margin = new Thickness(0, 2, 0, 0),
		};

		var btnStripClose = new Button
		{
			Width = 10,
			Height = 10,
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
			Content = new TextBlock { Text = "×", FontSize = 9, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
			[ToolTip.TipProperty] = "속성 팔레트 닫기",
		};
		btnStripClose.Click += (_, _) => this.CloseRequested?.Invoke();
		stripStack.Children.Add(btnStripClose);

		for (int i = 0; i < 6; i++)
		{
			var gripDot = new Border
			{
				Width = 4,
				Height = 2,
				Background = Brushes.Gray,
				Margin = new Thickness(0, 1),
			};
			stripStack.Children.Add(gripDot);
		}

		leftStrip.Child = stripStack;
		Grid.SetColumn(leftStrip, 0);
		rootGrid.Children.Add(leftStrip);

		// 2. 메인 영역
		var mainDock = new DockPanel();

		// 1) 머리글 "속성"
		var titleBar = new Grid
		{
			Height = 24,
			Background = CadTheme.PropertyTitleBg,
			ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
			VerticalAlignment = VerticalAlignment.Center,
		};

		var titleText = new TextBlock
		{
			Text = "속성",
			FontWeight = FontWeight.Bold,
			FontSize = 14,
			Foreground = CadTheme.PropertyTitleFg,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8, 0, 0, 0),
		};
		Grid.SetColumn(titleText, 0);
		titleBar.Children.Add(titleText);

		var btnMinimize = new Button
		{
			Content = "-",
			Width = 16,
			Height = 16,
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
			FontSize = 10,
			Foreground = Brushes.Black,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(btnMinimize, 1);
		titleBar.Children.Add(btnMinimize);

		var btnClose = new Button
		{
			Content = "×",
			Width = 16,
			Height = 16,
			Padding = new Thickness(0),
			Background = Brushes.Transparent,
			FontSize = 11,
			Foreground = Brushes.Black,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnClose.Click += (_, _) => this.CloseRequested?.Invoke();
		Grid.SetColumn(btnClose, 2);
		titleBar.Children.Add(btnClose);

		DockPanel.SetDock(titleBar, Dock.Top);
		mainDock.Children.Add(titleBar);

		// 2) 선택 드롭다운 및 도구 줄
		var topBar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("110,*,Auto,Auto,Auto,Auto,Auto"),
			Height = 30,
			Background = CadTheme.PropertyToolBarBg,
			Margin = new Thickness(2, 2, 2, 2),
			VerticalAlignment = VerticalAlignment.Center,
		};

		this._selectionTypeCombo.Width = 110;
		this._selectionTypeCombo.Height = 22;
		this._selectionTypeCombo.FontSize = 12;
		this._selectionTypeCombo.Padding = new Thickness(4, 0);
		this._selectionTypeCombo.Background = CadTheme.DropdownBg;
		this._selectionTypeCombo.Foreground = CadTheme.DropdownFg;
		this._selectionTypeCombo.BorderBrush = CadTheme.DropdownBorder;
		this._selectionTypeCombo.SelectionChanged += (_, _) =>
		{
			if (this._updatingUi) return;
			int idx = this._selectionTypeCombo.SelectedIndex;
			this._typeFilter = idx > 0 && idx < this._typeKeys.Count ? this._typeKeys[idx] : null;
			this.UpdateSelection(this._selected);
		};
		Grid.SetColumn(this._selectionTypeCombo, 0);
		topBar.Children.Add(this._selectionTypeCombo);

		var btnQuickSel = this.CreateToolButton(IconHelper.QuickSelect, "신속 선택", null);
		var btnPick = this.CreateToolButton(IconHelper.PickObject, "객체 선택", () => this.PickRequested?.Invoke());
		var btnPickAdd = this.CreateToolButton(IconHelper.PickAdd, "선택 세트에 추가 (PICKADD)", () => this.PickAddToggled?.Invoke());
		var btnSelectAll = this.CreateToolButton(IconHelper.SelectAll, "모두 선택 (Ctrl+A)", () => this.SelectAllRequested?.Invoke());
		var btnDeselect = this.CreateToolButton(IconHelper.Deselect, "선택 해제 (Esc)", () => this.ClearSelectionRequested?.Invoke());

		Grid.SetColumn(btnQuickSel, 2); topBar.Children.Add(btnQuickSel);
		Grid.SetColumn(btnPick, 3); topBar.Children.Add(btnPick);
		Grid.SetColumn(btnPickAdd, 4); topBar.Children.Add(btnPickAdd);
		Grid.SetColumn(btnSelectAll, 5); topBar.Children.Add(btnSelectAll);
		Grid.SetColumn(btnDeselect, 6); topBar.Children.Add(btnDeselect);

		DockPanel.SetDock(topBar, Dock.Top);
		mainDock.Children.Add(topBar);

		// 3) 본문 스크롤 영역 (하단 빈 공간 순백색 #FFFFFF)
		var scroll = new ScrollViewer
		{
			Content = this._categoriesPanel,
			Margin = new Thickness(0),
			Background = Brushes.White,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
		};
		mainDock.Children.Add(scroll);

		Grid.SetColumn(mainDock, 1);
		rootGrid.Children.Add(mainDock);

		this.Child = rootGrid;
	}

	private Button CreateToolButton(string iconKey, string toolTip, Action? action)
	{
		var btn = new Button
		{
			Width = 18,
			Height = 18,
			Padding = new Thickness(1),
			Margin = new Thickness(1, 0),
			Background = Brushes.Transparent,
			[ToolTip.TipProperty] = toolTip,
			Content = IconHelper.GetIcon(iconKey, 13),
		};
		if (action != null)
		{
			btn.Click += (_, _) => action();
		}
		else
		{
			btn.Opacity = 0.5;
		}
		return btn;
	}

	public void AttachDocument(EditorDocument? doc, Func<ViewTransform?>? viewProvider = null)
	{
		this._doc = doc;
		this._viewProvider = viewProvider;
		this.UpdateSelection(this._selected);
	}

	public void UpdateSelection(IReadOnlyCollection<Entity> selected)
	{
		// 같은 선택이 다시 그려질 때는 고른 종류를 유지, 선택이 바뀌면 전체로
		if (selected == null || this._selected.Count != selected.Count || !selected.All(this._selected.Contains))
		{
			this._typeFilter = null;
		}
		this._selected = selected ?? Array.Empty<Entity>();
		this._updatingUi = true;
		this._categoriesPanel.Children.Clear();

		if (this._doc == null)
		{
			this._txtViewCenterX = null;
			this._txtViewCenterY = null;
			this._txtViewWidth = null;
			this._txtViewHeight = null;
			this._selectionTypeCombo.ItemsSource = new[] { "도면 없음" };
			this._selectionTypeCombo.SelectedIndex = 0;
			this._updatingUi = false;
			return;
		}

		if (selected == null || selected.Count == 0)
		{
			this._selectionTypeCombo.ItemsSource = new[] { "No selection" };
			this._selectionTypeCombo.SelectedIndex = 0;

			this.BuildNoSelectionProperties();
		}
		else
		{
			this._txtViewCenterX = null;
			this._txtViewCenterY = null;
			this._txtViewWidth = null;
			this._txtViewHeight = null;

			var grouped = selected.GroupBy(TypeName).ToList();
			IReadOnlyCollection<Entity> shown = selected;
			if (grouped.Count == 1)
			{
				string typeName = grouped[0].Key;
				this._typeKeys = new List<string?> { null };
				this._selectionTypeCombo.ItemsSource = new[] { $"{typeName} ({selected.Count})" };
				this._selectionTypeCombo.SelectedIndex = 0;
			}
			else
			{
				var items = new List<string> { $"All ({selected.Count})" };
				items.AddRange(grouped.Select(g => $"{g.Key} ({g.Count()})"));
				this._typeKeys = new List<string?> { null };
				this._typeKeys.AddRange(grouped.Select(g => (string?)g.Key));
				this._selectionTypeCombo.ItemsSource = items;
				int fi = this._typeFilter == null ? 0 : this._typeKeys.IndexOf(this._typeFilter);
				if (fi < 0) { fi = 0; this._typeFilter = null; }
				this._selectionTypeCombo.SelectedIndex = fi;
				if (this._typeFilter != null)
				{
					shown = selected.Where(e => TypeName(e) == this._typeFilter).ToList();
				}
			}

			this.BuildSelectedProperties(shown);
		}

		this._updatingUi = false;
	}

	public void RefreshViewProperties()
	{
		if (this._selected.Count == 0 && !this._updatingUi)
		{
			var view = this._viewProvider?.Invoke();
			if (this._txtViewCenterX != null && this._txtViewCenterY != null && this._txtViewWidth != null && this._txtViewHeight != null && view != null)
			{
				double vw = view.Scale > 0 ? view.Width / view.Scale : 0.0;
				double vh = view.Scale > 0 ? view.Height / view.Scale : 0.0;
				this._txtViewCenterX.Text = $"{view.CenterX:F2}";
				this._txtViewCenterY.Text = $"{view.CenterY:F2}";
				this._txtViewWidth.Text = $"{vw:F2}";
				this._txtViewHeight.Text = $"{vh:F2}";
			}
			else
			{
				this.UpdateSelection(this._selected);
			}
		}
	}

	// 1. 선택 없음일 때 속성 (기존 CAD 순서: 일반, 3D 시각화, 플롯 스타일, View, 시각 스타일)
	private void BuildNoSelectionProperties()
	{
		// 1) 일반
		var generalGrid = new StackPanel { Spacing = 0 };
		this.AddPropertyRow(generalGrid, "색상", this.CreateColorDropdown(null));
		this.AddPropertyRow(generalGrid, "도면층", this.CreateLayerDropdown(null));
		this.AddPropertyRow(generalGrid, "선 종류", this.CreateLinetypeDropdown());
		this.AddGeneralExtraRows(generalGrid, null);
		this._categoriesPanel.Children.Add(this.CreateCategorySection("일반", generalGrid));

		// 2) 3D 시각화
		var vis3DGrid = new StackPanel { Spacing = 0 };
		this.AddPropertyRow(vis3DGrid, "재질", "ByLayer");
		this.AddPropertyRow(vis3DGrid, "그림자 표시", "그림자 생성 및 받기");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("3D 시각화", vis3DGrid));

		// 3) 플롯 스타일
		var plotGrid = new StackPanel { Spacing = 0 };
		this.AddPropertyRow(plotGrid, "플롯 스타일", "ByColor");
		this.AddPropertyRow(plotGrid, "플롯 스타일 ...", "없음");
		string spaceName = this._doc?.CurrentSpace.Name ?? "Model";
		this.AddPropertyRow(plotGrid, "플롯 테이블...", spaceName == "*Model_Space" ? "Model" : spaceName);
		this.AddPropertyRow(plotGrid, "플롯 테이블 ...", "색상 의존 플롯 스타일 테이블");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("플롯 스타일", plotGrid));

		// 4) View (실시간 뷰 속성)
		var view = this._viewProvider?.Invoke();
		var viewGrid = new StackPanel { Spacing = 0 };
		double cx = view?.CenterX ?? 0.0;
		double cy = view?.CenterY ?? 0.0;
		double vw = (view != null && view.Scale > 0) ? view.Width / view.Scale : 0.0;
		double vh = (view != null && view.Scale > 0) ? view.Height / view.Scale : 0.0;

		this._txtViewCenterX = new TextBlock
		{
			Text = $"{cx:F2}",
			FontSize = 13,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4, 0),
		};
		this._txtViewCenterY = new TextBlock
		{
			Text = $"{cy:F2}",
			FontSize = 13,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4, 0),
		};
		this._txtViewWidth = new TextBlock
		{
			Text = $"{vw:F2}",
			FontSize = 13,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4, 0),
		};
		this._txtViewHeight = new TextBlock
		{
			Text = $"{vh:F2}",
			FontSize = 13,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4, 0),
		};

		this.AddPropertyRow(viewGrid, "중심 X", this._txtViewCenterX);
		this.AddPropertyRow(viewGrid, "중심 Y", this._txtViewCenterY);
		this.AddPropertyRow(viewGrid, "중심 Z", "0");
		this.AddPropertyRow(viewGrid, "너비(폭)", this._txtViewWidth);
		this.AddPropertyRow(viewGrid, "높이", this._txtViewHeight);
		this._categoriesPanel.Children.Add(this.CreateCategorySection("View", viewGrid));

		// 5) 시각 스타일 (기존 CAD와 1:1 일치)
		var visualGrid = new StackPanel { Spacing = 0 };
		this.AddPropertyRow(visualGrid, "비주얼 스타일", "2D 와이어프레임");
		this.AddPropertyRow(visualGrid, "배경", "켜짐");
		this.AddPropertyRow(visualGrid, "재질 모드", "없음");
		this.AddPropertyRow(visualGrid, "할로 갭", "0");
		this.AddPropertyRow(visualGrid, "면 투명도", "-60");
		this.AddPropertyRow(visualGrid, "면 스타일", "없음");
		this.AddPropertyRow(visualGrid, "면 하이라이트", "-30");
		this._categoriesPanel.Children.Add(this.CreateCategorySection("시각 스타일", visualGrid));
	}

	// 2. 객체 선택 시 속성 (기존 CAD 스타일: 일반 + 3D 시각화 + 형상 + 문자 + 기타)
	private void BuildSelectedProperties(IReadOnlyCollection<Entity> selected)
	{
		// 1) 일반
		var generalGrid = new StackPanel { Spacing = 0 };
		this.AddPropertyRow(generalGrid, "색상", this.CreateColorDropdown(selected));
		this.AddPropertyRow(generalGrid, "도면층", this.CreateLayerDropdown(selected));
		this.AddPropertyRow(generalGrid, "선 종류", this.CreateLinetypeDropdown(selected));
		this.AddGeneralExtraRows(generalGrid, selected);
		this._categoriesPanel.Children.Add(this.CreateCategorySection("일반", generalGrid));

		// 치수만 골랐으면 기존 CAD처럼 치수 분류 (PropertyPanel.Dimension.cs)
		if (selected.All(e => e is Dimension))
		{
			this.BuildDimensionProperties(selected.Cast<Dimension>().ToList());
			return;
		}

		// 2) 3D 시각화 (기존 CAD: 재질만. 타원·해치·구성선·광선·다중선·다중 지시선·와이프아웃은 이 분류가 없다)
		bool oneType = selected.Select(TypeName).Distinct().Count() == 1;
		var ent = selected.First();
		if (!(oneType && ent is Ellipse or Hatch or XLine or Ray or MLine or MultiLeader or Wipeout))
		{
			var vis3DGrid = new StackPanel { Spacing = 0 };
			this.AddPropertyRow(vis3DGrid, "재질", "ByLayer");
			this._categoriesPanel.Children.Add(this.CreateCategorySection("3D 시각화", vis3DGrid));
		}

		// 종류가 섞이면 기존 CAD처럼 일반·3D 시각화만 (위쪽 목록에서 종류를 고르면 그 종류의 항목)
		if (!oneType)
		{
			return;
		}
		if (selected.Count > 1)
		{
			this.BuildStdMulti(selected.ToList()); // 같은 종류 여러 개: 값이 다르면 *다양함*, 넣으면 모두에 (PropertyPanel.Geometry.cs)
			return;
		}
		if (this.BuildStdEntity(ent))
		{
			return; // PropertyPanel.Geometry.cs
		}

		// 3) 기하학 (그 밖의 객체: 구성선·광선·다중선·표·지시선 등)
		{
			var geomGrid = new StackPanel { Spacing = 0 };
			this.BuildSingleGeometryProperties(geomGrid, ent);
			this._categoriesPanel.Children.Add(this.CreateCategorySection("기하학", geomGrid));
			if (ent is TextEntity || ent is MText)
			{
				var textGrid = new StackPanel { Spacing = 0 };
				this.BuildTextProperties(textGrid, ent);
				this._categoriesPanel.Children.Add(this.CreateCategorySection("텍스트", textGrid));
			}
		}

		// 4) 기타 (핸들, 소유 공간)
		var miscGrid = new StackPanel { Spacing = 0 };
		if (selected.Count == 1)
		{
			var e = selected.First();
			this.AddPropertyRow(miscGrid, "핸들", $"0x{e.Handle:X}");
			this.AddPropertyRow(miscGrid, "소유 공간", (e.Owner is ACadSharp.Tables.BlockRecord br ? (br.Layout != null && br.Layout.IsPaperSpace ? br.Layout.Name : "모형") : "모형"));
		}
		else
		{
			this.AddPropertyRow(miscGrid, "선택 수", $"{selected.Count}개");
			this.AddPropertyRow(miscGrid, "핸들", "*다양함*");
		}
		this._categoriesPanel.Children.Add(this.CreateCategorySection("기타", miscGrid));
	}

	private void BuildSingleGeometryProperties(StackPanel panel, Entity entity)
	{
		// 호·폴리선·블록·해치·타원·스플라인·점·구성선·다중선은 편집 가능한 행으로 (PropertyPanel.General.cs)
		if (this.BuildExtraGeometry(panel, entity))
		{
			return;
		}
		switch (entity)
		{
			case Line line:
				this.AddEditableNumericRow(panel, "시작 X", line.StartPoint.X, val =>
				{
					double oldX = line.StartPoint.X;
					this._doc?.Execute(new ModifyEntityPropertyAction("시작 X 변경",
						() => line.StartPoint = new XYZ(val, line.StartPoint.Y, line.StartPoint.Z),
						() => line.StartPoint = new XYZ(oldX, line.StartPoint.Y, line.StartPoint.Z)));
				});
				this.AddEditableNumericRow(panel, "시작 Y", line.StartPoint.Y, val =>
				{
					double oldY = line.StartPoint.Y;
					this._doc?.Execute(new ModifyEntityPropertyAction("시작 Y 변경",
						() => line.StartPoint = new XYZ(line.StartPoint.X, val, line.StartPoint.Z),
						() => line.StartPoint = new XYZ(line.StartPoint.X, oldY, line.StartPoint.Z)));
				});
				this.AddPropertyRow(panel, "시작 Z", $"{line.StartPoint.Z:F4}");
				this.AddEditableNumericRow(panel, "끝 X", line.EndPoint.X, val =>
				{
					double oldX = line.EndPoint.X;
					this._doc?.Execute(new ModifyEntityPropertyAction("끝 X 변경",
						() => line.EndPoint = new XYZ(val, line.EndPoint.Y, line.EndPoint.Z),
						() => line.EndPoint = new XYZ(oldX, line.EndPoint.Y, line.EndPoint.Z)));
				});
				this.AddEditableNumericRow(panel, "끝 Y", line.EndPoint.Y, val =>
				{
					double oldY = line.EndPoint.Y;
					this._doc?.Execute(new ModifyEntityPropertyAction("끝 Y 변경",
						() => line.EndPoint = new XYZ(line.EndPoint.X, val, line.EndPoint.Z),
						() => line.EndPoint = new XYZ(line.EndPoint.X, oldY, line.EndPoint.Z)));
				});
				this.AddPropertyRow(panel, "끝 Z", $"{line.EndPoint.Z:F4}");

				double dx = line.EndPoint.X - line.StartPoint.X;
				double dy = line.EndPoint.Y - line.StartPoint.Y;
				double len = Math.Sqrt(dx * dx + dy * dy);
				double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
				if (deg < 0) deg += 360.0;

				this.AddPropertyRow(panel, "증분 X", $"{dx:F4}");
				this.AddPropertyRow(panel, "증분 Y", $"{dy:F4}");
				this.AddPropertyRow(panel, "길이", $"{len:F4}");
				this.AddPropertyRow(panel, "각도", $"{deg:F2}°");
				break;

			// 규칙 2: Arc를 Circle보다 먼저 판정
			case Arc arc:
				this.AddEditableNumericRow(panel, "중심 X", arc.Center.X, val =>
				{
					double oldX = arc.Center.X;
					this._doc?.Execute(new ModifyEntityPropertyAction("중심 X 변경",
						() => arc.Center = new XYZ(val, arc.Center.Y, arc.Center.Z),
						() => arc.Center = new XYZ(oldX, arc.Center.Y, arc.Center.Z)));
				});
				this.AddEditableNumericRow(panel, "중심 Y", arc.Center.Y, val =>
				{
					double oldY = arc.Center.Y;
					this._doc?.Execute(new ModifyEntityPropertyAction("중심 Y 변경",
						() => arc.Center = new XYZ(arc.Center.X, val, arc.Center.Z),
						() => arc.Center = new XYZ(arc.Center.X, oldY, arc.Center.Z)));
				});
				this.AddPropertyRow(panel, "중심 Z", $"{arc.Center.Z:F4}");
				this.AddEditableNumericRow(panel, "반지름", arc.Radius, val =>
				{
					if (val <= 1e-6) return;
					double oldR = arc.Radius;
					this._doc?.Execute(new ModifyEntityPropertyAction("반지름 변경",
						() => arc.Radius = val,
						() => arc.Radius = oldR));
				});
				double startDeg = arc.StartAngle * 180.0 / Math.PI;
				double endDeg = arc.EndAngle * 180.0 / Math.PI;
				double arcSpan = arc.EndAngle > arc.StartAngle ? arc.EndAngle - arc.StartAngle : (arc.EndAngle + 2 * Math.PI) - arc.StartAngle;
				double arcLen = arc.Radius * arcSpan;

				this.AddPropertyRow(panel, "시작 각도", $"{startDeg:F2}°");
				this.AddPropertyRow(panel, "끝 각도", $"{endDeg:F2}°");
				this.AddPropertyRow(panel, "길이", $"{arcLen:F4}");
				break;

			case Circle circle:
				this.AddEditableNumericRow(panel, "중심 X", circle.Center.X, val =>
				{
					double oldX = circle.Center.X;
					this._doc?.Execute(new ModifyEntityPropertyAction("중심 X 변경",
						() => circle.Center = new XYZ(val, circle.Center.Y, circle.Center.Z),
						() => circle.Center = new XYZ(oldX, circle.Center.Y, circle.Center.Z)));
				});
				this.AddEditableNumericRow(panel, "중심 Y", circle.Center.Y, val =>
				{
					double oldY = circle.Center.Y;
					this._doc?.Execute(new ModifyEntityPropertyAction("중심 Y 변경",
						() => circle.Center = new XYZ(circle.Center.X, val, circle.Center.Z),
						() => circle.Center = new XYZ(circle.Center.X, oldY, circle.Center.Z)));
				});
				this.AddPropertyRow(panel, "중심 Z", $"{circle.Center.Z:F4}");
				this.AddEditableNumericRow(panel, "반지름", circle.Radius, val =>
				{
					if (val <= 1e-6) return;
					double oldR = circle.Radius;
					this._doc?.Execute(new ModifyEntityPropertyAction("반지름 변경",
						() => circle.Radius = val,
						() => circle.Radius = oldR));
				});
				this.AddEditableNumericRow(panel, "지름", circle.Radius * 2, val =>
				{
					if (val <= 1e-6) return;
					double oldR = circle.Radius;
					this._doc?.Execute(new ModifyEntityPropertyAction("지름 변경",
						() => circle.Radius = val / 2.0,
						() => circle.Radius = oldR));
				});
				this.AddPropertyRow(panel, "둘레", $"{2.0 * Math.PI * circle.Radius:F4}");
				this.AddPropertyRow(panel, "면적", $"{Math.PI * circle.Radius * circle.Radius:F4}");
				break;

			case LwPolyline poly:
				this.AddPropertyRow(panel, "정점 수", $"{poly.Vertices.Count}");
				this.AddPropertyRow(panel, "닫힘", poly.IsClosed ? "예" : "아니오");
				double pLen = 0;
				for (int i = 0; i < poly.Vertices.Count - 1; i++)
				{
					double px = poly.Vertices[i + 1].Location.X - poly.Vertices[i].Location.X;
					double py = poly.Vertices[i + 1].Location.Y - poly.Vertices[i].Location.Y;
					pLen += Math.Sqrt(px * px + py * py);
				}
				if (poly.IsClosed && poly.Vertices.Count > 2)
				{
					double px = poly.Vertices[0].Location.X - poly.Vertices[^1].Location.X;
					double py = poly.Vertices[0].Location.Y - poly.Vertices[^1].Location.Y;
					pLen += Math.Sqrt(px * px + py * py);
				}
				this.AddPropertyRow(panel, "길이", $"{pLen:F4}");
				break;

			case TableEntity table:
				this.BuildTableProperties(panel, table);
				break;

			case Insert insert:
				this.AddPropertyRow(panel, "이름", insert.Block?.Name ?? "(없음)");
				this.AddEditableNumericRow(panel, "위치 X", insert.InsertPoint.X, val =>
				{
					double oldX = insert.InsertPoint.X;
					this._doc?.Execute(new ModifyEntityPropertyAction("위치 X 변경",
						() => insert.InsertPoint = new XYZ(val, insert.InsertPoint.Y, insert.InsertPoint.Z),
						() => insert.InsertPoint = new XYZ(oldX, insert.InsertPoint.Y, insert.InsertPoint.Z)));
				});
				this.AddEditableNumericRow(panel, "위치 Y", insert.InsertPoint.Y, val =>
				{
					double oldY = insert.InsertPoint.Y;
					this._doc?.Execute(new ModifyEntityPropertyAction("위치 Y 변경",
						() => insert.InsertPoint = new XYZ(insert.InsertPoint.X, val, insert.InsertPoint.Z),
						() => insert.InsertPoint = new XYZ(insert.InsertPoint.X, oldY, insert.InsertPoint.Z)));
				});
				this.AddPropertyRow(panel, "위치 Z", $"{insert.InsertPoint.Z:F4}");
				this.AddPropertyRow(panel, "축척 X", $"{insert.XScale:F4}");
				this.AddPropertyRow(panel, "축척 Y", $"{insert.YScale:F4}");
				this.AddPropertyRow(panel, "회전", $"{insert.Rotation * 180.0 / Math.PI:F2}°");

				if (insert.Attributes.Count > 0)
				{
					foreach (var att in insert.Attributes)
					{
						this.AddEditableTextRow(panel, att.Tag, att.Value ?? "", newVal =>
						{
							string oldVal = att.Value ?? "";
							this._doc?.Execute(new ModifyEntityPropertyAction($"속성 {att.Tag} 변경",
								() => att.Value = newVal,
								() => att.Value = oldVal));
						});
					}
				}
				break;

			case Dimension dim:
				this.AddPropertyRow(panel, "측정값", $"{dim.Measurement:F4}");
				this.AddEditableTextRow(panel, "문자 재지정", dim.Text ?? "", newVal =>
				{
					string oldT = dim.Text ?? "";
					this._doc?.Execute(new ModifyEntityPropertyAction("치수 문자 변경",
						() => dim.Text = newVal,
						() => dim.Text = oldT));
				});
				this.AddPropertyRow(panel, "치수 스타일", dim.Style?.Name ?? "Standard");
				break;

			case Hatch hatch:
				this.AddPropertyRow(panel, "패턴 이름", hatch.Pattern?.Name ?? "SOLID");
				this.AddPropertyRow(panel, "경계 루프 수", $"{hatch.Paths.Count}");
				break;
		}
	}

	/// <summary>표: 크기, 열 너비·행 높이(모두), 행·열 추가/삭제(끝에서), 위치, CSV 내보내기. 바꾸면 되돌리기로 기록.</summary>
	private void BuildTableProperties(StackPanel panel, TableEntity table)
	{
		var doc = this._doc;
		if (doc == null)
		{
			return;
		}
		double width = table.Columns.Sum(c => c.Width), height = table.Rows.Sum(r => r.Height);
		this.AddPropertyRow(panel, "테이블 스타일", table.Style?.Name ?? "Standard");
		this.AddPropertyRow(panel, "행 수", $"{table.Rows.Count}");
		this.AddPropertyRow(panel, "열 수", $"{table.Columns.Count}");
		this.AddPropertyRow(panel, "표 너비", $"{width:F4}");
		this.AddPropertyRow(panel, "표 높이", $"{height:F4}");

		// 표 구조를 바꾸는 동작: 바꾸기 전·후 상태를 저장해 두고 되돌린다
		void Change(string name, Action<CadDocument, TableEntity> change)
		{
			var before = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			change(doc.Document, table);
			var after = new Free2DCad.Core.Tables.TableEditor.Snapshot(table);
			before.Restore(table);
			doc.Execute(new EntityEditAction(name, table, () => after.Restore(table), () => before.Restore(table)));
		}

		if (table.Columns.Count > 0)
		{
			this.AddEditableNumericRow(panel, "열 너비(모두)", table.Columns[0].Width, val =>
			{
				if (val > 0) Change("열 너비 변경", (d, t) => Free2DCad.Core.Tables.TableEditor.SetUniformColumnWidth(d, t, val));
			});
		}
		var dataRow = table.Rows.Skip(table.Rows.Count > 1 ? 1 : 0).FirstOrDefault();
		if (dataRow != null)
		{
			this.AddEditableNumericRow(panel, "행 높이(모두)", dataRow.Height, val =>
			{
				if (val > 0) Change("행 높이 변경", (d, t) => Free2DCad.Core.Tables.TableEditor.SetUniformRowHeight(d, t, val));
			});
		}

		Button Btn(string text, Action onClick)
		{
			var b = new Button { Content = text, FontSize = 11, Height = 20, MinHeight = 0, Padding = new Thickness(4, 0), Margin = new Thickness(0, 0, 3, 0) };
			b.Click += (_, _) => onClick();
			return b;
		}
		var rowButtons = new StackPanel { Orientation = Orientation.Horizontal };
		rowButtons.Children.Add(Btn("추가", () => Change("행 추가", (d, t) => Free2DCad.Core.Tables.TableEditor.InsertRow(d, t, t.Rows.Count))));
		rowButtons.Children.Add(Btn("마지막 삭제", () => Change("행 삭제", (d, t) => Free2DCad.Core.Tables.TableEditor.DeleteRow(d, t, t.Rows.Count - 1))));
		this.AddPropertyRow(panel, "행", rowButtons);
		var colButtons = new StackPanel { Orientation = Orientation.Horizontal };
		colButtons.Children.Add(Btn("추가", () => Change("열 추가", (d, t) => Free2DCad.Core.Tables.TableEditor.InsertColumn(d, t, t.Columns.Count))));
		colButtons.Children.Add(Btn("마지막 삭제", () => Change("열 삭제", (d, t) => Free2DCad.Core.Tables.TableEditor.DeleteColumn(d, t, t.Columns.Count - 1))));
		this.AddPropertyRow(panel, "열", colButtons);

		this.AddEditableNumericRow(panel, "위치 X", table.InsertPoint.X, val =>
		{
			var old = table.InsertPoint;
			doc.Execute(new EntityEditAction("표 이동", table, () => table.InsertPoint = new CSMath.XYZ(val, old.Y, old.Z), () => table.InsertPoint = old));
		});
		this.AddEditableNumericRow(panel, "위치 Y", table.InsertPoint.Y, val =>
		{
			var old = table.InsertPoint;
			doc.Execute(new EntityEditAction("표 이동", table, () => table.InsertPoint = new CSMath.XYZ(old.X, val, old.Z), () => table.InsertPoint = old));
		});
		this.AddPropertyRow(panel, "내보내기", Btn("CSV로 저장...", () => this.TableCsvRequested?.Invoke(Free2DCad.Core.Tables.TableEditor.ToCsv(table))));
		this.AddPropertyRow(panel, "칸 편집", "칸을 더블클릭 (Tab = 다음 칸)");
	}

	private void BuildTextProperties(StackPanel panel, Entity entity)
	{
		if (entity is TextEntity text)
		{
			this.AddEditableTextRow(panel, "내용", text.Value ?? "", newVal =>
			{
				this._doc?.ChangeText(text, newVal);
			});
			this.AddEditableNumericRow(panel, "문자 높이", text.Height, val =>
			{
				if (val <= 1e-6) return;
				double oldH = text.Height;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 높이 변경",
					() => text.Height = val,
					() => text.Height = oldH));
			});
			this.AddRotationRow(panel, text.Rotation, rad =>
			{
				double oldR = text.Rotation;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 회전 변경", () => text.Rotation = rad, () => text.Rotation = oldR));
			});
			this.AddPropertyRow(panel, "스타일", this.CreateTextStyleDropdown(text.Style, st =>
			{
				var oldS = text.Style;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 스타일 변경", () => text.Style = st, () => text.Style = oldS));
			}));
			this.AddPropertyRow(panel, "자리맞추기", TextJustifyName(text));
		}
		else if (entity is MText mtext)
		{
			this.AddEditableTextRow(panel, "내용", mtext.Value ?? "", newVal =>
			{
				this._doc?.ChangeText(mtext, newVal);
			});
			this.AddEditableNumericRow(panel, "문자 높이", mtext.Height, val =>
			{
				if (val <= 1e-6) return;
				double oldH = mtext.Height;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 높이 변경",
					() => mtext.Height = val,
					() => mtext.Height = oldH));
			});
			this.AddRotationRow(panel, mtext.Rotation, rad =>
			{

				var oldDir = mtext.AlignmentPoint;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 회전 변경",
					() => mtext.AlignmentPoint = new CSMath.XYZ(Math.Cos(rad), Math.Sin(rad), 0),
					() => mtext.AlignmentPoint = oldDir));
			});
			this.AddPropertyRow(panel, "스타일", this.CreateTextStyleDropdown(mtext.Style, st =>
			{
				var oldS = mtext.Style;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 스타일 변경", () => mtext.Style = st, () => mtext.Style = oldS));
			}));
			int ap = Math.Clamp((int)mtext.AttachmentPoint, 1, 9);
			this.AddPropertyRow(panel, "자리맞추기", this.CreateSimpleDropdown(AttachNames, AttachNames[ap - 1], name =>
			{
				int idx = Array.IndexOf(AttachNames, name);
				if (idx < 0) return;
				var oldA = mtext.AttachmentPoint;
				var newA = (AttachmentPointType)(idx + 1);
				this._doc?.Execute(new ModifyEntityPropertyAction("자리맞추기 변경", () => mtext.AttachmentPoint = newA, () => mtext.AttachmentPoint = oldA));
			}));
			this.AddEditableNumericRow(panel, "정의 폭", mtext.RectangleWidth, val =>
			{
				if (val < 0) return;
				double oldW = mtext.RectangleWidth;
				this._doc?.Execute(new ModifyEntityPropertyAction("문자 폭 변경", () => mtext.RectangleWidth = val, () => mtext.RectangleWidth = oldW));
			});
		}
	}

	private static readonly string[] AttachNames =
	{
		"상단 왼쪽", "상단 중앙", "상단 오른쪽",
		"중간 왼쪽", "중간 중앙", "중간 오른쪽",
		"하단 왼쪽", "하단 중앙", "하단 오른쪽",
	};

	/// <summary>단일 행 문자의 실제 자리맞추기 이름 (기존 CAD 표기).</summary>
	private static string TextJustifyName(TextEntity t)
	{
		switch (t.HorizontalAlignment)
		{
			case TextHorizontalAlignment.Aligned: return "정렬";
			case TextHorizontalAlignment.Fit: return "맞춤";
			case TextHorizontalAlignment.Middle: return "중간";
		}
		string h = t.HorizontalAlignment switch { TextHorizontalAlignment.Center => "중심", TextHorizontalAlignment.Right => "오른쪽", _ => "왼쪽" };
		return t.VerticalAlignment switch
		{
			TextVerticalAlignmentType.Top => "맨 위 " + h,
			TextVerticalAlignmentType.Middle => "중간 " + h,
			TextVerticalAlignmentType.Bottom => "맨 아래 " + h,
			_ => h == "중심" ? "중심" : h,
		};
	}

	/// <summary>문자 스타일 선택 (도면의 문자 스타일 목록, 모양 파일 스타일 제외).</summary>
	private Control CreateTextStyleDropdown(TextStyle? current, Action<TextStyle> onChange)
	{
		if (this._doc == null)
		{
			return new TextBlock { Text = current?.Name ?? "Standard" };
		}
		var styles = this._doc.Document.TextStyles.Where(st => !st.IsShapeFile).ToList();
		var names = styles.Select(st => st.Name).ToList();
		return this.CreateSimpleDropdown(names, current?.Name ?? "Standard", name =>
		{
			var st = styles.FirstOrDefault(x => x.Name == name);
			if (st != null && !ReferenceEquals(st, current))
			{
				onChange(st);
			}
		});
	}

	/// <summary>속성 칸용 콤보 (고르면 onChange).</summary>
	private Control CreateSimpleDropdown(IReadOnlyList<string> items, string selected, Action<string> onChange)
	{
		if (this._recording != null)
		{
			return new RecordedValue(new RecordedRow { Kind = 2, Text = selected, Items = items, Apply = onChange });
		}
		var combo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
			MaxDropDownHeight = 320,
			ItemsSource = items,
			SelectedItem = items.FirstOrDefault(i => i == selected),
		};
		combo.SelectionChanged += (_, _) =>
		{
			if (this._updatingUi) return;
			if (combo.SelectedItem is string name && name != selected)
			{
				onChange(name);
			}
		};
		return combo;
	}

	/// <summary>회전 각도(도) 편집 칸 → 라디안으로 넘긴다.</summary>
	private void AddRotationRow(StackPanel panel, double radians, Action<double> onApply)
	{
		this.AddEditableNumericRow(panel, "회전", Math.Round(radians * 180.0 / Math.PI, 4), deg => onApply(deg * Math.PI / 180.0));
	}

	private Control CreateColorDropdown(IReadOnlyCollection<Entity>? selected)
	{
		var combo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
		};

		combo.ItemTemplate = new FuncDataTemplate<ColorOption>((item, _) =>
		{
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
			sp.Children.Add(new Border
			{
				Width = 10,
				Height = 10,
				Background = item.Brush,
				BorderBrush = Brushes.Gray,
				BorderThickness = new Thickness(1),
				VerticalAlignment = VerticalAlignment.Center,
			});
			sp.Children.Add(new TextBlock { Text = item.Name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});

		var items = ColorOptions.ToList();

		if (selected == null || selected.Count == 0)
		{
			combo.ItemsSource = items;
			combo.SelectedItem = items[0];
		}
		else
		{
			var distinctColors = selected.Select(e => e.Color).Distinct().ToList();
			if (distinctColors.Count > 1)
			{
				items.Insert(0, new ColorOption("*다양함*", Color.ByLayer, Brushes.Transparent));
				combo.ItemsSource = items;
				combo.SelectedIndex = 0;
			}
			else
			{
				combo.ItemsSource = items;
				var matched = ColorOptions.FirstOrDefault(opt => opt.Color.Index == distinctColors[0].Index);
				combo.SelectedItem = matched ?? items[0];
			}

			combo.SelectionChanged += (_, _) =>
			{
				if (this._updatingUi || this._doc == null) return;
				if (combo.SelectedItem is ColorOption opt && opt.Name != "*다양함*")
				{
					this._doc.SetColor(selected.ToList(), opt.Color);
				}
			};
		}

		return combo;
	}

	private Control CreateLayerDropdown(IReadOnlyCollection<Entity>? selected)
	{
		var combo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
		};

		if (this._doc == null) return combo;

		var layerNames = this._doc.Document.Layers.Select(l => l.Name).OrderBy(n => n).ToList();

		if (selected == null || selected.Count == 0)
		{
			combo.ItemsSource = layerNames;
			combo.SelectedItem = this._doc.CurrentLayer.Name;
			combo.SelectionChanged += (_, _) =>
			{
				if (this._updatingUi || this._doc == null) return;
				if (combo.SelectedItem is string name && this._doc.Document.Layers.TryGetValue(name, out var lay))
				{
					this._doc.CurrentLayer = lay;
				}
			};
		}
		else
		{
			var distinctLayers = selected.Select(e => e.Layer?.Name).Distinct().ToList();
			if (distinctLayers.Count > 1)
			{
				layerNames.Insert(0, "*다양함*");
				combo.ItemsSource = layerNames;
				combo.SelectedIndex = 0;
			}
			else
			{
				combo.ItemsSource = layerNames;
				combo.SelectedItem = distinctLayers.FirstOrDefault() ?? "0";
			}

			combo.SelectionChanged += (_, _) =>
			{
				if (this._updatingUi || this._doc == null) return;
				if (combo.SelectedItem is string name && name != "*다양함*" && this._doc.Document.Layers.TryGetValue(name, out var lay))
				{
					this._doc.SetLayer(selected.ToList(), lay);
				}
			};
		}

		return combo;
	}

	private Control CreateLinetypeDropdown(IReadOnlyCollection<Entity>? selected = null)
	{
		var combo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(2, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
		};

		// 기존 CAD처럼 이름 앞에 선 모양 견본
		combo.ItemTemplate = new FuncDataTemplate<string>((name, _) =>
		{
			var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
			if (name == null)
			{
				return sp;
			}
			if (name != "*다양함*")
			{
				var lt = this._doc?.Document.LineTypes.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
				sp.Children.Add(new LinetypeSwatch(lt, CadTheme.PropertyValueFg, 26, 12));
			}
			sp.Children.Add(new TextBlock { Text = name, FontSize = 13, Foreground = CadTheme.PropertyValueFg, VerticalAlignment = VerticalAlignment.Center });
			return sp;
		});

		var ltNames = new List<string> { "BYLAYER", "ByBlock" };
		if (this._doc != null)
		{
			// 기존 CAD처럼 ByLayer·ByBlock 다음은 이름순
			foreach (var lt in this._doc.Document.LineTypes.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
			{
				if (!ltNames.Contains(lt.Name, StringComparer.OrdinalIgnoreCase))
				{
					ltNames.Add(lt.Name);
				}
			}
		}
		else
		{
			ltNames.AddRange(new[] { "Continuous", "Dashed", "Center" });
		}

		if (selected != null && selected.Count > 0)
		{
			var distinctLts = selected.Select(e => e.LineType?.Name ?? "BYLAYER").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (distinctLts.Count > 1)
			{
				ltNames.Insert(0, "*다양함*");
				combo.ItemsSource = ltNames;
				combo.SelectedIndex = 0;
			}
			else
			{
				combo.ItemsSource = ltNames;
				combo.SelectedItem = ltNames.FirstOrDefault(n => string.Equals(n, distinctLts.FirstOrDefault(), StringComparison.OrdinalIgnoreCase)) ?? "BYLAYER";
			}

			combo.SelectionChanged += (_, _) =>
			{
				if (this._updatingUi || this._doc == null) return;
				if (combo.SelectedItem is string name && name != "*다양함*")
				{
					LineType? targetLt = null;
					if (string.Equals(name, "BYLAYER", StringComparison.OrdinalIgnoreCase)) targetLt = LineType.ByLayer;
					else if (string.Equals(name, "ByBlock", StringComparison.OrdinalIgnoreCase)) targetLt = LineType.ByBlock;
					else targetLt = this._doc.Document.LineTypes.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

					if (targetLt != null)
					{
						var targets = selected.ToList();
						var oldLts = targets.Select(e => e.LineType).ToList();
						this._doc.Execute(new DelegateAction(
							"선종류 변경",
							() => { foreach (var e in targets) e.LineType = targetLt; },
							() => { for (int i = 0; i < targets.Count; i++) targets[i].LineType = oldLts[i]; }
						));
					}
				}
			};
		}
		else
		{
			combo.ItemsSource = ltNames;
			combo.SelectedIndex = 0;
		}

		return combo;
	}

	private void AddPropertyRow(StackPanel panel, string label, object valueControlOrText, string? tooltip = null)
	{
		if (this._recording != null)
		{
			this.RecordRow(label, valueControlOrText);
			return;
		}
		var borderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC0, 0xC0, 0xC0));

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("12,45*,55*"),
			Height = 22,
			MinHeight = 22,
			MaxHeight = 22,
			Background = Brushes.White,
		};

		// 1) 이름 칸 왼쪽의 회색 들여쓰기 띠 (기존 CAD 스타일 12px)
		var gutter = new Border
		{
			Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0xD4, 0xD4, 0xD4)),
			BorderBrush = borderBrush,
			BorderThickness = new Thickness(0, 0, 1, 1),
		};
		Grid.SetColumn(gutter, 0);
		grid.Children.Add(gutter);

		// 2) 이름 칸 (폭 45%, #F0F0F0)
		var lbl = new TextBlock
		{
			Text = label,
			FontSize = 13,
			Foreground = CadTheme.PropertyNameFg,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(4, 0),
			TextTrimming = TextTrimming.CharacterEllipsis,
		};
		if (!string.IsNullOrEmpty(tooltip))
		{
			ToolTip.SetTip(lbl, tooltip);
		}
		var lblBorder = new Border
		{
			Background = CadTheme.PropertyNameBg,
			BorderBrush = borderBrush,
			BorderThickness = new Thickness(0, 0, 1, 1),
			Child = lbl,
		};
		Grid.SetColumn(lblBorder, 1);
		grid.Children.Add(lblBorder);

		// 3) 값 칸 (폭 55%, 순백색 #FFFFFF)
		Control valCtrl;
		if (valueControlOrText is Control c)
		{
			valCtrl = c;
		}
		else
		{
			string textVal = valueControlOrText?.ToString() ?? "";
			valCtrl = new TextBlock
			{
				Text = textVal,
				FontSize = 13,
				Foreground = CadTheme.PropertyValueFg,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0),
				TextTrimming = TextTrimming.CharacterEllipsis,
			};
			ToolTip.SetTip(valCtrl, textVal);
		}

		var valBorder = new Border
		{
			Background = Brushes.White,
			BorderBrush = borderBrush,
			BorderThickness = new Thickness(0, 0, 0, 1),
			Child = valCtrl,
		};
		Grid.SetColumn(valBorder, 2);
		grid.Children.Add(valBorder);

		panel.Children.Add(grid);
	}

	private void AddEditableNumericRow(StackPanel panel, string label, double initialValue, Action<double> onApply)
	{
		if (this._recording != null)
		{
			this.AddPropertyRow(panel, label, this.EditBox(initialValue.ToString("F4"), t => { if (double.TryParse(t, out double v)) onApply(v); }));
			return;
		}
		var box = new TextBox
		{
			Text = $"{initialValue:F4}",
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(4, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};

		box.KeyDown += (_, ke) =>
		{
			if (ke.Key == Key.Enter)
			{
				if (double.TryParse(box.Text, out double val))
				{
					onApply(val);
				}
				ke.Handled = true;
			}
		};

		this.AddPropertyRow(panel, label, box);
	}

	private void AddEditableTextRow(StackPanel panel, string label, string initialValue, Action<string> onApply)
	{
		if (this._recording != null)
		{
			this.AddPropertyRow(panel, label, this.EditBox(initialValue, onApply));
			return;
		}
		var box = new TextBox
		{
			Text = initialValue,
			FontSize = 13,
			Height = 20,
			MinHeight = 20,
			Padding = new Thickness(4, 0),
			Background = Brushes.Transparent,
			Foreground = CadTheme.PropertyValueFg,
			BorderThickness = new Thickness(0),
			VerticalAlignment = VerticalAlignment.Center,
		};

		box.KeyDown += (_, ke) =>
		{
			if (ke.Key == Key.Enter)
			{
				onApply(box.Text ?? "");
				ke.Handled = true;
			}
		};

		this.AddPropertyRow(panel, label, box);
	}

	private Control CreateCategorySection(string title, Control content)
	{
		var section = new StackPanel { Margin = new Thickness(0, 0, 0, 1) };

		var toggleArrow = new TextBlock
		{
			Text = "▲",
			FontSize = 10,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 6, 0),
		};

		var headerGrid = new Grid
		{
			Height = 22,
			Background = new LinearGradientBrush
			{
				StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
				EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
				GradientStops =
				{
					new GradientStop(Avalonia.Media.Color.FromRgb(0xB4, 0xB4, 0xB4), 0.0),
					new GradientStop(Avalonia.Media.Color.FromRgb(0xA0, 0xA0, 0xA0), 1.0),
				},
			},
			ColumnDefinitions = new ColumnDefinitions("*,Auto"),
			Cursor = new Cursor(StandardCursorType.Hand),
		};

		var titleText = new TextBlock
		{
			Text = title,
			FontWeight = FontWeight.Bold,
			FontSize = 13,
			Foreground = Brushes.Black,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8, 0),
		};

		Grid.SetColumn(titleText, 0); headerGrid.Children.Add(titleText);
		Grid.SetColumn(toggleArrow, 1); headerGrid.Children.Add(toggleArrow);

		headerGrid.PointerPressed += (_, _) =>
		{
			content.IsVisible = !content.IsVisible;
			toggleArrow.Text = content.IsVisible ? "▲" : "▼";
		};

		// 분류 머리글 위아래 구분선 (Grid에는 테두리가 없으므로 Border로 감쌈)
		section.Children.Add(new Border { Child = headerGrid, BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromRgb(0x8C, 0x8C, 0x8C)), BorderThickness = new Thickness(0, 1, 0, 1) });
		section.Children.Add(content);
		return section;
	}

	/// <summary>기존 CAD 속성 팔레트의 객체 종류 이름 (치수는 종류별).</summary>
	private static string TypeName(Entity e) => e switch
	{
		DimensionLinear => "회전 치수",
		DimensionAligned => "정렬 치수",
		DimensionRadius => "반지름 치수",
		DimensionDiameter => "지름 치수",
		DimensionAngular2Line or DimensionAngular3Pt => "각도 치수",
		DimensionOrdinate => "세로좌표 치수",
		Dimension => "치수",
		_ => GetKoreanEntityName(e.ObjectName),
	};

	private static string GetKoreanEntityName(string objectName)
	{
		return objectName.ToUpperInvariant() switch
		{
			"LINE" => "선",
			"LWPOLYLINE" or "POLYLINE" => "폴리라인",
			"CIRCLE" => "원",
			"ARC" => "호",
			"TEXT" => "텍스트",
			"MTEXT" => "MText",
			"INSERT" => "블록 참조",
			"DIMENSION" or "DIMENSIONLINEAR" or "DIMENSIONALIGNED" => "치수",
			"HATCH" => "해치",
			"ACAD_TABLE" => "테이블",
			"SPLINE" => "스플라인",
			"XLINE" => "XLine",
			"RAY" => "레이",
			"MLINE" => "MLine",
			"LEADER" => "지시선",
			"MULTILEADER" => "다중지시선",
			"SOLID" => "솔리드",
			"ATTDEF" => "속성 정의",
			"WIPEOUT" => "지우기",
			"ELLIPSE" => "타원",
			"POINT" => "점",
			_ => objectName
		};
	}
}
