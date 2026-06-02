using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace CortexQR.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string? OpenFile(string filter, string title)
        {
            var dlg = new OpenFileDialog
            {
                Filter = filter,
                Title = title,
                CheckFileExists = true,
                CheckPathExists = true
            };

            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string? OpenFolder(string description)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description = description,
                ShowNewFolderButton = true
            };

            return dlg.ShowDialog() == WinForms.DialogResult.OK ? dlg.SelectedPath : null;
        }
    }
}
