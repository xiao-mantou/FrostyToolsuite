using Frosty.Controls;
using Frosty.Core.IO;
using Frosty.Core.Mod;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace FrostyEditor.Windows
{
    public partial class ModTrimWindow : FrostyDockableWindow
    {
        private FrostyMod source;
        private readonly ObservableCollection<Item> items = new ObservableCollection<Item>();
        public ModTrimWindow() { InitializeComponent(); resourceList.ItemsSource = items; }
        public ModTrimWindow(string filename) : this() { LoadMod(filename); }
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
            source = candidate; items.Clear(); foreach (BaseModResource resource in source.Resources) items.Add(new Item(resource));
            sourceText.Text = source.Filename; statusText.Text = items.Count + " resources loaded";
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (Item item in items) item.Keep = true; resourceList.Items.Refresh(); }
        private void Clear_Click(object sender, RoutedEventArgs e) { foreach (Item item in items) item.Keep = item.Resource.Type == ModResourceType.Embedded || item.Resource.Type == ModResourceType.Chunk; resourceList.Items.Refresh(); }
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (source == null) return;
            SaveFileDialog dialog = new SaveFileDialog { Filter = "Frosty Mod (*.fbmod)|*.fbmod", FileName = Path.GetFileNameWithoutExtension(source.Filename) + "_trimmed.fbmod" };
            if (dialog.ShowDialog() != true) return;
            using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write)) FrostyModWriter.WriteSelected(source, stream, items.Where(item => item.Keep).Select(item => item.Resource), CancellationToken.None);
            statusText.Text = "Saved: " + dialog.FileName;
        }
        private sealed class Item
        {
            public BaseModResource Resource { get; }
            public bool Keep { get; set; }
            public string Type => Resource.Type.ToString();
            public string Name => Resource.Name ?? "(embedded)";
            public Item(BaseModResource resource) { Resource = resource; Keep = resource.Type == ModResourceType.Embedded || resource.Type == ModResourceType.Chunk; }
        }
    }
}
