using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using TMUTarangConnect.Services;

namespace TMUTarangConnect.Views;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;
    private readonly Action? _onCardClick;

    public ToastWindow(string title, string message, string iconFile, Action? onCardClick = null)
    {
        InitializeComponent();
        _onCardClick = onCardClick;

        TxtTitle.Text = title;
        TxtMessage.Text = message;

        // Position at bottom-right corner of work area
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Bottom - Height - 16;

        // Color & Icon
        ApplyStyle(iconFile);

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.8) };
        _closeTimer.Tick += (s, e) =>
        {
            _closeTimer.Stop();
            Dismiss();
        };

        Loaded += (s, e) =>
        {
            var anim = (Storyboard)Resources["ShowAnim"];
            anim.Begin(this);
            _closeTimer.Start();
        };
    }

    private void ApplyStyle(string iconFile)
    {
        if (iconFile.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            AccentBar.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#EF4444"));
        }
        else if (iconFile.Contains("info", StringComparison.OrdinalIgnoreCase) || iconFile.Contains("disconnect", StringComparison.OrdinalIgnoreCase))
        {
            AccentBar.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#F59E0B"));
        }
        else
        {
            AccentBar.Background = new MediaBrush((MediaColor)MediaColorConverter.ConvertFromString("#10B981"));
        }

        try
        {
            var bmp = ResourceHelper.LoadBitmap(iconFile);
            if (bmp != null)
            {
                ImgIcon.Source = bmp;
            }
        }
        catch { }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _closeTimer.Stop();
        Dismiss();
    }

    private void OnCardClicked(object sender, MouseButtonEventArgs e)
    {
        _closeTimer.Stop();
        Dismiss();
        _onCardClick?.Invoke();
    }

    private void Dismiss()
    {
        var anim = (Storyboard)Resources["HideAnim"];
        anim.Completed += (s, e) => Close();
        anim.Begin(this);
    }
}
