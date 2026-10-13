using System.Windows;
using System.Windows.Input;

namespace GodjiVpn.Views;

/// <summary>Общий модальный диалог для устройств подписки (переименование/подтверждение
/// удаления/предложение написать в поддержку) — см. комментарий в PromptDialog.xaml.
/// Заполнять свойства нужно ДО ShowDialog(); DialogResult=true означает "нажали основную
/// кнопку" (InputValue тогда содержит введённый текст, если ShowInput=true).</summary>
public partial class PromptDialog : Window
{
    public string PromptTitle
    {
        get => TitleBlock.Text;
        set { TitleBlock.Text = value; Title = value; }
    }

    public string? Message
    {
        get => MessageBlock.Text;
        set
        {
            MessageBlock.Text = value ?? "";
            MessageBlock.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public bool ShowInput
    {
        get => InputBox.Visibility == Visibility.Visible;
        set
        {
            var visibility = value ? Visibility.Visible : Visibility.Collapsed;
            InputBox.Visibility = visibility;
            InputLabelBlock.Visibility = visibility;
        }
    }

    public string? InputLabel
    {
        get => InputLabelBlock.Text;
        set => InputLabelBlock.Text = value ?? "";
    }

    public string InputValue
    {
        get => InputBox.Text;
        set => InputBox.Text = value;
    }

    public string PrimaryText
    {
        get => (string)PrimaryBtn.Content;
        set => PrimaryBtn.Content = value;
    }

    public bool IsPrimaryDanger
    {
        set => PrimaryBtn.Style = value
            ? (Style)Resources["DangerPillButton"]
            : (Style)Application.Current.Resources["InkPillButton"];
    }

    public PromptDialog()
    {
        InitializeComponent();
        ShowInput = true;
        Loaded += (_, _) =>
        {
            if (InputBox.Visibility == Visibility.Visible)
            {
                InputBox.Focus();
                InputBox.SelectAll();
            }
        };
        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) OnPrimaryClick(this, new RoutedEventArgs());
        };
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSecondaryClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
