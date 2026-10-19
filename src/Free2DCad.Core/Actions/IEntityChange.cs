using ACadSharp.Entities;

namespace Free2DCad.Core.Actions;

/// <summary>
/// 특정 엔티티만 바꾸는 동작. 화면이 전체 장면을 다시 만들기 전에 바뀐 도형만 즉시 다시 그리는 데 쓴다.
/// 이 인터페이스가 없는 동작(도면층 색 변경 등)은 전체 다시 그리기가 필요하다.
/// </summary>
public interface IEntityChange
{
	IEnumerable<Entity> AffectedEntities { get; }
}
