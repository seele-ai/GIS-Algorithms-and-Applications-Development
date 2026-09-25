using System;

namespace GIS
{
    /// <summary>
    /// 符号类型常数
    /// </summary>
    public enum SymbolTypeConstant
    {
        /// <summary>简单点符号</summary>
        SimpleMarkerSymbol,

        /// <summary>简单线符号</summary>
        SimpleLineSymbol,

        /// <summary>简单面符号</summary>
        SimpleFillSymbol
    }

    /// <summary>
    /// 符号抽象基类。图层渲染模块（唯一值渲染、分级渲染等）可在此基础上扩展。
    /// </summary>
    public abstract class Symbol
    {
        private string _Label = "";         //符号标签（图例中显示）
        private bool _Visible = true;       //符号是否绘制到地图上

        /// <summary>
        /// 获取符号类型
        /// </summary>
        public abstract SymbolTypeConstant SymbolType { get; }

        /// <summary>
        /// 获取或设置符号标签
        /// </summary>
        public virtual string Label
        {
            get { return _Label; }
            set { _Label = value; }
        }

        /// <summary>
        /// 获取或设置符号是否可见。渲染符号文件导入后若绑定字段不存在，
        /// 对应符号会被置为不可见（仅在图例中显示“绑定属性错误”），
        /// 因此不会被绘制到地图上。
        /// </summary>
        public bool Visible
        {
            get { return _Visible; }
            set { _Visible = value; }
        }

        /// <summary>
        /// 克隆符号（深复制）
        /// </summary>
        public abstract Symbol Clone();

        #region 私有函数

        //生成随机颜色：随机颜色RGB中总有一个通道取指定值，其他两个通道取值范围为179-245
        protected static System.Drawing.Color CreateRandomColor(byte fixedChannel)
        {
            byte[] sBytes = new byte[4];
            using (System.Security.Cryptography.RandomNumberGenerator sRng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                sRng.GetBytes(sBytes);
            }
            Int32 sChannelValue = sBytes[0];
            byte R, G, B;
            if (sChannelValue <= 85)
            {
                R = fixedChannel;
                G = (byte)(179 + 66 * sBytes[2] / 255);
                B = (byte)(179 + 66 * sBytes[3] / 255);
            }
            else if (sChannelValue <= 170)
            {
                G = fixedChannel;
                R = (byte)(179 + 66 * sBytes[1] / 255);
                B = (byte)(179 + 66 * sBytes[3] / 255);
            }
            else
            {
                B = fixedChannel;
                R = (byte)(179 + 66 * sBytes[1] / 255);
                G = (byte)(179 + 66 * sBytes[2] / 255);
            }
            return System.Drawing.Color.FromArgb(255, R, G, B);
        }

        #endregion
    }
}
