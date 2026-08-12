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
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;

namespace FrostyEditor.Windows
{
    public partial class ModTrimWindow : FrostyDockableWindow
    {
        private FrostyMod source;
        private readonly ObservableCollection<TreeNode> roots = new ObservableCollection<TreeNode>();
        private readonly List<BaseModResource> selected = new List<BaseModResource>();
        private readonly bool fastMode;
        private CancellationTokenSource scanCancellation;
        public ModTrimWindow() { InitializeComponent(); resourceTree.ItemsSource = roots; }
        public ModTrimWindow(string filename) : this() { fastMode = true; _ = LoadModAsync(filename); }
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Frosty Mod (*.fbmod)|*.fbmod" };
            if (dialog.ShowDialog() != true) return;
            _ = LoadModAsync(dialog.FileName);
        }
        private async Task LoadModAsync(string filename)
        {
            scanCancellation?.Cancel();
            scanCancellation = new CancellationTokenSource();
            CancellationToken token = scanCancellation.Token;
            FrostyMod candidate = new FrostyMod(filename, true);
            if (!candidate.NewFormat) { FrostyMessageBox.Show("Only standard Frosty binary Mods are supported.", "Trim Mod"); return; }
            source = candidate; roots.Clear(); selected.Clear();
            foreach (BaseModResource resource in source.Resources) AddResource(resource);
            sourceText.Text = source.Filename; statusText.Text = source.Resources.Count() + " resources loaded";
            if (!fastMode)
            {
                try { await AnalyzeDependenciesAsync(token); }
                catch (OperationCanceledException) { statusText.Text = "Dependency scan cancelled"; }
            }
        }
        private async Task AnalyzeDependenciesAsync(CancellationToken token)
        {
            BaseModResource[] resources = source.Resources.ToArray();
            Dictionary<Guid, TreeNode> modChunks = resources
                .Where(resource => resource.Type == ModResourceType.Chunk && Guid.TryParse(resource.Name, out _))
                .ToDictionary(resource => Guid.Parse(resource.Name), FindNode);
            int matched = 0;
            int chunkTotal = modChunks.Count;
            int dataRead = 0;
            int dataMissing = 0;
            int decompressed = 0;
            Dictionary<string, int> noMatchByType = new Dictionary<string, int>();
            Dictionary<string, List<string>> noMatchExamples = new Dictionary<string, List<string>>();
            scanProgress.Visibility = Visibility.Visible; cancelScanButton.Visibility = Visibility.Visible;
            scanProgress.Value = 0; statusText.Text = "Scanning dependencies...";
            int done = 0;
            foreach (BaseModResource resource in resources)
            {
                token.ThrowIfCancellationRequested();
                if (resource.Type == ModResourceType.Ebx || resource.Type == ModResourceType.Res)
                {
                    byte[] rawData = source.GetResourceData(resource);
                    if (rawData == null || rawData.Length == 0)
                    {
                        dataMissing++;
                        AddScanIssue(noMatchByType, noMatchExamples, "no data", resource.Name);
                        done++;
                        scanProgress.Value = done * 100.0 / resources.Length;
                        statusText.Text = "Scanning dependencies " + done + "/" + resources.Length;
                        continue;
                    }
                    dataRead++;
                    bool expanded;
                    byte[] data = ExpandModData(rawData, out expanded);
                    if (expanded) decompressed++;
                    HashSet<Guid> found = await Task.Run(() => FindChunkGuids(data, modChunks.Keys), token);
                    bool usedGameData = false;
                    if (found.Count == 0 && !fastMode && App.AssetManager != null)
                    {
                        HashSet<Guid> gameFound = await Task.Run(() => FindGameChunkGuids(resource, modChunks.Keys), token);
                        usedGameData = true;
                        found = gameFound;
                    }
                    if (found.Count == 0)
                        AddScanIssue(noMatchByType, noMatchExamples, usedGameData ? "game data has no matching mod CHK" : "mod data has no matching mod CHK", resource.Name);
                    foreach (Guid id in found)
                    {
                        modChunks[id].SetMatch(true);
                        matched++;
                    }
                }
                done++;
                scanProgress.Value = done * 100.0 / resources.Length;
                statusText.Text = "Scanning dependencies " + done + "/" + resources.Length;
                await Task.Delay(1);
            }
            scanProgress.Visibility = Visibility.Collapsed; cancelScanButton.Visibility = Visibility.Collapsed;
            int unmatched = chunkTotal - modChunks.Values.Count(node => node.IsMatched);
            string unmatchedChunks = string.Join(", ", resources
                .Where(resource => resource.Type == ModResourceType.Chunk
                    && Guid.TryParse(resource.Name, out Guid id)
                    && !modChunks[id].IsMatched)
                .Select(resource => resource.Name));
            statusText.Text = "Dependency scan complete. Bound CHK: " + matched;
            string summary = "Dependency scan complete\n"
                + "EBX/RES: " + (dataRead + dataMissing) + "\n"
                + "Data read: " + dataRead + "\n"
                + "Decompressed: " + decompressed + "\n"
                + "CHK total: " + chunkTotal + "\n"
                + "Bound CHK: " + matched + "\n"
                + "Unmatched CHK: " + unmatched;
            if (unmatched != 0)
                summary += "\nUnmatched CHK GUID: " + unmatchedChunks;
            foreach (KeyValuePair<string, int> issue in noMatchByType)
            {
                summary += "\n\n" + issue.Key + ": " + issue.Value;
                List<string> examples = noMatchExamples[issue.Key];
                if (examples.Count != 0) summary += "\nExamples: " + string.Join(", ", examples);
            }
            reportText.Text = summary;
            reportText.Visibility = Visibility.Visible;
        }

        private static void AddScanIssue(Dictionary<string, int> counts, Dictionary<string, List<string>> examples, string reason, string name)
        {
            counts[reason] = counts.ContainsKey(reason) ? counts[reason] + 1 : 1;
            if (!examples.ContainsKey(reason)) examples[reason] = new List<string>();
            if (examples[reason].Count < 5 && !string.IsNullOrEmpty(name)) examples[reason].Add(name);
        }

        private HashSet<Guid> FindGameChunkGuids(BaseModResource resource, IEnumerable<Guid> chunkIds)
        {
            AssetEntry entry;
            if (resource.Type == ModResourceType.Ebx)
                entry = App.AssetManager.GetEbxEntry(resource.Name);
            else
                entry = App.AssetManager.GetResEntry(resource.Name);
            if (entry == null) return new HashSet<Guid>();
            byte[] data = null;
            using (MemoryStream memory = new MemoryStream())
            {
                Stream stream = resource.Type == ModResourceType.Ebx
                    ? App.AssetManager.GetEbxStream((EbxAssetEntry)entry)
                    : App.AssetManager.GetRes((ResAssetEntry)entry);
                using (stream)
                    stream.CopyTo(memory);
                data = memory.ToArray();
            }
            return FindChunkGuids(data, chunkIds);
        }

        private static HashSet<Guid> FindChunkGuids(byte[] data, IEnumerable<Guid> chunkIds)
        {
            HashSet<Guid> result = new HashSet<Guid>();
            if (data == null) return result;
            Dictionary<string, Guid> lookup = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (Guid id in chunkIds)
            {
                byte[] bytes = id.ToByteArray();
                lookup[Convert.ToBase64String(bytes)] = id;
                lookup[Convert.ToBase64String(bytes.Reverse().ToArray())] = id;
            }
            for (int i = 0; i <= data.Length - 16; i++)
            {
                byte[] candidate = new byte[16];
                Buffer.BlockCopy(data, i, candidate, 0, 16);
                if (lookup.TryGetValue(Convert.ToBase64String(candidate), out Guid id)) result.Add(id);
            }
            return result;
        }

        private static byte[] ExpandModData(byte[] data, out bool expanded)
        {
            expanded = false;
            if (data == null || data.Length < 8)
                return data;
            try
            {
                using (CasReader reader = new CasReader(new MemoryStream(data)))
                {
                    byte[] result = reader.Read();
                    if (result != null && result.Length != 0)
                    {
                        expanded = true;
                        return result;
                    }
                }
            }
            catch
            {
            }
            return data;
        }
        private void CancelScanButton_Click(object sender, RoutedEventArgs e)
        {
            scanCancellation?.Cancel();
            scanProgress.Visibility = Visibility.Collapsed; cancelScanButton.Visibility = Visibility.Collapsed;
            statusText.Text = "Dependency scan cancelled";
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
            // Keep RES while dependency mapping is being validated. Removing an RES
            // without a verified EBX -> RES mapping can produce a loadable but broken Mod.
            foreach (BaseModResource resource in source.Resources.Where(resource => resource.Type == ModResourceType.Res && !selected.Contains(resource)))
                selected.Add(resource);
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

        private TreeNode FindNode(BaseModResource resource)
        {
            foreach (TreeNode root in roots)
            {
                TreeNode found = root.Find(resource);
                if (found != null) return found;
            }
            return null;
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
            public string TypeLabel => Resources.Count == 0 ? "" : "[" + Resources[0].Type.ToString().ToUpperInvariant() + "]";
            public Brush MatchBrush { get; private set; } = Brushes.Black;
            public bool IsMatched { get; private set; }
            public ObservableCollection<TreeNode> Children { get; } = new ObservableCollection<TreeNode>();
            internal List<BaseModResource> Resources { get; } = new List<BaseModResource>();
            private bool keep;
            public bool Keep { get => keep; set { if (keep == value) return; keep = value; OnPropertyChanged("Keep"); foreach (TreeNode child in Children) child.SetKeep(value); } }
            public TreeNode(string name) { DisplayName = name; }
            public void SetKeep(bool value, bool preserveRequired = false) { keep = value || (preserveRequired && Resources.Any(IsRequired)); OnPropertyChanged("Keep"); foreach (TreeNode child in Children) child.SetKeep(value, preserveRequired); }
            public void SetChunks(bool value) { if (Resources.Any(resource => resource.Type == ModResourceType.Chunk)) { keep = value; OnPropertyChanged("Keep"); } foreach (TreeNode child in Children) child.SetChunks(value); }
            public void SetMatch(bool found) { IsMatched = found; MatchBrush = found ? Brushes.ForestGreen : Brushes.Firebrick; OnPropertyChanged("MatchBrush"); }
            public TreeNode Find(BaseModResource resource) { if (Resources.Contains(resource)) return this; foreach (TreeNode child in Children) { TreeNode found = child.Find(resource); if (found != null) return found; } return null; }
            public void Collect(List<BaseModResource> output) { output.AddRange(Resources.Where(resource => Keep || IsRequired(resource))); foreach (TreeNode child in Children) child.Collect(output); }
            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        }
    }
}
