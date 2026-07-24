using System.Windows;
using System.Windows.Media.Imaging;

namespace AstrBar.Views;

public partial class ImagePreviewWindow : Window
{
    public ImagePreviewWindow(string imagePath)
    {
        InitializeComponent();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
    }
}
