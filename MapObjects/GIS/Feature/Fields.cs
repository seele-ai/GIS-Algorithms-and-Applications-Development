using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 属性字段集合，描述一个要素类的属性表结构（模式）
    /// </summary>
    public class Fields : IEnumerable<Field>
    {
        private List<Field> _Items = new List<Field>();

        #region 属性

        /// <summary>字段个数</summary>
        public Int32 Count
        {
            get { return _Items.Count; }
        }

        /// <summary>按下标访问字段</summary>
        public Field this[Int32 index]
        {
            get { return _Items[index]; }
            set { _Items[index] = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 获取指定下标的字段
        /// </summary>
        public Field GetItem(Int32 index)
        {
            return _Items[index];
        }

        /// <summary>
        /// 按字段名称获取字段，不存在时返回null
        /// </summary>
        public Field GetItem(string name)
        {
            Int32 sIndex = FindField(name);
            if (sIndex < 0)
                return null;
            return _Items[sIndex];
        }

        /// <summary>
        /// 按字段名称查找字段下标，不存在时返回-1
        /// </summary>
        public Int32 FindField(string name)
        {
            for (Int32 i = 0; i <= _Items.Count - 1; i++)
            {
                if (_Items[i].Name == name)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 追加一个字段（字段名称重复时抛出异常）
        /// </summary>
        public void Add(Field field)
        {
            if (FindField(field.Name) >= 0)
                throw new ArgumentException("字段名称已存在：" + field.Name);
            _Items.Add(field);
        }

        /// <summary>
        /// 删除指定下标的字段
        /// </summary>
        public void RemoveAt(Int32 index)
        {
            _Items.RemoveAt(index);
        }

        /// <summary>
        /// 清空所有字段
        /// </summary>
        public void Clear()
        {
            _Items.Clear();
        }

        #endregion

        #region IEnumerable成员

        public IEnumerator<Field> GetEnumerator()
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
