using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 要素集合。支持Replace/Union/Except/Intersect四种集合运算，
    /// 供图形选择（点选、框选）模块组织选择集使用。
    /// </summary>
    public class Features : IEnumerable<Feature>
    {
        private List<Feature> _Items = new List<Feature>();

        #region 属性

        /// <summary>要素个数</summary>
        public Int32 Count
        {
            get { return _Items.Count; }
        }

        /// <summary>按下标访问要素</summary>
        public Feature this[Int32 index]
        {
            get { return _Items[index]; }
            set { _Items[index] = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取指定下标的要素
        /// </summary>
        public Feature GetItem(Int32 index)
        {
            return _Items[index];
        }

        /// <summary>
        /// 追加一个要素
        /// </summary>
        public void Add(Feature feature)
        {
            _Items.Add(feature);
        }

        /// <summary>
        /// 删除指定下标的要素
        /// </summary>
        public void RemoveAt(Int32 index)
        {
            _Items.RemoveAt(index);
        }

        /// <summary>
        /// 删除指定要素（第一个匹配项）
        /// </summary>
        public void Remove(Feature feature)
        {
            _Items.Remove(feature);
        }

        /// <summary>
        /// 清空所有要素
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        /// <summary>
        /// 获取要素集合的整体外接矩形
        /// </summary>
        public Envelope GetEnvelope()
        {
            Envelope sEnvelope = new Envelope();
            for (Int32 i = 0; i <= _Items.Count - 1; i++)
            {
                sEnvelope.ExpandToInclude(_Items[i].GetEnvelope());
            }
            return sEnvelope;
        }

        #endregion

        #region 集合运算

        /// <summary>
        /// 用指定要素集合替换本集合内容
        /// </summary>
        public void Replace(Features features)
        {
            _Items.Clear();
            Union(features);
        }

        /// <summary>
        /// 并入指定要素集合（已存在的要素不重复加入）
        /// </summary>
        public void Union(Features features)
        {
            for (Int32 i = 0; i <= features.Count - 1; i++)
            {
                if (_Items.Contains(features.GetItem(i)) == false)
                    _Items.Add(features.GetItem(i));
            }
        }

        /// <summary>
        /// 从本集合中移除指定要素集合中包含的要素
        /// </summary>
        public void Except(Features features)
        {
            for (Int32 i = 0; i <= features.Count - 1; i++)
            {
                _Items.Remove(features.GetItem(i));
            }
        }

        /// <summary>
        /// 仅保留本集合与指定要素集合共有的要素
        /// </summary>
        public void Intersect(Features features)
        {
            List<Feature> sRemain = new List<Feature>();
            for (Int32 i = 0; i <= _Items.Count - 1; i++)
            {
                if (features.ContainsByReference(_Items[i]) == true)
                    sRemain.Add(_Items[i]);
            }
            _Items.Clear();
            _Items.AddRange(sRemain);
        }

        //判断集合中是否包含指定要素（按引用比较）
        private bool ContainsByReference(Feature feature)
        {
            for (Int32 i = 0; i <= _Items.Count - 1; i++)
            {
                if (object.ReferenceEquals(_Items[i], feature) == true)
                    return true;
            }
            return false;
        }

        #endregion

        #region IEnumerable成员

        public IEnumerator<Feature> GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _Items.GetEnumerator();
        }

        #endregion
    }

    /// <summary>
    /// 图形选择方式常数，供选择集的集合运算使用
    /// </summary>
    public enum SelectMethodConstant
    {
        /// <summary>创建新选择集</summary>
        CreateNew,

        /// <summary>加入当前选择集</summary>
        AddToCurrent,

        /// <summary>从当前选择集中移除</summary>
        RemoveFromCurrent,

        /// <summary>从当前选择集内部选取</summary>
        SelectFromCurrent
    }

    /// <summary>
    /// 选择集运算辅助类（静态类）
    /// </summary>
    public static class SelectTools
    {
        /// <summary>
        /// 按指定方式用要素集合更新选择集
        /// </summary>
        public static void ExcuteSelect(Features selectedFeatures, Features features, SelectMethodConstant selectMethod)
        {
            if (selectMethod == SelectMethodConstant.CreateNew)
            {
                selectedFeatures.Replace(features);
            }
            else if (selectMethod == SelectMethodConstant.AddToCurrent)
            {
                selectedFeatures.Union(features);
            }
            else if (selectMethod == SelectMethodConstant.RemoveFromCurrent)
            {
                selectedFeatures.Except(features);
            }
            else if (selectMethod == SelectMethodConstant.SelectFromCurrent)
            {
                selectedFeatures.Intersect(features);
            }
        }
    }
}
