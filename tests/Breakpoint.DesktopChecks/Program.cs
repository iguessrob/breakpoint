using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Breakpoint.Core;
using Breakpoint.Desktop;

internal static class Program
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowStyle(IntPtr window,int index);
    private static int passed;
    private static bool animatedPointer;
    private static void Enter(IslandWindow island){animatedPointer=true;Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});}
    private static void Exit(IslandWindow island){animatedPointer=false;Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});}
    private static void Check(bool value,string description){if(!value)throw new Exception("FAIL: "+description);passed++;Console.WriteLine("PASS "+description);}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var inner in Descendants(child))yield return inner;}
    }
    private static void Click(IslandWindow island,string key){island.GetActionForCheck(key).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout(island);}
    private static void Layout(IslandWindow island)
    {
        var root=(FrameworkElement)island.Content;root.Measure(new Size(360,island.Height));root.Arrange(new Rect(0,0,360,island.Height));root.UpdateLayout();
    }
    private static Border Shell(IslandWindow island)=>(Border)VisualTreeHelper.GetChild((DependencyObject)island.Content,0);
    private static T Named<T>(IslandWindow island,string name) where T:DependencyObject=>Descendants((DependencyObject)island.Content).OfType<T>().First(x=>System.Windows.Automation.AutomationProperties.GetName(x)==name);
    private static void Capture(IslandWindow island,string output,string name)
    {
        Layout(island);var shell=Shell(island);var image=new RenderTargetBitmap((int)Math.Ceiling(shell.ActualWidth),(int)Math.Ceiling(shell.ActualHeight),96,96,PixelFormats.Pbgra32);
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush(shell),null,new Rect(0,0,shell.ActualWidth,shell.ActualHeight));image.Render(drawing);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
    }
    private static double Contrast(Color a,Color b)
    {
        double Lum(Color c){double Ch(byte v){var n=v/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}return .2126*Ch(c.R)+.7152*Ch(c.G)+.0722*Ch(c.B);}
        var x=Lum(a);var y=Lum(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    private static void Pump(int milliseconds)
    {
        var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Contains("--installed-service-check"))
        {
            try
            {
                var response=new ServiceClient(Protocol.LivePipe).SendAsync(new Request()).GetAwaiter().GetResult();
                Check(response.Success&&response.Status is { Preview:false },"unelevated desktop client connects to the installed service");
                Console.WriteLine("Curfew enabled: "+response.Status!.Schedule.Enabled);return 0;
            }
            catch(Exception failure){Console.WriteLine("FAIL installed service connection: "+failure.Message);return 1;}
        }
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.ThrowException);
        var auth=typeof(ServiceClient).GetMethod("CheckRegistration",BindingFlags.NonPublic|BindingFlags.Static)!;
        var trusted="\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Breakpoint","Breakpoint.Service.exe")+"\" --owner-sid test";
        auth.Invoke(null,new object[]{1u,1u,4u,0x10u,trusted,"LocalSystem"});Check(true,"registered SYSTEM service with matching pipe PID is accepted");
        foreach(var invalid in new (uint Pid,uint State,uint Type,string Image,string Account,string Label)[]{
            (2,4,0x10,trusted,"LocalSystem","different pipe process"),
            (1,1,0x10,trusted,"LocalSystem","stopped service"),
            (1,4,0x20,trusted,"LocalSystem","shared service host"),
            (1,4,0x10,trusted.Replace("Breakpoint.Service.exe","Other.exe"),"LocalSystem","different executable"),
            (1,4,0x10,trusted,"OtherUser","different service account"),
            (1,4,0x10,trusted.Replace("\" --","\"extra --"),"LocalSystem","executable path suffix")})
        {
            var rejected=false;try{auth.Invoke(null,new object[]{invalid.Pid,1u,invalid.State,invalid.Type,invalid.Image,invalid.Account});}
            catch(TargetInvocationException failure){rejected=failure.InnerException is IOException;}
            Check(rejected,"service identity rejects "+invalid.Label);
        }
        var app=new App();app.InitializeComponent();
        var family=(FontFamily)typeof(IslandWindow).Assembly.GetType("Breakpoint.Desktop.AppTypography")!.GetProperty("Family",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        foreach(var weight in new[]{FontWeights.Normal,FontWeights.Medium,FontWeights.Bold})
            Check(new Typeface(family,FontStyles.Normal,weight,FontStretches.Normal).TryGetGlyphTypeface(out var glyph)&&glyph.FamilyNames.Values.Contains("ABC Areal")&&glyph.Weight==weight,"packaged Areal resolves real "+weight+" glyphs without a system font install");
        var output=args[0];Directory.CreateDirectory(output);
        var now=new DateTimeOffset(2026,10,13,12,0,0,TimeSpan.Zero);
        var current=new Status { Configured=true,Preview=true,Phase="Scheduled",Now=now,Schedule=new Schedule { Enabled=true,ZoneId="Singapore Standard Time" },Next=new Breakpoint.Core.Window("sample",now.AddHours(2),now.AddHours(10)) };
        var requests=new List<Request>();
        Task<Response> Send(Request request)
        {
            requests.Add(request);
            if(request.Pin!="246810")return Task.FromResult(new Response(false,"Incorrect PIN or recovery code.",current));
            if(request.Action=="Skip")return Task.FromResult(new Response(true,"Internet restored; this curfew is skipped. Windows may still complete an earlier shutdown request.",current with{Active=null,Shutdown="None"}));
            if(request.Schedule is { } schedule)current=current with { Schedule=schedule with { Revision=current.Schedule.Revision+1 } };
            return Task.FromResult(new Response(true,"Saved.",current));
        }
        App.ApplyTheme("Dark");
        var animated=new IslandWindow(false,Send,pointerInside:()=>animatedPointer);typeof(IslandWindow).GetProperty("CloseOnDeactivate",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(animated,false);animated.Opacity=0;animated.Topmost=false;animated.Update(current);animated.Show();animated.Left=-10000;Pump(450);
        Check(animated.Icon is BitmapFrame iconFrame&&iconFrame.PixelWidth>=16,"branded Windows icon loads from packaged resources");
        using(var trayArtwork=(System.Drawing.Icon)typeof(IslandWindow).Assembly.GetType("Breakpoint.Desktop.BrandAssets")!.GetMethod("TrayIcon",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!)
        {
            using var trayBitmap=trayArtwork.ToBitmap();
            Check(trayArtwork.Width==16&&trayBitmap.GetPixel(0,0).A==0,"branded tray icon loads at 16px with a transparent background");
        }
        var failures=new List<string>();
        void AnimatedCheck(bool value,string description){if(value)Check(true,description);else{failures.Add(description);Console.WriteLine("FAIL: "+description);}}
        var nativeStyle=GetWindowStyle(new WindowInteropHelper(animated).Handle,-20);
        AnimatedCheck((nativeStyle&0x80)!=0&&(nativeStyle&0x40000)==0,"native island is a tool window excluded from Alt+Tab");
        animated.OpenTimer();
        if(SystemParameters.ClientAreaAnimation)
        {
            var body=(StackPanel)typeof(IslandWindow).GetField("content",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(animated)!;
            var header=(Grid)typeof(IslandWindow).GetField("capRow",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(animated)!;
            AnimatedCheck(header.Opacity<.05,"opening header starts hidden during reveal delay");
            AnimatedCheck(body.Opacity<.05,"incoming text starts hidden during reveal delay");Pump(25);
            AnimatedCheck(body.Opacity<.05,"delayed reveal has no fully visible first frame");
            var lastOpacity=body.Opacity;var monotonic=true;var fixedText=true;
            for(var frame=0;frame<24;frame++){Pump(16);monotonic&=body.Opacity+.01>=lastOpacity;lastOpacity=body.Opacity;fixedText&=((TranslateTransform)body.RenderTransform).Y==0;}
            AnimatedCheck(monotonic&&lastOpacity>.99,"text reveal progresses without an opacity flash");
            AnimatedCheck(fixedText,"text does not move through fractional pixel offsets");
        }
        animated.RequestClose();Pump(450);
        Enter(animated);Pump(230);
        var brand=Descendants(Shell(animated)).OfType<TextBlock>().First(t=>t.Text=="breakpoint");var stableBrand=true;
        for(var frame=0;frame<26;frame++){Pump(16);stableBrand &= brand.ActualHeight<=20;}
        AnimatedCheck(stableBrand,"brand stays on one line throughout hover morph");
        var animatedContent=Descendants(Shell(animated)).OfType<StackPanel>().First(p=>p.RenderTransform is TranslateTransform);
        AnimatedCheck(animated.CurrentView=="peek"&&animatedContent.Opacity>.99,"animated hover reveals peek content after collapse");
        animated.OpenSchedule();Pump(450);var largeHeight=Shell(animated).ActualHeight;
        animated.OpenSettings();Pump(450);var returnHeight=Shell(animated).ActualHeight;
        var fresh=new IslandWindow(true,Send);fresh.Update(current);fresh.OpenSettings();Layout(fresh);
        AnimatedCheck(Math.Abs(returnHeight-Shell(fresh).ActualHeight)<2&&returnHeight<largeHeight,"return from taller page shrinks to natural menu height");
        animated.RequestClose();Pump(450);Enter(animated);Pump(650);
        AnimatedCheck(animatedContent.Opacity>.99,"second animated hover restores body opacity");
        Exit(animated);Pump(100);animated.Update(current);Pump(650);
        AnimatedCheck(animated.CurrentView=="idle","service update during hover exit does not cancel dismissal");
        Enter(animated);Pump(650);
        animatedPointer=false;Pump(800);
        AnimatedCheck(animated.CurrentView=="idle","pointer fallback dismisses peek when mouse-leave event is missed");
        animated.OpenSettings();animatedPointer=false;Pump(800);
        AnimatedCheck(animated.CurrentView=="menu","pointer fallback does not dismiss explicitly opened settings");
        animated.RequestClose();Pump(450);Enter(animated);Pump(650);
        animated.RequestClose();Pump(450);Enter(animated);Pump(250);
        var fadingOpacity=animatedContent.Opacity;animated.Update(current);
        AnimatedCheck(animatedContent.Opacity<=fadingOpacity+.05,"status polling preserves in-flight hover fade");Pump(450);
        Capture(animated,output,"animated-hover-fixed");
        for(var repeat=0;repeat<3;repeat++){animated.RequestClose();Pump(40);Enter(animated);Pump(620);}
        AnimatedCheck(animated.CurrentView=="peek"&&animatedContent.Opacity>.99,"rapid repeated hover interrupts collapse without hiding body");
        animated.Height=230;animated.OpenSchedule();Pump(450);
        var animatedScroll=Descendants(Shell(animated)).OfType<ScrollViewer>().First(s=>s.Content is Border);
        animatedScroll.ScrollToBottom();Pump(100);animated.OpenSettings();Pump(450);
        AnimatedCheck(animatedScroll.VerticalOffset==0&&Shell(animated).ActualHeight<=animated.Height+.01,"new page resets scrolling within smaller available height");
        animated.ResetPosition();Pump(100);
        AnimatedCheck(Math.Abs(Shell(animated).ActualHeight-Shell(fresh).ActualHeight)<2,"display reposition restores natural panel height");
        Capture(animated,output,"animated-menu-fixed");
        nativeStyle=GetWindowStyle(new WindowInteropHelper(animated).Handle,-20);
        AnimatedCheck((nativeStyle&0x80)!=0&&(nativeStyle&0x40000)==0,"Alt+Tab exclusion remains after opening interactive settings");
        animated.OpenOwner();animated.AuthorizeQuit();Pump(150);
        AnimatedCheck(FocusManager.GetFocusedElement(animated)==Named<PasswordBox>(animated,"Owner PIN"),"quit confirmation focuses owner PIN without another click");
        fresh.FinishClose();animated.FinishClose();
        if(failures.Count>0)throw new Exception(string.Join("; ",failures));
        foreach(var theme in new[]{"Dark","Light"})
        {
            current=current with { Schedule=new Schedule { Enabled=true,ZoneId="Singapore Standard Time" } };
            App.ApplyTheme(theme);var clock=new CheckClock(now);var island=new IslandWindow(true,Send,clock);island.Update(current);Layout(island);
            using(var trayMenu=new BreakpointTrayMenu(true,()=>true,()=>{},()=>{},()=>{},()=>{},()=>{},()=>{}))
            {
                for(var reopen=0;reopen<8;reopen++){trayMenu.Show(new System.Drawing.Point(-10000,-10000));Pump(25);trayMenu.Close();}
                Check(true,"tray menu can reopen repeatedly without a drawing exception in "+theme);
                Check(trayMenu.Font.Name=="ABC Areal","tray menu uses the private Areal font");trayMenu.RefreshAppearance();trayMenu.Show(new System.Drawing.Point(-10000,-10000));Pump(80);
                using var menuImage=new System.Drawing.Bitmap(trayMenu.Width,trayMenu.Height);
                trayMenu.DrawToBitmap(menuImage,new System.Drawing.Rectangle(0,0,trayMenu.Width,trayMenu.Height));
                menuImage.Save(Path.Combine(output,"tray-menu-"+theme.ToLowerInvariant()+".png"),System.Drawing.Imaging.ImageFormat.Png);trayMenu.Close();
            }

            var online=new IslandWindow(true,Send);var onlineStatus=current with{Network="Fake rules removed",Error=null,Active=null,Shutdown="None",Next=new Breakpoint.Core.Window("online-end",now.AddHours(2),now.AddHours(10))};
            online.Update(onlineStatus);Layout(online);
            var onlineText=(TextBlock)typeof(IslandWindow).GetField("capText",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(online)!;
            var onlineLogo=(System.Windows.Shapes.Path)typeof(IslandWindow).GetField("logo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(online)!;
            Check(Shell(online).Width==118&&onlineText.Text=="02:00:00"&&onlineText.Visibility==Visibility.Visible,"online pill shows remaining internet time in "+theme);
            Check(((SolidColorBrush)onlineText.Foreground).Color==((SolidColorBrush)app.Resources["Accent"]).Color&&((SolidColorBrush)onlineLogo.Fill).Color==((SolidColorBrush)app.Resources["Accent"]).Color,"online countdown and logo are green in "+theme);
            Capture(online,output,"online-countdown-"+theme.ToLowerInvariant());
            online.Update(onlineStatus with{Now=now.AddHours(2).AddSeconds(-301)});Layout(online);Check(onlineText.Text=="05:01"&&Shell(online).Width==100&&((SolidColorBrush)onlineText.Foreground).Color==((SolidColorBrush)app.Resources["Accent"]).Color,"online countdown stays green before last five minutes");
            online.Update(onlineStatus with{Now=now.AddHours(2).AddSeconds(-300)});Layout(online);Check(onlineText.Text=="05:00"&&((SolidColorBrush)onlineText.Foreground).Color==((SolidColorBrush)app.Resources["Warning"]).Color,"online countdown turns orange at five minutes");
            Capture(online,output,"online-warning-"+theme.ToLowerInvariant());
            online.Update(onlineStatus with{Active=onlineStatus.Next,Now=now.AddHours(2),Shutdown="Countdown",ShutdownAt=now.AddHours(2).AddSeconds(60)});Layout(online);Check(onlineText.Text=="01:00"&&((SolidColorBrush)onlineText.Foreground).Color==((SolidColorBrush)app.Resources["Warning"]).Color,"End switches online timer to orange shutdown countdown");
            online.Update(onlineStatus with{Network="Failed restoration",Error="Restoration failed"});Layout(online);Check(Shell(online).Width==56&&onlineText.Visibility==Visibility.Collapsed,"failed restoration never shows a green internet countdown");
            online.Update(onlineStatus with{Next=null});Layout(online);Check(Shell(online).Width==56,"turning off schedule restores logo-only pill");online.FinishClose();
            var suffix=theme.ToLowerInvariant();Check(island.CurrentView=="idle"&&Shell(island).Width==56&&Shell(island).Height==28,"logo-only 40 × 28 startup pill in "+theme);Capture(island,output,"integrated-minimal-"+suffix);
            Check(Descendants(Shell(island)).OfType<TextBlock>().All(t=>!t.IsVisible),"idle cap contains no visible text");
            var active=current with { Active=new Breakpoint.Core.Window("active",now,now.AddHours(8)),Shutdown="Countdown",ShutdownAt=now.AddSeconds(60) };
            island.Update(active);Layout(island);
            Check(Shell(island).Width==100&&Shell(island).Height==28&&Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text=="01:00"&&t.Visibility==Visibility.Visible),"active curfew widens pill to show shutdown countdown");
            Capture(island,output,"integrated-active-"+suffix);
            island.Update(active with { Now=now.AddSeconds(17) });Layout(island);
            Check(Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text=="00:43"&&t.Visibility==Visibility.Visible),"compact countdown updates from service deadline");
            island.Update(active with{Shutdown="Blocked or delayed"});
            Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});Pump(270);Layout(island);
            Check(Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text=="Internet is off")&&!Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text.StartsWith("Shutdown in "))&&!System.Windows.Automation.AutomationProperties.GetName(island.GetActionForCheck("cap")).Contains("Shutdown in"),"delayed shutdown displays internet status instead of a false countdown");
            island.Skip();Layout(island);
            Check(Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text.Contains("Restore internet for this curfew")),"PIN override explains internet recovery independently of shutdown cancellation");
            Named<PasswordBox>(island,"Owner PIN").Password="246810";Click(island,"confirm");
            Check(Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text.Contains("Windows may still complete")),"successful override preserves service shutdown warning in notification");
            Click(island,"dismiss-notification");island.RequestClose();island.Update(active);Layout(island);
            island.ShowNotification("Curfew active");Click(island,"dismiss-notification");
            Check(island.CurrentView=="idle"&&Shell(island).Width==100,"notification dismissal restores compact active countdown");
            island.Update(current);Layout(island);
            Check(Shell(island).Width==56,"skip or release restores logo-only idle pill");
            Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});Pump(270);Layout(island);
            Check(island.CurrentView=="peek"&&Shell(island).Width==240,"hover opens small peek only");Capture(island,output,"integrated-peek-"+suffix);
            Click(island,"settings");Layout(island);Check(island.CurrentView=="menu"&&Shell(island).Width==360,"settings open inside same surface");Capture(island,output,"integrated-menu-"+suffix);
            Check(Descendants(Shell(island)).OfType<TextBlock>().Any(t=>t.Text=="Preview mode · internet and shutdown are simulated."),"settings clearly identify simulated enforcement");
            Click(island,"schedule");Layout(island);Capture(island,output,"integrated-schedule-"+suffix);
            Check(Descendants((DependencyObject)island.Content).OfType<TextBlock>().Any(t=>t.Text=="6:00 AM"),"curfew Start displays internet-on time in AM/PM");
            var primary=island.GetActionForCheck("save-schedule");var label=Descendants(primary).OfType<TextBlock>().First(t=>t.Text=="Save curfew · PIN");
            Check(Contrast(((SolidColorBrush)label.Foreground).Color,((SolidColorBrush)primary.Background).Color)>=4.5,"curfew save label contrast in "+theme);
            Click(island,"edit-start");Named<TextBox>(island,"Hours").Text="12";Click(island,"pm");Click(island,"use-time");
            Check(Descendants((DependencyObject)island.Content).OfType<TextBlock>().Any(t=>t.Text=="12:00 PM"),"time wheel converts noon correctly");
            Click(island,"edit-start");Named<TextBox>(island,"Hours").Text="12";Click(island,"am");Click(island,"use-time");
            Check(Descendants((DependencyObject)island.Content).OfType<TextBlock>().Any(t=>t.Text=="12:00 AM"),"time wheel converts midnight correctly");
            Click(island,"edit-start");Named<TextBox>(island,"Hours").Text="7";Named<TextBox>(island,"Minutes").Text="30";Click(island,"am");Capture(island,output,"integrated-time-picker-"+suffix);Click(island,"use-time");
            island.Update(current with { Sequence=100 });Check(Descendants((DependencyObject)island.Content).OfType<TextBlock>().Any(t=>t.Text=="7:30 AM"),"service polling preserves curfew draft");
            island.RequestClose();Check(island.CurrentView=="discard","closing dirty form asks for confirmation");Click(island,"keep-editing");Check(island.CurrentView=="schedule","keep editing retains curfew draft");
            Click(island,"save-schedule");var pin=Named<PasswordBox>(island,"Owner PIN");pin.Password="123";island.ShowNotification("Internet turns off soon");Layout(island);
            Check(island.CurrentView=="pin"&&pin.Password=="123","notification preserves inline PIN entry");
            var banner=(Border)typeof(IslandWindow).GetField("notice",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
            var bannerText=(TextBlock)typeof(IslandWindow).GetField("noticeText",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
            Check(bannerText.TextWrapping==TextWrapping.Wrap&&bannerText.VerticalAlignment==VerticalAlignment.Center,"inline notification wraps and aligns with its dismiss control");
            Check(Contrast(((SolidColorBrush)bannerText.Foreground).Color,((SolidColorBrush)banner.Background).Color)>=4.5,"notification text has readable contrast in "+theme);
            island.ShowNotification("Your curfew was saved. Internet will turn off at the selected End time.");Layout(island);
            Check(bannerText.ActualHeight>20&&bannerText.ActualWidth<270,"long inline notifications use available width without clipping");
            Capture(island,output,"inline-notification-"+suffix);
            island.ShowNotification("Internet turns off soon");Layout(island);Capture(island,output,"integrated-pin-"+suffix);Click(island,"dismiss-notification");Click(island,"back");Check(island.CurrentView=="schedule","PIN Back returns to Curfew");
            Click(island,"save-schedule");pin=Named<PasswordBox>(island,"Owner PIN");pin.Password="000000";Click(island,"confirm");Check(island.CurrentView=="pin"&&pin.Password=="","failed PIN keeps auth page and clears secret");
            pin.Password="246810";Click(island,"confirm");Check(island.CurrentView=="schedule"&&!island.HasUnsavedChanges,"successful service-confirmed save returns to Curfew");
            Check(requests.Last().Schedule!.Start=="07:30"&&requests.Last().Schedule!.WindowKind=="Allowed","curfew sends internet-on Start and allowed-window policy");
            Click(island,"dismiss-notification");Capture(island,output,"integrated-schedule-"+suffix);
            island.RequestClose();Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});island.ShowNotification("Curfew begins in 5 minutes");
            Check(island.CurrentView=="notify"&&Shell(island).Width<300&&Shell(island).Height==38,"short notification fits its message in a compact pill");Capture(island,output,"integrated-notification-"+suffix);
            island.ShowNotification("No curfew to skip.");Layout(island);var shortNoticeWidth=Shell(island).Width;
            var compactText=(TextBlock)typeof(IslandWindow).GetField("capText",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
            var mark=(FrameworkElement)typeof(IslandWindow).GetField("logo",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(island)!;
            var gap=compactText.TranslatePoint(new Point(0,0),Shell(island)).X-mark.TranslatePoint(new Point(mark.ActualWidth,0),Shell(island)).X;
            Check(gap>=6&&gap<=17,"notification text sits beside the speaking logo without an empty spacer");Capture(island,output,"compact-notification-"+suffix);
            island.ShowNotification("Your curfew was saved. Internet will turn off at the selected End time.");Layout(island);
            Check(Shell(island).Width>shortNoticeWidth&&Shell(island).Width<=360&&compactText.ActualHeight>20&&Shell(island).Height>=compactText.ActualHeight+20,"long pill notification wraps and grows without clipping");Capture(island,output,"wrapped-notification-"+suffix);
            island.ShowNotification("No curfew to skip.");Layout(island);
            Check(Shell(island).Width==shortNoticeWidth&&Shell(island).Height==38,"replacing a long notification restores compact dimensions");
            Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});clock.Advance(7000);TickNotice(island);Check(island.CurrentView=="notify","hover pauses notification expiry");
            Shell(island).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});clock.Advance(5999);TickNotice(island);Check(island.CurrentView=="notify","notification lasts six unhovered seconds");clock.Advance(2);TickNotice(island);Check(island.CurrentView=="idle","notification returns to tiny pill");
            island.OpenAppearance();Layout(island);Capture(island,output,"integrated-appearance-"+suffix);island.OpenOwner();Layout(island);Capture(island,output,"integrated-owner-"+suffix);
            var setupState=current with { Configured=false,Schedule=current.Schedule with { Enabled=false },Next=null };
            var setupIsland=new IslandWindow(true,r=>Task.FromResult(new Response(r.Action=="Setup"&&r.NewPin=="135790","Saved",setupState with { Configured=true },"TEST-ONLY-RECOVERY-CODE")),clock);
            setupIsland.Update(setupState);setupIsland.OpenOwner();Click(setupIsland,"setup");
            Named<PasswordBox>(setupIsland,"New PIN (6–12 digits)").Password="135790";Named<PasswordBox>(setupIsland,"Confirm PIN").Password="135790";Click(setupIsland,"confirm");
            Check(setupIsland.CurrentView=="recovery","owner setup shows recovery inside island");
            setupIsland.ShowNotification("Recovery code copied");Layout(setupIsland);Capture(setupIsland,output,"recovery-notification-"+suffix);setupIsland.RequestClose();Check(setupIsland.CurrentView=="recovery","recovery code requires acknowledgement before dismissal");
            var acknowledged=false;setupIsland.OwnerSetupCompleted+=()=>acknowledged=true;Click(setupIsland,"saved-recovery");Check(acknowledged&&setupIsland.CurrentView=="owner","acknowledgement completes owner setup");setupIsland.FinishClose();
            Check(Shell(island).ActualHeight<=IslandWindow.ExpandedHeightLimit,"page respects maximum height");island.FinishClose();
        }
        Console.WriteLine($"{passed} integrated UI checks passed. No Windows enforcement executed.");return 0;
    }
    private static void TickNotice(IslandWindow island)=>typeof(IslandWindow).GetMethod("AdvanceNotification",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(island,null);
}
internal sealed class CheckClock(DateTimeOffset now):TimeProvider
{
    private long ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public override long GetTimestamp()=>ticks;public override DateTimeOffset GetUtcNow()=>now;
    public void Advance(int milliseconds){ticks+=TimeSpan.FromMilliseconds(milliseconds).Ticks;now=now.AddMilliseconds(milliseconds);}
}
