using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CathodeLib
{
    /// <summary>
    /// Custom read-only collections that throw errors on modification attempts
    /// </summary>
    public class ReadOnlyEntityCollection<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly List<T> _items;

        public ReadOnlyEntityCollection(IEnumerable<T> items)
        {
            _items = items.ToList();
        }

        public T this[int index]
        {
            get => _items[index];
            set => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Add/Remove methods on the Composite class instead.");
        }

        public int Count => _items.Count;
        public bool IsReadOnly => true;

        public void Add(T item) => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Add methods on the Composite class instead.");
        public void Clear() => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Remove methods on the Composite class instead.");
        public void Insert(int index, T item) => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Add methods on the Composite class instead.");
        public bool Remove(T item) => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Remove methods on the Composite class instead.");
        public void RemoveAt(int index) => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Remove methods on the Composite class instead.");
        public bool RemoveAll(Predicate<T> match) => throw new NotSupportedException("Cannot modify read-only collection. Use the appropriate Remove methods on the Composite class instead.");

        public bool Contains(T item) => _items.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(T item) => _items.IndexOf(item);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // Additional methods to support List<T> compatibility
        public List<T> FindAll(Predicate<T> match) => _items.FindAll(match);
        public T Find(Predicate<T> match) => _items.Find(match);
        public bool Exists(Predicate<T> match) => _items.Exists(match);
        public void ForEach(Action<T> action) => _items.ForEach(action);
    }

}
