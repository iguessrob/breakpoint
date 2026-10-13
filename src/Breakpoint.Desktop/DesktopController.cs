using System.Windows;
using System.Windows.Threading;
using System.Text.Json;
using System.Diagnostics;
using Breakpoint.Core;
using Forms=System.Windows.Forms;

namespace Breakpoint.Desktop;

public sealed class DesktopController : IDisposable
{
    public IslandWindow Island { get; }
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon trayArtwork;
    private readonly DispatcherTimer poll=new() { Interval=TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> warnings=[];
    private bool polling,unavailable,setupRequested;
    private string? lastError;
    private readonly string preferences=Path.Combine(Path.GetDirectoryName(App.PreferencePath)!,"island-preferences.json");
    private sealed record Preferences(string Appearance,bool WarningSound,bool ReduceMotion);
    public DesktopController(bool setup=false)
    {
        Island=new IslandWindow();
        if(File.Exists(preferences))
        {
            try { var saved=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferences));if(saved is not null){App.ApplyTheme(saved.Appearance);Island.WarningSound=saved.WarningSound;Island.ReduceMotion=saved.ReduceMotion;} }
            catch(Exception e) when(e is IOException or JsonException){ }
        }
        trayArtwork=BrandAssets.TrayIcon();
        tray=new Forms.NotifyIcon { Icon=trayArtwork,Text=App.Preview?"Breakpoint · preview":"Breakpoint",Visible=true };
        var menu=new BreakpointTrayMenu(App.Preview,()=>Island.IsVisible,
            ()=>Island.Dispatcher.Invoke(()=>{Island.Show();Island.OpenSettings();}),
            ()=>Island.Dispatcher.Invoke(()=>{if(Island.IsVisible)Island.Hide();else Island.Show();}),
            ()=>Island.Dispatcher.Invoke(()=>{Island.Show();Island.Skip();}),
            ()=>Island.Dispatcher.Invoke(()=>Island.ShowNotification("Curfew begins in 5 minutes")),
            ()=>Island.Dispatcher.Invoke(()=>{Island.Show();Island.OpenSettings();Island.PreviewWarning();}),
            ()=>Island.Dispatcher.Invoke(()=>{Island.Show();Island.OpenOwner();Island.AuthorizeQuit();}));
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Island.Dispatcher.Invoke(()=>{Island.Show();Island.OpenSettings();});
        Island.PreferencesChanged+=SavePreferences;
        Island.QuitApproved+=()=>{Dispose();App.Current.Shutdown();};
        Island.SetupElevationRequested+=()=>
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--setup") { UseShellExecute=true,Verb="runas" }); }
            catch(System.ComponentModel.Win32Exception){Island.ShowNotification("Windows setup approval cancelled");}
        };
        if(setup)Island.OwnerSetupCompleted+=()=>{Dispose();App.Current.Shutdown();};
        setupRequested=setup;
        poll.Tick+=async(_,_)=>await Refresh();poll.Start();
        _=Refresh();
    }
    private async Task Refresh()
    {
        if(polling)return;polling=true;
        try
        {
            var response=await App.Client.SendAsync(new Request());
            if(response.Status is not { } status)return;
            Island.Update(status);unavailable=false;
            if(setupRequested){setupRequested=false;Island.OpenOwner();if(!status.Configured)Island.SetupOwner();}
            if(status.Error is { } error && error!=lastError)Notify("Enforcement needs attention",error);
            lastError=status.Error;
            string? key=null,title=null,body=null;
            if(status.Active is { } active && status.Shutdown=="Countdown")
            { key=active.Id+":shutdown";title="Shutdown begins in one minute";body="Save your work. Use the owner PIN to skip this curfew."; }
            else if(status.Next is { } next)
            {
                var seconds=(next.Start-status.Now).TotalSeconds;var threshold=seconds<=60?60:seconds<=300?300:seconds<=900?900:0;
                if(threshold>0){key=next.Id+":"+threshold;var minutes=Math.Max(1,(int)Math.Ceiling(seconds/60));title=$"Curfew begins in {minutes} minute{(minutes==1?"":"s")}";body="Internet turns off at End. Save your work.";}
            }
            if(key is not null&&warnings.Add(key))Notify(title!,body!);
        }
        catch(Exception)
        {
            if(!unavailable){Island.Unavailable();unavailable=true;}
        }
        finally {polling=false;}
    }
    private void Notify(string title,string body)
    {
        Island.ShowNotification(title);
        if(!Island.IsVisible)tray.ShowBalloonTip(6000,"Breakpoint",body,Forms.ToolTipIcon.Warning);
        if(Island.WarningSound)System.Media.SystemSounds.Exclamation.Play();
    }
    private void SavePreferences()
    {
        try{Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);File.WriteAllText(preferences,JsonSerializer.Serialize(new Preferences(App.CurrentTheme,Island.WarningSound,Island.ReduceMotion)));}
        catch(IOException){Island.ShowNotification("Appearance preferences could not be saved");}
    }
    public void Dispose(){poll.Stop();tray.Visible=false;tray.ContextMenuStrip?.Dispose();tray.Dispose();trayArtwork.Dispose();Island.FinishClose();}
}
