using System;
using System.Collections.Generic;

namespace Roy_T.AStar.Collections
{
    // C# Adaptation of a min heap built for C++ by Robin Thomas
    // Original source code at: https://github.com/robin-thomas/min-heap

    internal sealed class MinHeap<T>
        where T : IComparable<T>
    {
        private readonly List<T> Items;

        public MinHeap()
        {
            this.Items = new List<T>();
        }

        public int Count => this.Items.Count;

        public T Peek() => this.Items[0];

        public void Insert(T item)
        {
            this.Items.Add(item);
            this.SortItem(item, this.Items.Count - 1);
        }

        public T Extract()
        {
            var node = this.Items[0];

            this.ReplaceFirstItemWithLastItem();
            this.Heapify(0);

            return node;
        }

        public void Remove(T item)
        {
            var index = this.Items.IndexOf(item);
            if (index < 0) return;
            var replacement = this.Items[this.Items.Count - 1];
            this.Items.RemoveAt(this.Items.Count - 1);
            if (index == this.Items.Count) return;
            this.Items[index] = replacement;
            // 本项目修复：删除内部节点后从替换位置恢复堆序，不能只检查根。
            if (HasParent(index) && ItemAIsSmallerThanItemB(replacement, this.Items[GetParentIndex(index)]))
                this.SortItem(replacement, index);
            else
                this.Heapify(index);
        }

        public void Clear() => this.Items.Clear();

        private void ReplaceFirstItemWithLastItem()
        {
            this.Items[0] = this.Items[this.Items.Count - 1];
            this.Items.RemoveAt(this.Items.Count - 1);
        }

        private void SortItem(T item, int index)
        {
            while (HasParent(index))
            {
                var parentIndex = GetParentIndex(index);
                if (ItemAIsSmallerThanItemB(item, this.Items[parentIndex]))
                {
                    this.Items[index] = this.Items[parentIndex];
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }

            this.Items[index] = item;
        }

        private void Heapify(int startIndex)
        {
            var bestIndex = startIndex;

            if (this.HasLeftChild(startIndex))
            {
                var leftChildIndex = GetLeftChildIndex(startIndex);
                if (ItemAIsSmallerThanItemB(this.Items[leftChildIndex], this.Items[bestIndex]))
                {
                    bestIndex = leftChildIndex;
                }
            }

            if (this.HasRightChild(startIndex))
            {
                var rightChildIndex = GetRightChildIndex(startIndex);
                if (ItemAIsSmallerThanItemB(this.Items[rightChildIndex], this.Items[bestIndex]))
                {
                    bestIndex = rightChildIndex;
                }
            }

            if (bestIndex != startIndex)
            {
                var temp = this.Items[bestIndex];
                this.Items[bestIndex] = this.Items[startIndex];
                this.Items[startIndex] = temp;
                this.Heapify(bestIndex);
            }
        }

        private static bool ItemAIsSmallerThanItemB(T a, T b) => a.CompareTo(b) < 0;

        private static bool HasParent(int index) => index > 0;
        private bool HasLeftChild(int index) => GetLeftChildIndex(index) < this.Items.Count;
        private bool HasRightChild(int index) => GetRightChildIndex(index) < this.Items.Count;

        private static int GetParentIndex(int i) => (i - 1) / 2;
        private static int GetLeftChildIndex(int i) => (2 * i) + 1;
        private static int GetRightChildIndex(int i) => (2 * i) + 2;
    }
}
