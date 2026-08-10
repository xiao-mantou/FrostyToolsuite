using Frosty.Controls;
using Frosty.Core.IO;
using Frosty.Core.Mod;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using FrostySdk.Managers;

namespace FrostyEditor.Windows
{
    public partial class ModTrimWindow : FrostyDockableWindow
    {
        private FrostyMod source;
        private readonly ObservableCollection<TreeNode> roots = new ObservableCollection<TreeNode>();
        private readonly List<BaseModResource> selected = new List<BaseModResource>();
        private readonly Dictionary<string, TreeNode> resourceNodes = new Dictionary<string, TreeNode>(System.StringComparer.OrdinalIgnoreCase);
        private readonly bool fastMode;
        public ModTrimWindow() { InitializeComponent(); resourceTree.ItemsSource = roots; }
        public ModTrimWindow(string filename) : this() { fastMode = true; LoadMod(filename); }
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Frosty Mod (*.fbmod)|*.fbmod" };
            if (dialog.ShowDialog() != true) return;
            LoadMod(dialog.FileName);
        }
        private void LoadMod(string filename)
        {
            FrostyMod candidate = new FrostyMod(filename, true);
            if (!candidate.NewFormat) { FrostyMessageBox.Show("Only standard Frosty binary Mods are supported.", "Trim Mod"); return; }
            source = candidate; roots.Clear(); selected.Clear(); resourceNodes.Clear();
            foreach (BaseModResource resource in source.Resources) AddResource(resource);
            if (!fastMode)
                ApplyEbxDependencies();
            sourceText.Text = source.Filename; statusText.Text = source.Resources.Count() + " resources loaded";
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetKeep(true); }
        private void Clear_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetKeep(false, true); }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (source == null) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "Frosty Mod (*.fbmod)|*.fbmod", FileName = Path.GetFileNameWithoutExtension(source.Filename) + "_trimmed.fbmod" };
            if (dialog.ShowDialog() != true) return;
            selected.Clear(); foreach (TreeNode root in roots) root.Collect(selected);
            using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write)) FrostyModWriter.WriteSelected(source, stream, selected, CancellationToken.None);
            statusText.Text = "Saved: " + dialog.FileName;
        }

        private void AddResource(BaseModResource resource)
        {
            string path = resource.Name ?? (resource.Type == ModResourceType.Chunk ? "Chunks" : "Metadata");
            string[] parts = path.Replace('\\', '/').Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { "Metadata" };
            ObservableCollection<TreeNode> children = roots;
            TreeNode current = null;
            foreach (string part in parts)
            {
                current = children.FirstOrDefault(node => node.DisplayName == part);
                if (current == null) { current = new TreeNode(part); children.Add(current); }
                children = current.Children;
            }
            current.Resources.Add(resource);
            current.Keep = IsRequired(resource);
            resourceNodes[resource.Name ?? resource.Type + ":" + resourceNodes.Count] = current;
        }

        private void ApplyEbxDependencies()
        {
            if (App.AssetManager == null)
                return;
            Queue<EbxAssetEntry> queue = new Queue<EbxAssetEntry>();
            HashSet<Guid> visited = new HashSet<Guid>();
            foreach (BaseModResource resource in source.Resources.Where(item => item.Type == ModResourceType.Ebx))
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(resource.Name);
                if (entry != null) queue.Enqueue(entry);
            }
            while (queue.Count != 0)
            {
                EbxAssetEntry entry = queue.Dequeue();
                if (!visited.Add(entry.Guid)) continue;
                foreach (Guid dependency in entry.EnumerateDependencies())
                {
                    EbxAssetEntry dependencyEntry = App.AssetManager.GetEbxEntry(dependency);
                    if (dependencyEntry == null) continue;
                    if (resourceNodes.TryGetValue(dependencyEntry.Name, out TreeNode node)) node.SetKeep(true);
                    queue.Enqueue(dependencyEntry);
                }
            }
        }

        private static bool IsRequired(BaseModResource resource)
        {
            return resource.Type == ModResourceType.Embedded || resource.Type == ModResourceType.Bundle || resource.Type == ModResourceType.Chunk;
        }

        public sealed class TreeNode : INotifyPropertyChanged
        {
            public string DisplayName { get; }
            public ObservableCollection<TreeNode> Children { get; } = new ObservableCollection<TreeNode>();
            internal List<BaseModResource> Resources { get; } = new List<BaseModResource>();
            private bool keep;
            public bool Keep { get => keep; set { if (keep == value) return; keep = value; OnPropertyChanged("Keep"); foreach (TreeNode child in Children) child.SetKeep(value); } }
            public TreeNode(string name) { DisplayName = name; }
            public void SetKeep(bool value, bool preserveRequired = false) { keep = value || (preserveRequired && Resources.Any(IsRequired)); OnPropertyChanged("Keep"); foreach (TreeNode child in Children) child.SetKeep(value, preserveRequired); }
            public void Collect(List<BaseModResource> output) { output.AddRange(Resources.Where(resource => Keep || IsRequired(resource))); foreach (TreeNode child in Children) child.Collect(output); }
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        }
    }
}
