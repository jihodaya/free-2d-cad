using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Free2DCad.Core;
using Free2DCad.Core.Actions;
using AvColor = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 스타일 문자 스타일 대화상자 (STYLE).
/// </summary>
public sealed class TextStyleDialog : Window
{
	private readonly EditorDocument _doc;
	private TextStyle? _currentStyle;

	// 상단 스타일 선택
	private readonly ComboBox _cboStyles = new();

	// 좌측: 폭 비율 & 문자 생성
	private readonly TextBox _txtHeight = new();
	private readonly TextBox _txtWidth = new();
	private readonly TextBox _txtOblique = new();
	private readonly CheckBox _chkBackward = new();
	private readonly CheckBox _chkUpsideDown = new();

	// 우측: 글꼴 & 미리보기
	private readonly ComboBox _cboFontName = new();
	private readonly ComboBox _cboFontStyle = new();
	private readonly ComboBox _cboLanguage = new();
	private readonly ComboBox _cboBigFont = new();
	private readonly Border _previewBorder = new();
	private readonly TextBlock _previewText = new();
	private readonly ShxPreview _shxPreview = new();
	private const string NoBigFont = "(없음)";

	public TextStyleDialog(EditorDocument doc)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));

		this.Title = "문자 스타일";
		this.Width = 560;
		this.Height = 440;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(12) };

		// 하단 버튼 바
		var bottomBar = this.BuildBottomBar();
		DockPanel.SetDock(bottomBar, Dock.Bottom);
		root.Children.Add(bottomBar);

		var mainStack = new StackPanel { Spacing = 8 };

		// 1. 현재 스타일 이름
		var styleRow = this.BuildStyleSelectorRow();
		mainStack.Children.Add(styleRow);

		// 2. 본문 (좌측: 폭 비율/생성, 우측: 글꼴/미리보기)
		var centerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("230, 12, *"),
		};

		var leftStack = new StackPanel { Spacing = 8 };
		leftStack.Children.Add(this.BuildWidthRatioGroup());
		leftStack.Children.Add(this.BuildTextGenGroup());
		Grid.SetColumn(leftStack, 0);
		centerGrid.Children.Add(leftStack);

		var rightStack = new StackPanel { Spacing = 8 };
		rightStack.Children.Add(this.BuildFontGroup());
		rightStack.Children.Add(this.BuildPreviewGroup());
		Grid.SetColumn(rightStack, 2);
		centerGrid.Children.Add(rightStack);

		mainStack.Children.Add(centerGrid);
		root.Children.Add(mainStack);
		this.Content = root;

		this.InitFonts();
		this.PopulateStylesCombo(this._doc.Document.Header.CurrentTextStyleName);
	}

	private Control BuildStyleSelectorRow()
	{
		var group = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Background = Brushes.White,
			Padding = new Thickness(8, 6),
		};

		var dock = new DockPanel();
		var lbl = new TextBlock
		{
			Text = "현재 스타일 이름",
			FontWeight = FontWeight.SemiBold,
			FontSize = 11,
			Foreground = new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0)),
			Margin = new Thickness(0, 0, 0, 4),
		};
		DockPanel.SetDock(lbl, Dock.Top);
		dock.Children.Add(lbl);

		var row = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, 8, Auto, 4, Auto, 4, Auto"),
		};

		this.StyleCombo(this._cboStyles);
		this._cboStyles.SelectionChanged += (_, _) =>
		{
			if (this._cboStyles.SelectedItem is string name)
			{
				this.SelectStyle(name);
			}
		};
		Grid.SetColumn(this._cboStyles, 0);
		row.Children.Add(this._cboStyles);

		var btnNew = this.CreateSmallButton("새 데이터(N)...", async () => await this.OnNewStyleAsync());
		Grid.SetColumn(btnNew, 2);
		row.Children.Add(btnNew);

		var btnRename = this.CreateSmallButton("이름 바꾸기(R)...", async () => await this.OnRenameStyleAsync());
		Grid.SetColumn(btnRename, 4);
		row.Children.Add(btnRename);

		var btnDelete = this.CreateSmallButton("삭제(D)", this.OnDeleteStyle);
		Grid.SetColumn(btnDelete, 6);
		row.Children.Add(btnDelete);

		dock.Children.Add(row);
		group.Child = dock;
		return group;
	}

	private Control BuildWidthRatioGroup()
	{
		var stack = new StackPanel { Spacing = 4 };

		var chkAnnotative = new CheckBox { Content = "Annotative", FontSize = 11, IsEnabled = false };
		stack.Children.Add(chkAnnotative);

		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*, 60"),
			RowDefinitions = new RowDefinitions("26, 26, 26"),
			Margin = new Thickness(0, 2),
		};

		grid.Children.Add(new TextBlock { Text = "고정 문자높이(H):", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
		this.StyleTextBox(this._txtHeight);
		this._txtHeight.TextChanged += (_, _) => this.UpdatePreview();
		Grid.SetColumn(this._txtHeight, 1);
		grid.Children.Add(this._txtHeight);

		var lblWidth = new TextBlock { Text = "폭 비율:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblWidth, 1);
		grid.Children.Add(lblWidth);

		this.StyleTextBox(this._txtWidth);
		this._txtWidth.TextChanged += (_, _) => this.UpdatePreview();
		Grid.SetRow(this._txtWidth, 1);
		Grid.SetColumn(this._txtWidth, 1);
		grid.Children.Add(this._txtWidth);

		var lblOblique = new TextBlock { Text = "문자 측정(기울기):", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblOblique, 2);
		grid.Children.Add(lblOblique);

		this.StyleTextBox(this._txtOblique);
		this._txtOblique.TextChanged += (_, _) => this.UpdatePreview();
		Grid.SetRow(this._txtOblique, 2);
		Grid.SetColumn(this._txtOblique, 1);
		grid.Children.Add(this._txtOblique);

		stack.Children.Add(grid);
		return CreateGroupBox("폭 비율", stack);
	}

	private Control BuildTextGenGroup()
	{
		var stack = new StackPanel { Spacing = 4 };
		this._chkBackward.Content = "문자 반대로 인쇄(B)";
		this._chkBackward.FontSize = 11;
		this._chkBackward.IsCheckedChanged += (_, _) => this.UpdatePreview();
		stack.Children.Add(this._chkBackward);

		this._chkUpsideDown.Content = "문자 거꾸로 인쇄(U)";
		this._chkUpsideDown.FontSize = 11;
		this._chkUpsideDown.IsCheckedChanged += (_, _) => this.UpdatePreview();
		stack.Children.Add(this._chkUpsideDown);

		var chkVertical = new CheckBox { Content = "문자 세로 인쇄(V)", FontSize = 11, IsEnabled = false };
		stack.Children.Add(chkVertical);

		return CreateGroupBox("문자 생성", stack);
	}

	private Control BuildFontGroup()
	{
		var grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("50, *"),
			RowDefinitions = new RowDefinitions("26, 26, 26, 26"),
			Margin = new Thickness(0, 2),
		};

		grid.Children.Add(new TextBlock { Text = "이름:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
		this.StyleCombo(this._cboFontName);
		this._cboFontName.SelectionChanged += (_, _) => this.UpdatePreview();
		Grid.SetColumn(this._cboFontName, 1);
		grid.Children.Add(this._cboFontName);

		var lblStyle = new TextBlock { Text = "스타일:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblStyle, 1);
		grid.Children.Add(lblStyle);

		this.StyleCombo(this._cboFontStyle);
		this._cboFontStyle.ItemsSource = new[] { "Regular", "Bold", "Italic", "Bold Italic" };
		this._cboFontStyle.SelectedIndex = 0;
		this._cboFontStyle.SelectionChanged += (_, _) => this.UpdatePreview();
		Grid.SetRow(this._cboFontStyle, 1);
		Grid.SetColumn(this._cboFontStyle, 1);
		grid.Children.Add(this._cboFontStyle);

		var lblLang = new TextBlock { Text = "언어:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblLang, 2);
		grid.Children.Add(lblLang);

		this.StyleCombo(this._cboLanguage);
		this._cboLanguage.ItemsSource = new[] { "한글" };
		this._cboLanguage.SelectedIndex = 0;
		Grid.SetRow(this._cboLanguage, 2);
		Grid.SetColumn(this._cboLanguage, 1);
		grid.Children.Add(this._cboLanguage);

		var lblBigFont = new TextBlock { Text = "큰 글꼴:", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
		Grid.SetRow(lblBigFont, 3);
		grid.Children.Add(lblBigFont);

		// 큰 글꼴(한글 등 2바이트 글자용 SHX): SHX 글꼴을 골랐을 때만
		this.StyleCombo(this._cboBigFont);
		this._cboBigFont.ItemsSource = new[] { NoBigFont }.Concat(Free2DCad.Rendering.UserFonts.ShxList()).ToList();
		this._cboBigFont.SelectedIndex = 0;
		this._cboBigFont.MaxDropDownHeight = 320;
		this._cboBigFont.SelectionChanged += (_, _) => this.UpdatePreview();
		Grid.SetRow(this._cboBigFont, 3);
		Grid.SetColumn(this._cboBigFont, 1);
		grid.Children.Add(this._cboBigFont);

		return CreateGroupBox("문자 글꼴", grid);
	}

	private Control BuildPreviewGroup()
	{
		this._previewBorder.Background = Brushes.White;
		this._previewBorder.BorderBrush = Brushes.Gray;
		this._previewBorder.BorderThickness = new Thickness(1);
		this._previewBorder.Height = 70;
		this._previewBorder.CornerRadius = new CornerRadius(2);

		this._previewText.Text = "가나다 AaBb 123";
		this._previewText.FontSize = 18;
		this._previewText.Foreground = Brushes.Black;
		this._previewText.HorizontalAlignment = HorizontalAlignment.Center;
		this._previewText.VerticalAlignment = VerticalAlignment.Center;
		this._shxPreview.IsVisible = false;
		this._previewBorder.Child = new Panel { Children = { this._previewText, this._shxPreview } };

		return CreateGroupBox("문자 미리보기", this._previewBorder);
	}

	private Control BuildBottomBar()
	{
		var bar = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, Auto, Auto"),
			Height = 36,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 10, 0, 0),
		};

		var btnHelp = new Button
		{
			Content = "?",
			Width = 26,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			FontWeight = FontWeight.Bold,
			Padding = new Thickness(0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(btnHelp, 0);
		bar.Children.Add(btnHelp);

		var btnApply = new Button
		{
			Content = "적용(A)",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			Margin = new Thickness(0, 0, 6, 0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnApply.Click += (_, _) => this.ApplyCurrentStyle();
		Grid.SetColumn(btnApply, 2);
		bar.Children.Add(btnApply);

		var btnOk = new Button
		{
			Content = "확인",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsDefault = true,
			Margin = new Thickness(0, 0, 6, 0),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnOk.Click += (_, _) =>
		{
			this.ApplyCurrentStyle();
			this.Close();
		};
		Grid.SetColumn(btnOk, 3);
		bar.Children.Add(btnOk);

		var btnCancel = new Button
		{
			Content = "취소",
			Width = 72,
			Height = 26,
			MinHeight = 0,
			FontSize = 12,
			IsCancel = true,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnCancel.Click += (_, _) => this.Close();
		Grid.SetColumn(btnCancel, 4);
		bar.Children.Add(btnCancel);

		return bar;
	}

	// 글꼴 목록: 실제 글꼴 파일 기준 (DWG 문자 스타일에는 파일 이름을 저장해야 기존 CAD도 같은 글꼴을 찾는다)
	private readonly Dictionary<string, string> _fontDisplayToFile = new();

	private void InitFonts()
	{
		var items = new List<string>();
		var catalog = Free2DCad.Rendering.UserFonts.Catalog;
		foreach (var e in catalog.Where(c => c.IsUser))
		{
			if (this._fontDisplayToFile.TryAdd(e.Display, e.File))
			{
				items.Add(e.Display);
			}
		}
		// SHX 선 글꼴 (Fonts 폴더와 하위 폴더): 도면 기본 글꼴
		foreach (string shx in Free2DCad.Rendering.UserFonts.ShxList())
		{
			if (this._fontDisplayToFile.TryAdd(shx, shx))
			{
				items.Add(shx);
			}
		}
		foreach (var e in catalog.Where(c => !c.IsUser))
		{
			if (this._fontDisplayToFile.TryAdd(e.Display, e.File))
			{
				items.Add(e.Display);
			}
		}
		this._cboFontName.MaxDropDownHeight = 360;
		// 도면에 쓰인 SHX 글꼴도 목록에 (그리기는 대응표/기본 글꼴로 대체)
		foreach (var st in this._doc.Document.TextStyles)
		{
			if (!string.IsNullOrEmpty(st.Filename) && st.Filename.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)
				&& this._fontDisplayToFile.TryAdd(st.Filename, st.Filename))
			{
				items.Add(st.Filename);
			}
		}
		this._cboFontName.ItemsSource = items;
		this._cboFontName.SelectedItem = items.FirstOrDefault(i => i.Contains("(malgun.ttf)", StringComparison.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
	}

	/// <summary>콤보에서 고른 항목 → 글꼴 파일 이름.</summary>
	private string SelectedFontFile() =>
		this._cboFontName.SelectedItem is string d && this._fontDisplayToFile.TryGetValue(d, out var f) ? f : "malgun.ttf";

	private void PopulateStylesCombo(string? selectName)
	{
		var names = this._doc.Document.TextStyles.Select(s => s.Name).ToList();
		this._cboStyles.ItemsSource = names;
		this._cboStyles.SelectedItem = (selectName != null && names.Contains(selectName)) ? selectName : (names.FirstOrDefault() ?? "Standard");
	}

	private void SelectStyle(string name)
	{
		var s = this._doc.Document.TextStyles.FirstOrDefault(st => string.Equals(st.Name, name, StringComparison.OrdinalIgnoreCase));
		if (s == null) return;

		this._currentStyle = s;
		this._txtHeight.Text = $"{s.Height:0.####}";
		this._txtWidth.Text = $"{s.Width:0.####}";
		double deg = s.ObliqueAngle * 180.0 / Math.PI;
		this._txtOblique.Text = $"{deg:0.##}";

		this._chkBackward.IsChecked = s.MirrorFlag.HasFlag(TextMirrorFlag.Backward);
		this._chkUpsideDown.IsChecked = s.MirrorFlag.HasFlag(TextMirrorFlag.UpsideDown);

		if (!string.IsNullOrEmpty(s.Filename))
		{
			string file = System.IO.Path.GetFileName(s.Filename);
			var match = this._fontDisplayToFile.FirstOrDefault(kv => string.Equals(kv.Value, file, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(System.IO.Path.GetFileNameWithoutExtension(kv.Value), file, StringComparison.OrdinalIgnoreCase)).Key;
			if (match != null)
			{
				this._cboFontName.SelectedItem = match;
			}
		}

		string big = string.IsNullOrWhiteSpace(s.BigFontFilename) ? string.Empty : System.IO.Path.GetFileName(s.BigFontFilename);
		if (big.Length > 0 && !System.IO.Path.HasExtension(big))
		{
			big += ".shx";
		}
		var bigItems = (IList<string>)this._cboBigFont.ItemsSource!;
		string? bigMatch = bigItems.FirstOrDefault(i => string.Equals(i, big, StringComparison.OrdinalIgnoreCase));
		if (bigMatch == null && big.Length > 0)
		{
			// Fonts 폴더에 없는 큰 글꼴도 그대로 보존되도록 목록에 넣는다 (그리기는 대체 큰 글꼴)
			bigItems.Add(big);
			bigMatch = big;
		}
		this._cboBigFont.SelectedItem = bigMatch ?? NoBigFont;

		if (s.TrueType.HasFlag(FontFlags.Bold) && s.TrueType.HasFlag(FontFlags.Italic))
		{
			this._cboFontStyle.SelectedIndex = 3;
		}
		else if (s.TrueType.HasFlag(FontFlags.Bold))
		{
			this._cboFontStyle.SelectedIndex = 1;
		}
		else if (s.TrueType.HasFlag(FontFlags.Italic))
		{
			this._cboFontStyle.SelectedIndex = 2;
		}
		else
		{
			this._cboFontStyle.SelectedIndex = 0;
		}

		this.UpdatePreview();
	}

	private void UpdatePreview()
	{
		// 목록 항목("패밀리 (파일)")에서 패밀리 이름으로 미리보기
		string file = this.SelectedFontFile();
		bool isShx = file.EndsWith(".shx", StringComparison.OrdinalIgnoreCase);
		this._cboBigFont.IsEnabled = isShx;
		this._cboFontStyle.IsEnabled = !isShx;
		this._shxPreview.IsVisible = isShx;
		this._previewText.IsVisible = !isShx;
		if (isShx)
		{
			string? bigName = this.SelectedBigFont();
			this._shxPreview.Main = Free2DCad.Rendering.UserFonts.LoadShx(file);
			this._shxPreview.Big = Free2DCad.Rendering.UserFonts.LoadShx(bigName ?? Free2DCad.Rendering.UserFonts.AlternateBigFont);
			this._shxPreview.WidthFactor = double.TryParse(this._txtWidth.Text, out var wf) && wf > 0 ? wf : 1;
			this._shxPreview.InvalidateVisual();
		}
		var entry = Free2DCad.Rendering.UserFonts.Catalog.FirstOrDefault(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));
		if (entry != null)
		{
			this._previewText.FontFamily = new FontFamily(entry.Family);
		}

		int styleIdx = this._cboFontStyle.SelectedIndex;
		bool isBold = styleIdx == 1 || styleIdx == 3;
		bool isItalic = styleIdx == 2 || styleIdx == 3;
		this._previewText.FontWeight = isBold ? FontWeight.Bold : FontWeight.Normal;
		this._previewText.FontStyle = isItalic ? FontStyle.Italic : FontStyle.Normal;

		// 텍스트 변환 (미러링, 기울기)
		double oblique = double.TryParse(this._txtOblique.Text, out var ob) ? ob : 0;
		bool backward = this._chkBackward.IsChecked == true;
		bool upsideDown = this._chkUpsideDown.IsChecked == true;

		var group = new TransformGroup();
		if (backward || upsideDown)
		{
			group.Children.Add(new ScaleTransform(backward ? -1 : 1, upsideDown ? -1 : 1));
		}
		if (Math.Abs(oblique) > 1e-4)
		{
			group.Children.Add(new SkewTransform(-oblique, 0));
		}
		this._previewText.RenderTransform = group;
		this._shxPreview.RenderTransform = group;
	}

	/// <summary>큰 글꼴 콤보 → 파일 이름 (SHX 글꼴이 아니거나 (없음)이면 null).</summary>
	private string? SelectedBigFont() =>
		this._cboBigFont.IsEnabled && this._cboBigFont.SelectedItem is string b && b != NoBigFont ? b : null;

	private void ApplyCurrentStyle()
	{
		if (this._currentStyle == null) return;

		var s = this._currentStyle;
		string styleName = s.Name;

		// 이전 값 복사
		double oldH = s.Height;
		double oldW = s.Width;
		double oldObl = s.ObliqueAngle;
		var oldMirror = s.MirrorFlag;
		string oldFile = s.Filename;
		string oldBig = s.BigFontFilename;
		var oldTt = s.TrueType;
		string oldCurrStyle = this._doc.Document.Header.CurrentTextStyleName;

		// 새 값
		double newH = double.TryParse(this._txtHeight.Text, out var h) && h >= 0 ? h : 0;
		double newW = double.TryParse(this._txtWidth.Text, out var w) && w > 0 ? w : 1.0;
		double newOblDeg = double.TryParse(this._txtOblique.Text, out var obl) ? obl : 0;
		double newOblRad = newOblDeg * Math.PI / 180.0;

		TextMirrorFlag newMirror = TextMirrorFlag.None;
		if (this._chkBackward.IsChecked == true) newMirror |= TextMirrorFlag.Backward;
		if (this._chkUpsideDown.IsChecked == true) newMirror |= TextMirrorFlag.UpsideDown;

		string newFont = this.SelectedFontFile();
		string newBig = newFont.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? this.SelectedBigFont() ?? string.Empty : string.Empty;
		FontFlags newTt = FontFlags.Regular;
		int stIdx = this._cboFontStyle.SelectedIndex;
		if (stIdx == 1) newTt = FontFlags.Bold;
		else if (stIdx == 2) newTt = FontFlags.Italic;
		else if (stIdx == 3) newTt = FontFlags.Bold | FontFlags.Italic;

		this._doc.Execute(new DelegateAction(
			$"문자 스타일 '{styleName}' 변경",
			() =>
			{
				s.Height = newH;
				s.Width = newW;
				s.ObliqueAngle = newOblRad;
				s.MirrorFlag = newMirror;
				s.Filename = newFont;
				s.BigFontFilename = newBig;
				s.TrueType = newTt;
				this._doc.Document.Header.CurrentTextStyleName = styleName;
			},
			() =>
			{
				s.Height = oldH;
				s.Width = oldW;
				s.ObliqueAngle = oldObl;
				s.MirrorFlag = oldMirror;
				s.Filename = oldFile;
				s.BigFontFilename = oldBig;
				s.TrueType = oldTt;
				this._doc.Document.Header.CurrentTextStyleName = oldCurrStyle;
			}));
	}

	private async Task OnNewStyleAsync()
	{
		string name = await PromptInputAsync("새 스타일 이름 입력:", "새 문자 스타일");
		if (string.IsNullOrWhiteSpace(name)) return;
		name = name.Trim();

		if (this._doc.Document.TextStyles.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
		{
			await ShowMessageAsync("이미 존재하는 스타일 이름입니다.");
			return;
		}

		var newStyle = new TextStyle(name)
		{
			Filename = this.SelectedFontFile(),
			BigFontFilename = this.SelectedFontFile().EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? this.SelectedBigFont() ?? string.Empty : string.Empty,
			Width = 1.0,
		};
		this._doc.Document.TextStyles.Add(newStyle);
		this.PopulateStylesCombo(name);
	}

	private async Task OnRenameStyleAsync()
	{
		if (this._currentStyle == null) return;
		if (string.Equals(this._currentStyle.Name, TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase))
		{
			await ShowMessageAsync("Standard 스타일의 이름은 바꿀 수 없습니다.");
			return;
		}

		string oldName = this._currentStyle.Name;
		string newName = await PromptInputAsync("새 이름 입력:", "스타일 이름 바꾸기", oldName);
		if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return;
		newName = newName.Trim();

		if (this._doc.Document.TextStyles.Any(s => string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)))
		{
			await ShowMessageAsync("이미 존재하는 스타일 이름입니다.");
			return;
		}

		// 이름 변경 (내부 Name 필드 또는 삭제 후 재추가)
		var s = this._currentStyle;
		this._doc.Document.TextStyles.Remove(oldName);
		var renamed = new TextStyle(newName)
		{
			Filename = s.Filename,
			BigFontFilename = s.BigFontFilename,
			Height = s.Height,
			Width = s.Width,
			ObliqueAngle = s.ObliqueAngle,
			MirrorFlag = s.MirrorFlag,
			TrueType = s.TrueType,
		};
		this._doc.Document.TextStyles.Add(renamed);
		this.PopulateStylesCombo(newName);
	}

	private async void OnDeleteStyle()
	{
		if (this._currentStyle == null) return;
		if (string.Equals(this._currentStyle.Name, TextStyle.DefaultName, StringComparison.OrdinalIgnoreCase))
		{
			await ShowMessageAsync("Standard 스타일은 삭제할 수 없습니다.");
			return;
		}

		string name = this._currentStyle.Name;
		// 객체 사용 여부 확인
		bool isUsed = this._doc.Document.Entities.OfType<TextEntity>().Any(t => t.Style?.Name == name) ||
			this._doc.Document.Entities.OfType<MText>().Any(t => t.Style?.Name == name);
		if (isUsed)
		{
			await ShowMessageAsync("도면의 객체에서 사용 중인 스타일은 삭제할 수 없습니다.");
			return;
		}

		this._doc.Document.TextStyles.Remove(name);
		this.PopulateStylesCombo("Standard");
	}

	private async Task<string> PromptInputAsync(string prompt, string title, string initialText = "")
	{
		var w = new Window
		{
			Title = title,
			Width = 320,
			Height = 140,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var stack = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
		stack.Children.Add(new TextBlock { Text = prompt, FontSize = 12 });
		var txt = new TextBox { Text = initialText, Height = 24, MinHeight = 0, FontSize = 12 };
		stack.Children.Add(txt);

		var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
		var btnOk = new Button { Content = "확인", Width = 60, Height = 24, MinHeight = 0, FontSize = 11, IsDefault = true };
		var btnCancel = new Button { Content = "취소", Width = 60, Height = 24, MinHeight = 0, FontSize = 11, IsCancel = true };
		string res = string.Empty;
		btnOk.Click += (_, _) => { res = txt.Text ?? string.Empty; w.Close(); };
		btnCancel.Click += (_, _) => w.Close();
		btnRow.Children.Add(btnOk);
		btnRow.Children.Add(btnCancel);
		stack.Children.Add(btnRow);
		w.Content = stack;
		await w.ShowDialog(this);
		return res;
	}

	private async Task ShowMessageAsync(string msg)
	{
		var w = new Window
		{
			Title = "문자 스타일",
			Width = 320,
			Height = 120,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var stack = new StackPanel { Margin = new Thickness(14), Spacing = 10 };
		stack.Children.Add(new TextBlock { Text = msg, FontSize = 12, TextWrapping = TextWrapping.Wrap });
		var btnOk = new Button { Content = "확인", Width = 60, Height = 24, MinHeight = 0, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
		btnOk.Click += (_, _) => w.Close();
		stack.Children.Add(btnOk);
		w.Content = stack;
		await w.ShowDialog(this);
	}

	private static Border CreateGroupBox(string header, Control content)
	{
		var border = new Border
		{
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(3),
			Background = Brushes.White,
			Padding = new Thickness(8, 6),
		};
		var dock = new DockPanel();
		var lbl = new TextBlock
		{
			Text = header,
			FontWeight = FontWeight.SemiBold,
			FontSize = 11,
			Foreground = new SolidColorBrush(AvColor.FromRgb(0x15, 0x65, 0xC0)),
			Margin = new Thickness(0, 0, 0, 4),
		};
		DockPanel.SetDock(lbl, Dock.Top);
		dock.Children.Add(lbl);
		dock.Children.Add(content);
		border.Child = dock;
		return border;
	}

	private Button CreateSmallButton(string text, Action onClick)
	{
		var btn = new Button
		{
			Content = text,
			Height = 24,
			MinHeight = 0,
			FontSize = 11,
			Padding = new Thickness(6, 2),
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btn.Click += (_, _) => onClick();
		return btn;
	}

	private void StyleCombo(ComboBox combo)
	{
		combo.Height = 24;
		combo.MinHeight = 0;
		combo.FontSize = 11;
		combo.Background = Brushes.White;
		combo.BorderBrush = CadTheme.DropdownBorder;
		combo.VerticalContentAlignment = VerticalAlignment.Center;
	}

	private void StyleTextBox(TextBox box)
	{
		box.Height = 24;
		box.MinHeight = 0;
		box.FontSize = 11;
		box.Padding = new Thickness(4, 0);
		box.VerticalAlignment = VerticalAlignment.Center;
		box.VerticalContentAlignment = VerticalAlignment.Center;
		box.TextAlignment = TextAlignment.Right;
	}
}

/// <summary>SHX 선 글꼴 미리보기 (문자 스타일 창): "가나다 AaBb 123"을 선으로 그린다.</summary>
internal sealed class ShxPreview : Control
{
	public Free2DCad.Rendering.ShxFont? Main { get; set; }

	public Free2DCad.Rendering.ShxFont? Big { get; set; }

	public double WidthFactor { get; set; } = 1;

	public string Text { get; set; } = "가나다 AaBb 123";

	public override void Render(DrawingContext context)
	{
		double h = Math.Min(this.Bounds.Height * 0.4, 26);
		var (strokes, width) = Free2DCad.Rendering.ShxFont.LayoutLine(this.Text, this.Main, this.Big, h, this.WidthFactor);
		if (width > this.Bounds.Width - 16 && width > 0)
		{
			double k = (this.Bounds.Width - 16) / width;
			h *= k;
			(strokes, width) = Free2DCad.Rendering.ShxFont.LayoutLine(this.Text, this.Main, this.Big, h, this.WidthFactor);
		}
		double ox = (this.Bounds.Width - width) / 2;
		double oy = (this.Bounds.Height + h) / 2;
		var pen = new Pen(Brushes.Black, 1.2);
		foreach (double[] s in strokes)
		{
			for (int i = 0; i + 3 < s.Length; i += 2)
			{
				context.DrawLine(pen, new Avalonia.Point(ox + s[i], oy - s[i + 1]), new Avalonia.Point(ox + s[i + 2], oy - s[i + 3]));
			}
		}
	}
}
