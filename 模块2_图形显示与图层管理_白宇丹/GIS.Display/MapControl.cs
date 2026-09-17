using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using ScreenPoint = System.Drawing.Point;

namespace GIS.Display
{
    public class MapControl : UserControl, IMapControl
    {
        private readonly List<Layer> layers = new List<Layer>();
        private readonly Dictionary<Layer, Features> selections = new Dictionary<Layer, Features>();
        private readonly HashSet<Layer> nonSelectable = new HashSet<Layer>();
        private readonly MapTransform transform = new MapTransform();
        private ILayerRenderer renderer = new BasicGeometryDrawer();
        private MapInteractionMode mode = MapInteractionMode.Pan;
        private bool dragging;
        private ScreenPoint dragStart, dragCurrent, lastPanPoint;
        private double startX, startY, startScale;
        private MapInteractionMode dragMode;

        public MapControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            TabStop = true;
            Cursor = Cursors.Hand;
            Size = new Size(800, 600);
            transform.UpdateWindowSize(Width, Height);
        }

        // 索引0在最底部；返回只读集合，防止绕过事件直接改顺序。
        public IReadOnlyList<Layer> Layers => layers.AsReadOnly();
        public MapTransform Transform => transform;
        public MapInteractionMode InteractionMode
        {
            get => mode;
            set
            {
                CancelGesture();
                mode = value;
                Cursor = value == MapInteractionMode.Pan ? Cursors.Hand : Cursors.Cross;
            }
        }
        public SelectMethodConstant SelectionMethod { get; set; } = SelectMethodConstant.CreateNew;
        public ILayerRenderer Renderer
        {
            get => renderer;
            set { renderer = value ?? throw new ArgumentNullException(nameof(value)); Invalidate(); }
        }
        public event EventHandler LayersChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler ViewChanged;
        public event EventHandler<MapCoordinateEventArgs> MapMouseMoved;
        public event EventHandler<MapOverlayEventArgs> OverlayPaint;

        public void AddLayer(Layer layer)
        {
            if (layer == null || layer.FeatureClass == null) throw new ArgumentNullException(nameof(layer));
            if (layers.Contains(layer)) return;
            layers.Add(layer);
            selections.Add(layer, new Features());
            LayersChanged?.Invoke(this, EventArgs.Empty);
            if (layers.Count == 1) FullExtent();
            Invalidate();
        }

        public bool RemoveLayer(Layer layer)
        {
            if (layer == null || !layers.Remove(layer)) return false;
            bool hadSelection = selections[layer].Count > 0;
            selections.Remove(layer);
            nonSelectable.Remove(layer);
            LayersChanged?.Invoke(this, EventArgs.Empty);
            if (hadSelection) SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return true;
        }

        public void MoveLayer(Layer layer, int newIndex)
        {
            RequireLayer(layer);
            if (newIndex < 0 || newIndex >= layers.Count) throw new ArgumentOutOfRangeException(nameof(newIndex));
            layers.Remove(layer);
            layers.Insert(newIndex, layer);
            LayersChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void SetLayerVisible(Layer layer, bool visible)
        {
            RequireLayer(layer);
            layer.Visible = visible;
            if (!visible && selections[layer].Count > 0)
            {
                selections[layer].Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            LayersChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public bool IsLayerSelectable(Layer layer)
        {
            RequireLayer(layer);
            return !nonSelectable.Contains(layer);
        }

        public void SetLayerSelectable(Layer layer, bool selectable)
        {
            RequireLayer(layer);
            if (selectable) nonSelectable.Remove(layer);
            else
            {
                nonSelectable.Add(layer);
                selections[layer].Clear();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
            LayersChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public Features GetSelection(Layer layer)
        {
            RequireLayer(layer);
            var result = new Features();
            result.Union(selections[layer]); // 集合快照，内部仍是原Feature引用，供编辑模块使用。
            return result;
        }

        public void SetSelection(Layer layer, Features features, SelectMethodConstant method)
        {
            RequireLayer(layer);
            if (features == null) throw new ArgumentNullException(nameof(features));
            var valid = new Features();
            if (layer.Visible && IsLayerSelectable(layer))
                foreach (Feature feature in features)
                    if (layer.FeatureClass.Features.Contains(feature)) valid.Add(feature);
            SelectTools.ExcuteSelect(selections[layer], valid, method);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void ClearSelection()
        {
            foreach (Features features in selections.Values) features.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void SelectAt(ScreenPoint screenPoint, SelectMethodConstant method)
        {
            Coordinate point = transform.ScreenToMap(screenPoint);
            double tolerance = 5 * transform.MapUnitsPerPixel();
            SelectAcrossLayers(fc => fc.SearchByPoint(point, tolerance), method);
        }

        public void SelectBox(Rectangle screenBox, SelectMethodConstant method)
        {
            var box = new Envelope(transform.ScreenToMap(screenBox.Left, screenBox.Top),
                transform.ScreenToMap(screenBox.Right, screenBox.Bottom));
            SelectAcrossLayers(fc => fc.SearchByBox(box), method);
        }

        private void SelectAcrossLayers(Func<FeatureClass, Features> search, SelectMethodConstant method)
        {
            foreach (Layer layer in layers)
            {
                if (!layer.Visible || nonSelectable.Contains(layer))
                {
                    selections[layer].Clear();
                    continue;
                }
                SelectTools.ExcuteSelect(selections[layer], search(layer.FeatureClass), method);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void Zoom(double ratio) => ZoomAt(transform.GetExtent().Center, ratio);
        public void ZoomAt(Coordinate anchor, double ratio)
        {
            if (!Finite(ratio) || ratio <= 0) throw new ArgumentOutOfRangeException(nameof(ratio));
            transform.ZoomByCenter(anchor, ratio);
            NotifyViewChanged();
        }
        public void Pan(double deltaX, double deltaY)
        {
            if (!Finite(deltaX) || !Finite(deltaY)) throw new ArgumentOutOfRangeException(nameof(deltaX));
            transform.PanDelta(deltaX, deltaY);
            NotifyViewChanged();
        }
        public Envelope GetExtent() => transform.GetExtent();
        public void SetExtent(Envelope extent)
        {
            if (extent == null || extent.IsNull || !Finite(extent.MinX) || !Finite(extent.MaxX) ||
                !Finite(extent.MinY) || !Finite(extent.MaxY))
                throw new ArgumentException("地图范围必须有效。", nameof(extent));
            // 单点、水平线和垂直线也能定位；避免模块1的零宽/零高范围除零。
            double minimum = Math.Max(Math.Max(extent.Width, extent.Height) * 0.05, 10);
            double width = Math.Max(extent.Width, minimum), height = Math.Max(extent.Height, minimum);
            var padded = new Envelope(extent.CenterX - width / 2, extent.CenterX + width / 2,
                extent.CenterY - height / 2, extent.CenterY + height / 2);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            transform.ZoomToExtent(padded);
            NotifyViewChanged();
        }
        public void FullExtent()
        {
            var extent = new Envelope();
            foreach (Layer layer in layers.Where(l => l.Visible))
                extent.ExpandToInclude(layer.GetEnvelope());
            if (extent.IsNull) return;
            double padding = Math.Max(Math.Max(extent.Width, extent.Height) * 0.05, 5);
            SetExtent(new Envelope(extent.MinX - padding, extent.MaxX + padding,
                extent.MinY - padding, extent.MaxY + padding));
        }

        public void RefreshMap()
        {
            // 编辑模块删除要素后调用本方法，清理失效的选择引用。
            bool changed = false;
            foreach (Layer layer in layers)
            {
                int before = selections[layer].Count;
                if (!layer.Visible || nonSelectable.Contains(layer)) selections[layer].Clear();
                else selections[layer].Intersect(layer.FeatureClass.Features);
                changed |= before != selections[layer].Count;
            }
            if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = CreateGraphics()) transform.Dpm = g.DpiX / 0.0254;
            transform.UpdateWindowSize(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (transform == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Coordinate center = transform.GetExtent().Center;
            transform.UpdateWindowSize(ClientSize.Width, ClientSize.Height);
            transform.PanTo(center.X, center.Y);
            NotifyViewChanged();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Layer layer in layers)
            {
                if (!layer.Visible) continue;
                var state = e.Graphics.Save();
                try { renderer.DrawLayer(e.Graphics, layer, transform, ClientRectangle); }
                finally { e.Graphics.Restore(state); }
            }
            // 高亮叠加在所有普通图层之上。
            foreach (Layer layer in layers.Where(l => l.Visible))
                foreach (Feature feature in selections[layer])
                    if (feature.Geometry != null)
                        BasicGeometryDrawer.DrawGeometry(e.Graphics, feature.Geometry,
                            BasicGeometryDrawer.HighlightSymbol(feature.Geometry), transform);
            var overlayState = e.Graphics.Save();
            try { OverlayPaint?.Invoke(this, new MapOverlayEventArgs(e.Graphics, transform)); }
            finally { e.Graphics.Restore(overlayState); }
            if (dragging && dragMode != MapInteractionMode.Pan)
                using (var pen = new Pen(Color.FromArgb(139, 40, 49), 1) { DashStyle = DashStyle.Dash })
                    e.Graphics.DrawRectangle(pen, DragRectangle());
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle) return;
            Focus();
            dragging = true;
            Capture = true;
            dragStart = dragCurrent = lastPanPoint = e.Location;
            dragMode = e.Button == MouseButtons.Middle ? MapInteractionMode.Pan : mode;
            startX = transform.MapOffsetX;
            startY = transform.MapOffsetY;
            startScale = transform.MapScale;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                dragCurrent = e.Location;
                if (dragMode == MapInteractionMode.Pan)
                {
                    double units = transform.MapUnitsPerPixel();
                    Pan((e.X - lastPanPoint.X) * units, (lastPanPoint.Y - e.Y) * units);
                    lastPanPoint = e.Location;
                }
                Invalidate();
            }
            MapMouseMoved?.Invoke(this, new MapCoordinateEventArgs(transform.ScreenToMap(e.Location)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle)) return;
            dragCurrent = e.Location;
            Rectangle box = DragRectangle();
            bool isClick = Math.Abs(e.X - dragStart.X) <= 3 && Math.Abs(e.Y - dragStart.Y) <= 3;
            dragging = false;
            Capture = false;
            if (dragMode == MapInteractionMode.Pan)
            {
                double units = transform.MapUnitsPerPixel();
                Pan((e.X - lastPanPoint.X) * units, (lastPanPoint.Y - e.Y) * units);
            }
            else if (dragMode == MapInteractionMode.Select)
            {
                if (isClick) SelectAt(e.Location, SelectionMethod);
                else SelectBox(box, SelectionMethod);
            }
            else if (dragMode == MapInteractionMode.ZoomIn)
            {
                if (isClick) ZoomAt(transform.ScreenToMap(e.Location), 2);
                else SetExtent(new Envelope(transform.ScreenToMap(box.Left, box.Top),
                    transform.ScreenToMap(box.Right, box.Bottom)));
            }
            else if (dragMode == MapInteractionMode.ZoomOut)
            {
                ZoomAt(transform.ScreenToMap(e.Location), 0.5);
            }
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (dragging || e.Delta == 0) return;
            ZoomAt(transform.ScreenToMap(e.Location), Math.Pow(1.2, e.Delta / 120.0));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && dragging) CancelGesture();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && dragging) { CancelGesture(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CancelGesture()
        {
            if (!dragging) return;
            dragging = false;
            Capture = false;
            transform.SetView(startX, startY, startScale);
            NotifyViewChanged();
        }
        private Rectangle DragRectangle() => Rectangle.FromLTRB(
            Math.Min(dragStart.X, dragCurrent.X), Math.Min(dragStart.Y, dragCurrent.Y),
            Math.Max(dragStart.X, dragCurrent.X), Math.Max(dragStart.Y, dragCurrent.Y));
        private void RequireLayer(Layer layer)
        {
            if (layer == null || !selections.ContainsKey(layer))
                throw new ArgumentException("图层尚未加入此地图。", nameof(layer));
        }
        private void NotifyViewChanged()
        {
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

