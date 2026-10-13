using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Automation;
using Breakpoint.Core;

namespace Breakpoint.Desktop;

public sealed partial class IslandWindow : Window
{
    public const double ExpandedHeightLimit=460;
    private readonly bool check;
    private readonly Func<bool> pointerProbe;
    private readonly DispatcherTimer pointerClock = new() { Interval=TimeSpan.FromMilliseconds(100) };
    private readonly Func<Request,Task<Response>> send;
    private readonly Border shell;
    private readonly Button cap;
    private readonly Grid capRow;
    private readonly System.Windows.Shapes.Path logo;
    private readonly TextBlock brand = new(), capText = new(), noticeText = new();
    private readonly Border notice;
    private readonly StackPanel content = new();
    private readonly ContentControl page = new() { HorizontalContentAlignment=HorizontalAlignment.Stretch };
    private readonly ScrollViewer scroll;
    private readonly Border contentPadding;
    private readonly TranslateTransform offset = new();
    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromMilliseconds(260) };
    private readonly DispatcherTimer noticeClock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<string,Button> actions = [];
    private Status? latest;
    private string view = "idle", message = "", pendingTitle = "";
    private bool pointer, dirty, busy, notification, closingAllowed, clearingSecrets;
    private double remaining = 6000;
    private readonly TimeProvider time;
    private long lastNoticeTick;
    private long transition;
    private FrameworkElement? preserved;
    private string preservedView = "menu";
    private Request? pending;
    private TextBlock? authError, peekTitle, peekDetail;
    private PasswordBox? credential, newPin, confirmPin;
    private Schedule? draft;
    internal bool CloseOnDeactivate { get; set; } = true;
    public bool WarningSound { get; set; } = true;
    public bool ReduceMotion { get; set; }
    public string CurrentView => view;
    public bool IsExpanded => view is not ("idle" or "peek" or "notify");
    public bool HasUnsavedChanges => dirty;
    public event Action? QuitApproved;
    public event Action? OwnerSetupCompleted;
    public event Action? SetupElevationRequested;
    public event Action? PreferencesChanged;

    public IslandWindow(bool renderingCheck = false, Func<Request,Task<Response>>? sender = null, TimeProvider? timeProvider = null, Func<bool>? pointerInside = null)
    {
        check = renderingCheck; send = sender ?? (request => App.Client.SendAsync(request));time=timeProvider??TimeProvider.System;
        Title = "Breakpoint"; Icon=BrandAssets.WindowIcon(); ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        SourceInitialized+=(_,_)=>IslandNative.ExcludeFromSwitcher(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13; SetResourceReference(ForegroundProperty,"Ink");
        Width = 360; Height = Math.Min(ExpandedHeightLimit,SystemParameters.WorkArea.Height-24); ResizeMode = ResizeMode.NoResize;
        shell = new Border { Width = 56, Height = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        shell.SetResourceReference(Border.BackgroundProperty,"Bg"); shell.SetResourceReference(Border.BorderBrushProperty,"Line");
        shell.SizeChanged += (_,_) => UpdateClip();
        var host = new Grid(); host.Children.Add(shell);
        var stack = new StackPanel(); shell.Child = stack;
        var row = capRow = new Grid { HorizontalAlignment=HorizontalAlignment.Left }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = logo = new System.Windows.Shapes.Path { Data = BrandAssets.CreateMark(), Stretch=Stretch.Uniform, Width = 13, Height = 14, Margin = new Thickness(0,0,9,0), VerticalAlignment = VerticalAlignment.Center };
        mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Accent"); row.Children.Add(mark);
        brand.Text = "breakpoint"; brand.FontSize = 13; brand.TextWrapping=TextWrapping.NoWrap; brand.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(brand,1); row.Children.Add(brand);
        capText.Text = "Checking"; capText.FontSize = 11; capText.VerticalAlignment = VerticalAlignment.Center; capText.TextWrapping = TextWrapping.NoWrap;
        capText.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(capText,2); row.Children.Add(capText);
        cap = Button(row,()=> { if(view is "idle" or "peek" or "notify") OpenSettings(); else RequestClose(); },"cap");
        cap.HorizontalContentAlignment = HorizontalAlignment.Left; cap.Padding = new Thickness(10,0,10,0); cap.MinHeight = 0; cap.Height = 26;
        cap.Background = Brushes.Transparent; cap.BorderThickness = new Thickness(0); AutomationProperties.SetName(cap,"Open Breakpoint controls"); stack.Children.Add(cap);
        noticeText.FontSize = 12; noticeText.TextWrapping=TextWrapping.Wrap; noticeText.VerticalAlignment=VerticalAlignment.Center;
        noticeText.SetResourceReference(TextBlock.ForegroundProperty,"Ink"); AutomationProperties.SetLiveSetting(noticeText,AutomationLiveSetting.Polite);
        var noticeRow = new Grid();
        noticeRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        noticeRow.ColumnDefinitions.Add(new ColumnDefinition());
        noticeRow.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var noticeIcon=Text("\uE946",14);noticeIcon.FontFamily=new FontFamily("Segoe MDL2 Assets");noticeIcon.VerticalAlignment=VerticalAlignment.Center;
        noticeIcon.Margin=new Thickness(0,0,10,0);noticeIcon.SetResourceReference(TextBlock.ForegroundProperty,"Accent");noticeRow.Children.Add(noticeIcon);
        Grid.SetColumn(noticeText,1);noticeRow.Children.Add(noticeText);
        var dismiss=IconButton("\uE8BB",EndNotification,"dismiss-notification","Dismiss notification");dismiss.Width=28;dismiss.Height=28;dismiss.MinHeight=28;
        dismiss.Margin=new Thickness(8,0,0,0);dismiss.VerticalAlignment=VerticalAlignment.Center;dismiss.Background=Brushes.Transparent;
        Grid.SetColumn(dismiss,2);noticeRow.Children.Add(dismiss);
        notice = new Border { CornerRadius=new CornerRadius(12),Padding=new Thickness(12,8,8,8),Margin=new Thickness(20,12,20,0),BorderThickness=new Thickness(1),Child=noticeRow,Visibility=Visibility.Collapsed };
        notice.SetResourceReference(Border.BackgroundProperty,"Raised");notice.SetResourceReference(Border.BorderBrushProperty,"Line");
        content.Width = 318; content.RenderTransform = offset; content.Children.Add(page);stack.Children.Add(notice);
        contentPadding=new Border { Child=content,Padding=new Thickness(20,18,20,18) };
        scroll = new ScrollViewer { Content = contentPadding, Padding = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Visibility = Visibility.Collapsed, MaxHeight = 414, CanContentScroll = false };
        stack.Children.Add(scroll); Content = host;
        pointerProbe=pointerInside??PointerInsideSurface;
        shell.MouseEnter += (_,_) => TrackPointer(true);
        shell.MouseLeave += (_,_) => TrackPointer(false);
        pointerClock.Tick+=(_,_)=>{if(view is "idle" or "peek" or "notify"){var inside=pointerProbe();if(inside!=pointer)TrackPointer(inside);}};
        hover.Tick += (_,_) => { hover.Stop(); if(pointer && view=="idle")ShowPeek(); };
        leave.Tick += (_,_) => { leave.Stop(); if(!pointer && view=="peek")SetView("idle",null); };
        noticeClock.Tick += (_,_) => AdvanceNotification();
        PreviewKeyDown += (_,e) => { if(e.Key==Key.Escape){RequestClose();e.Handled=true;} };
        Deactivated += (_,_) => { if(CloseOnDeactivate&&IsExpanded) RequestClose(); };
        Closing += (_,e) => { if(!closingAllowed){e.Cancel=true;RequestClose();} };
        Loaded += (_,_) => {ResetPosition();if(!check)pointerClock.Start();};
        if(!check) Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_,_) => { pointerClock.Stop();hover.Stop();leave.Stop();noticeClock.Stop();ClearSecrets();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplayChanged; };
        SetView("idle",null,false);
    }

    private bool PointerInsideSurface()
    {
        var position=System.Windows.Forms.Cursor.Position;
        var local=shell.PointFromScreen(new Point(position.X,position.Y));
        return shell.Clip?.FillContains(local)==true;
    }
    private void TrackPointer(bool inside)
    {
        pointer=inside;
        if(inside){leave.Stop();if(view=="idle")hover.Start();}
        else{hover.Stop();if(view=="peek")leave.Start();}
    }

    private Button Button(object label, Action action, string key, bool primary=false)
    {
        var b = new Button { Content=label, MinHeight=42, FontSize=13, Padding=new Thickness(14,10,14,10), HorizontalContentAlignment=HorizontalAlignment.Center };
        if(primary)b.SetResourceReference(StyleProperty,"Primary");
        b.Click+=(_,_)=>action();actions[key]=b;AutomationProperties.SetName(b,label is string text?text:key);return b;
    }
    private TextBlock Text(string text,int size=13,bool muted=false)
    {
        var t=new TextBlock { Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap };
        if(muted)t.SetResourceReference(TextBlock.ForegroundProperty,"Dim");return t;
    }
    private Button IconButton(string glyph,Action action,string key,string name)
    {
        var b=Button(new TextBlock { Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=14,VerticalAlignment=VerticalAlignment.Center },action,key);
        b.Width=32;b.MinHeight=32;b.Height=32;b.Padding=new Thickness(0);b.BorderThickness=new Thickness(0);AutomationProperties.SetName(b,name);return b;
    }
    private StackPanel Heading(string text,bool back=true,Action? onBack=null)
    {
        var panel=new StackPanel();var row=new DockPanel { Margin=new Thickness(0,0,0,16) };
        var close=IconButton("\uE8BB",RequestClose,"close","Close settings");DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);
        if(back){var previous=IconButton("\uE72B",onBack??(()=>Navigate(OpenSettings)),"back","Back");previous.Margin=new Thickness(0,0,9,0);DockPanel.SetDock(previous,Dock.Left);row.Children.Add(previous);}
        var title=Text(text,18);title.FontWeight=FontWeights.Medium;title.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(title);panel.Children.Add(row);return panel;
    }
    private void AddRow(StackPanel panel,string label,string glyph,Action action,string key)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var icon=Text(glyph,14,true);icon.FontFamily=new FontFamily("Segoe MDL2 Assets");icon.Margin=new Thickness(0,0,12,0);row.Children.Add(icon);
        var name=Text(label);Grid.SetColumn(name,1);row.Children.Add(name);var arrow=new System.Windows.Shapes.Path { Data=Geometry.Parse("M1,1 L6,6 L1,11"),Width=8,Height=12,StrokeThickness=1.6,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,4,0) };
        arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Ink");Grid.SetColumn(arrow,2);row.Children.Add(arrow);
        var button=Button(row,action,key);button.Background=Brushes.Transparent;button.Padding=new Thickness(2,12,2,12);button.BorderThickness=new Thickness(0,0,0,1);button.MinHeight=44;
        button.HorizontalContentAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(button,label);panel.Children.Add(button);
    }
    private TextBox Field(StackPanel parent,string name,string initial,int maximum=5)
    {
        var label=Text(name,12,true);label.Margin=new Thickness(0,0,0,6);parent.Children.Add(label);
        var input=new TextBox { Text=initial,MaxLength=maximum,MinHeight=42,FontSize=16 };AutomationProperties.SetName(input,name);
        input.TextChanged+=(_,_)=>dirty=true;parent.Children.Add(input);return input;
    }
    private void ClearSecrets(){clearingSecrets=true;try{credential?.Clear();newPin?.Clear();confirmPin?.Clear();credential=null;newPin=null;confirmPin=null;pending=null;}finally{clearingSecrets=false;}}
    private void Navigate(Action action){if(busy)return;if(dirty){DiscardPrompt(action);return;}ClearSecrets();action();}
    public void OpenSettings()
    {
        if(busy)return;
        var panel=Heading("Your stopping point.",false);
        AddRow(panel,"Curfew","\uE787",OpenSchedule,"schedule");
        AddRow(panel,"Appearance","\uE713",OpenAppearance,"appearance");AddRow(panel,"Owner access","\uE72E",OpenOwner,"owner");
        if(latest?.Active is not null){var skip=Button("Skip this curfew · PIN",Skip,"skip",true);skip.Margin=new Thickness(0,14,0,0);panel.Children.Add(skip);}
        if(IsPreview){var preview=Text("Preview mode · internet and shutdown are simulated.",12,true);preview.Margin=new Thickness(0,14,0,0);panel.Children.Add(preview);}
        SetView("menu",panel);if(!check)Activate();
    }
    private void RefreshPeek()
    {
        if(peekTitle is null||peekDetail is null)return;
        peekTitle.Text=latest?.Active is not null?(latest.Shutdown=="Countdown"?"Shutdown in "+Countdown():"Internet is off"):HasOnlineCountdown?"Internet off in "+OnlineCountdown:latest?.Next is not null?"Curfew at "+NextTime():"Curfew off";
        peekDetail.Text=IsPreview?"Preview · no internet blocking or shutdown":latest?.Active is not null?"Owner PIN required to skip":latest?.Configured==false?"Set up the owner PIN":latest?.Next is not null?"Internet on at "+DisplayTime(latest.Schedule.WindowKind=="Allowed"?latest.Schedule.Start:latest.Schedule.End):"Set a schedule in settings";
    }
    private void ShowPeek()
    {
        var row=new DockPanel();var settings=IconButton("\uE713",OpenSettings,"settings","Open settings");DockPanel.SetDock(settings,Dock.Right);row.Children.Add(settings);
        var labels=new StackPanel();peekTitle=Text("",12);peekDetail=Text("",12,true);labels.Children.Add(peekTitle);labels.Children.Add(peekDetail);RefreshPeek();row.Children.Add(labels);SetView("peek",row);
    }
    public void OpenAppearance()
    {
        var panel=Heading("Appearance");
        void Toggle(string text,bool value,Action<bool> change)
        {
            var row=new DockPanel { Margin=new Thickness(0,0,0,12) };var box=new CheckBox { IsChecked=value,Margin=new Thickness(0),MinHeight=35 };
            DockPanel.SetDock(box,Dock.Right);row.Children.Add(box);row.Children.Add(Text(text));AutomationProperties.SetName(box,text);
            box.Click+=(_,_)=>{change(box.IsChecked==true);PreferencesChanged?.Invoke();};panel.Children.Add(row);
        }
        Toggle("Light appearance",App.CurrentTheme=="Light",value=>App.ApplyTheme(value?"Light":"Dark"));
        Toggle("Warning sound",WarningSound,value=>WarningSound=value);Toggle("Reduce motion",ReduceMotion,value=>ReduceMotion=value);
        panel.Children.Add(Text("Small when idle. Hover for a peek; click for controls.",12,true));SetView("appearance",panel);
    }
    public void OpenOwner()
    {
        var panel=Heading("Owner access");var help=Text("Your owner PIN protects overrides and schedule changes. Keep the recovery code privately.",12,true);help.Margin=new Thickness(0,0,0,14);panel.Children.Add(help);
        if(latest?.Configured!=true)AddRow(panel,"Set up owner PIN","\uE72E",SetupOwner,"setup");
        else AddRow(panel,"Change PIN","\uE72E",()=>Authenticate(new Request { Action="ChangePin" },"Change owner PIN","Enter your current PIN and choose a replacement.",true),"change-pin");
        AddRow(panel,"Recover access","\uE8A7",()=>Authenticate(new Request { Action="RecoverPin" },"Recover access","Use your privately stored recovery code to choose a new PIN.",true,true),"recover-pin");SetView("owner",panel);
    }
    public void SetupOwner()
    {
        if(!App.Preview&&!check&&!new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        {SetupElevationRequested?.Invoke();return;}
        Authenticate(new Request { Action="Setup" },"Set up owner PIN","Choose a private PIN with 6–12 digits. Curfew stays off until you enable a schedule.",true);
    }
    public void Skip()
    {
        var occurrence=latest?.Active??latest?.Next;
        if(occurrence is null){ShowNotification("No curfew to skip.");return;}
        OpenSettings();Authenticate(new Request { Action="Skip",OccurrenceId=occurrence.Id },"Skip this curfew?","Restore internet for this curfew. Future schedules stay enabled. Windows may still complete a shutdown already requested.");
    }
    public void AuthorizeQuit()=>Authenticate(new Request { Action="AuthorizeQuit" },"Quit interface?","The live background service keeps enforcing enabled curfews.");
    public void PreviewWarning()=>Authenticate(new Request { Action="PreviewWarning" },"Test shutdown warning","Preview only: no networking changes or real shutdown will occur.");
    private void Authenticate(Request request,string title,string explanation,bool replacePin=false,bool recovery=false)
    {
        if(latest is null){ShowNotification("Reconnect to the service first.");return;}
        preserved=page.Content as FrameworkElement;preservedView=view;pending=request;pendingTitle=title;
        var panel=Heading(title,onBack:()=>{ClearSecrets();SetView(preservedView,preserved);});var help=Text(explanation,12,true);help.Margin=new Thickness(0,0,0,14);panel.Children.Add(help);
        credential=null;newPin=null;confirmPin=null;
        PasswordBox Password(string label,int max=12)
        {
            var name=Text(label,12,true);name.Margin=new Thickness(0,0,0,6);panel.Children.Add(name);
            var box=new PasswordBox { MaxLength=max,MinHeight=42,FontSize=16,Margin=new Thickness(0,0,0,12) };AutomationProperties.SetName(box,label);panel.Children.Add(box);return box;
        }
        if(request.Action!="Setup")credential=Password(recovery?"Recovery code":"Owner PIN",recovery?100:12);
        if(replacePin){newPin=Password("New PIN (6–12 digits)");confirmPin=Password("Confirm PIN");newPin.PasswordChanged+=(_,_)=>{if(!clearingSecrets&&!busy)dirty=true;};confirmPin.PasswordChanged+=(_,_)=>{if(!clearingSecrets&&!busy)dirty=true;};}
        authError=Text("",12);authError.SetResourceReference(TextBlock.ForegroundProperty,"Warning");AutomationProperties.SetLiveSetting(authError,AutomationLiveSetting.Polite);panel.Children.Add(authError);
        var submit=Button("Confirm",()=>_ = SubmitAsync(replacePin,recovery),"confirm",true);submit.Margin=new Thickness(0,8,0,0);panel.Children.Add(submit);
        panel.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){_ = SubmitAsync(replacePin,recovery);e.Handled=true;}};
        SetView("pin",panel);
        if(!check)
        {
            var field=credential??newPin;
            // Tray menus release focus after their click handler returns; the new page also needs layout first.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
            {
                if(view!="pin"||page.Content!=panel||field is null||!IsVisible)return;
                Activate();UpdateLayout();FocusManager.SetFocusedElement(this,field);Keyboard.Focus(field);
            }));
        }
    }
    private async Task SubmitAsync(bool replacePin,bool recovery)
    {
        if(busy||pending is null)return;
        if(replacePin&&newPin!.Password!=confirmPin!.Password){authError!.Text="The PINs do not match.";Fit();return;}
        var command=pending with { Pin=recovery?null:credential?.Password,RecoveryCode=recovery?credential?.Password:null,NewPin=newPin?.Password };
        busy=true;actions["confirm"].IsEnabled=false;
        try
        {
            var result=await send(command);
            if(result.Status is not null)Update(result.Status);
            if(!result.Success){authError!.Text=result.Message;credential?.Clear();Fit();return;}
            var action=command.Action;ClearSecrets();dirty=false;preserved=null;busy=false;
            if(result.RecoveryCode is { } code){Recovery(code,action=="Setup");return;}
            if(action=="AuthorizeQuit"){QuitApproved?.Invoke();return;}
            if(action=="SaveSchedule")OpenSchedule();else if(action is "StartTimer" or "CancelTimer")OpenTimer();else OpenSettings();
            ShowNotification(action is "Skip" or "SaveSchedule"?result.Message:"Saved");
        }
        catch(Exception){authError!.Text="Service unavailable. No change was confirmed.";credential?.Clear();Fit();}
        finally{busy=false;if(actions.TryGetValue("confirm",out var submit))submit.IsEnabled=true;}
    }
    private void Recovery(string code,bool setup=false)
    {
        var panel=Heading("Keep this private.",false);panel.Children.Add(Text("This code can reset the owner PIN. It is shown once; a new code replaces the previous one.",12,true));
        var value=new TextBox { Text=code,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14) };panel.Children.Add(value);
        panel.Children.Add(Button("Copy recovery code",()=>{Clipboard.SetText(value.Text);ShowNotification("Recovery code copied");},"copy-recovery"));
        var done=Button("I have saved it privately",()=>{value.Clear();OpenOwner();if(setup)OwnerSetupCompleted?.Invoke();},"saved-recovery",true);done.Margin=new Thickness(0,10,0,0);panel.Children.Add(done);SetView("recovery",panel);
    }
    private void DiscardPrompt(Action next)
    {
        if(view=="discard")return;var old=page.Content as FrameworkElement;var oldView=view;
        var panel=new StackPanel();panel.Children.Add(Text("Discard unsaved edits?",18));var help=Text("Your existing schedule will stay unchanged.",12,true);help.Margin=new Thickness(0,8,0,14);panel.Children.Add(help);
        var row=new UniformGrid { Columns=2 };var keep=Button("Keep editing",()=>SetView(oldView,old),"keep-editing");keep.Margin=new Thickness(0,0,8,0);row.Children.Add(keep);
        row.Children.Add(Button("Discard",()=>{dirty=false;ClearSecrets();preserved=null;next();},"discard",true));panel.Children.Add(row);SetView("discard",panel);
    }
    public void RequestClose()
    {
        if(busy)return;
        if(view=="recovery"){ShowNotification("Save your recovery code first");return;}
        if(dirty){DiscardPrompt(()=>SetView("idle",null));return;}
        ClearSecrets();preserved=null;SetView("idle",null);
    }
    public void Update(Status status)
    {
        var wasConfigured=latest?.Configured;var oldWidth=IdleWidth;latest=status;Header();if(view=="idle"&&oldWidth!=IdleWidth)Fit();if(view=="owner"&&wasConfigured!=status.Configured)OpenOwner();
        // Snapshot updates never rebuild an editor or its PIN fields.
        if(view=="peek"&&!busy)RefreshPeek();
    }
    public void Unavailable(){latest=null;Header();ShowNotification("Service status unavailable");}
    private string NextTime()
    {
        if(latest?.Next is not { } next)return latest?.Configured==false?"Set PIN":"Curfew off";
        return TimeZoneInfo.ConvertTime(next.Start,TimeZoneInfo.FindSystemTimeZoneById(latest.Schedule.ZoneId)).ToString("h:mm tt",System.Globalization.CultureInfo.InvariantCulture);
    }
    private string ActiveLabel()=>latest?.Shutdown=="Countdown"?Countdown():latest?.Shutdown switch { "Accepted"=>"Closing", "Blocked or delayed"=>view=="idle"?"Off":"Internet off", "Preview complete"=>"Done", "Failed"=>"Failed", _=>view=="idle"?"Off":"Internet off" };
    private string Countdown()
    {
        var seconds=latest?.ShutdownAt is { } at?Math.Max(0,(int)Math.Ceiling((at-latest.Now).TotalSeconds)):0;
        return $"{seconds/60:00}:{seconds%60:00}";
    }
    private bool IsPreview=>App.Preview||latest?.Preview==true;
    private bool HasOnlineCountdown=>latest is {Active:null,Next: { } next,Error:null} status&&next.Start>status.Now&&status.Network is not ("Unknown" or "Failed restoration" or "Failed restriction");
    private double OnlineSeconds=>HasOnlineCountdown?Math.Max(0,Math.Ceiling((latest!.Next!.Start-latest.Now).TotalSeconds)):0;
    private string OnlineCountdown
    {
        get
        {
            var seconds=(long)OnlineSeconds;
            return seconds>=3600?$"{seconds/3600:00}:{seconds/60%60:00}:{seconds%60:00}":$"{seconds/60:00}:{seconds%60:00}";
        }
    }
    private string OnlineColor=>OnlineSeconds<=300?"Warning":"Accent";
    private double IdleWidth=>latest?.Active is not null?100:HasOnlineCountdown?(OnlineCountdown.Length>8?132:OnlineSeconds>=3600?118:100):56;
    private void Header()
    {
        bool idle=view=="idle",notify=view=="notify",logoOnly=view=="idle"&&latest?.Active is null&&!HasOnlineCountdown;
        brand.Visibility=idle||notify?Visibility.Collapsed:Visibility.Visible;capText.Visibility=logoOnly?Visibility.Collapsed:Visibility.Visible;
        logo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,latest?.Active is not null?"Warning":HasOnlineCountdown?OnlineColor:idle?"Dim":"Accent");
        logo.Margin=new Thickness(0,0,logoOnly?0:9,0);Grid.SetColumnSpan(logo,logoOnly?3:1);logo.HorizontalAlignment=logoOnly?HorizontalAlignment.Center:HorizontalAlignment.Left;
        cap.Height=idle?26:notify?52:44;cap.Padding=new Thickness(idle?10:18,0,idle?10:18,0);
        var targetWidth=idle?IdleWidth:view=="peek"?240:notify?300:360;
        capRow.Width=targetWidth-2-cap.Padding.Left-cap.Padding.Right;
        capText.FontSize=idle?(logoOnly?11:13):notify?12:13;capText.Text=notify?message:latest?.Active is not null?ActiveLabel():HasOnlineCountdown?OnlineCountdown:NextTime();
        if(IsPreview&&IsExpanded)capText.Text="Preview · "+capText.Text;
        capText.SetResourceReference(TextBlock.ForegroundProperty,notify||latest?.Active is not null?"Warning":HasOnlineCountdown?OnlineColor:idle?"Ink":"Dim");
        cap.BorderThickness=idle||notify?new Thickness(0):new Thickness(0,0,0,1);
        AutomationProperties.SetName(cap,$"Breakpoint. {(latest?.Active is not null?(latest.Shutdown=="Countdown"?"Shutdown in "+Countdown():"Internet off"):HasOnlineCountdown?"Internet off in "+OnlineCountdown:"Curfew "+capText.Text)}. Activate to open settings.");
        noticeText.Text=message;notice.Visibility=notification&&IsExpanded?Visibility.Visible:Visibility.Collapsed;
    }
    private void SetView(string next,FrameworkElement? body,bool animate=true)
    {
        var previous=view;if(previous!=next){hover.Stop();leave.Stop();}
        var closing=next is "idle" or "notify" && previous is not ("idle" or "notify") && animate && !check && !ReduceMotion && SystemParameters.ClientAreaAnimation;
        view=next;if(!closing)page.Content=body;if(previous!=next)scroll.ScrollToTop();Header();
        content.Width=next=="peek"?206:318;contentPadding.Padding=next=="peek"?new Thickness(16,10,16,10):new Thickness(20,18,20,18);
        scroll.Visibility=closing?Visibility.Visible:next is "idle" or "notify"?Visibility.Collapsed:Visibility.Visible;
        Fit(animate);
        capRow.BeginAnimation(OpacityProperty,null);capRow.Opacity=1;
        if(previous is "idle" or "notify" && next is not ("idle" or "notify") && animate && !check && !ReduceMotion && SystemParameters.ClientAreaAnimation)
        {
            capRow.Opacity=0;
            capRow.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)) { BeginTime=TimeSpan.FromMilliseconds(70) });
        }
        if(closing)
        {
            var version=transition;var fade=new DoubleAnimation(content.Opacity,0,TimeSpan.FromMilliseconds(100));
            fade.Completed+=(_,_)=>{if(transition==version && view is "idle" or "notify"){page.Content=null;scroll.Visibility=Visibility.Collapsed;}};
            content.BeginAnimation(OpacityProperty,fade,HandoffBehavior.SnapshotAndReplace);
        }
        else {content.BeginAnimation(OpacityProperty,null);content.Opacity=1;offset.BeginAnimation(TranslateTransform.YProperty,null);offset.Y=0;}
        if(previous!=next&&next is not ("idle" or "notify")&&animate&&!check&&!ReduceMotion&&SystemParameters.ClientAreaAnimation)
        {
            content.Opacity=0;
            content.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(240)) { BeginTime=TimeSpan.FromMilliseconds(70),EasingFunction=new QuadraticEase { EasingMode=EasingMode.EaseOut } });
            // Keep glyphs at fixed device-pixel positions; only the shell and opacity animate.
            offset.Y=0;
        }
    }
    private void Fit(bool animate=true)
    {
        var width=view=="idle"?IdleWidth:view=="peek"?240:view=="notify"?300:360;
        var height=view=="idle"?28d:view=="notify"?54d:0;
        if(height==0){var body=page.Content as FrameworkElement;body?.Measure(new Size(content.Width,double.PositiveInfinity));notice.Measure(new Size(width-2,double.PositiveInfinity));contentPadding.Padding=new Thickness(view=="peek"?16:20,notification&&IsExpanded?14:view=="peek"?10:18,view=="peek"?16:20,view=="peek"?10:18);height=Math.Min(Height,Math.Ceiling(cap.Height+(body?.DesiredSize.Height??0)+contentPadding.Padding.Top+contentPadding.Padding.Bottom+2+(notice.Visibility==Visibility.Visible?notice.DesiredSize.Height:0))+1);}
        scroll.MaxHeight=Math.Max(0,height-cap.Height-2-(notice.Visibility==Visibility.Visible?notice.DesiredSize.Height:0));++transition;
        shell.CornerRadius=new CornerRadius(view=="idle"?14:view=="peek"?20:24);
        if(!animate||check||ReduceMotion||!SystemParameters.ClientAreaAnimation)
        {shell.BeginAnimation(WidthProperty,null);shell.BeginAnimation(HeightProperty,null);shell.Width=width;shell.Height=height;return;}
        void Morph(DependencyProperty property,double target)
        {
            shell.BeginAnimation(property,new DoubleAnimation((double)shell.GetValue(property),target,TimeSpan.FromMilliseconds(view=="idle"?340:420)) { EasingFunction=new IslandEase { EasingMode=EasingMode.EaseIn } },HandoffBehavior.SnapshotAndReplace);
        }
        Morph(WidthProperty,width);Morph(HeightProperty,height);
        // Corner radii are kept concentric across the morph; clipping follows the current animated size.
        shell.CornerRadius=new CornerRadius(view=="idle"?14:view=="peek"?20:24);UpdateClip();
    }
    private void UpdateClip()=>shell.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,shell.ActualWidth),Math.Max(0,shell.ActualHeight)),shell.CornerRadius.TopLeft,shell.CornerRadius.TopLeft);
    public void ShowNotification(string text)
    {
        var firstNotice=!notification;message=text;notification=true;remaining=6000;lastNoticeTick=time.GetTimestamp();noticeClock.Start();
        if(view is "idle" or "peek" or "notify")SetView("notify",null);else{Header();Fit();}
        if(firstNotice&&IsExpanded&&!check&&!ReduceMotion&&SystemParameters.ClientAreaAnimation)
            notice.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));
        AutomationProperties.SetName(shell,text);var peer=System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(noticeText);peer?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void AdvanceNotification()
    {
        var now=time.GetTimestamp();var elapsed=time.GetElapsedTime(lastNoticeTick,now).TotalMilliseconds;lastNoticeTick=now;
        if(!pointer)remaining-=elapsed;if(remaining<=0)EndNotification();
    }
    private void EndNotification(){noticeClock.Stop();notification=false;if(view=="notify")SetView("idle",null);else{Header();Fit();}}
    public void ResetPosition(){Height=Math.Max(60,Math.Min(ExpandedHeightLimit,SystemParameters.WorkArea.Height-24));Fit(false);Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+12;}
    private void DisplayChanged(object? sender,EventArgs e)=>Dispatcher.Invoke(ResetPosition);
    public void FinishClose(){closingAllowed=true;Close();}
    public Button GetActionForCheck(string key)=>actions[key];
}





// Critically damped motion: a soft acceleration and a settled finish, without overshoot.
internal sealed class IslandEase : EasingFunctionBase
{
    protected override double EaseInCore(double progress)
        =>(1-(1+8*progress)*Math.Exp(-8*progress))/(1-9*Math.Exp(-8));
    protected override Freezable CreateInstanceCore()=>new IslandEase();
}
