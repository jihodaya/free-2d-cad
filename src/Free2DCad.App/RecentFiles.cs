using System.IO;
using System.Text.Json;

namespace Free2DCad.App;

/// <summary>최근 연 도면 (최대 10개). %LOCALAPPDATA%\Free2DCad\recent.json에 저장해 다음 실행에도 유지.</summary>
internal static class RecentFiles
{
	private const int Max = 10;

	private static readonly string StorePath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Free2DCad", "recent.json");

	private static List<string>? _items;

	/// <summary>최근 파일 (최신이 앞). 없어진 파일은 뺀다.</summary>
	public static IReadOnlyList<string> Items
	{
		get
		{
			_items ??= Load();
			return _items.Where(File.Exists).ToList();
		}
	}

	public static void Add(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return;
		_items ??= Load();
		string full = Path.GetFullPath(path);
		_items.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
		_items.Insert(0, full);
		if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
		Save();
	}

	public static void Clear()
	{
		_items = new List<string>();
		Save();
	}

	private static List<string> Load()
	{
		try
		{
			if (File.Exists(StorePath))
			{
				return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? new List<string>();
			}
		}
		catch (Exception)
		{
			// 손상된 파일은 무시
		}
		return new List<string>();
	}

	private static void Save()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
			File.WriteAllText(StorePath, JsonSerializer.Serialize(_items));
		}
		catch (Exception)
		{
			// 저장 실패는 무시
		}
	}
}
