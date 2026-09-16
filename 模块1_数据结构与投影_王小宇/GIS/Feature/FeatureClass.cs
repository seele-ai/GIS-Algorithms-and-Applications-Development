using System;
using System.Collections.Generic;
using System.Data;

namespace GIS
{
    /// <summary>
    /// 要素类 = 属性表模式（Fields）+ 要素集合（Features）+ 几何类型约束，
    /// 另携带所属投影坐标系统（供数据导入导出与PostGIS模块使用）。
    /// </summary>
    public class FeatureClass
    {
        private string _Name = "";                              //要素类名称
        private GeometryTypeConstant _GeometryType;             //允许的几何类型
        private Fields _Fields = new Fields();                  //属性表模式
        private Features _Features = new Features();            //要素集合
        private ProjectionCS _ProjectionCS;                     //所属投影，null表示经纬度或未定义

        #region 构造函数

        public FeatureClass(string name, GeometryTypeConstant geometryType)
        {
            _Name = name;
            _GeometryType = geometryType;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置要素类名称</summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = value; }
        }

        /// <summary>获取要素类允许的几何类型</summary>
        public GeometryTypeConstant GeometryType
        {
            get { return _GeometryType; }
        }

        /// <summary>获取属性表模式</summary>
        public Fields Fields
        {
            get { return _Fields; }
        }

        /// <summary>获取要素集合</summary>
        public Features Features
        {
            get { return _Features; }
        }

        /// <summary>获取或设置所属投影坐标系统（null表示经纬度或未定义）</summary>
        public ProjectionCS ProjectionCS
        {
            get { return _ProjectionCS; }
            set { _ProjectionCS = value; }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 判断指定几何对象的类型是否为本要素类允许的类型
        /// （线类允许LineString/MultiLineString，面类允许Polygon/MultiPolygon）
        /// </summary>
        public bool IsGeometryTypeAllowed(Geometry geometry)
        {
            if (geometry == null)
                return false;
            GeometryTypeConstant sType = geometry.GeometryType;
            if (sType == _GeometryType)
                return true;
            if (_GeometryType == GeometryTypeConstant.LineString &&
                (sType == GeometryTypeConstant.LineString || sType == GeometryTypeConstant.MultiLineString))
                return true;
            if (_GeometryType == GeometryTypeConstant.Polygon &&
                (sType == GeometryTypeConstant.Polygon || sType == GeometryTypeConstant.MultiPolygon))
                return true;
            return false;
        }

        /// <summary>
        /// 向要素类中添加要素（校验几何类型与属性模式，不合法时抛出异常）
        /// </summary>
        public void Add(Feature feature)
        {
            //（1）校验几何类型
            if (IsGeometryTypeAllowed(feature.Geometry) == false)
                throw new ArgumentException("要素几何类型与本要素类不匹配：" + feature.Geometry.GeometryType);
            //（2）校验属性模式
            if (feature.Attributes == null)
            {
                feature.Attributes = new Attributes(_Fields);
            }
            else if (feature.Attributes.Count != _Fields.Count)
            {
                throw new ArgumentException("要素属性个数与要素类字段个数不一致");
            }
            //（3）加入集合
            _Features.Add(feature);
        }

        /// <summary>
        /// 获取要素类的整体外接矩形
        /// </summary>
        public Envelope GetEnvelope()
        {
            return _Features.GetEnvelope();
        }

        /// <summary>
        /// 根据指定点和指定容限搜索要素（面要素忽略容限，按包含判断）
        /// </summary>
        public Features SearchByPoint(Coordinate point, double tolerance)
        {
            Features sFeatures = new Features();
            for (Int32 i = 0; i <= _Features.Count - 1; i++)
            {
                Feature sFeature = _Features.GetItem(i);
                if (sFeature.Geometry.Contains(point, tolerance) == true)
                {
                    sFeatures.Add(sFeature);
                }
            }
            return sFeatures;
        }

        /// <summary>
        /// 根据矩形范围搜索要素（几何对象与范围相交即选中）
        /// </summary>
        public Features SearchByBox(Envelope box)
        {
            Features sFeatures = new Features();
            for (Int32 i = 0; i <= _Features.Count - 1; i++)
            {
                Feature sFeature = _Features.GetItem(i);
                if (sFeature.Geometry.Intersects(box) == true)
                {
                    sFeatures.Add(sFeature);
                }
            }
            return sFeatures;
        }

        /// <summary>
        /// 根据属性表达式搜索要素（表达式语法同DataTable.Select，如"POP > 100"），
        /// 表达式不合法时返回空集合并置isExpressionCorrect为否
        /// </summary>
        public Features SearchByExpression(string expression, out bool isExpressionCorrect)
        {
            //新建数据表并按字段模式生成列
            DataTable sDataTable = new DataTable();
            for (Int32 i = 0; i <= _Fields.Count - 1; i++)
            {
                Field sField = _Fields.GetItem(i);
                Type sType = FieldTypeTools.GetFrameworkValueType(sField.ValueType);
                sDataTable.Columns.Add(new DataColumn(sField.Name, sType));
            }
            //增加一个存储原始下标的辅助列
            DataColumn sIndexCol = new DataColumn("?OriginalIndex?", typeof(Int32));
            sDataTable.Columns.Add(sIndexCol);
            //按要素生成数据行
            for (Int32 i = 0; i <= _Features.Count - 1; i++)
            {
                Feature sFeature = _Features.GetItem(i);
                List<object> sValues = new List<object>();
                if (sFeature.Attributes != null)
                    sValues.AddRange(sFeature.Attributes.ToArray());
                while (sValues.Count < _Fields.Count)
                    sValues.Add(null);
                sValues.Add(i);
                DataRow sRow = sDataTable.NewRow();
                sRow.ItemArray = sValues.ToArray();
                sDataTable.Rows.Add(sRow);
            }
            //执行表达式查询
            DataRow[] sSelRows;
            try
            {
                sSelRows = sDataTable.Select(expression);
            }
            catch
            {
                isExpressionCorrect = false;
                return new Features();
            }
            //按原始下标返回要素集合
            Features sFeatures = new Features();
            Int32 sColCount = sDataTable.Columns.Count;
            for (Int32 i = 0; i <= sSelRows.Length - 1; i++)
            {
                Int32 sOriginalIndex = (Int32)sSelRows[i].ItemArray[sColCount - 1];
                sFeatures.Add(_Features.GetItem(sOriginalIndex));
            }
            isExpressionCorrect = true;
            return sFeatures;
        }

        #endregion
    }
}
