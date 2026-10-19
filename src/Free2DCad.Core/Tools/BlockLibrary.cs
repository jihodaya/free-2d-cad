using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Tools;

/// <summary>
/// 블록 라이브러리 (Blocks 폴더의 .dwg): 도면 파일의 모형 공간 객체를 현재 도면의 블록으로 가져온다.
/// 파일 이름 = 블록 이름, 원본 (0,0) = 삽입 기준점. 이미 같은 이름의 블록이 있으면 그것을 쓴다.
/// </summary>
public static class BlockLibrary
{
	/// <summary>Blocks 폴더의 도면 파일 목록.</summary>
	public static IReadOnlyList<string> Files => UserFolders.Files(UserFolders.Blocks, "*.dwg");

	/// <summary>가져오기 결과: 블록, 건너뛴 객체 수 (중첩 블록·치수 등 아직 옮기지 못하는 종류).</summary>
	public sealed record ImportResult(BlockRecord Block, int Imported, int Skipped, bool AlreadyExisted);

	public static ImportResult Import(EditorDocument doc, string path)
	{
		CadDocument target = doc.Document;
		string name = SafeName(Path.GetFileNameWithoutExtension(path));
		if (target.BlockRecords.TryGetValue(name, out BlockRecord? existing))
		{
			return new ImportResult(existing, 0, 0, true);
		}

		CadDocument source = path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase) ? DxfReader.Read(path) : DwgReader.Read(path);
		var record = new BlockRecord(name);
		int imported = 0, skipped = 0;
		var layerCache = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
		foreach (Entity e in source.ModelSpace.Entities.ToList())
		{
			// 다른 도면의 블록·치수 스타일을 참조하는 객체는 아직 옮기지 않는다
			if (e is Insert or Dimension or Leader or MultiLeader or Viewport or TableEntity)
			{
				skipped++;
				continue;
			}
			if (e.Clone() is not Entity copy)
			{
				skipped++;
				continue;
			}
			copy.Layer = TargetLayer(target, e.Layer, layerCache);
			copy.LineType = e.LineType != null && target.LineTypes.TryGetValue(e.LineType.Name, out LineType? lt) ? lt : target.LineTypes[LineType.ByLayerName];
			switch (copy)
			{
				case TextEntity t:
					t.Style = TargetTextStyle(target, (e as TextEntity)?.Style);
					break;
				case MText mt:
					mt.Style = TargetTextStyle(target, (e as MText)?.Style);
					break;
			}
			copy.ExtendedData.Clear(); // 원본 도면의 앱 등록(XData)은 가져오지 않음
			record.Entities.Add(copy);
			imported++;
		}

		doc.Execute(new CreateBlockAction(target, record));
		return new ImportResult(record, imported, skipped, false);
	}

	private static Layer TargetLayer(CadDocument target, Layer? src, Dictionary<string, Layer> cache)
	{
		string name = src?.Name ?? Layer.DefaultName;
		if (cache.TryGetValue(name, out Layer? l))
		{
			return l;
		}
		if (!target.Layers.TryGetValue(name, out l))
		{
			// 없는 도면층은 같은 이름·색으로 만든다 (도면층은 실행 취소 대상에서 제외: 남아 있어도 해가 없음)
			l = new Layer(name) { Color = src?.Color ?? Color.ByLayer };
			target.Layers.Add(l);
		}
		cache[name] = l;
		return l;
	}

	private static TextStyle TargetTextStyle(CadDocument target, TextStyle? src)
	{
		if (src != null && target.TextStyles.TryGetValue(src.Name, out TextStyle? ts))
		{
			return ts;
		}
		return target.TextStyles[TextStyle.DefaultName];
	}

	private static string SafeName(string name)
	{
		var bad = new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', ',', '=', '`' };
		var chars = name.Select(c => bad.Contains(c) ? '_' : c).ToArray();
		string s = new string(chars).Trim();
		return s.Length == 0 ? "BLOCK" : s;
	}
}
