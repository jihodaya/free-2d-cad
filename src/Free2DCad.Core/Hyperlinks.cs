using ACadSharp.Entities;
using ACadSharp.XData;
using Free2DCad.Core.Actions;

namespace Free2DCad.Core;

/// <summary>
/// 객체 하이퍼링크 (기존 CAD XData "PE_URL"). 기존 CAD는 주소 문자열(1000) 하나만 저장한다 — 같은 형식으로 읽고 쓴다.
/// </summary>
public static class Hyperlinks
{
	public const string AppName = "PE_URL";

	/// <summary>객체의 하이퍼링크 주소 (없으면 빈 문자열).</summary>
	public static string Get(Entity e)
	{
		if (e.ExtendedData.TryGet(AppName, out ExtendedData data))
		{
			return data.Records.OfType<ExtendedDataString>().FirstOrDefault()?.Value ?? "";
		}
		return "";
	}

	/// <summary>하이퍼링크 주소 바꾸기 (빈 문자열 = 지우기).</summary>
	public static void Set(Entity e, string url)
	{
		url = url?.Trim() ?? "";
		if (url.Length == 0)
		{
			e.ExtendedData.Remove(AppName);
			return;
		}
		var data = e.ExtendedData.TryAdd(AppName, new ExtendedData());
		data.Records.Clear();
		data.Records.Add(new ExtendedDataString(url));
	}

	/// <summary>여러 객체의 하이퍼링크를 한 번에 (되돌리기 가능).</summary>
	public static IUndoableAction SetAction(IReadOnlyCollection<Entity> entities, string url)
	{
		var olds = entities.Select(e => (e, Get(e))).ToList();
		return new DelegateAction("하이퍼링크 변경",
			() => { foreach (var (e, _) in olds) Set(e, url); },
			() => { foreach (var (e, old) in olds) Set(e, old); });
	}
}
