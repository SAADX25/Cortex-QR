namespace CortexQR.Services
{
    public interface IFileDialogService
    {
        string? OpenFile(string filter, string title);
        string? OpenFolder(string description);
    }
}
