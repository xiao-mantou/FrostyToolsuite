using Frosty.Controls;
using Frosty.Core.IO;
using Frosty.Core.Mod;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using FrostySdk;
using FrostySdk.Managers;

namespace FrostyEditor.Windows
{
    public partial class ModTrimWindow : FrostyDockableWindow
    {
        private FrostyMod source;
        private readonly ObservableCollection<TreeNode> roots = new ObservableCollection<TreeNode>();
        private readonly List<BaseModResource> selected = new List<BaseModResource>();
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
            source = candidate; roots.Clear(); selected.Clear();
            foreach (BaseModResource resource in source.Resources) AddResource(resource);
            sourceText.Text = source.Filename; statusText.Text = source.Resources.Count() + " resources loaded";
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetKeep(true); }
        private void Clear_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetKeep(false, true); }
        private void SelectAllChunks_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetChunks(true); }
        private void ClearChunks_Click(object sender, RoutedEventArgs e) { foreach (TreeNode node in roots) node.SetChunks(false); }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (source == null) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "Frosty Mod (*.fbmod)|*.fbmod", FileName = Path.GetFileNameWithoutExtension(source.Filename) + "_trimmed.fbmod" };
            if (dialog.ShowDialog() != true) return;
            selected.Clear(); foreach (TreeNode root in roots) root.Collect(selected);
            if (!fastMode)
                AddEbxDependencies(selected);
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
            current.Keep = IsRequired(resource) || resource.Type == ModResourceType.Chunk;
        }

        private static bool IsRequired(BaseModResource resource)
        {
            return resource.Type == ModResourceType.Embedded || resource.Type == ModResourceType.Bundle;
        }

        private void AddEbxDependencies(List<BaseModResource> output)
        {
            if (App.AssetManager == null)
                return;

            Dictionary<string, BaseModResource> modResources = source.Resources
                .Where(resource => !string.IsNullOrEmpty(resource.Name))
                .GroupBy(resource => resource.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), System.StringComparer.OrdinalIgnoreCase);

            Queue<AssetEntry> pending = new Queue<AssetEntry>();
            HashSet<AssetEntry> visited = new HashSet<AssetEntry>();
            foreach (BaseModResource resource in output.Where(resource => resource.Type == ModResourceType.Ebx))
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(resource.Name);
                if (entry != null)
                    pending.Enqueue(entry);
            }

            while (pending.Count != 0)
            {
                AssetEntry entry = pending.Dequeue();
                EbxAssetEntry ebxEntry = entry as EbxAssetEntry;
                if (!visited.Add(entry))
                    continue;

                if (ebxEntry != null)
                {
                    foreach (Guid dependencyGuid in ebxEntry.EnumerateDependencies())
                    {
                        EbxAssetEntry dependency = App.AssetManager.GetEbxEntry(dependencyGuid);
                        if (dependency != null) pending.Enqueue(dependency);
                    }
                }

                foreach (AssetEntry linkedEntry in entry.LinkedAssets)
                {
                    if (linkedEntry == null)
                        continue;
                    if (modResources.TryGetValue(linkedEntry.Name, out BaseModResource linkedResource) && !output.Contains(linkedResource))
                        output.Add(linkedResource);
                    pending.Enqueue(linkedEntry);
                }
            }
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
            public void SetChunks(bool value) { if (Resources.Any(resource => resource.Type == ModResourceType.Chunk)) { keep = value; OnPropertyChanged("Keep"); } foreach (TreeNode child in Children) child.SetChunks(value); }
            public void Collect(List<BaseModResource> output) { output.AddRange(Resources.Where(resource => Keep || IsRequired(resource))); foreach (TreeNode child in Children) child.Collect(output); }
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        }
    }
}
