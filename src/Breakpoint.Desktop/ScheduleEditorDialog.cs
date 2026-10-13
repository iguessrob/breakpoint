using System.Windows;
using System.Windows.Controls;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public sealed class ScheduleEditorDialog : Window
{
    public Schedule? Edited { get; private set; }
    public ScheduleEditorDialog(Schedule schedule)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Edit Start – End schedule"; Width = 440; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Evening curfew", FontSize = 23, FontWeight = FontWeights.SemiBold });
        var help = new TextBlock { Text = "Start begins curfew. End restores networking. Use 24-hour time; an End before Start is the next day.", Margin = new Thickness(0,12,0,18) };
        help.SetResourceReference(TextBlock.ForegroundProperty, "Dim"); panel.Children.Add(help);
        var start = new TextBox { Text = schedule.Start, MaxLength = 5 };
        var end = new TextBox { Text = schedule.End, MaxLength = 5 };
        foreach (var pair in new[] { ("Start (HH:mm)", start), ("End (HH:mm)", end) })
        {
            panel.Children.Add(new TextBlock { Text = pair.Item1, Margin = new Thickness(0,10,0,6) });
            System.Windows.Automation.AutomationProperties.SetName(pair.Item2, pair.Item1); panel.Children.Add(pair.Item2);
        }
        panel.Children.Add(new TextBlock { Text = "Days", Margin = new Thickness(0,16,0,6) });
        var days = new Dictionary<int, CheckBox>(); var week = new WrapPanel();
        foreach (var day in new[] {1,2,3,4,5,6,0})
        {
            var check = new CheckBox { Content = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[day], IsChecked = schedule.Days.Contains(day) };
            days[day] = check; week.Children.Add(check);
        }
        panel.Children.Add(week);
        panel.Children.Add(new TextBlock { Text = "Time zone", Margin = new Thickness(0,14,0,6) });
        var zone = new ComboBox { ItemsSource = TimeZoneInfo.GetSystemTimeZones(), DisplayMemberPath = "DisplayName", SelectedValuePath = "Id", SelectedValue = schedule.ZoneId, MinHeight = 36 };
        System.Windows.Automation.AutomationProperties.SetName(zone, "Schedule time zone"); panel.Children.Add(zone);
        var enabled = new CheckBox { Content = "Enable recurring curfew", IsChecked = schedule.Enabled, Margin = new Thickness(0,16,0,0) }; panel.Children.Add(enabled);
        var error = new TextBlock { Margin = new Thickness(0,10,0,0) }; error.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,20,0,0) };
        actions.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0,0,8,0) });
        var save = new Button { Content = "Continue · PIN", IsDefault = true }; save.SetResourceReference(StyleProperty, "Primary"); actions.Children.Add(save); panel.Children.Add(actions);
        save.Click += (_, _) =>
        {
            var edited = new Schedule { Start = start.Text.Trim(), End = end.Text.Trim(), Enabled = enabled.IsChecked == true, ZoneId = zone.SelectedValue as string ?? schedule.ZoneId, Days = days.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToArray(), Revision = schedule.Revision };
            try { edited.Validate(); Edited = edited; DialogResult = true; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        };
        Content = panel;
    }
}
