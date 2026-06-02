namespace CortexQR.Services
{
    public interface IMessageDialogService
    {
        void ShowInfo(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
    }
}
