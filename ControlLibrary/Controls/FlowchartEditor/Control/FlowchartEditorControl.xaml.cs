using ControlLibrary.Controls.FlowchartEditor.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlLibrary.Controls.FlowchartEditor.Control
{
    /// <summary>
    /// FlowchartEditorControl.xaml 的交互逻辑
    /// </summary>
    // 流程图编辑器的交互和渲染逻辑。
    public partial class FlowchartEditorControl : UserControl
    {
        // 画布使用一个很大的固定工作区，再通过缩放和平移变换显示局部区域。
        private const double WorkspaceSize = 10000;
        private const double DefaultNodeWidth = 150;
        private const double DefaultNodeHeight = 70;
        private const double AnchorSize = 14;
        private const double MinZoom = 0.25;
        private const double MaxZoom = 3.0;
        // 连线避障时，节点矩形会向外扩这个距离，避免折线贴着节点边缘走。
        private const double ConnectionClearance = FlowchartOrthogonalRouter.DefaultClearance;

        // _nodes 是模型数据；_nodeVisuals 和 _nodeOutlines 是模型到界面元素的索引。
        private readonly List<FlowchartNodeModel> _nodes = new List<FlowchartNodeModel>();
        private readonly List<FlowchartConnectionModel> _connections = new List<FlowchartConnectionModel>();
        private readonly Dictionary<Guid, FlowchartNodeModel> _nodesById = new Dictionary<Guid, FlowchartNodeModel>();
        private readonly Dictionary<Guid, FrameworkElement> _nodeVisuals = new Dictionary<Guid, FrameworkElement>();
        private readonly Dictionary<Guid, FrameworkElement> _nodeOutlines = new Dictionary<Guid, FrameworkElement>();

        private bool _isViewportInitialized;
        private bool _isPanning;
        private Point _panStartPoint;
        private double _panStartTranslateX;
        private double _panStartTranslateY;

        private FlowchartNodeModel? _draggingNode;
        private Point _nodeDragStartWorldPoint;
        private double _nodeDragStartX;
        private double _nodeDragStartY;

        // 创建连线时记录起点节点和起点锚点，鼠标移动时用它们生成预览折线。
        private bool _isConnecting;
        private FlowchartNodeModel? _connectionSourceNode;
        private FlowchartAnchor _connectionSourceAnchor;
        private Polyline? _previewLine;
        private Polygon? _previewArrow;

        private Guid? _selectedNodeId;
        private Guid? _selectedConnectionId;
        private string? _lastProcessedDragId;

        public FlowchartEditorControl()
        {
            InitializeComponent();
        }

        private void FlowchartEditorControl_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureWorkspaceInitialized();
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            EnsureWorkspaceInitialized();
        }

        private void EnsureWorkspaceInitialized()
        {
            if (_isViewportInitialized || Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0)
            {
                return;
            }

            WorkspaceScaleTransform.ScaleX = 1;
            WorkspaceScaleTransform.ScaleY = 1;
            WorkspaceTranslateTransform.X = (Viewport.ActualWidth / 2) - (WorkspaceSize / 2);
            WorkspaceTranslateTransform.Y = (Viewport.ActualHeight / 2) - (WorkspaceSize / 2);
            _isViewportInitialized = true;
        }

        private void Viewport_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Viewport_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                return;
            }

            string? dragId = e.Data.GetDataPresent(FlowchartDragDataFormats.DragId)
                ? e.Data.GetData(FlowchartDragDataFormats.DragId)?.ToString()
                : null;

            if (!string.IsNullOrWhiteSpace(dragId) && string.Equals(_lastProcessedDragId, dragId, StringComparison.Ordinal))
            {
                e.Handled = true;
                return;
            }

            string nodeText = e.Data.GetDataPresent(FlowchartDragDataFormats.PaletteText)
                ? e.Data.GetData(FlowchartDragDataFormats.PaletteText)?.ToString() ?? string.Empty
                : e.Data.GetData(DataFormats.StringFormat)?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(nodeText))
            {
                return;
            }

            // 新版拖拽数据会直接传节点类型；旧版只传文本时，通过文本兜底识别“判断”节点。
            FlowchartNodeKind nodeKind = ResolveNodeKind(nodeText);
            if (e.Data.GetDataPresent(FlowchartDragDataFormats.PaletteNodeKind))
            {
                string? nodeKindValue = e.Data.GetData(FlowchartDragDataFormats.PaletteNodeKind)?.ToString();
                if (Enum.TryParse(nodeKindValue, out FlowchartNodeKind parsedNodeKind))
                {
                    nodeKind = parsedNodeKind;
                }
            }

            _lastProcessedDragId = dragId;
            AddNode(nodeText, nodeKind, e.GetPosition(Viewport));
            e.Handled = true;
        }

        private void AddNode(string text, FlowchartNodeKind kind, Point viewportPoint)
        {
            Point worldPoint = ViewportToWorld(viewportPoint);
            FlowchartNodeModel node = new FlowchartNodeModel
            {
                Text = text,
                Kind = kind,
                Width = DefaultNodeWidth,
                Height = DefaultNodeHeight,
                X = Clamp(worldPoint.X - (DefaultNodeWidth / 2), 0, WorkspaceSize - DefaultNodeWidth),
                Y = Clamp(worldPoint.Y - (DefaultNodeHeight / 2), 0, WorkspaceSize - DefaultNodeHeight)
            };

            // 节点模型和界面元素分开保存：模型负责坐标/类型，界面元素负责实际绘制和鼠标事件。
            _nodes.Add(node);
            _nodesById[node.Id] = node;
            CreateNodeVisual(node);
            SelectNode(node.Id);
            Focus();
        }

        private void CreateNodeVisual(FlowchartNodeModel node)
        {
            // Grid 是整个节点的命中区域；内部的 Border/Polygon 只负责外形。
            Grid nodeRoot = new Grid
            {
                Width = node.Width,
                Height = node.Height,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                Tag = node
            };

            // 判断节点用横向菱形，其他节点保持原来的圆角矩形。
            FrameworkElement nodeOutline = node.Kind == FlowchartNodeKind.Decision
                ? CreateDecisionOutline(node)
                : CreateRectangleOutline();

            TextBlock textBlock = new TextBlock
            {
                Text = node.Text,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333")),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            nodeRoot.Children.Add(nodeOutline);
            nodeRoot.Children.Add(textBlock);
            nodeRoot.Children.Add(CreateAnchorHandle(node, FlowchartAnchor.Top));
            nodeRoot.Children.Add(CreateAnchorHandle(node, FlowchartAnchor.Right));
            nodeRoot.Children.Add(CreateAnchorHandle(node, FlowchartAnchor.Bottom));
            nodeRoot.Children.Add(CreateAnchorHandle(node, FlowchartAnchor.Left));

            nodeRoot.MouseLeftButtonDown += NodeRoot_MouseLeftButtonDown;
            nodeRoot.MouseMove += NodeRoot_MouseMove;
            nodeRoot.MouseLeftButtonUp += NodeRoot_MouseLeftButtonUp;

            Canvas.SetLeft(nodeRoot, node.X);
            Canvas.SetTop(nodeRoot, node.Y);
            Canvas.SetZIndex(nodeRoot, 10);

            NodesCanvas.Children.Add(nodeRoot);
            _nodeVisuals[node.Id] = nodeRoot;
            _nodeOutlines[node.Id] = nodeOutline;
            UpdateNodeSelectionVisuals();
        }

        private static Border CreateRectangleOutline()
        {
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9")),
                Background = Brushes.White
            };
        }

        private static Polygon CreateDecisionOutline(FlowchartNodeModel node)
        {
            return new Polygon
            {
                // 四个点分别是上、右、下、左，所以视觉上是横向菱形。
                Points = new PointCollection(new[]
                {
                    new Point(node.Width / 2, 0),
                    new Point(node.Width, node.Height / 2),
                    new Point(node.Width / 2, node.Height),
                    new Point(0, node.Height / 2)
                }),
                Fill = Brushes.White,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9")),
                StrokeThickness = 2,
                Stretch = Stretch.None
            };
        }

        private FrameworkElement CreateAnchorHandle(FlowchartNodeModel node, FlowchartAnchor anchor)
        {
            // 锚点小圆点放在节点四边中点；拖动它开始创建连线。
            Ellipse handle = new Ellipse
            {
                Width = AnchorSize,
                Height = AnchorSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED")),
                StrokeThickness = 2,
                Cursor = Cursors.Cross,
                Tag = new AnchorHandleInfo(node.Id, anchor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            switch (anchor)
            {
                case FlowchartAnchor.Top:
                    handle.VerticalAlignment = VerticalAlignment.Top;
                    handle.Margin = new Thickness(0, -(AnchorSize / 2), 0, 0);
                    break;
                case FlowchartAnchor.Right:
                    handle.HorizontalAlignment = HorizontalAlignment.Right;
                    handle.Margin = new Thickness(0, 0, -(AnchorSize / 2), 0);
                    break;
                case FlowchartAnchor.Bottom:
                    handle.VerticalAlignment = VerticalAlignment.Bottom;
                    handle.Margin = new Thickness(0, 0, 0, -(AnchorSize / 2));
                    break;
                default:
                    handle.HorizontalAlignment = HorizontalAlignment.Left;
                    handle.Margin = new Thickness(-(AnchorSize / 2), 0, 0, 0);
                    break;
            }

            handle.MouseLeftButtonDown += AnchorHandle_MouseLeftButtonDown;
            return handle;
        }

        private void NodeRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not FlowchartNodeModel node)
            {
                return;
            }

            Focus();
            SelectNode(node.Id);
            _draggingNode = node;
            _nodeDragStartWorldPoint = ViewportToWorld(e.GetPosition(Viewport));
            _nodeDragStartX = node.X;
            _nodeDragStartY = node.Y;
            element.CaptureMouse();
            e.Handled = true;
        }

        private void NodeRoot_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingNode is null || sender is not FrameworkElement element || !ReferenceEquals(_draggingNode, element.Tag))
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point worldPoint = ViewportToWorld(e.GetPosition(Viewport));
            double offsetX = worldPoint.X - _nodeDragStartWorldPoint.X;
            double offsetY = worldPoint.Y - _nodeDragStartWorldPoint.Y;

            _draggingNode.X = Clamp(_nodeDragStartX + offsetX, 0, WorkspaceSize - _draggingNode.Width);
            _draggingNode.Y = Clamp(_nodeDragStartY + offsetY, 0, WorkspaceSize - _draggingNode.Height);
            UpdateNodeVisualPosition(_draggingNode);
            RenderConnections();
            e.Handled = true;
        }

        private void NodeRoot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingNode is null)
            {
                return;
            }

            if (sender is UIElement element)
            {
                element.ReleaseMouseCapture();
            }

            _draggingNode = null;
            e.Handled = true;
        }

        private void AnchorHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not AnchorHandleInfo handleInfo)
            {
                return;
            }

            if (!_nodesById.TryGetValue(handleInfo.NodeId, out FlowchartNodeModel? sourceNode))
            {
                return;
            }

            Focus();
            SelectNode(sourceNode.Id);
            _isConnecting = true;
            _connectionSourceNode = sourceNode;
            _connectionSourceAnchor = handleInfo.Anchor;

            // 先画一条长度为 0 的预览线，鼠标移动时再更新成真正的避障折线。
            Point startPoint = sourceNode.GetAnchorPoint(handleInfo.Anchor);
            ShowPreviewConnection(startPoint, startPoint);
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            ClearSelection();

            _isPanning = true;
            _panStartPoint = e.GetPosition(Viewport);
            _panStartTranslateX = WorkspaceTranslateTransform.X;
            _panStartTranslateY = WorkspaceTranslateTransform.Y;
            Viewport.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            Point viewportPoint = e.GetPosition(Viewport);

            if (_isConnecting && _connectionSourceNode is not null)
            {
                UpdatePreviewConnection(_connectionSourceNode.GetAnchorPoint(_connectionSourceAnchor), ViewportToWorld(viewportPoint));
                e.Handled = true;
                return;
            }

            if (!_isPanning || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector offset = viewportPoint - _panStartPoint;
            WorkspaceTranslateTransform.X = _panStartTranslateX + offset.X;
            WorkspaceTranslateTransform.Y = _panStartTranslateY + offset.Y;
            e.Handled = true;
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isConnecting)
            {
                CompleteConnection(e.GetPosition(Viewport));
                Viewport.ReleaseMouseCapture();
                _isConnecting = false;
                _connectionSourceNode = null;
                ClearPreviewConnection();
                e.Handled = true;
                return;
            }

            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;
            Cursor = Cursors.Arrow;
            Viewport.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void CompleteConnection(Point viewportPoint)
        {
            if (_connectionSourceNode is null)
            {
                return;
            }

            // 只有鼠标松开时落在另一个节点的锚点上，才会真正创建连接。
            if (!TryGetAnchorHandleAt(viewportPoint, out FlowchartNodeModel? targetNode, out FlowchartAnchor targetAnchor))
            {
                return;
            }

            if (targetNode is null || targetNode.Id == _connectionSourceNode.Id)
            {
                return;
            }

            bool exists = _connections.Any(connection =>
                connection.SourceNodeId == _connectionSourceNode.Id &&
                connection.SourceAnchor == _connectionSourceAnchor &&
                connection.TargetNodeId == targetNode.Id &&
                connection.TargetAnchor == targetAnchor);

            if (exists)
            {
                return;
            }

            FlowchartConnectionModel connectionModel = new FlowchartConnectionModel
            {
                SourceNodeId = _connectionSourceNode.Id,
                SourceAnchor = _connectionSourceAnchor,
                TargetNodeId = targetNode.Id,
                TargetAnchor = targetAnchor
            };

            _connections.Add(connectionModel);
            SelectConnection(connectionModel.Id);
            RenderConnections();
        }

        private bool TryGetAnchorHandleAt(Point viewportPoint, out FlowchartNodeModel? node, out FlowchartAnchor anchor)
        {
            node = null;
            anchor = FlowchartAnchor.Top;

            // WPF 命中测试拿到的是最内层元素，需要一路向父级查找锚点 Tag。
            HitTestResult? hitTestResult = VisualTreeHelper.HitTest(Viewport, viewportPoint);
            DependencyObject? current = hitTestResult?.VisualHit;

            while (current is not null)
            {
                if (current is FrameworkElement element && element.Tag is AnchorHandleInfo handleInfo)
                {
                    if (_nodesById.TryGetValue(handleInfo.NodeId, out FlowchartNodeModel? targetNode))
                    {
                        node = targetNode;
                        anchor = handleInfo.Anchor;
                        return true;
                    }
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point viewportPoint = e.GetPosition(Viewport);
            Point worldPoint = ViewportToWorld(viewportPoint);
            double currentScale = WorkspaceScaleTransform.ScaleX;
            double nextScale = e.Delta > 0 ? currentScale * 1.1 : currentScale / 1.1;
            nextScale = Clamp(nextScale, MinZoom, MaxZoom);

            if (Math.Abs(nextScale - currentScale) < 0.0001)
            {
                return;
            }

            WorkspaceScaleTransform.ScaleX = nextScale;
            WorkspaceScaleTransform.ScaleY = nextScale;
            // 缩放时保持鼠标指向的世界坐标不变，用户会感觉是在以鼠标位置为中心缩放。
            WorkspaceTranslateTransform.X = viewportPoint.X - (worldPoint.X * nextScale);
            WorkspaceTranslateTransform.Y = viewportPoint.Y - (worldPoint.Y * nextScale);
            e.Handled = true;
        }

        private void FlowchartEditorControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
            {
                return;
            }

            if (_selectedNodeId.HasValue)
            {
                RemoveNode(_selectedNodeId.Value);
                e.Handled = true;
                return;
            }

            if (_selectedConnectionId.HasValue)
            {
                RemoveConnection(_selectedConnectionId.Value);
                e.Handled = true;
            }
        }

        private void RemoveNode(Guid nodeId)
        {
            _nodes.RemoveAll(node => node.Id == nodeId);
            _nodesById.Remove(nodeId);
            _connections.RemoveAll(connection => connection.SourceNodeId == nodeId || connection.TargetNodeId == nodeId);

            if (_nodeVisuals.TryGetValue(nodeId, out FrameworkElement? nodeVisual))
            {
                NodesCanvas.Children.Remove(nodeVisual);
                _nodeVisuals.Remove(nodeId);
            }

            _nodeOutlines.Remove(nodeId);
            _selectedNodeId = null;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void RemoveConnection(Guid connectionId)
        {
            _connections.RemoveAll(connection => connection.Id == connectionId);
            _selectedConnectionId = null;
            RenderConnections();
        }

        private void SelectNode(Guid nodeId)
        {
            _selectedNodeId = nodeId;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void SelectConnection(Guid connectionId)
        {
            _selectedConnectionId = connectionId;
            _selectedNodeId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void ClearSelection()
        {
            _selectedNodeId = null;
            _selectedConnectionId = null;
            UpdateNodeSelectionVisuals();
            RenderConnections();
        }

        private void UpdateNodeSelectionVisuals()
        {
            Brush selectedBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED"));
            Brush defaultBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B9B9B9"));
            Brush selectedBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EDF5FF"));

            foreach (KeyValuePair<Guid, FrameworkElement> item in _nodeOutlines)
            {
                bool isSelected = _selectedNodeId == item.Key;
                // 矩形节点是 Border，判断节点是 Polygon；两种外形分别更新边框和填充。
                if (item.Value is Border border)
                {
                    border.BorderBrush = isSelected ? selectedBorderBrush : defaultBorderBrush;
                    border.Background = isSelected ? selectedBackground : Brushes.White;
                }
                else if (item.Value is Shape shape)
                {
                    shape.Stroke = isSelected ? selectedBorderBrush : defaultBorderBrush;
                    shape.Fill = isSelected ? selectedBackground : Brushes.White;
                }
            }
        }

        private void RenderConnections()
        {
            ConnectionsCanvas.Children.Clear();

            foreach (FlowchartConnectionModel connection in _connections)
            {
                if (!_nodesById.TryGetValue(connection.SourceNodeId, out FlowchartNodeModel? sourceNode) ||
                    !_nodesById.TryGetValue(connection.TargetNodeId, out FlowchartNodeModel? targetNode))
                {
                    continue;
                }

                Point startPoint = sourceNode.GetAnchorPoint(connection.SourceAnchor);
                Point endPoint = targetNode.GetAnchorPoint(connection.TargetAnchor);
                // 每次渲染都重新路由，这样拖动节点后折线会自动重新避开所有节点。
                IReadOnlyList<Point> route = CreateConnectionRoute(sourceNode, connection.SourceAnchor, targetNode, connection.TargetAnchor);

                if ((endPoint - startPoint).Length < 0.1 || route.Count < 2)
                {
                    continue;
                }

                bool isSelected = _selectedConnectionId == connection.Id;
                Brush lineBrush = isSelected
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));

                // 透明粗折线专门用来命中鼠标，避免细线很难点中。
                Polyline hitTarget = new Polyline
                {
                    Points = new PointCollection(route),
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 14,
                    StrokeLineJoin = PenLineJoin.Round,
                    Tag = connection,
                    Cursor = Cursors.Hand
                };
                hitTarget.MouseLeftButtonDown += ConnectionElement_MouseLeftButtonDown;

                // 可见折线不参与命中，点击交给上面的透明粗折线处理。
                Polyline visibleLine = new Polyline
                {
                    Points = new PointCollection(route),
                    Stroke = lineBrush,
                    StrokeThickness = isSelected ? 3 : 2,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false
                };

                Polygon arrow = new Polygon
                {
                    Fill = lineBrush,
                    // 箭头方向取折线最后一段，避免拐弯后的箭头仍按首尾直线方向画。
                    Points = CreateArrowHead(route),
                    Tag = connection,
                    Cursor = Cursors.Hand
                };
                arrow.MouseLeftButtonDown += ConnectionElement_MouseLeftButtonDown;

                Canvas.SetZIndex(hitTarget, 1);
                Canvas.SetZIndex(visibleLine, 2);
                Canvas.SetZIndex(arrow, 3);

                ConnectionsCanvas.Children.Add(hitTarget);
                ConnectionsCanvas.Children.Add(visibleLine);
                ConnectionsCanvas.Children.Add(arrow);
            }
        }

        private void ConnectionElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not FlowchartConnectionModel connection)
            {
                return;
            }

            Focus();
            SelectConnection(connection.Id);
            e.Handled = true;
        }

        private void ShowPreviewConnection(Point startPoint, Point endPoint)
        {
            ClearPreviewConnection();

            _previewLine = new Polyline
            {
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A7A7A")),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(new[] { 5d, 4d }),
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };

            _previewArrow = new Polygon
            {
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7A7A7A")),
                IsHitTestVisible = false
            };

            ConnectionsCanvas.Children.Add(_previewLine);
            ConnectionsCanvas.Children.Add(_previewArrow);
            UpdatePreviewConnection(startPoint, endPoint);
        }

        private void UpdatePreviewConnection(Point startPoint, Point endPoint)
        {
            if (_previewLine is null || _previewArrow is null)
            {
                return;
            }

            // 预览线和正式连线使用同一个路由器，只是终点是当前鼠标位置。
            IReadOnlyList<Point> route = CreatePreviewRoute(startPoint, endPoint);
            _previewLine.Points = new PointCollection(route);
            _previewArrow.Points = CreateArrowHead(route);
        }

        private void ClearPreviewConnection()
        {
            if (_previewLine is not null)
            {
                ConnectionsCanvas.Children.Remove(_previewLine);
                _previewLine = null;
            }

            if (_previewArrow is not null)
            {
                ConnectionsCanvas.Children.Remove(_previewArrow);
                _previewArrow = null;
            }
        }

        private PointCollection CreateArrowHead(IReadOnlyList<Point> route)
        {
            if (route.Count < 2)
            {
                return new PointCollection();
            }

            Point endPoint = route[^1];
            // 从后往前找最后一段非零长度的线段，作为箭头朝向。
            for (int i = route.Count - 2; i >= 0; i--)
            {
                if ((endPoint - route[i]).Length >= 0.1)
                {
                    return CreateArrowHead(route[i], endPoint);
                }
            }

            return new PointCollection();
        }

        private static PointCollection CreateArrowHead(Point startPoint, Point endPoint)
        {
            Vector direction = startPoint - endPoint;
            if (direction.Length < 0.1)
            {
                return new PointCollection();
            }

            direction.Normalize();
            Vector perpendicular = new Vector(-direction.Y, direction.X);
            const double arrowLength = 14;
            const double arrowWidth = 6;

            Point basePoint = endPoint + (direction * arrowLength);
            Point point1 = basePoint + (perpendicular * arrowWidth);
            Point point2 = basePoint - (perpendicular * arrowWidth);

            return new PointCollection(new[] { endPoint, point1, point2 });
        }

        private IReadOnlyList<Point> CreateConnectionRoute(
            FlowchartNodeModel sourceNode,
            FlowchartAnchor sourceAnchor,
            FlowchartNodeModel targetNode,
            FlowchartAnchor targetAnchor)
        {
            // 路由器只关心几何信息：起终点、锚点方向、所有节点矩形和工作区范围。
            return FlowchartOrthogonalRouter.Route(
                sourceNode.GetAnchorPoint(sourceAnchor),
                sourceAnchor,
                targetNode.GetAnchorPoint(targetAnchor),
                targetAnchor,
                GetNodeBounds(),
                GetWorkspaceBounds(),
                ConnectionClearance);
        }

        private IReadOnlyList<Point> CreatePreviewRoute(Point startPoint, Point endPoint)
        {
            // 预览时没有目标锚点，路由器会根据鼠标相对起点的位置推断一个进入方向。
            return FlowchartOrthogonalRouter.RouteToPoint(
                startPoint,
                _connectionSourceAnchor,
                endPoint,
                GetNodeBounds(),
                GetWorkspaceBounds(),
                ConnectionClearance);
        }

        private IEnumerable<Rect> GetNodeBounds()
        {
            return _nodes.Select(node => node.GetBounds());
        }

        private static Rect GetWorkspaceBounds()
        {
            return new Rect(0, 0, WorkspaceSize, WorkspaceSize);
        }

        private static FlowchartNodeKind ResolveNodeKind(string text)
        {
            // 兼容旧的拖拽数据：如果只传了文本，也能把“判断”识别成菱形节点。
            return text.Trim() switch
            {
                "\u5f00\u59cb" => FlowchartNodeKind.Start,
                "\u5224\u65ad" => FlowchartNodeKind.Decision,
                "\u7ed3\u675f" => FlowchartNodeKind.End,
                _ => FlowchartNodeKind.Process
            };
        }

        private void UpdateNodeVisualPosition(FlowchartNodeModel node)
        {
            if (!_nodeVisuals.TryGetValue(node.Id, out FrameworkElement? nodeVisual))
            {
                return;
            }

            Canvas.SetLeft(nodeVisual, node.X);
            Canvas.SetTop(nodeVisual, node.Y);
        }

        private Point ViewportToWorld(Point viewportPoint)
        {
            double scale = WorkspaceScaleTransform.ScaleX;
            return new Point(
                (viewportPoint.X - WorkspaceTranslateTransform.X) / scale,
                (viewportPoint.Y - WorkspaceTranslateTransform.Y) / scale);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        private sealed class AnchorHandleInfo
        {
            public AnchorHandleInfo(Guid nodeId, FlowchartAnchor anchor)
            {
                NodeId = nodeId;
                Anchor = anchor;
            }

            public Guid NodeId { get; }
            public FlowchartAnchor Anchor { get; }
        }
    }
}
