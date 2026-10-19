using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using CSMath;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core.Blocks;

/// <summary>블록 정의 대화상자에서 고른 원본 객체 처리 (기존 CAD "객체 유지 / 블록으로 변환 / 객체 삭제").</summary>
public enum BlockSourceMode { Retain, Convert, Delete }

/// <summary>BLOCK(블록 정의) 공통 처리: 고른 객체로 블록 레코드를 만들고 원본을 유지·변환·삭제한다 (한 번에 되돌리기).</summary>
public static class BlockDefinition
{
	/// <summary>
	/// 블록을 정의한다. 같은 이름이 있으면 그 정의의 내용을 바꾼다(기존 CAD처럼 다시 정의). 만든(또는 바꾼) 블록을 돌려준다.
	/// </summary>
	public static BlockRecord Define(EditorDocument doc, string name, string description, XYZ basePoint, IReadOnlyList<Entity> targets,
		BlockSourceMode mode, bool explodable = true, bool uniformScale = false, UnitsType units = UnitsType.Millimeters)
	{
		var cad = doc.Document;
		var local = Transform.CreateTranslation(new XYZ(-basePoint.X, -basePoint.Y, -basePoint.Z));
		var clones = targets.Select(e =>
		{
			var c = (Entity)e.Clone();
			c.ApplyTransform(local);
			return c;
		}).ToList();

		doc.BeginTransaction();
		BlockRecord br;
		if (cad.BlockRecords.TryGetValue(name, out var existing))
		{
			// 다시 정의: 내용만 바꾸고, 삽입들은 새 내용으로 그려진다
			br = existing;
			var oldEntities = br.Entities.ToList();
			var oldDesc = br.BlockEntity.Comments;
			doc.Execute(new DelegateAction("블록 다시 정의",
				() =>
				{
					foreach (var e in br.Entities.ToList()) br.Entities.Remove(e);
					foreach (var c in clones) br.Entities.Add(c);
					br.BlockEntity.Comments = description;
				},
				() =>
				{
					foreach (var e in br.Entities.ToList()) br.Entities.Remove(e);
					foreach (var e in oldEntities) br.Entities.Add(e);
					br.BlockEntity.Comments = oldDesc;
				}));
		}
		else
		{
			br = new BlockRecord(name);
			foreach (var c in clones) br.Entities.Add(c);
			br.BlockEntity.Comments = description;
			br.IsExplodable = explodable;
			br.CanScale = uniformScale; // DXF 281: 1 = 균일한 축척만
			br.Units = units;
			doc.Execute(new CreateBlockAction(cad, br));
		}
		br.BlockEntity.BasePoint = XYZ.Zero;

		switch (mode)
		{
			case BlockSourceMode.Delete:
				doc.Erase(targets);
				break;
			case BlockSourceMode.Convert:
				doc.Erase(targets);
				doc.AddEntities(MakeInsert(br, basePoint));
				break;
		}
		doc.CommitTransaction("BLOCK");
		return br;
	}

	/// <summary>블록 삽입 객체 (속성 정의가 있으면 기본값으로 속성을 붙인다).</summary>
	public static Insert MakeInsert(BlockRecord br, XYZ at, double sx = 1, double sy = 1, double sz = 1, double rotation = 0)
	{
		var insert = new Insert(br) { InsertPoint = at, XScale = sx, YScale = sy, ZScale = sz, Rotation = rotation };
		insert.Attributes.Clear();
		var tr = insert.GetTransform();
		foreach (var def in br.AttributeDefinitions)
		{
			var att = new AttributeEntity(def);
			att.ApplyTransform(tr);
			insert.Attributes.Add(att);
		}
		return insert;
	}
}
