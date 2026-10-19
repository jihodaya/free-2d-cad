using ACadSharp;
using ACadSharp.Blocks;
using ACadSharp.Entities;
using ACadSharp.Tables;
using CSMath;
using Free2DCad.Core.Actions;
using Free2DCad.Rendering;

namespace Free2DCad.Core.Blocks;

/// <summary>
/// 외부 참조 (XREF·XATTACH): 기존 CAD "Xref 관리자"의 부착·분리·다시 로드·언로드·결합·경로 변경.
/// 도면에는 블록 레코드(XRef 플래그 + 경로)와 그 삽입만 저장하고 내용은 참조 파일에서 읽어 그린다 (기존 CAD와 같은 형식).
/// </summary>
public static class Xrefs
{
	public enum PathMode { Full, Relative, None }

	public static IReadOnlyList<BlockRecord> List(CadDocument doc) => doc.BlockRecords.Where(XrefLoader.IsXref).OrderBy(b => b.Name).ToList();

	/// <summary>참조 수 (모든 공간·블록의 삽입).</summary>
	public static int RefCount(CadDocument doc, BlockRecord br) =>
		doc.BlockRecords.SelectMany(b => b.Entities).OfType<Insert>().Count(i => ReferenceEquals(i.Block, br));

	/// <summary>저장할 경로: 전체 / 호스트 도면 기준 상대 / 이름만.</summary>
	public static string StoredPath(string file, string? hostPath, PathMode mode)
	{
		string full = Path.GetFullPath(file);
		return mode switch
		{
			PathMode.None => Path.GetFileName(full),
			PathMode.Relative when !string.IsNullOrEmpty(hostPath) => Path.GetRelativePath(Path.GetDirectoryName(Path.GetFullPath(hostPath))!, full),
			_ => full,
		};
	}

	/// <summary>부착: 같은 파일의 외부 참조가 있으면 그것을, 없으면 새로 (이름 = 파일 이름, 겹치면 번호).</summary>
	public static BlockRecord Attach(EditorDocument doc, string file, bool overlay, PathMode mode)
	{
		var cad = doc.Document;
		string full = Path.GetFullPath(file);
		var existing = List(cad).FirstOrDefault(b => XrefLoader.Resolve(cad, b) is { } p && string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
		if (existing != null) return existing;
		string baseName = Path.GetFileNameWithoutExtension(full);
		string name = baseName;
		for (int i = 2; cad.BlockRecords.Contains(name); i++) name = $"{baseName}({i})";
		var br = new BlockRecord(name, StoredPath(full, doc.FilePath, mode), overlay);
		doc.Execute(new DelegateAction("외부 참조 부착", () => cad.BlockRecords.Add(br), () => cad.BlockRecords.Remove(br.Name)));
		return br;
	}

	public static Insert MakeInsert(BlockRecord br, XYZ at, double sx = 1, double sy = 1, double rotation = 0) => new(br)
	{
		InsertPoint = at,
		XScale = sx,
		YScale = sy,
		ZScale = sx,
		Rotation = rotation,
	};

	/// <summary>분리: 삽입을 모두 지우고 정의도 없앤다 (되돌리기 가능).</summary>
	public static void Detach(EditorDocument doc, BlockRecord br)
	{
		var cad = doc.Document;
		var inserts = cad.BlockRecords.SelectMany(b => b.Entities).OfType<Insert>().Where(i => ReferenceEquals(i.Block, br)).ToList();
		doc.BeginTransaction();
		if (inserts.Count > 0) doc.Erase(inserts);
		doc.Execute(new DelegateAction("외부 참조 분리", () => cad.BlockRecords.Remove(br.Name), () => cad.BlockRecords.Add(br)));
		doc.CommitTransaction("외부 참조 분리");
	}

	public static void SetUnloaded(EditorDocument doc, BlockRecord br, bool unloaded)
	{
		bool old = br.BlockEntity.IsUnloaded;
		if (old == unloaded) return;
		doc.Execute(new DelegateAction(unloaded ? "외부 참조 언로드" : "외부 참조 로드",
			() => br.BlockEntity.IsUnloaded = unloaded, () => br.BlockEntity.IsUnloaded = old));
	}

	/// <summary>다시 로드: 파일을 다시 읽는다 (언로드 상태면 로드).</summary>
	public static void Reload(EditorDocument doc, BlockRecord br)
	{
		if (XrefLoader.Resolve(doc.Document, br) is { } p) XrefLoader.Invalidate(p);
		SetUnloaded(doc, br, false);
	}

	/// <summary>경로 바꾸기 (찾아보기).</summary>
	public static void SetPath(EditorDocument doc, BlockRecord br, string file, PathMode mode)
	{
		string old = br.BlockEntity.XRefPath;
		string nw = StoredPath(file, doc.FilePath, mode);
		doc.Execute(new DelegateAction("외부 참조 경로", () => br.BlockEntity.XRefPath = nw, () => br.BlockEntity.XRefPath = old));
	}

	/// <summary>
	/// 결합(BIND): 참조 도면의 모형 공간을 일반 블록으로 복사하고 외부 참조를 끊는다. 도면층·선종류는 이름이 같으면 호스트 것, 없으면 복사.
	/// </summary>
	public static bool Bind(EditorDocument doc, BlockRecord br)
	{
		var cad = doc.Document;
		if (XrefLoader.ContentOf(cad, br) is not { } xdoc) return false;
		var copies = new List<Entity>();
		foreach (var e in xdoc.ModelSpace.Entities)
		{
			if (e.Clone() is not Entity c) continue;
			c.Layer = HostLayer(cad, e.Layer);
			if (e.LineType != null && !e.LineType.Name.Equals(LineType.ByLayerName, StringComparison.OrdinalIgnoreCase)
				&& !e.LineType.Name.Equals(LineType.ByBlockName, StringComparison.OrdinalIgnoreCase))
			{
				c.LineType = cad.LineTypes.TryGetValue(e.LineType.Name, out var lt) ? lt : LineType.Continuous;
			}
			copies.Add(c);
		}
		var oldFlags = br.Flags;
		string oldPath = br.BlockEntity.XRefPath;
		doc.Execute(new DelegateAction("외부 참조 결합",
			() =>
			{
				br.Flags &= ~(BlockTypeFlags.XRef | BlockTypeFlags.XRefOverlay | BlockTypeFlags.XRefResolved);
				br.BlockEntity.XRefPath = string.Empty;
				foreach (var c in copies) br.Entities.Add(c);
			},
			() =>
			{
				foreach (var c in copies) br.Entities.Remove(c);
				br.Flags = oldFlags;
				br.BlockEntity.XRefPath = oldPath;
			}));
		return true;
	}

	private static Layer HostLayer(CadDocument cad, Layer? src)
	{
		if (src == null) return cad.Layers[Layer.DefaultName];
		if (cad.Layers.TryGetValue(src.Name, out var l)) return l;
		var copy = new Layer(src.Name) { Color = src.Color, LineWeight = src.LineWeight, IsOn = src.IsOn };
		cad.Layers.Add(copy);
		return copy;
	}
}
