using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VisionSystem.Controls.Geometry
{
    /// <summary>
    /// 图形集合。增删改均会触发 <see cref="CollectionChanged"/>，
    /// 控件订阅该事件后自动重绘。
    /// </summary>
    public class ShapeCollection : Collection<IGeometryShape>
    {
        /// <summary>集合内容发生变化（增、删、改、清空）。</summary>
        public event EventHandler CollectionChanged;

        /// <summary>创建空集合。</summary>
        public ShapeCollection()
        {
        }

        /// <summary>批量添加。</summary>
        public void AddRange(IEnumerable<IGeometryShape> shapes)
        {
            if (shapes == null) return;
            bool changed = false;
            foreach (IGeometryShape s in shapes)
            {
                if (s == null) continue;
                Items.Add(s);
                changed = true;
            }
            if (changed) OnCollectionChanged();
        }

        /// <summary>按名称查找第一个匹配的图形。</summary>
        public IGeometryShape FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (IGeometryShape s in Items)
            {
                if (s != null && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
            return null;
        }

        /// <summary>获取所有处于选中状态的图形。</summary>
        public IList<IGeometryShape> GetSelected()
        {
            var result = new List<IGeometryShape>();
            foreach (IGeometryShape s in Items)
            {
                if (s != null && s.IsSelected) result.Add(s);
            }
            return result;
        }

        /// <summary>取消所有图形的选中状态。</summary>
        public void ClearSelection()
        {
            bool changed = false;
            foreach (IGeometryShape s in Items)
            {
                if (s != null && s.IsSelected)
                {
                    s.IsSelected = false;
                    changed = true;
                }
            }
            if (changed) OnCollectionChanged();
        }

        /// <summary>移除所有处于选中状态的图形，返回移除的数量。</summary>
        public int RemoveSelected()
        {
            var toRemove = GetSelected();
            if (toRemove.Count == 0) return 0;

            foreach (IGeometryShape s in toRemove)
                Items.Remove(s);

            OnCollectionChanged();
            return toRemove.Count;
        }

        /// <summary>触发 <see cref="CollectionChanged"/>（外部直接修改图形几何后调用以请求重绘）。</summary>
        public void NotifyChanged()
        {
            OnCollectionChanged();
        }

        protected override void InsertItem(int index, IGeometryShape item)
        {
            base.InsertItem(index, item);
            OnCollectionChanged();
        }

        protected override void SetItem(int index, IGeometryShape item)
        {
            base.SetItem(index, item);
            OnCollectionChanged();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            OnCollectionChanged();
        }

        protected override void ClearItems()
        {
            base.ClearItems();
            OnCollectionChanged();
        }

        protected virtual void OnCollectionChanged()
        {
            EventHandler handler = CollectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
