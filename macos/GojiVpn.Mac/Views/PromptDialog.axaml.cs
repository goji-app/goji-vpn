using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GodjiVpn.Views;

/// <summary>Результат ShowDialog&lt;bool&gt;: true — нажата основная кнопка (InputValue — введённый текст).</summary>
public partial class PromptDialog : Window
{
    public PromptDialog()
    {
        InitializeComponent();
        ShowInput = true;
        PrimaryBtn.Click += (_, _) => Close(true);
        SecondaryBtn.Click += (_, _) => Close(false);
        InputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Close(true); };
        Opened += (_, _) =>
        {
            if (!InputBox.IsVisible) return;
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string PromptTitle
    {
        get => TitleBlock.Text ?? "";
        set { TitleBlock.Text = value; Title = value; }
    }

    public string? Message
    {
        get => MessageBlock.Text;
        set { MessageBlock.Text = value; MessageBlock.IsVisible = !string.IsNullOrEmpty(value); }
    }

    public bool ShowInput
    {
        get => InputBox.IsVisible;
        set { InputBox.IsVisible = value; InputLabelBlock.IsVisible = value; }
    }

    public string? InputLabel
    {
        get => InputLabelBlock.Text;
        set => InputLabelBlock.Text = value;
    }

    public string InputValue
    {
        get => InputBox.Text ?? "";
        set => InputBox.Text = value;
    }

    public string PrimaryText
    {
        get => PrimaryBtn.Content as string ?? "";
        set => PrimaryBtn.Content = value;
    }

    public bool IsPrimaryDanger
    {
        set
        {
            PrimaryBtn.Classes.Set("ink", !value);
            PrimaryBtn.Classes.Set("danger", value);
        }
    }
}
