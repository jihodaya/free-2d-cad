using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// 기존 CAD 스타일 치수 STYLE 관리자 대화상자 (DIMSTYLE / D / DST / DDIM).
/// </summary>
public sealed class DimStyleManagerDialog : Window
{
	private readonly EditorDocument _doc;
	private readonly TextBlock _lblCurrentStyle = new();
	private readonly ListBox _lstStyles = new();
	private readonly ComboBox _cboFilter = new();
	private readonly TextBlock _lblPreviewTitle = new();
	private readonly DimStylePreview _preview = new();
	private readonly TextBlock _lblDesc = new();

	private DimensionStyle? _selectedStyle;

	public DimStyleManagerDialog(EditorDocument doc)
	{
		this._doc = doc ?? throw new ArgumentNullException(nameof(doc));

		this.Title = "치수 STYLE 관리자";
		this.Width = 680;
		this.Height = 480;
		this.CanResize = false;
		this.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		this.RequestedThemeVariant = ThemeVariant.Light;
		this.Background = CadTheme.DialogBg;

		var root = new DockPanel { Margin = new Thickness(12) };

		// 1. 상단 현재 스타일 표시
		var topPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			Margin = new Thickness(0, 0, 0, 8),
		};
		topPanel.Children.Add(new TextBlock
		{
			Text = "현재 치수 STYLE:",
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
		});
		this._lblCurrentStyle.FontSize = 12;
		this._lblCurrentStyle.FontWeight = FontWeight.Bold;
		this._lblCurrentStyle.VerticalAlignment = VerticalAlignment.Center;
		topPanel.Children.Add(this._lblCurrentStyle);
		DockPanel.SetDock(topPanel, Dock.Top);
		root.Children.Add(topPanel);

		// 2. 하단 닫기 및 도움말
		var bottomPanel = new Grid
		{
			Height = 32,
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
			Margin = new Thickness(0, 8, 0, 0),
		};
		var btnHelp = new Button
		{
			Content = "?",
			Width = 26,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(btnHelp, 0);
		bottomPanel.Children.Add(btnHelp);

		var btnClose = new Button
		{
			Content = "닫기",
			Width = 84,
			Height = 26,
			MinHeight = 0,
			HorizontalContentAlignment = HorizontalAlignment.Center,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		btnClose.Click += (_, _) => this.Close();
		Grid.SetColumn(btnClose, 2);
		bottomPanel.Children.Add(btnClose);

		DockPanel.SetDock(bottomPanel, Dock.Bottom);
		root.Children.Add(bottomPanel);

		// 3. 중앙 3열 구조: 좌측 스타일 목록 + 중앙 미리보기 + 우측 관리 버튼들
		var centerGrid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("180,10,*,10,100"),
		};

		// 좌측 스타일 목록
		var leftStack = new StackPanel { Spacing = 4 };
		leftStack.Children.Add(new TextBlock { Text = "Styles:", FontSize = 12 });

		this._lstStyles.Height = 240;
		this._lstStyles.Background = Brushes.White;
		this._lstStyles.BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0));
		this._lstStyles.BorderThickness = new Thickness(1);
		this._lstStyles.SelectionChanged += (_, _) => this.OnStyleSelectionChanged();
		leftStack.Children.Add(this._lstStyles);

		leftStack.Children.Add(new TextBlock { Text = "목록 필터:", FontSize = 12, Margin = new Thickness(0, 6, 0, 0) });
		this._cboFilter.ItemsSource = new[] { "모든 스타일", "사용 중인 스타일" };
		this._cboFilter.SelectedIndex = 0;
		this._cboFilter.Height = 24;
		this._cboFilter.MinHeight = 0;
		this._cboFilter.FontSize = 12;
		this._cboFilter.SelectionChanged += (_, _) => this.RefreshStylesList();
		leftStack.Children.Add(this._cboFilter);

		var chkXref = new CheckBox
		{
			Content = "X참조에서 STYLE 목록 표시 안 함",
			FontSize = 11,
			Margin = new Thickness(0, 4, 0, 0),
		};
		leftStack.Children.Add(chkXref);
		Grid.SetColumn(leftStack, 0);
		centerGrid.Children.Add(leftStack);

		// 중앙 미리보기
		var midStack = new StackPanel { Spacing = 4 };
		this._lblPreviewTitle.Text = "미리보기:";
		this._lblPreviewTitle.FontSize = 12;
		midStack.Children.Add(this._lblPreviewTitle);

		var previewBorder = new Border
		{
			Background = Brushes.Black,
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0x80, 0x80, 0x80)),
			BorderThickness = new Thickness(1),
			Height = 240,
			Child = this._preview,
		};
		midStack.Children.Add(previewBorder);

		midStack.Children.Add(new TextBlock { Text = "설명:", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });

		var descBox = new Border
		{
			Background = Brushes.White,
			BorderBrush = new SolidColorBrush(AvColor.FromRgb(0xD0, 0xD0, 0xD0)),
			BorderThickness = new Thickness(1),
			Height = 56,
			Padding = new Thickness(6),
			Child = new ScrollViewer
			{
				Content = this._lblDesc,
				VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			},
		};
		this._lblDesc.FontSize = 11;
		this._lblDesc.TextWrapping = TextWrapping.Wrap;
		midStack.Children.Add(descBox);
		Grid.SetColumn(midStack, 2);
		centerGrid.Children.Add(midStack);

		// 우측 관리 버튼들
		var btnStack = new StackPanel { Spacing = 5 };

		Button MakeBtn(string text, Action onClick, bool isEnabled = true)
		{
			var btn = new Button
			{
				Content = text,
				Width = 96,
				Height = 24,
				MinHeight = 0,
				FontSize = 12,
				IsEnabled = isEnabled,
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
			};
			btn.Click += (_, _) => onClick();
			return btn;
		}

		btnStack.Children.Add(MakeBtn("현재 설정", () => this.SetCurrentStyle()));
		btnStack.Children.Add(MakeBtn("New...", async () => await this.CreateNewStyleAsync()));
		btnStack.Children.Add(MakeBtn("Delete...", () => this.DeleteSelectedStyle()));
		btnStack.Children.Add(MakeBtn("이름 바꾸기...", async () => await this.RenameSelectedStyleAsync()));
		btnStack.Children.Add(MakeBtn("수정...", async () => await this.EditSelectedStyleAsync()));
		btnStack.Children.Add(MakeBtn("덮어쓰기...", () => { }, false));
		btnStack.Children.Add(MakeBtn("덮어쓰기 저장", () => { }, false));
		btnStack.Children.Add(MakeBtn("덮어쓰기 지우기", () => { }, false));
		btnStack.Children.Add(MakeBtn("비교...", () => { }, false));

		Grid.SetColumn(btnStack, 4);
		centerGrid.Children.Add(btnStack);

		root.Children.Add(centerGrid);
		this.Content = root;

		this.RefreshStylesList();
	}

	private void RefreshStylesList()
	{
		string curStyleName = this._doc.Document.Header.CurrentDimensionStyleName ?? "Standard";
		this._lblCurrentStyle.Text = curStyleName;

		var allStyles = this._doc.Document.DimensionStyles.ToList();
		if (this._cboFilter.SelectedIndex == 1)
		{
			// 사용 중인 스타일 필터
			var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var d in this._doc.Document.Entities.OfType<Dimension>())
			{
				if (d.Style != null) usedNames.Add(d.Style.Name);
			}
			usedNames.Add(curStyleName);
			allStyles = allStyles.Where(s => usedNames.Contains(s.Name)).ToList();
		}

		var names = allStyles.Select(s => s.Name).ToList();
		this._lstStyles.ItemsSource = names;

		if (this._selectedStyle != null && names.Contains(this._selectedStyle.Name))
		{
			this._lstStyles.SelectedItem = this._selectedStyle.Name;
		}
		else
		{
			this._lstStyles.SelectedItem = names.FirstOrDefault(n => string.Equals(n, curStyleName, StringComparison.OrdinalIgnoreCase)) ?? names.FirstOrDefault();
		}
	}

	private void OnStyleSelectionChanged()
	{
		string? selName = this._lstStyles.SelectedItem as string;
		if (string.IsNullOrEmpty(selName))
		{
			this._selectedStyle = null;
			this._preview.UpdateStyle(null);
			this._lblPreviewTitle.Text = "미리보기:";
			this._lblDesc.Text = string.Empty;
			return;
		}

		this._selectedStyle = this._doc.Document.DimensionStyles.FirstOrDefault(s => string.Equals(s.Name, selName, StringComparison.OrdinalIgnoreCase));
		if (this._selectedStyle != null)
		{
			this._lblPreviewTitle.Text = $"미리보기: {this._selectedStyle.Name}";
			this._preview.UpdateStyle(this._selectedStyle);

			string dimClr = this._selectedStyle.DimensionLineColor.IsByBlock ? "BYBLOCK" : (this._selectedStyle.DimensionLineColor.IsByLayer ? "BYLAYER" : ColorPickerDialog.GetColorDescription(this._selectedStyle.DimensionLineColor.Index));
			string extClr = this._selectedStyle.ExtensionLineColor.IsByBlock ? "BYBLOCK" : (this._selectedStyle.ExtensionLineColor.IsByLayer ? "BYLAYER" : ColorPickerDialog.GetColorDescription(this._selectedStyle.ExtensionLineColor.Index));
			string txtClr = this._selectedStyle.TextColor.IsByBlock ? "BYBLOCK" : (this._selectedStyle.TextColor.IsByLayer ? "BYLAYER" : ColorPickerDialog.GetColorDescription(this._selectedStyle.TextColor.Index));

			this._lblDesc.Text = $"{this._selectedStyle.Name} + 치수선 색상 = {dimClr}; 확장선 색상 = {extClr}; 문자 색상 = {txtClr}; 축척 = {this._selectedStyle.ScaleFactor:F2}; 화살표 크기 = {this._selectedStyle.ArrowSize:F2}; 문자 높이 = {this._selectedStyle.TextHeight:F2}";
		}
	}

	private void SetCurrentStyle()
	{
		if (this._selectedStyle == null) return;
		string prev = this._doc.Document.Header.CurrentDimensionStyleName ?? "Standard";
		string next = this._selectedStyle.Name;
		this._doc.Execute(new DelegateAction(
			$"현재 치수 스타일 설정: {next}",
			() =>
			{
				this._doc.Document.Header.CurrentDimensionStyleName = next;
				this._lblCurrentStyle.Text = next;
			},
			() =>
			{
				this._doc.Document.Header.CurrentDimensionStyleName = prev;
				this._lblCurrentStyle.Text = prev;
			}
		));
	}

	private async Task CreateNewStyleAsync()
	{
		var source = this._selectedStyle ?? this._doc.Document.DimensionStyles.FirstOrDefault() ?? DimensionStyle.Default;
		string newName = await PromptInputAsync(this, "새 치수 스타일 생성", "새 스타일 이름:", $"{source.Name}_Copy");
		if (string.IsNullOrWhiteSpace(newName)) return;

		if (this._doc.Document.DimensionStyles.Contains(newName))
		{
			await ShowMessageAsync(this, "오류", $"치수 스타일 \"{newName}\"은(는) 이미 존재합니다.");
			return;
		}

		var newStyle = (DimensionStyle)source.Clone();
		newStyle.Name = newName;

		this._doc.Execute(new DelegateAction(
			$"치수 스타일 추가: {newName}",
			() =>
			{
				this._doc.Document.DimensionStyles.Add(newStyle);
				this.RefreshStylesList();
				this._lstStyles.SelectedItem = newName;
			},
			() =>
			{
				this._doc.Document.DimensionStyles.Remove(newName);
				this.RefreshStylesList();
			}
		));

		// 바로 수정 대화상자 열기
		await this.EditSelectedStyleAsync();
	}

	private void DeleteSelectedStyle()
	{
		if (this._selectedStyle == null) return;

		string name = this._selectedStyle.Name;
		if (string.Equals(name, "Standard", StringComparison.OrdinalIgnoreCase))
		{
			_ = ShowMessageAsync(this, "삭제 불가", "Standard 스타일은 삭제할 수 없습니다.");
			return;
		}

		if (string.Equals(this._doc.Document.Header.CurrentDimensionStyleName, name, StringComparison.OrdinalIgnoreCase))
		{
			_ = ShowMessageAsync(this, "삭제 불가", "현재 사용 중인 치수 스타일은 삭제할 수 없습니다.");
			return;
		}

		bool inUse = this._doc.Document.Entities.OfType<Dimension>().Any(d => d.Style != null && string.Equals(d.Style.Name, name, StringComparison.OrdinalIgnoreCase));
		if (inUse)
		{
			_ = ShowMessageAsync(this, "삭제 불가", $"치수 스타일 \"{name}\"은(는) 도면 내 치수 객체에서 사용 중이므로 삭제할 수 없습니다.");
			return;
		}

		var target = this._selectedStyle;
		this._doc.Execute(new DelegateAction(
			$"치수 스타일 삭제: {name}",
			() =>
			{
				this._doc.Document.DimensionStyles.Remove(name);
				this.RefreshStylesList();
			},
			() =>
			{
				this._doc.Document.DimensionStyles.Add(target);
				this.RefreshStylesList();
			}
		));
	}

	private async Task RenameSelectedStyleAsync()
	{
		if (this._selectedStyle == null) return;

		string oldName = this._selectedStyle.Name;
		if (string.Equals(oldName, "Standard", StringComparison.OrdinalIgnoreCase))
		{
			await ShowMessageAsync(this, "이름 바꾸기 불가", "Standard 스타일은 이름을 바꿀 수 없습니다.");
			return;
		}

		string newName = await PromptInputAsync(this, "치수 스타일 이름 바꾸기", "새 스타일 이름:", oldName);
		if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		if (this._doc.Document.DimensionStyles.Contains(newName))
		{
			await ShowMessageAsync(this, "오류", $"치수 스타일 \"{newName}\"은(는) 이미 존재합니다.");
			return;
		}

		var target = this._selectedStyle;
		this._doc.Execute(new DelegateAction(
			$"치수 스타일 이름 변경: {oldName} -> {newName}",
			() =>
			{
				target.Name = newName;
				if (string.Equals(this._doc.Document.Header.CurrentDimensionStyleName, oldName, StringComparison.OrdinalIgnoreCase))
				{
					this._doc.Document.Header.CurrentDimensionStyleName = newName;
				}
				this.RefreshStylesList();
			},
			() =>
			{
				target.Name = oldName;
				if (string.Equals(this._doc.Document.Header.CurrentDimensionStyleName, newName, StringComparison.OrdinalIgnoreCase))
				{
					this._doc.Document.Header.CurrentDimensionStyleName = oldName;
				}
				this.RefreshStylesList();
			}
		));
	}

	private async Task EditSelectedStyleAsync()
	{
		if (this._selectedStyle == null) return;
		var dlg = new DimStyleEditDialog(this._doc, this._selectedStyle);
		await dlg.ShowDialog(this);
		this.OnStyleSelectionChanged();
	}

	private static async Task<string> PromptInputAsync(Window owner, string title, string prompt, string initial)
	{
		var win = new Window
		{
			Title = title,
			Width = 320,
			Height = 140,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var sp = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
		sp.Children.Add(new TextBlock { Text = prompt, FontSize = 12 });
		var tb = new TextBox { Text = initial, Height = 24, MinHeight = 0, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
		sp.Children.Add(tb);

		string result = string.Empty;
		var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
		var btnOk = new Button { Content = "확인", Width = 64, Height = 24, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center };
		btnOk.Click += (_, _) => { result = tb.Text?.Trim() ?? string.Empty; win.Close(); };
		var btnCancel = new Button { Content = "취소", Width = 64, Height = 24, MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center };
		btnCancel.Click += (_, _) => win.Close();

		btnRow.Children.Add(btnOk);
		btnRow.Children.Add(btnCancel);
		sp.Children.Add(btnRow);
		win.Content = sp;

		tb.Focus();
		tb.SelectAll();
		await win.ShowDialog(owner);
		return result;
	}

	private static async Task ShowMessageAsync(Window owner, string title, string message)
	{
		var win = new Window
		{
			Title = title,
			Width = 340,
			Height = 130,
			CanResize = false,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			RequestedThemeVariant = ThemeVariant.Light,
			Background = CadTheme.DialogBg,
		};
		var sp = new StackPanel { Margin = new Thickness(14), Spacing = 12 };
		sp.Children.Add(new TextBlock { Text = message, FontSize = 12, TextWrapping = TextWrapping.Wrap });
		var btnOk = new Button { Content = "확인", Width = 64, Height = 24, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
		btnOk.Click += (_, _) => win.Close();
		sp.Children.Add(btnOk);
		win.Content = sp;
		await win.ShowDialog(owner);
	}
}
