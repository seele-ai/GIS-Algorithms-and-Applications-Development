namespace GIS
{
    /// <summary>
    /// 属性字段，描述要素属性表中一列的名称与类型
    /// </summary>
    public class Field
    {
        private string _Name = "";                          //字段名称
        private string _Alias = "";                         //字段别名
        private FieldTypeConstant _ValueType;               //字段类型

        #region 构造函数

        public Field()
        {
        }

        public Field(string name, FieldTypeConstant valueType)
        {
            _Name = name;
            _ValueType = valueType;
            _Alias = name;
        }

        public Field(string name, FieldTypeConstant valueType, string alias)
        {
            _Name = name;
            _ValueType = valueType;
            _Alias = alias;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置字段名称</summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value; }
        }

        /// <summary>获取或设置字段别名</summary>
        public string Alias
        {
            get { return _Alias; }
            set { _Alias = value; }
        }

        /// <summary>获取或设置字段类型</summary>
        public FieldTypeConstant ValueType
        {
            get { return _ValueType; }
            set { _ValueType = value; }
        }

        #endregion
    }
}
