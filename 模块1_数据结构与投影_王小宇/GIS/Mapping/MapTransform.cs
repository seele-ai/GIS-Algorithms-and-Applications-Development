using System;

namespace GIS
{
    /// <summary>
    /// 地图窗口视图变换类（纯数学类，不含UI）。
    /// 维护比例尺_MapScale与窗口左上角的地图坐标_MapOffsetX/_MapOffsetY，
    /// 提供模块内置函数MapToScreen(Coordinate)/ScreenToMap(System.Drawing.Point)
    /// 以及缩放、平移、范围定位等视图操作，供图形显示模块的MapControl直接使用。
    ///
    /// 坐标换算模型（与原moMapControl一致）：
    ///   screenX = (mapX - _MapOffsetX) * _Dpm * _Mpu / _MapScale
    ///   screenY = (_MapOffsetY - mapY) * _Dpm * _Mpu / _MapScale
    /// 其中_MapOffsetX/_MapOffsetY为屏幕左上角(0,0)对应的地图坐标。
    /// </summary>
    public class MapTransform
    {
        private double _MapScale = 10000;               //比例尺的倒数
        private double _MapOffsetX = 0;                 //屏幕左上角对应的地图X坐标
        private double _MapOffsetY = 0;                 //屏幕左上角对应的地图Y坐标
        private double _Dpm = 96 / 0.0254;              //屏幕上每米代表的像素数（按96DPI计算）
        private double _Mpu = 1.0;                      //1个地图坐标单位代表的米数，一般为1
        private Int32 _ViewWidth = 800;                 //地图窗口宽度（像素）
        private Int32 _ViewHeight = 600;                //地图窗口高度（像素）
        private double _MinMapScale = 1;                //允许的最小比例尺倒数
        private double _MaxMapScale = 1000000000;       //允许的最大比例尺倒数

        #region 构造函数

        public MapTransform()
        {
        }

        public MapTransform(Int32 viewWidth, Int32 viewHeight)
        {
            _ViewWidth = viewWidth;
            _ViewHeight = viewHeight;
        }

        #endregion

        #region 属性

        /// <summary>获取或设置比例尺的倒数</summary>
        public double MapScale
        {
            get { return _MapScale; }
            set { _MapScale = GetValidMapScale(value); }
        }

        /// <summary>获取或设置屏幕左上角对应的地图X坐标</summary>
        public double MapOffsetX
        {
            get { return _MapOffsetX; }
            set { _MapOffsetX = value; }
        }

        /// <summary>获取或设置屏幕左上角对应的地图Y坐标</summary>
        public double MapOffsetY
        {
            get { return _MapOffsetY; }
            set { _MapOffsetY = value; }
        }

        /// <summary>获取或设置屏幕上每米代表的像素数（DPI相关）</summary>
        public double Dpm
        {
            get { return _Dpm; }
            set { _Dpm = value; }
        }

        /// <summary>获取或设置1个地图坐标单位代表的米数</summary>
        public double Mpu
        {
            get { return _Mpu; }
            set { _Mpu = value; }
        }

        /// <summary>获取或设置地图窗口宽度（像素）</summary>
        public Int32 ViewWidth
        {
            get { return _ViewWidth; }
            set { _ViewWidth = value; }
        }

        /// <summary>获取或设置地图窗口高度（像素）</summary>
        public Int32 ViewHeight
        {
            get { return _ViewHeight; }
            set { _ViewHeight = value; }
        }

        /// <summary>获取或设置允许的最小比例尺倒数</summary>
        public double MinMapScale
        {
            get { return _MinMapScale; }
            set { _MinMapScale = value; }
        }

        /// <summary>获取或设置允许的最大比例尺倒数</summary>
        public double MaxMapScale
        {
            get { return _MaxMapScale; }
            set { _MaxMapScale = value; }
        }

        #endregion

        #region 视图设置

        /// <summary>
        /// 设置视图参数
        /// </summary>
        public void SetView(double mapOffsetX, double mapOffsetY, double mapScale)
        {
            _MapOffsetX = mapOffsetX;
            _MapOffsetY = mapOffsetY;
            _MapScale = GetValidMapScale(mapScale);
        }

        /// <summary>
        /// 地图窗口尺寸变化时更新窗口大小
        /// </summary>
        public void UpdateWindowSize(Int32 viewWidth, Int32 viewHeight)
        {
            _ViewWidth = viewWidth;
            _ViewHeight = viewHeight;
        }

        /// <summary>
        /// 检查并规范比例尺到允许范围
        /// </summary>
        public double GetValidMapScale(double mapScale)
        {
            if (mapScale < _MinMapScale)
                return _MinMapScale;
            if (mapScale > _MaxMapScale)
                return _MaxMapScale;
            return mapScale;
        }

        /// <summary>
        /// 获取当前视图下1个屏幕像素代表的地图单位数
        /// </summary>
        public double MapUnitsPerPixel()
        {
            return _MapScale / (_Dpm * _Mpu);
        }

        #endregion

        #region 坐标换算（模块内置函数）

        /// <summary>
        /// 将地图坐标转换为屏幕像素坐标（模块内置函数MapToScreen）
        /// </summary>
        public System.Drawing.Point MapToScreen(Coordinate mapPoint)
        {
            double k = _Dpm * _Mpu / _MapScale;
            double sScreenX = (mapPoint.X - _MapOffsetX) * k;
            double sScreenY = (_MapOffsetY - mapPoint.Y) * k;
            return new System.Drawing.Point((Int32)Math.Round(sScreenX), (Int32)Math.Round(sScreenY));
        }

        /// <summary>
        /// 将屏幕像素坐标转换为地图坐标（模块内置函数ScreenToMap）
        /// </summary>
        public Coordinate ScreenToMap(System.Drawing.Point screenPoint)
        {
            double k = _MapScale / (_Dpm * _Mpu);
            double sMapX = _MapOffsetX + screenPoint.X * k;
            double sMapY = _MapOffsetY - screenPoint.Y * k;
            return new Coordinate(sMapX, sMapY);
        }

        /// <summary>
        /// 将屏幕坐标（可为小数像素）转换为地图坐标
        /// </summary>
        public Coordinate ScreenToMap(double screenX, double screenY)
        {
            double k = _MapScale / (_Dpm * _Mpu);
            return new Coordinate(_MapOffsetX + screenX * k, _MapOffsetY - screenY * k);
        }

        /// <summary>
        /// 获取当前地图窗口范围
        /// </summary>
        public Envelope GetExtent()
        {
            double k = _MapScale / (_Dpm * _Mpu);
            double sMaxX = _MapOffsetX + _ViewWidth * k;
            double sMinY = _MapOffsetY - _ViewHeight * k;
            return new Envelope(_MapOffsetX, sMaxX, sMinY, _MapOffsetY);
        }

        #endregion

        #region 视图操作（缩放与平移）

        /// <summary>
        /// 以指定中心和指定系数进行缩放（ratio大于1为放大）
        /// </summary>
        public void ZoomByCenter(Coordinate center, double ratio)
        {
            double sMapScale = GetValidMapScale(_MapScale / ratio);     //新的比例尺
            double sRatio = _MapScale / sMapScale;                      //实际的缩放系数
            //缩放后保持中心点的屏幕位置不变
            double sOffsetX = _MapOffsetX + (1 - 1 / sRatio) * (center.X - _MapOffsetX);
            double sOffsetY = _MapOffsetY + (1 - 1 / sRatio) * (center.Y - _MapOffsetY);
            _MapOffsetX = sOffsetX;
            _MapOffsetY = sOffsetY;
            _MapScale = sMapScale;
        }

        /// <summary>
        /// 将地图缩放至指定比例尺（保持当前窗口中心不变）
        /// </summary>
        public void ZoomToScale(double mapScale)
        {
            Envelope sExtent = GetExtent();
            Coordinate sCenter = new Coordinate((sExtent.MinX + sExtent.MaxX) / 2, (sExtent.MinY + sExtent.MaxY) / 2);
            double sMapScale = GetValidMapScale(mapScale);
            double sRatio = _MapScale / sMapScale;
            _MapOffsetX = _MapOffsetX + (1 - 1 / sRatio) * (sCenter.X - _MapOffsetX);
            _MapOffsetY = _MapOffsetY + (1 - 1 / sRatio) * (sCenter.Y - _MapOffsetY);
            _MapScale = sMapScale;
        }

        /// <summary>
        /// 在窗口内显示指定范围（保持范围纵横比，充满窗口）
        /// </summary>
        public void ZoomToExtent(Envelope extent)
        {
            ZoomMapExtentToScreenExtent(extent, 0, 0, _ViewWidth, _ViewHeight);
        }

        /// <summary>
        /// 在窗口内显示地图全部范围（全图显示）
        /// </summary>
        public void FullExtent(Envelope fullExtent)
        {
            if (fullExtent.Width <= 0 || fullExtent.Height <= 0)
                return;
            if (_ViewWidth <= 0 || _ViewHeight <= 0)
                return;
            ZoomMapExtentToScreenExtent(fullExtent, 0, 0, _ViewWidth, _ViewHeight);
        }

        /// <summary>
        /// 将地图平移指定量（地图单位）
        /// </summary>
        public void PanDelta(double deltaX, double deltaY)
        {
            _MapOffsetX = _MapOffsetX - deltaX;
            _MapOffsetY = _MapOffsetY - deltaY;
        }

        /// <summary>
        /// 将地图平移至指定位置（使地图点(x,y)位于窗口中心）
        /// </summary>
        public void PanTo(double x, double y)
        {
            Envelope sExtent = GetExtent();
            double sCenterX = (sExtent.MinX + sExtent.MaxX) / 2;
            double sCenterY = (sExtent.MinY + sExtent.MaxY) / 2;
            _MapOffsetX = _MapOffsetX + x - sCenterX;
            _MapOffsetY = _MapOffsetY + y - sCenterY;
        }

        /// <summary>
        /// 将指定地图范围缩放至指定的屏幕范围
        /// </summary>
        public void ZoomMapExtentToScreenExtent(Envelope mapExtent, double srcX, double srcY, double srcWidth, double srcHeight)
        {
            double sMapWidth = mapExtent.Width, sMapHeight = mapExtent.Height;
            //计算宽高比例
            double sMapRatio = sMapWidth / sMapHeight;              //地图范围的宽高比
            double sScreenRatio = srcWidth / srcHeight;             //屏幕范围的宽高比
            //计算缩放后比例尺
            double sMapScale;
            if (sMapRatio <= sScreenRatio)
            {
                //按照垂向充满窗体
                sMapScale = sMapHeight * _Mpu / srcHeight * _Dpm;
            }
            else
            {
                //按照横向充满窗体
                sMapScale = sMapWidth * _Mpu / srcWidth * _Dpm;
            }
            //检查比例尺
            sMapScale = GetValidMapScale(sMapScale);
            //计算新的偏移量（屏幕范围中心对应地图范围中心）
            double sOffsetX = (mapExtent.MinX + mapExtent.MaxX) / 2 - (srcWidth / 2 + srcX) * sMapScale / (_Dpm * _Mpu);
            double sOffsetY = (mapExtent.MinY + mapExtent.MaxY) / 2 + (srcHeight / 2 + srcY) * sMapScale / (_Dpm * _Mpu);
            //赋值
            _MapOffsetX = sOffsetX;
            _MapOffsetY = sOffsetY;
            _MapScale = sMapScale;
        }

        #endregion
    }
}
