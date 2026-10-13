using System.Windows;
using System.Windows.Controls;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public sealed class OwnerDialog : Window
{
    public Response? Result { get; private set; }
    public OwnerDialog(Window owner, string title, string description, Request command, bool newPin = false, bool recovery = false)
    {
        SetResourceReference(StyleProperty, typeof(Window)); Owner = owner; Title = title; Width = 400; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
        var descriptionBlock = new TextBlock { Text = description, Margin = new Thickness(0, 12, 0, 18), TextWrapping = TextWrapping.Wrap };
        descriptionBlock.SetResourceReference(TextBlock.ForegroundProperty, "Dim"); panel.Children.Add(descriptionBlock);
        PasswordBox? pin = null, replacement = null, confirm = null;
        TextBox? code = null;
        if (command.Action != "Setup")
        {
            panel.Children.Add(new TextBlock { Text = recovery ? "Recovery code" : "Owner PIN", Margin = new Thickness(0, 0, 0, 7) });
            if (recovery) { code = new TextBox { MaxLength = 100 }; System.Windows.Automation.AutomationProperties.SetName(code, "Recovery code"); panel.Children.Add(code); }
            else { pin = new PasswordBox(); System.Windows.Automation.AutomationProperties.SetName(pin, "Owner PIN"); panel.Children.Add(pin); }
        }
        if (newPin)
        {
            panel.Children.Add(new TextBlock { Text = "New PIN (6–12 digits)", Margin = new Thickness(0, 16, 0, 7) });
            replacement = new PasswordBox(); System.Windows.Automation.AutomationProperties.SetName(replacement, "New owner PIN"); panel.Children.Add(replacement);
            panel.Children.Add(new TextBlock { Text = "Confirm PIN", Margin = new Thickness(0, 12, 0, 7) });
            confirm = new PasswordBox(); System.Windows.Automation.AutomationProperties.SetName(confirm, "Confirm new owner PIN"); panel.Children.Add(confirm);
        }
        var error = new TextBlock { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
        error.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); System.Windows.Automation.AutomationProperties.SetLiveSetting(error, System.Windows.Automation.AutomationLiveSetting.Polite); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var submit = new Button { Content = command.Action == "Skip" ? "Confirm skip" : "Confirm", IsDefault = true };
        submit.SetResourceReference(StyleProperty, "Primary"); buttons.Children.Add(cancel); buttons.Children.Add(submit); panel.Children.Add(buttons);
        Content = panel;
        submit.Click += async (_, _) =>
        {
            if (newPin && replacement!.Password != confirm!.Password) { error.Text = "The PINs do not match."; return; }
            submit.IsEnabled = false;
            try
            {
                var request = command with { Pin = pin?.Password, NewPin = replacement?.Password, RecoveryCode = code?.Text };
                Result = await App.Client.SendAsync(request);
                if (Result.Success) { DialogResult = true; }
                else error.Text = Result.Message;
            }
            catch (Exception) { error.Text = "The service is unavailable. No change was confirmed."; }
            finally { submit.IsEnabled = true; if (pin is not null) pin.Clear(); }
        };
        Closed += (_, _) => { pin?.Clear(); replacement?.Clear(); confirm?.Clear(); if (code is not null) code.Clear(); };
        Loaded += (_, _) => { if (pin is not null) pin.Focus(); else if (code is not null) code.Focus(); else replacement?.Focus(); };
    }
    public static void ShowRecovery(Window owner, string code)
    {
        var dialog = new Window { Owner = owner, Title = "Save your recovery code", Width = 460, SizeToContent = SizeToContent.Height, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Keep this somewhere private.", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "This code can reset the owner PIN. It is shown only once. A new code replaces any previous one.", Margin = new Thickness(0, 12, 0, 16), TextWrapping = TextWrapping.Wrap });
        var value = new TextBox { Text = code, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(value);
        var copy = new Button { Content = "Copy recovery code", Margin = new Thickness(0, 16, 0, 8) };
        copy.Click += (_, _) => { System.Windows.Clipboard.SetText(code); copy.Content = "Copied — clear clipboard after storing"; }; panel.Children.Add(copy);
        var done = new Button { Content = "I have saved it privately", IsDefault = true }; done.Click += (_, _) => dialog.Close(); panel.Children.Add(done);
        dialog.Content = panel; dialog.ShowDialog(); value.Clear();
    }
}
