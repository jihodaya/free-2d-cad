using System.Runtime.CompilerServices;
using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.IO;
using ACadSharp.Tables;

namespace Free2DCad.Rendering;

/// <summary>
/// 외부 참조(XREF): 참조 파일 찾기·읽기·캐시. 도면 파일에는 경로만 저장되고(기존 CAD와 같음) 내용은 열 때 읽어 그린다.
/// 경로 찾는 순서: 저장된 경로(절대) → 호스트 도면 폴더 기준 상대 경로 → 호스트 도면 폴더의 같은 파일 이름
/// → 외부참조 검색 추가 경로(기존 CAD Xref 관리자, 폴더마다 상대 경로·같은 파일 이름).
/// </summary>
public static class XrefLoader
{
	private static readonly ConditionalWeakTable<CadDocument, string> HostPaths = new();
	private static readonly Dictionary<string, (DateTime Stamp, CadDocument? Doc, string? Error)> Cache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object Gate = new();

	/// <summary>호스트 도면의 파일 경로를 알려 둔다 (상대 경로 찾기용).</summary>
	public static void SetHostPath(CadDocument doc, string? path)
	{
		HostPaths.Remove(doc);
		if (!string.IsNullOrEmpty(path)) HostPaths.Add(doc, path);
	}

	public static string? HostPathOf(CadDocument doc) => HostPaths.TryGetValue(doc, out var p) ? p : null;

	/// <summary>외부참조 검색 추가 경로 (";"로 여러 폴더). 기존 CAD 기본 = 프로그램 폴더.</summary>
	public static string SearchPaths { get; set; } = AppContext.BaseDirectory;

	private static IEnumerable<string> SearchDirs() =>
		(SearchPaths ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Directory.Exists);

	public static bool IsXref(BlockRecord br) => br.Flags.HasFlag(BlockTypeFlags.XRef) || br.Flags.HasFlag(BlockTypeFlags.XRefOverlay);

	public static bool IsUnloaded(BlockRecord br) => br.BlockEntity?.IsUnloaded == true;

	/// <summary>참조 파일의 실제 위치 (없으면 null).</summary>
	public static string? Resolve(CadDocument host, BlockRecord br)
	{
		string? stored = br.BlockEntity?.XRefPath;
		if (string.IsNullOrWhiteSpace(stored)) return null;
		string? hostDir = HostPathOf(host) is { } hp ? Path.GetDirectoryName(hp) : null;
		try
		{
			if (Path.IsPathRooted(stored) && File.Exists(stored)) return Path.GetFullPath(stored);
			if (hostDir != null)
			{
				string rel = Path.GetFullPath(Path.Combine(hostDir, stored));
				if (File.Exists(rel)) return rel;
				string byName = Path.Combine(hostDir, Path.GetFileName(stored));
				if (File.Exists(byName)) return byName;
			}
			foreach (string dir in SearchDirs())
			{
				if (!Path.IsPathRooted(stored))
				{
					string rel = Path.GetFullPath(Path.Combine(dir, stored));
					if (File.Exists(rel)) return rel;
				}
				string byName = Path.Combine(dir, Path.GetFileName(stored));
				if (File.Exists(byName)) return byName;
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	/// <summary>참조 도면 (파일이 바뀌었으면 다시 읽음). 못 읽으면 null.</summary>
	public static CadDocument? Load(string fullPath)
	{
		lock (Gate)
		{
			DateTime stamp;
			try
			{
				stamp = File.GetLastWriteTimeUtc(fullPath);
			}
			catch (Exception)
			{
				return null;
			}
			if (Cache.TryGetValue(fullPath, out var c) && c.Stamp == stamp) return c.Doc;
			CadDocument? doc = null;
			string? error = null;
			try
			{
				doc = fullPath.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? DxfReader.Read(fullPath) : DwgReader.Read(fullPath);
			}
			catch (Exception ex)
			{
				error = ex.Message;
			}
			Cache[fullPath] = (stamp, doc, error);
			return doc;
		}
	}

	/// <summary>다시 로드 (캐시 버림).</summary>
	public static void Invalidate(string? fullPath = null)
	{
		lock (Gate)
		{
			if (fullPath == null) Cache.Clear();
			else Cache.Remove(fullPath);
		}
	}

	/// <summary>그릴 내용: 언로드됐거나 못 찾으면 null.</summary>
	public static CadDocument? ContentOf(CadDocument host, BlockRecord br)
	{
		if (!IsXref(br) || IsUnloaded(br)) return null;
		return Resolve(host, br) is { } p ? Load(p) : null;
	}

	/// <summary>상태 글 (기존 CAD Xref 관리자와 같은 말): 로드됨 / 언로드됨 / 찾을 수 없음 / 읽을 수 없음.</summary>
	public static string StatusOf(CadDocument host, BlockRecord br)
	{
		if (IsUnloaded(br)) return "언로드됨";
		if (Resolve(host, br) is not { } p) return "찾을 수 없음";
		return Load(p) != null ? "로드됨" : "읽을 수 없음";
	}
}
