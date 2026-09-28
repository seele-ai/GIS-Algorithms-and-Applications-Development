using System;
using System.Collections.Generic;

namespace GIS
{
    /// <summary>
    /// 要素属性集合。属性值序列与Fields字段集合按位置一一对应。
    /// </summary>
    public class Attributes
    {
        private Fields _Fields;                             //字段模式（可为null，此时仅按下标访问）
        private List<object> _Values = new List<object>();  //属性值序列

        #region 构造函数

        /// <summary>
        /// 按字段模式构造属性集合，全部属性初始化为null
        /// </summary>
        public Attributes(Fields fields)
        {
            _Fields = fields;
            for (Int32 i = 0; i <= fields.Count - 1; i++)
            {
                _Values.Add(null);
            }
        }

        /// <summary>
        /// 按字段模式与初始值序列构造属性集合
        /// </summary>
        public Attributes(Fields fields, object[] values)
        {
            _Fields = fields;
            if (values.Length != fields.Count)
                throw new ArgumentException("属性值个数与字段个数不一致");
            _Values.AddRange(values);
        }

        #endregion

        #region 属性

        /// <summary>属性个数</summary>
        public Int32 Count
        {
            get { return _Values.Count; }
        }

        /// <summary>获取属性集合绑定的字段模式（可能为null）</summary>
        public Fields Fields
        {
            get { return _Fields; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 按下标获取属性值
        /// </summary>
        public object GetItem(Int32 index)
        {
            return _Values[index];
        }

        /// <summary>
        /// 按字段名称获取属性值，字段不存在时返回null
        /// </summary>
        public object GetItem(string name)
        {
            if (_Fields == null)
                return null;
            Int32 sIndex = _Fields.FindField(name);
            if (sIndex < 0)
                return null;
            return _Values[sIndex];
        }

        /// <summary>
        /// 按下标设置属性值
        /// </summary>
        public void SetItem(Int32 index, object value)
        {
            _Values[index] = value;
        }

        /// <summary>
        /// 按字段名称设置属性值，字段不存在时忽略
        /// </summary>
        public void SetItem(string name, object value)
        {
            if (_Fields == null)
                return;
            Int32 sIndex = _Fields.FindField(name);
            if (sIndex >= 0)
                _Values[sIndex] = value;
        }

        /// <summary>
        /// 获取全部属性值的数组
        /// </summary>
        public object[] ToArray()
        {
            return _Values.ToArray();
        }

        #endregion
    }
}
