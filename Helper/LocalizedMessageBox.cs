using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace CMDevicesManager.Helper
{
    public static class LocalizedMessageBox
    {
        public static MessageBoxResult Show(string messageKey, string titleKey = null, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        {
            string message = GetLocalizedString(messageKey) ?? messageKey;
            string title = string.IsNullOrEmpty(titleKey) ? "" : (GetLocalizedString(titleKey) ?? titleKey);
            
            return MessageBox.Show(message, title, button, image);
        }

        public static MessageBoxResult Show(string message, string titleKey, MessageBoxButton button, MessageBoxImage image, bool useDirectMessage)
        {
            if (useDirectMessage)
            {
                string title = string.IsNullOrEmpty(titleKey) ? "" : (GetLocalizedString(titleKey) ?? titleKey);
                return MessageBox.Show(message, title, button, image);
            }
            else
            {
                return Show(message, titleKey, button, image);
            }
        }

        private static string GetLocalizedString(string key)
        {
            try
            {
                return Application.Current.FindResource(key)?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}