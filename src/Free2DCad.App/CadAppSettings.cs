using System;
using System.IO;
using System.Text.Json;
using Free2DCad.Core;

namespace Free2DCad.App;

/// <summary>
/// %LOCALAPPDATA%\Free2DCad\settings.json 에 저장되는 앱 전역 설정 (스냅, 모눈, 객체 스냅 등).
/// </summary>
public sealed class CadAppSettings
{
	public bool SnapEnabled { get; set; } = false;
	public double SnapSpacingX { get; set; } = 10.0;
	public double SnapSpacingY { get; set; } = 10.0;
	public bool GridEnabled { get; set; } = true;
	public double GridSpacingX { get; set; } = 10.0;
	public double GridSpacingY { get; set; } = 10.0;
	public int GridMajorEvery { get; set; } = 5;
	public bool ObjectSnapEnabled { get; set; } = true;
	public SnapModes SnapModes { get; set; } = SnapModes.Default;
	public bool OrthoEnabled { get; set; } = false;
	public bool PolarTrackingEnabled { get; set; } = false;
	public double PolarIncrementDeg { get; set; } = 45;
	public bool ObjectSnapTrackingEnabled { get; set; } = true;

	/// <summary>명령줄 높이 (위 경계를 끌어 조절).</summary>
	public double CommandLineHeight { get; set; } = 92;

	/// <summary>도면의 SHX 글꼴이 없을 때 대신 쓸 글꼴 (.shx 또는 .ttf).</summary>
	public string AlternateFont { get; set; } = Free2DCad.Rendering.UserFonts.DefaultAlternateFont;

	/// <summary>한글 등 2바이트 글자용 대체 큰 글꼴.</summary>
	public string AlternateBigFont { get; set; } = Free2DCad.Rendering.UserFonts.DefaultAlternateBigFont;

	/// <summary>문자·치수를 만들 때 도면이 용지보다 크면 주석 축척을 제안 (다시 묻지 않음을 고르면 false).</summary>
	public bool SuggestAnnotationScale { get; set; } = true;

	/// <summary>지금 값 (저장 때 함께 기록).</summary>
	public static bool CurrentSuggestAnnotationScale { get; set; } = true;

	/// <summary>외부참조 검색 추가 경로 (";"로 여러 폴더, 비어 있으면 프로그램 폴더).</summary>
	public string XrefSearchPaths { get; set; } = string.Empty;

	/// <summary>지금 캔버스 상태를 설정으로 (저장용).</summary>
	public static CadAppSettings FromCanvas(DrawingCanvas c) => new()
	{
		SnapEnabled = c.SnapEnabled,
		SnapSpacingX = c.SnapSpacingX,
		SnapSpacingY = c.SnapSpacingY,
		GridEnabled = c.GridEnabled,
		GridSpacingX = c.GridSpacingX,
		GridSpacingY = c.GridSpacingY,
		GridMajorEvery = c.GridMajorEvery,
		ObjectSnapEnabled = c.ObjectSnapEnabled,
		SnapModes = c.SnapModes,
		OrthoEnabled = c.OrthoEnabled,
		PolarTrackingEnabled = c.PolarTrackingEnabled,
		PolarIncrementDeg = c.PolarIncrementDeg,
		ObjectSnapTrackingEnabled = c.ObjectSnapTrackingEnabled,
		CommandLineHeight = CurrentCommandLineHeight,
		AlternateFont = Free2DCad.Rendering.UserFonts.AlternateFont,
		AlternateBigFont = Free2DCad.Rendering.UserFonts.AlternateBigFont,
		XrefSearchPaths = Free2DCad.Rendering.XrefLoader.SearchPaths == AppContext.BaseDirectory ? string.Empty : Free2DCad.Rendering.XrefLoader.SearchPaths,
		SuggestAnnotationScale = CurrentSuggestAnnotationScale,
	};

	/// <summary>화면에서 쓰는 명령줄 높이 (MainWindow가 갱신, 저장 때 함께 기록).</summary>
	public static double CurrentCommandLineHeight { get; set; } = 92;

	/// <summary>설정 파일 경로 (설정 초기화에서 지움).</summary>
	public static readonly string SettingsPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Free2DCad",
		"settings.json");

	public static CadAppSettings Load()
	{
		try
		{
			if (File.Exists(SettingsPath))
			{
				string json = File.ReadAllText(SettingsPath);
				var s = JsonSerializer.Deserialize<CadAppSettings>(json);
				if (s != null) return s;
			}
		}
		catch { }
		return new CadAppSettings();
	}

	public void Save()
	{
		try
		{
			string dir = Path.GetDirectoryName(SettingsPath)!;
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
			string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(SettingsPath, json);
		}
		catch { }
	}

	/// <summary>저장된 설정을 캔버스에 적용 (앱 시작 시).</summary>
	public void ApplyToCanvas(DrawingCanvas canvas)
	{
		canvas.SnapEnabled = this.SnapEnabled;
		canvas.SnapSpacingX = this.SnapSpacingX;
		canvas.SnapSpacingY = this.SnapSpacingY;
		canvas.GridEnabled = this.GridEnabled;
		canvas.GridSpacingX = this.GridSpacingX;
		canvas.GridSpacingY = this.GridSpacingY;
		canvas.GridMajorEvery = this.GridMajorEvery;
		canvas.ObjectSnapEnabled = this.ObjectSnapEnabled;
		canvas.SnapModes = this.SnapModes;
		canvas.OrthoEnabled = this.OrthoEnabled;
		canvas.PolarTrackingEnabled = this.PolarTrackingEnabled;
		canvas.PolarIncrementDeg = this.PolarIncrementDeg > 0 ? this.PolarIncrementDeg : 45;
		canvas.ObjectSnapTrackingEnabled = this.ObjectSnapTrackingEnabled;
		Free2DCad.Rendering.UserFonts.AlternateFont = string.IsNullOrWhiteSpace(this.AlternateFont) ? Free2DCad.Rendering.UserFonts.DefaultAlternateFont : this.AlternateFont;
		Free2DCad.Rendering.UserFonts.AlternateBigFont = string.IsNullOrWhiteSpace(this.AlternateBigFont) ? Free2DCad.Rendering.UserFonts.DefaultAlternateBigFont : this.AlternateBigFont;
		Free2DCad.Rendering.XrefLoader.SearchPaths = string.IsNullOrWhiteSpace(this.XrefSearchPaths) ? AppContext.BaseDirectory : this.XrefSearchPaths;
		CurrentSuggestAnnotationScale = this.SuggestAnnotationScale;
	}
}
