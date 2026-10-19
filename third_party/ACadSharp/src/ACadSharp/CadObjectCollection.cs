using CSUtilities.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace ACadSharp;

/// <summary>
/// Collection formed by <see cref="CadObject"/> managed by an owner.
/// </summary>
/// <typeparam name="T"></typeparam>
public class CadObjectCollection<T> : IObservableCadCollection<T>
	where T : CadObject
{
	/// <inheritdoc/>
	public event EventHandler<CollectionChangedEventArgs> OnAdd;

	/// <summary>
	/// Occurs before an item is removed from the collection.
	/// </summary>
	/// <remarks>Subscribers can use this event to perform actions or validation before the removal operation
	/// completes. To cancel the removal, handle the event and set the appropriate property on the event arguments if
	/// supported.</remarks>
	public event EventHandler<CollectionChangedEventArgs> OnBeforeRemove;

	/// <inheritdoc/>
	public event EventHandler<CollectionChangedEventArgs> OnRemove;

	/// <summary>
	/// Gets the number of elements that are contained in the collection.
	/// </summary>
	public int Count { get { return this._entries.Count; } }

	/// <summary>
	/// Owner of the collection.
	/// </summary>
	public CadObject Owner { get; }

	// F2D: 넣은 순서를 지키는 집합 (HashSet은 지운 자리에 새 항목이 들어가 그리기 순서가 섞였다)
	protected readonly OrderedEntrySet<T> _entries = new OrderedEntrySet<T>();

	/// <summary>
	/// F2D: 항목 순서를 바꾼다 (그리기 순서). order에 없는 항목은 뒤에 원래 순서대로 남는다. 이벤트는 일으키지 않는다.
	/// </summary>
	public void Reorder(IEnumerable<T> order)
	{
		this._entries.Reorder(order);
	}

	/// <summary>
	/// Default constructor for a <see cref="CadObjectCollection{T}"/> with it's owner assigned.
	/// </summary>
	/// <param name="owner">Owner of the collection.</param>
	public CadObjectCollection(CadObject owner)
	{
		this.Owner = owner;
	}

	/// <summary>
	/// Add a <see cref="CadObject"/> to the collection, this method triggers <see cref="OnAdd"/>.
	/// </summary>
	/// <param name="item"></param>
	/// <exception cref="ArgumentException"></exception>
	/// <exception cref="ArgumentNullException"></exception>
	public virtual void Add(T item)
	{
		if (item is null) throw new ArgumentNullException(nameof(item));

		if (item.Owner != null)
			throw new ArgumentException($"Item {item.GetType().FullName} already has an owner", nameof(item));

		if (!this._entries.Add(item))
			throw new ArgumentException($"Item {item.GetType().FullName} is already in the collection", nameof(item));

		item.Owner = this.Owner;

		OnAdd?.Invoke(this, new CollectionChangedEventArgs(item));
	}

	/// <summary>
	/// Add multiple <see cref="CadObject"/> to the collection, this method triggers <see cref="OnAdd"/>.
	/// </summary>
	/// <param name="items"></param>
	public void AddRange(IEnumerable<T> items)
	{
		foreach (var item in items)
		{
			this.Add(item);
		}
	}

	/// <summary>
	/// Removes all elements from the Collection.
	/// </summary>
	public void Clear()
	{
		Queue<T> q = new(this._entries.ToList());
		while (q.TryDequeue(out T entry))
		{
			this.Remove(entry);
		}
	}

	/// <inheritdoc/>
	public IEnumerator<T> GetEnumerator()
	{
		return this._entries.GetEnumerator();
	}

	/// <inheritdoc/>
	IEnumerator IEnumerable.GetEnumerator()
	{
		return this._entries.GetEnumerator();
	}

	/// <summary>
	/// Removes the specified item from the collection.
	/// </summary>
	/// <remarks>If an event handler for the removal is registered and cancels the operation, the item will not be
	/// removed. After successful removal, the item's owner is set to null and a removal event is raised.</remarks>
	/// <param name="item">The item to remove from the collection.</param>
	/// <returns>true if the item was successfully removed; otherwise, false.</returns>
	public virtual bool Remove(T item)
	{
		if (this.OnBeforeRemove != null)
		{
			CollectionChangedEventArgs args = new(item);
			this.OnBeforeRemove.Invoke(this, args);
			if (args.Cancel)
			{
				return false;
			}
		}

		if (!this._entries.Remove(item))
		{
			return false;
		}

		item.Owner = null;

		OnRemove?.Invoke(this, new CollectionChangedEventArgs(item));

		return true;
	}
	
	/// <summary>
	/// Removes multiple items from the collection.
	/// </summary>
	/// <param name="items">The items to remove.</param>
	public void Remove(IEnumerable<T> items)
	{
		foreach (var item in items)
		{
			this.Remove(item);
		}
	}

	public T this[int index]
	{
		get
		{
			return this._entries.ElementAtOrDefault(index);
		}
	}
}

/// <summary>F2D: 넣은 순서를 지키는 집합 (추가·삭제 O(1)).</summary>
public sealed class OrderedEntrySet<T> : IEnumerable<T>
{
	private readonly LinkedList<T> _list = new();
	private readonly Dictionary<T, LinkedListNode<T>> _nodes = new();

	public int Count => this._nodes.Count;

	public bool Add(T item)
	{
		if (this._nodes.ContainsKey(item)) return false;
		this._nodes[item] = this._list.AddLast(item);
		return true;
	}

	public bool Remove(T item)
	{
		if (!this._nodes.Remove(item, out var node)) return false;
		this._list.Remove(node);
		return true;
	}

	public bool Contains(T item) => this._nodes.ContainsKey(item);

	public void Reorder(IEnumerable<T> order)
	{
		var seen = new HashSet<T>();
		var newList = new List<T>();
		foreach (var item in order)
		{
			if (this._nodes.ContainsKey(item) && seen.Add(item)) newList.Add(item);
		}
		foreach (var item in this._list)
		{
			if (!seen.Contains(item)) newList.Add(item);
		}
		this._list.Clear();
		this._nodes.Clear();
		foreach (var item in newList) this._nodes[item] = this._list.AddLast(item);
	}

	public LinkedList<T>.Enumerator GetEnumerator() => this._list.GetEnumerator();

	IEnumerator<T> IEnumerable<T>.GetEnumerator() => this._list.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => this._list.GetEnumerator();
}
