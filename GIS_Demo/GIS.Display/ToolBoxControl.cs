using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GIS.Display.UI;

namespace GIS.Display
{
    /// <summary>
    /// 工具箱里的一个工具（树的叶子节点）：所属工具箱 + 工具名 + 打开工具界面的工厂方法。
    /// 界面从 <see cref="ToolFormBase"/> 派生（参考模板工具 <see cref="TemplateToolForm"/>）。
    /// </summary>
    public sealed class ToolDescriptor
    {
        public ToolDescriptor(string category, string name, Func<MapControl, Form> createForm, string description)
        {
            Category = category == null ? "" : category;
            Name = name == null ? "" : name;
            CreateForm = createForm;
            Description = description == null ? "" : description;
        }

        /// <summary>所属工具箱（树的顶层节点）。</summary>
        public string Category { get; }
        /// <summary>工具名（叶子节点）。</summary>
        public string Name { get; }
        /// <summary>工具说明（鼠标悬停提示）。</summary>
        public string Description { get; }
        /// <summary>创建工具界面；参数是当前绑定的地图（可能为 null，工具需自行兜底）。</summary>
        public Func<MapControl, Form> CreateForm { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// 工具箱面板（窗口右侧）：树状结构，**文件夹 = 工具箱、叶子 = 工具**，
    /// 点工具箱前的 +/- 即可展开/收起，后续同学开发的工具挂到对应工具箱下面。
    ///
    /// 默认内容：叠加分析工具箱、邻近分析工具箱、数据管理工具箱（占位，待添加工具）
    ///          + 模板工具箱 → 模板工具（点击打开工具界面模板）。
    ///
    /// 后续加工具只要两步（不需要改本类以外的地方）：
    ///   1) 从 <see cref="ToolFormBase"/> 派生工具界面（照抄 TemplateToolForm 的骨架）；
    ///   2) 在 <see cref="BuildDefaultTools"/> 里加一行：
    ///      AddTool(OverlayCategory, "相交（Intersect）", map => new IntersectToolForm(map), "说明");
    /// </summary>
    public sealed class ToolBoxControl : UserControl
    {
        /// <summary>叠加分析工具箱。</summary>
        public const string OverlayCategory = "叠加分析工具箱";
        /// <summary>邻近分析工具箱。</summary>
        public const string ProximityCategory = "邻近分析工具箱";
        /// <summary>数据管理工具箱。</summary>
        public const string DataManagementCategory = "数据管理工具箱";
        /// <summary>模板工具箱（放工具界面模板）。</summary>
        public const string TemplateCategory = "模板工具箱";
        /// <summary>模板工具名。</summary>
        public const string TemplateToolName = "模板工具";

        private const string PlaceholderText = "（暂无工具）";
        private const string PlaceholderTag = "__placeholder__";

        private readonly TreeView tree = new TreeView
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            HideSelection = false,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            ShowNodeToolTips = true,
            FullRowSelect = false,
            ItemHeight = 24,
            Font = new Font("Microsoft YaHei UI", 10F)
        };
        private readonly List<ToolDescriptor> tools = new List<ToolDescriptor>();
        private readonly List<string> categories = new List<string>();
        private bool opening;

        /// <summary>当前绑定的地图（工具界面用它取图层与已选要素）。</summary>
        public MapControl Map { get; private set; }

        /// <summary>叶子节点被激活（左键单击 / 回车）时触发，参数是该工具。</summary>
        public event Action<ToolDescriptor> ToolRequested;

        /// <summary>树控件本身（自动检查用）。</summary>
        public TreeView Tree
        {
            get { return tree; }
        }

        /// <summary>已注册的工具（按添加顺序）。</summary>
        public IReadOnlyList<ToolDescriptor> Tools
        {
            get { return tools.AsReadOnly(); }
        }

        /// <summary>工具箱（顶层节点）名称，按添加顺序。</summary>
        public IReadOnlyList<string> Categories
        {
            get { return categories.AsReadOnly(); }
        }

        public ToolBoxControl()
        {
            var title = new Label
            {
                Text = "工具箱",
                Dock = DockStyle.Top,
                Height = 36,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold)
            };
            Controls.Add(tree);
            Controls.Add(title);
            tree.NodeMouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ActivateNode(e.Node);
            };
            tree.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
                ActivateNode(tree.SelectedNode);
                e.Handled = true;
            };
            BuildDefaultTools();
        }

        /// <summary>绑定地图（工具界面里的“输入要素 / 其他图层”取自它）。</summary>
        public void Bind(MapControl map)
        {
            Map = map;
        }

        // 默认的工具箱与工具。
        // 三个分析工具箱先占位；模板工具箱里放工具界面模板（后续同学照着它写自己的工具界面）。
        private void BuildDefaultTools()
        {
            AddCategory(OverlayCategory);
            AddCategory(ProximityCategory);
            AddCategory(DataManagementCategory);
            AddCategory(TemplateCategory);
            AddTool(TemplateCategory, TemplateToolName, map => new TemplateToolForm(map),
                "工具界面模板：输入要素 → 其他图层 → 必要参数（手动输入 / 选择）→ 框选参数 → 输出图层名 → 运行");
        }

        /// <summary>添加一个工具箱（已存在时直接返回）。空工具箱里放一行“（暂无工具）”占位，便于展开/收起。</summary>
        public TreeNode AddCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            TreeNode existing = FindCategoryNode(name);
            if (existing != null) return existing;
            var node = new TreeNode(name);
            node.Nodes.Add(new TreeNode(PlaceholderText)
            {
                Tag = PlaceholderTag,
                ForeColor = Color.FromArgb(150, 150, 150)
            });
            tree.Nodes.Add(node);
            categories.Add(name);
            return node;
        }

        /// <summary>往指定工具箱里添加一个工具（叶子）；工具箱不存在时会自动创建。</summary>
        public ToolDescriptor AddTool(string category, string name, Func<MapControl, Form> createForm, string description = "")
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            var tool = new ToolDescriptor(category, name, createForm, description);
            TreeNode categoryNode = AddCategory(category);
            for (int i = categoryNode.Nodes.Count - 1; i >= 0; i--)
            {
                if ((categoryNode.Nodes[i].Tag as string) == PlaceholderTag) categoryNode.Nodes.RemoveAt(i);
            }
            categoryNode.Nodes.Add(new TreeNode(name) { Tag = tool });
            categoryNode.Expand();
            tools.Add(tool);
            return tool;
        }

        /// <summary>按名称找工具箱节点；没有返回 null。</summary>
        public TreeNode FindCategoryNode(string name)
        {
            foreach (TreeNode node in tree.Nodes)
                if (node.Text == name) return node;
            return null;
        }

        /// <summary>按“工具箱 / 工具”找叶子节点；没有返回 null。</summary>
        public TreeNode FindToolNode(string category, string toolName)
        {
            TreeNode categoryNode = FindCategoryNode(category);
            if (categoryNode == null) return null;
            foreach (TreeNode node in categoryNode.Nodes)
                if (node.Tag is ToolDescriptor descriptor && descriptor.Name == toolName) return node;
            return null;
        }

        /// <summary>
        /// 激活一个节点：工具箱节点只展开/收起；工具节点则创建并弹出工具界面。
        /// 供点击与自动检查共用。
        /// </summary>
        public void ActivateNode(TreeNode node)
        {
            if (node == null) return;
            var tool = node.Tag as ToolDescriptor;
            if (tool == null)
            {
                node.Toggle();
                return;
            }
            tree.SelectedNode = node;
            if (ToolRequested != null) ToolRequested(tool);
            OpenTool(tool);
        }

        /// <summary>创建工具界面但不显示（自动检查用；界面创建失败时返回 null）。</summary>
        public Form CreateToolForm(ToolDescriptor tool)
        {
            if (tool == null || tool.CreateForm == null) return null;
            return tool.CreateForm(Map);
        }

        private void OpenTool(ToolDescriptor tool)
        {
            if (opening) return;
            Form form = CreateToolForm(tool);
            if (form == null) return;
            opening = true;
            try
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                Form owner = FindForm();
                if (owner != null) form.ShowDialog(owner);
                else form.ShowDialog();
            }
            finally
            {
                opening = false;
                form.Dispose();
            }
        }
    }
}
