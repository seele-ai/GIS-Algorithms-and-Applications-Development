using System;

namespace GIS
{
    /// <summary>
    /// 属性字段的数据类型常数
    /// </summary>
    public enum FieldTypeConstant
    {
        /// <summary>字节型</summary>
        Byte,

        /// <summary>短整型</summary>
        Int16,

        /// <summary>长整型</summary>
        Int32,

        /// <summary>单精度浮点型</summary>
        Single,

        /// <summary>双精度浮点型</summary>
        Double,

        /// <summary>文本型</summary>
        Text,

        /// <summary>日期型</summary>
        Date,

        /// <summary>布尔型</summary>
        Boolean
    }

    /// <summary>
    /// 字段类型辅助类
    /// </summary>
    public static class FieldTypeTools
    {
        /// <summary>
        /// 获取字段类型对应的框架数据类型（供属性表达式查询的数据表使用）
        /// </summary>
        public static Type GetFrameworkValueType(FieldTypeConstant fieldType)
        {
            switch (fieldType)
            {
                case FieldTypeConstant.Byte:
                    return typeof(Byte);
                case FieldTypeConstant.Int16:
                    return typeof(Int16);
                case FieldTypeConstant.Int32:
                    return typeof(Int32);
                case FieldTypeConstant.Single:
                    return typeof(Single);
                case FieldTypeConstant.Double:
                    return typeof(Double);
                case FieldTypeConstant.Text:
                    return typeof(String);
                case FieldTypeConstant.Date:
                    return typeof(DateTime);
                case FieldTypeConstant.Boolean:
                    return typeof(Boolean);
                default:
                    return typeof(Object);
            }
        }
    }
}
