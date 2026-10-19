using Avalonia;
using Avalonia.Media;
using SkiaSharp;
using Color = Avalonia.Media.Color;

namespace Free2DCad.App;

/// <summary>
/// 기존 CAD 실측 기반 통합 UI 색상 및 브러시 정의.
/// </summary>
public static class CadTheme
{
	// 1. 제목 줄 (빠른 실행 도구 + 제목)
	public static readonly Color TitleBarBgColor = Color.FromRgb(0xFC, 0xFC, 0xFC);
	public static readonly IBrush TitleBarBg = new SolidColorBrush(TitleBarBgColor);
	public static readonly Color TitleBarFgColor = Color.FromRgb(0x1E, 0x1E, 0x1E);
	public static readonly IBrush TitleBarFg = new SolidColorBrush(TitleBarFgColor);
	public static readonly IBrush TitleBarSeparator = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

	// 2. 메뉴 막대
	public static readonly Color MenuBarBgColor = Color.FromRgb(0x24, 0x28, 0x2C);
	public static readonly IBrush MenuBarBg = new SolidColorBrush(MenuBarBgColor);
	public static readonly IBrush MenuBarFg = Brushes.White;

	// 3. 리본 탭 줄
	public static readonly Color RibbonTabStripBgColor = Color.FromRgb(0x2C, 0x2C, 0x2C);
	public static readonly IBrush RibbonTabStripBg = new SolidColorBrush(RibbonTabStripBgColor);
	public static readonly IBrush RibbonTabInactiveFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
	public static readonly Color RibbonTabActiveBgColor = Color.FromRgb(0xB9, 0xBD, 0xBE);
	public static readonly IBrush RibbonTabActiveBg = new SolidColorBrush(RibbonTabActiveBgColor);
	public static readonly IBrush RibbonTabActiveFg = Brushes.Black;

	// 4. 리본 본문
	public static readonly LinearGradientBrush RibbonBodyBg = new()
	{
		StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
		EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
		GradientStops =
		{
			new GradientStop(Color.FromRgb(0x30, 0x30, 0x30), 0.0),
			new GradientStop(Color.FromRgb(0x48, 0x48, 0x48), 1.0),
		}
	};
	public static readonly IBrush RibbonBodyFg = Brushes.White;
	public static readonly IBrush RibbonPanelTitleBg = new SolidColorBrush(Color.FromRgb(0x34, 0x34, 0x34));
	public static readonly IBrush RibbonPanelTitleFg = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
	public static readonly IBrush RibbonPanelBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

	// 5. 문서 탭 줄
	public static readonly Color DocTabStripBgColor = Color.FromRgb(0x34, 0x34, 0x34);
	public static readonly IBrush DocTabStripBg = new SolidColorBrush(DocTabStripBgColor);
	public static readonly IBrush DocTabInactiveBg = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4A));
	public static readonly IBrush DocTabInactiveFg = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
	public static readonly IBrush DocTabActiveBg = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC));
	public static readonly IBrush DocTabActiveFg = Brushes.Black;

	// 6. 속성 팔레트
	public static readonly IBrush PropertyTitleBg = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
	public static readonly IBrush PropertyTitleFg = Brushes.Black;
	public static readonly IBrush PropertyToolBarBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
	public static readonly IBrush PropertyToolBarFg = Brushes.Black;
	public static readonly IBrush PropertyCategoryBg = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8));
	public static readonly IBrush PropertyCategoryFg = Brushes.Black;
	public static readonly IBrush PropertyNameBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
	public static readonly IBrush PropertyNameFg = Brushes.Black;
	public static readonly IBrush PropertyValueBg = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC));
	public static readonly IBrush PropertyValueFg = Brushes.Black;
	public static readonly IBrush PropertyBorder = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
	public static readonly IBrush PropertyEmptyBg = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC));

	// 7. 도면 영역 & 그리드
	public static readonly SKColor CanvasBgSkia = SKColors.Black;
	public static readonly SKColor GridMinorSkia = new(0x1A, 0x1A, 0x1A);
	public static readonly SKColor GridMajorSkia = new(0x2C, 0x2C, 0x2C);

	// 8. 배치 탭 줄
	public static readonly Color LayoutTabStripBgColor = Color.FromRgb(0xF0, 0xF0, 0xF0);
	public static readonly IBrush LayoutTabStripBg = new SolidColorBrush(LayoutTabStripBgColor);
	public static readonly IBrush LayoutTabBorder = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0));
	public static readonly IBrush LayoutTabInactiveBg = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));
	public static readonly IBrush LayoutTabInactiveFg = Brushes.Black;
	public static readonly IBrush LayoutTabActiveBg = Brushes.White;
	public static readonly IBrush LayoutTabActiveFg = Brushes.Black;

	// 9. 명령줄
	public static readonly IBrush CmdLineBg = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2C));
	public static readonly IBrush CmdLineBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
	public static readonly IBrush CmdLineHistoryFg = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
	public static readonly IBrush CmdLineInputFg = Brushes.White;
	public static readonly IBrush CmdLinePromptFg = Brushes.White;

	// 10. 상태 표시줄
	public static readonly Color StatusBarBgColor = Color.FromRgb(0x70, 0x70, 0x70);
	public static readonly IBrush StatusBarBg = new SolidColorBrush(StatusBarBgColor);
	public static readonly IBrush StatusBarFg = Brushes.White;
	public static readonly IBrush StatusBtnInactiveFg = Brushes.White;
	public static readonly IBrush StatusBtnActiveBg = new SolidColorBrush(Color.FromRgb(0x4A, 0x90, 0xD9));
	public static readonly IBrush StatusBtnActiveFg = Brushes.White;

	// 11. 드롭다운 및 대화상자
	public static readonly IBrush DropdownBg = new SolidColorBrush(Color.FromRgb(0xFC, 0xFC, 0xFC));
	public static readonly IBrush DropdownFg = Brushes.Black;
	public static readonly IBrush DropdownBorder = new SolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A));

	public static readonly IBrush DialogBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
	public static readonly IBrush DialogTableBg = Brushes.White;
	public static readonly IBrush DialogFg = Brushes.Black;
}
