using System;
using System.Collections.Generic;
using System.Drawing;

namespace GIS.Display
{
    public enum MapInteractionMode { Pan, ZoomIn, ZoomOut, Select }

    // 模块2提出的对接接口，不修改模块1的公共类型。
    public interface IMapControl
    {
        IReadOnlyList<Layer> Layers { get; }
        void AddLayer(Layer layer);
        bool RemoveLayer(Layer layer);
        void MoveLayer(Layer layer, int newIndex);
        void SetLayerVisible(Layer layer, bool visible);
        void SetLayerSelectable(Layer layer, bool selectable);
        void Zoom(double ratio);
        void Pan(double deltaX, double deltaY);
        void SetExtent(Envelope extent);
        Envelope GetExtent();
        void FullExtent();
        void RefreshMap();
        Features GetSelection(Layer layer);
        void SetSelection(Layer layer, Features features, SelectMethodConstant method);
        void ClearSelection();
        event EventHandler LayersChanged;
        event EventHandler ViewChanged;
        event EventHandler SelectionChanged;
    }

    // 模块3可实现此接口替换基本绘图；不拥有传入Graphics的生命周期。
    public interface ILayerRenderer
    {
        void DrawLayer(Graphics graphics, Layer layer, MapTransform transform, Rectangle viewport);
    }

    // 可选：实现后由地图控件在“所有图层几何绘制完成后”统一绘制注记，保证注记在最上层。
    public interface ILayerLabelRenderer
    {
        void DrawLayerLabels(Graphics graphics, Layer layer, MapTransform transform, Rectangle viewport);
    }

    public sealed class MapCoordinateEventArgs : EventArgs
    {
        public Coordinate Coordinate { get; }
        public MapCoordinateEventArgs(Coordinate coordinate) { Coordinate = coordinate; }
    }

    public sealed class MapOverlayEventArgs : EventArgs
    {
        public Graphics Graphics { get; }
        public MapTransform Transform { get; }
        public MapOverlayEventArgs(Graphics graphics, MapTransform transform)
        {
            Graphics = graphics;
            Transform = transform;
        }
    }
}

