using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Automation;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public sealed partial class IslandWindow
{
    private static string DisplayTime(string value)=>TimeOnly.ParseExact(value,"HH:mm").ToString("h:mm tt",CultureInfo.InvariantCulture);
    public void OpenSchedule()
    {
        var saved=latest?.Schedule??new Schedule();
        draft=saved with { Days=saved.Days.ToArray(),WindowKind="Allowed" };
        if(saved.WindowKind=="Blocked")draft=draft with { Start=saved.End,End=saved.Start };
        var panel=Heading("Curfew");var revision=draft.Revision;
        var times=new Grid();times.ColumnDefinitions.Add(new ColumnDefinition());times.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(10) });times.ColumnDefinitions.Add(new ColumnDefinition());
        var startValue=Text(DisplayTime(draft.Start),22);var endValue=Text(DisplayTime(draft.End),22);
        CheckBox? enabledSwitch=null;var summary=Text("",12,true);summary.Margin=new Thickness(0,4,0,14);
        void Refresh(){startValue.Text=DisplayTime(draft!.Start);endValue.Text=DisplayTime(draft.End);summary.Text=enabledSwitch?.IsChecked==false?"Curfew off":$"Internet off {DisplayTime(draft.End)} · shutdown {TimeOnly.ParseExact(draft.End,"HH:mm").AddMinutes(1).ToString("h:mm tt",CultureInfo.InvariantCulture)}";}
        Button TimeCard(string label,TextBlock value,string key,bool start)
        {
            var body=new StackPanel();body.Children.Add(Text(label,12,true));value.Margin=new Thickness(0,4,0,0);body.Children.Add(value);
            var button=Button(body,()=>OpenTimePicker(start,panel,Refresh),key);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Padding=new Thickness(10);button.Margin=new Thickness(0,0,0,14);return button;
        }
        times.Children.Add(TimeCard("Start · internet on",startValue,"edit-start",true));var end=TimeCard("End · internet off",endValue,"edit-end",false);Grid.SetColumn(end,2);times.Children.Add(end);panel.Children.Add(times);
        var repeatRow=new DockPanel { Margin=new Thickness(0,0,0,10) };var repeat=new ComboBox { Width=154,MinHeight=34,FontSize=13 };
        foreach(var name in new[]{"Every day","Weekdays","Custom days","Once"})repeat.Items.Add(name);
        repeat.SelectedIndex=draft.OneTimeDate is not null?3:draft.Days.Length==7?0:draft.Days.Order().SequenceEqual(new[]{1,2,3,4,5})?1:2;
        DockPanel.SetDock(repeat,Dock.Right);repeatRow.Children.Add(repeat);var repeatLabel=Text("Repeat");repeatLabel.VerticalAlignment=VerticalAlignment.Center;repeatRow.Children.Add(repeatLabel);AutomationProperties.SetName(repeat,"Repeat curfew");panel.Children.Add(repeatRow);
        var days=new UniformGrid { Columns=7,Margin=new Thickness(0,0,0,10),Visibility=repeat.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed };
        var chosen=draft.Days.ToHashSet();
        foreach(var day in new[]{1,2,3,4,5,6,0})
        {
            var b=Button(CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[day][..1],()=>{},"day-"+day);b.MinHeight=32;b.Height=32;b.Padding=new Thickness(0);b.Margin=new Thickness(0,0,4,0);b.Opacity=chosen.Contains(day)?1:.5;
            AutomationProperties.SetName(b,((DayOfWeek)day).ToString());b.Click+=(_,_)=>{if(!chosen.Add(day))chosen.Remove(day);b.Opacity=chosen.Contains(day)?1:.5;dirty=true;};days.Children.Add(b);
        }
        panel.Children.Add(days);
        repeat.SelectionChanged+=(_,_)=>{days.Visibility=repeat.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;dirty=true;Fit();};
        var enabled=new CheckBox { Content="Curfew enabled",IsChecked=draft.Enabled,FontSize=13,Margin=new Thickness(0,0,0,10) };enabledSwitch=enabled;enabled.Click+=(_,_)=>{dirty=true;Refresh();};panel.Children.Add(enabled);
        Refresh();panel.Children.Add(summary);
        if(IsPreview){var mode=Text("Preview · no internet blocking or shutdown",12,true);mode.Margin=new Thickness(0,0,0,12);panel.Children.Add(mode);}
        var error=Text("",12);error.Visibility=Visibility.Collapsed;error.SetResourceReference(TextBlock.ForegroundProperty,"Warning");panel.Children.Add(error);
        panel.Children.Add(Button("Save curfew · PIN",()=>
        {
            var updated=draft! with { Enabled=enabled.IsChecked==true,Days=repeat.SelectedIndex==1?[1,2,3,4,5]:repeat.SelectedIndex==2?chosen.Order().ToArray():[0,1,2,3,4,5,6],Revision=revision,OneTimeDate=null };
            if(repeat.SelectedIndex==3)
            {
                var local=TimeZoneInfo.ConvertTime(latest?.Now??DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById(updated.ZoneId));
                var date=DateOnly.FromDateTime(local.DateTime);if(TimeOnly.ParseExact(updated.Start,"HH:mm")<=TimeOnly.FromDateTime(local.DateTime))date=date.AddDays(1);
                updated=updated with { OneTimeDate=date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) };
            }
            try{updated.Validate();}catch(ArgumentException e){error.Text=e.Message;error.Visibility=Visibility.Visible;Fit();return;}
            Authenticate(new Request { Action="SaveSchedule",Schedule=updated,Revision=revision },"Owner approval","Save this curfew?");
        },"save-schedule",true));dirty=false;SetView("schedule",panel);
    }
    private void OpenTimePicker(bool start,FrameworkElement editor,Action refresh)
    {
        var wasDirty=dirty;var original=TimeOnly.ParseExact(start?draft!.Start:draft!.End,"HH:mm");var pm=original.Hour>=12;
        void Back(){dirty=wasDirty;SetView("schedule",editor);}
        var panel=Heading(start?"Start · internet on":"End · internet off",onBack:Back);
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(18) });grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(60) });
        TextBox Wheel(string name,int value,int min,int max,int column)
        {
            var group=new StackPanel { Margin=new Thickness(0,0,6,0) };group.Children.Add(Text(name,12,true));
            var field=new TextBox { Text=name=="Hours"?value.ToString():value.ToString("00"),MaxLength=2,FontSize=30,Height=52,HorizontalContentAlignment=HorizontalAlignment.Center };
            var up=Button("⌃",()=>Step(1),name.ToLower()+"-up");up.Background=Brushes.Transparent;up.BorderThickness=new Thickness(0);up.Height=32;up.MinHeight=32;up.Padding=new Thickness(0);
            var down=Button("⌄",()=>Step(-1),name.ToLower()+"-down");down.Background=Brushes.Transparent;down.BorderThickness=new Thickness(0);down.Height=32;down.MinHeight=32;down.Padding=new Thickness(0);
            void Step(int by){if(!int.TryParse(field.Text,out var n))n=value;n=n+by;if(n>max)n=min;if(n<min)n=max;field.Text=name=="Hours"?n.ToString():n.ToString("00");if(!check&&!ReduceMotion&&SystemParameters.ClientAreaAnimation){field.BeginAnimation(OpacityProperty,null);field.Opacity=0;field.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(140)));}}
            group.Children.Add(up);group.Children.Add(field);group.Children.Add(down);field.TextChanged+=(_,_)=>dirty=true;AutomationProperties.SetName(field,name);Grid.SetColumn(group,column);grid.Children.Add(group);return field;
        }
        var hours=Wheel("Hours",original.Hour%12==0?12:original.Hour%12,1,12,0);var minutes=Wheel("Minutes",original.Minute,0,59,2);
        var colon=Text(":",28,true);colon.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(colon,1);grid.Children.Add(colon);
        var period=new StackPanel { VerticalAlignment=VerticalAlignment.Center };var am=Button("AM",()=>{},"am");var afternoon=Button("PM",()=>{},"pm");am.Padding=afternoon.Padding=new Thickness(4,6,4,6);am.MinHeight=afternoon.MinHeight=36;afternoon.Margin=new Thickness(0,8,0,0);
        void Period(){am.SetResourceReference(BackgroundProperty,pm?"Raised":"Accent");am.SetResourceReference(ForegroundProperty,pm?"Ink":"AccentInk");afternoon.SetResourceReference(BackgroundProperty,pm?"Accent":"Raised");afternoon.SetResourceReference(ForegroundProperty,pm?"AccentInk":"Ink");}
        am.Click+=(_,_)=>{pm=false;dirty=true;Period();};afternoon.Click+=(_,_)=>{pm=true;dirty=true;Period();};Period();period.Children.Add(am);period.Children.Add(afternoon);Grid.SetColumn(period,3);grid.Children.Add(period);panel.Children.Add(grid);
        var error=Text("",12);error.SetResourceReference(TextBlock.ForegroundProperty,"Warning");error.Visibility=Visibility.Collapsed;error.Margin=new Thickness(0,4,0,4);panel.Children.Add(error);
        var use=Button("Use time",()=>
        {
            if(!int.TryParse(hours.Text,out var h)||h is <1 or >12||!int.TryParse(minutes.Text,out var m)||m is <0 or >59){error.Text="Hours 1–12 · minutes 00–59";error.Visibility=Visibility.Visible;Fit();return;}
            var value=new TimeOnly(h%12+(pm?12:0),m).ToString("HH:mm",CultureInfo.InvariantCulture);
            draft=start?draft! with { Start=value }:draft! with { End=value };dirty=wasDirty||value!=original.ToString("HH:mm");refresh();SetView("schedule",editor);
        },"use-time",true);use.Margin=new Thickness(0,14,0,0);panel.Children.Add(use);SetView("time-picker",panel);
        if(!check)Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>{if(view=="time-picker"&&page.Content==panel){Activate();Keyboard.Focus(hours);hours.SelectAll();}}));
    }
    // Compatibility entry point for older callers; there is no separate Timer page.
    public void OpenTimer()=>OpenSchedule();
}
