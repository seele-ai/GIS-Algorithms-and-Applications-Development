using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 坐标点集合
    /// </summary>
    public class Points : IEnumerable<Coordinate>
    {
        private List<Coordinate> _Items = new List<Coordinate>();

        #region 属性

        /// <summary>点的个数</summary>
        public Int32 Count
        {
            get { return _Items.Count; }
        }

        /// <summary>是否为空集合</summary>
        public bool IsEmpty
        {
            get { return _Items.Count == 0; }
        }

        /// <summary>按下标访问坐标点</summary>
        public Coordinate this[Int32 index]
        {
            get { return _Items[index]; }
            set { _Items[index] = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取指定下标的坐标点
        /// </summary>
        public Coordinate GetItem(Int32 index)
        {
            return _Items[index];
        }

        /// <summary>
        /// 设置指定下标的坐标点
        /// </summary>
        public void SetItem(Int32 index, Coordinate coordinate)
        {
            _Items[index] = coordinate;
        }

        /// <summary>
        /// 追加一个坐标点
        /// </summary>
        public void Add(Coordinate coordinate)
        {
            _Items.Add(coordinate);
        }

        /// <summary>
        /// 在指定位置插入一个坐标点
        /// </summary>
        public void Insert(Int32 index, Coordinate coordinate)
        {
            _Items.Insert(index, coordinate);
        }

        /// <summary>
        /// 删除指定下标的坐标点
        /// </summary>
        public void RemoveAt(Int32 index)
        {
            _Items.RemoveAt(index);
        }

        /// <summary>
        /// 删除指定坐标点（第一个匹配项）
        /// </summary>
        public void Remove(Coordinate coordinate)
        {
            _Items.Remove(coordinate);
        }

        /// <summary>
        /// 清空所有坐标点
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        /// <summary>
        /// 获取点集合的外接矩形
        /// </summary>
        public Envelope GetEnvelope()
        {
            Envelope sEnvelope = new Envelope();
            for (Int32 i = 0; i <= _Items.Count - 1; i++)
            {
                sEnvelope.ExpandToInclude(_Items[i]);
            }
            return sEnvelope;
        }

        /// <summary>
        /// 克隆点集合（深复制）
        /// </summary>
        public Points Clone()
        {
            Points sPoints = new Points();
            sPoints._Items.AddRange(_Items);
            return sPoints;
        }

        #endregion

        #region IEnumerable成员

        public IEnumerator<Coordinate> GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        #endregion
    }
}
