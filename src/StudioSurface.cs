using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Ports;

using System.Windows.Forms;

static class Theme
{
    public static readonly Color Back = Color.FromArgb(247, 249, 248);
    public static readonly Color Rail = Color.FromArgb(231, 238, 235);
    public static readonly Color Surface = Color.FromArgb(251, 252, 251);
    public static readonly Color Raised = Color.FromArgb(230, 237, 234);
    public static readonly Color Outline = Color.FromArgb(200, 211, 207);
    public static readonly Color Text = Color.FromArgb(24, 36, 35);
    public static readonly Color Muted = Color.FromArgb(78, 91, 88);
    public static readonly Color Accent = Color.FromArgb(22, 111, 101);
    public static readonly Color Ink = Color.FromArgb(255, 255, 255);
    public static readonly Color Amber = Color.FromArgb(255, 181, 108);
    public static readonly Color Green = Color.FromArgb(168, 227, 195);
    public static readonly Color Error = Color.FromArgb(255, 171, 164);

    public static GraphicsPath Round(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = 2 * Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

static class StudioStyle
{
    public static readonly Font Symbol = new Font("Segoe MDL2 Assets",20,GraphicsUnit.Pixel);
    public static readonly Font ButtonFont = new Font("Segoe UI Semibold",11);
    public static readonly Font RowFont = new Font("Segoe UI",12);
    public static readonly Color Paper=Color.FromArgb(251,252,251);
    public static readonly Color Line=Color.FromArgb(211,220,216);
    public static readonly Color Soft=Color.FromArgb(229,240,235);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SystemParametersInfo(uint action,uint parameter,out int value,uint flags);
    public static bool MotionAllowed()
    {
        int enabled;
        return !SystemInformation.HighContrast && SystemParametersInfo(0x1042,0,out enabled,0) && enabled!=0;
    }
    public static Color Blend(Color a,Color b,float t)
    {
        return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
    }
    public static void Box(Graphics g,RectangleF r,float radius,Color fill,Color stroke)
    {
        using(var path=Theme.Round(r,radius))
        using(var brush=new SolidBrush(fill)) {
            g.FillPath(brush,path);
            if(stroke.A!=0) using(var pen=new Pen(stroke)) g.DrawPath(pen,path);
        }
    }
}

sealed class Pill : Button
{
    public bool Emphasis,Selected,IsSwitch;
    bool hovered,pressed;
    public Pill(string caption)
    {
        Text=caption; AccessibleName=caption; FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0; Cursor=Cursors.Hand; Font=StudioStyle.ButtonFont;
        BackColor=StudioStyle.Paper; UseVisualStyleBackColor=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<5||Height<5)return;
        Graphics g=e.Graphics; g.Clear(BackColor); g.SmoothingMode=SmoothingMode.AntiAlias;
        Color text=Enabled?Theme.Text:Color.FromArgb(99,111,107);
        if(IsSwitch){
            bool unknown=Text=="Unknown";
            var r=new RectangleF(Width-48,(Height-24)/2f,44,24);
            Color track=unknown?Color.FromArgb(231,236,233):Selected?Theme.Accent:Color.FromArgb(123,138,131);
            StudioStyle.Box(g,r,12,track,unknown?StudioStyle.Line:Color.Transparent);
            float x=unknown?r.X+13:Selected?r.Right-21:r.Left+3;
            using(var brush=new SolidBrush(Color.White))g.FillEllipse(brush,x,r.Y+3,18,18);
            if(unknown)using(var pen=new Pen(text,1.5f))g.DrawLine(pen,x+6,r.Y+12,x+12,r.Y+12);
            if(Focused && ShowFocusCues)StudioStyle.Box(g,new RectangleF(r.X-3,r.Y-3,50,30),15,Color.Transparent,Theme.Accent);
            return;
        }
        var bounds=new RectangleF(1,1,Width-3,Height-3);
        Color fill=Emphasis?Theme.Accent:!Enabled?Color.FromArgb(244,246,244):pressed?Color.FromArgb(204,226,217):hovered?StudioStyle.Soft:Color.White;
        StudioStyle.Box(g,bounds,9,fill,Focused&&ShowFocusCues?Theme.Accent:StudioStyle.Line);
        TextRenderer.DrawText(g,Text,Font,Rectangle.Inflate(ClientRectangle,-5,0),Emphasis?Color.White:text,
            TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}

// Only the inset children own rectangular native surfaces. The card paints its
// complete silhouette (including the outside corners) in one pass.
sealed class SettingsCard : TableLayoutPanel
{
    public SettingsCard()
    {
        ColumnCount=1; BackColor=StudioStyle.Paper; Padding=new Padding(20,14,20,14);
        Margin=new Padding(0); Dock=DockStyle.Fill;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(Width<6||Height<6)return;
        var g=e.Graphics; g.Clear(Theme.Back); g.SmoothingMode=SmoothingMode.AntiAlias;
        StudioStyle.Box(g,new RectangleF(1,3,Width-3,Height-4),18,Color.FromArgb(228,233,230),Color.Transparent);
        StudioStyle.Box(g,new RectangleF(1,1,Width-3,Height-5),18,StudioStyle.Paper,StudioStyle.Line);
    }
}

sealed class GroupTitle : Control
{
    readonly string symbol;
    public GroupTitle(string caption,string glyph,Font heading)
    {
        Text=caption; symbol=glyph; Font=heading; BackColor=StudioStyle.Paper;
        Dock=DockStyle.Fill; Margin=new Padding(0); AccessibleRole=AccessibleRole.StaticText;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        StudioStyle.Box(g,new RectangleF(0,4,34,34),10,StudioStyle.Soft,Color.Transparent);
        var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g,symbol,StudioStyle.Symbol,new Rectangle(0,4,34,34),Theme.Accent,flags|TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g,Text,Font,new Rectangle(46,2,Width-47,38),Theme.Text,flags|TextFormatFlags.EndEllipsis);
    }
}

sealed class SettingsViewport : Panel
{
    Panel content;
    int offset,limit,wheelRemainder,grab;
    bool dragging;
    public SettingsViewport()
    {
        BackColor=Theme.Back; TabStop=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    public void Fit(Panel page,int width,int height)
    {
        if(content!=page){content=page;offset=0;}
        limit=Math.Max(0,height-ClientSize.Height);
        offset=Math.Min(offset,limit);
        content.Bounds=new Rectangle(0,-offset,Math.Max(1,width),Math.Max(1,height));
        Invalidate();
    }
    void ScrollTo(int value)
    {
        offset=Math.Max(0,Math.Min(limit,value));
        if(content!=null)content.Top=-offset;
        Invalidate();
    }
    public void Reveal(Control control)
    {
        if(content==null || !control.IsHandleCreated)return;
        var bounds=RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
        if(bounds.Top<0)ScrollTo(offset+bounds.Top-8);
        else if(bounds.Bottom>Height)ScrollTo(offset+bounds.Bottom-Height+8);
    }
    RectangleF Thumb()
    {
        float track=Math.Max(1,Height-6), h=Math.Max(34,track*Height/(Height+(float)limit));
        return new RectangleF(Width-9,3+(track-h)*offset/Math.Max(1,limit),5,h);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(limit==0)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        StudioStyle.Box(e.Graphics,Thumb(),2.5f,dragging?Theme.Accent:Color.FromArgb(164,181,173),Color.Transparent);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(limit>0 && e is HandledMouseEventArgs)((HandledMouseEventArgs)e).Handled=true;
        Wheel(e.Delta);
        base.OnMouseWheel(e);
    }
    public void Wheel(int delta)
    {
        wheelRemainder+=delta;
        int steps=wheelRemainder/SystemInformation.MouseWheelScrollDelta;
        wheelRemainder-=steps*SystemInformation.MouseWheelScrollDelta;
        int lines=SystemInformation.MouseWheelScrollLines;
        ScrollTo(offset-steps*(lines<0?Height:lines*22));
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button!=MouseButtons.Left||limit==0||e.X<Width-16)return;
        var thumb=Thumb();
        if(e.Y>=thumb.Top && e.Y<=thumb.Bottom){grab=e.Y-(int)thumb.Top;dragging=true;Capture=true;Invalidate();}
        else ScrollTo(offset+(e.Y<thumb.Top?-Height:Height));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(dragging)ScrollTo((int)((e.Y-grab-3)*limit/Math.Max(1f,Height-6-Thumb().Height)));
    }
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);Capture=false;dragging=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;}
    public bool ScrollKey(Keys key)
    {
        if(limit==0)return false;
        switch(key){
            case Keys.PageDown: ScrollTo(offset+Height-35); return true;
            case Keys.PageUp: ScrollTo(offset-Height+35); return true;
            case Keys.End: ScrollTo(limit); return true;
            case Keys.Home: ScrollTo(0); return true;
            default: return false;
        }
    }
}

sealed class StudioSurface : Panel
{
    sealed class Hit
    {
        public string Name;
        public Rectangle Bounds;
        public bool Overview, SettingsOnly;
        public Action Action;
    }
    readonly ClockWindow window;
    readonly ClockEngine clock;
    readonly SettingsViewport settings;
    readonly Action<string> navigate;
    readonly Bitmap skin, faces;
    readonly Bitmap[] separators=new Bitmap[2];
    readonly Font navActive=new Font("Segoe UI Semibold",18,GraphicsUnit.Pixel);
    readonly List<Hit> hits = new List<Hit>();
    readonly ToolTip tips = new ToolTip();
    readonly Font regular = new Font("Segoe UI", 18, GraphicsUnit.Pixel);
    readonly Font bold = new Font("Segoe UI Semibold", 20, GraphicsUnit.Pixel);
    readonly Font headline = new Font("Segoe UI Semibold", 41, GraphicsUnit.Pixel);
    static readonly Color white = Color.FromArgb(248,249,248), ink = Color.FromArgb(24,32,33),
        teal = Color.FromArgb(21,112,103), muted = Color.FromArgb(78,88,90), dark = Color.FromArgb(24,37,36);
    static readonly string[] pages = {"Overview","Display","Sound","Alarms & schedules","Device"};
    readonly int[] centers = {581,702,861,981,1145,1263};
    string page = "Overview", hovered = "", notice = "";
    DateTime noticeUntil, nextRead = DateTime.UtcNow.AddSeconds(90), edit = DateTime.Now;
    int percent = 72, scene = 0, focusIndex = -1;
    bool seconds = true, hours24 = true, dragging, editing, keyFocus;
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    ToolStripDropDown picker;
    bool overview { get { return page == "Overview"; } }

    static readonly string[] navIcons={"\uE80F","\uE7F4","\uE767","\uEA8F","\uE713"};
    static readonly string[] captions={"Overview","Display","Sound","Alarms & Schedules","Device"};
    static readonly string[] subtitles={"","Set the appearance of your tubes and backlight.","Adjust the clock’s voice, volume, and chimes.",
        "Manage alarms, power schedules, and countdowns.","Manage power, tube protection, and test mode."};
    readonly Timer motion=new Timer { Interval=16 };
    readonly System.Diagnostics.Stopwatch elapsed=new System.Diagnostics.Stopwatch();
    float navPosition,navStart,hourPosition=1,secondPosition=1,hourStart=1,secondStart=1;
    bool targetHour=true,targetSeconds=true;
    float scenePosition,sceneStart,sceneOpacity=1,sceneStartOpacity=1;
    int targetScene;
    void Animate()
    {
        navStart=navPosition; hourStart=hourPosition; secondStart=secondPosition;
        targetHour=hours24; targetSeconds=seconds;
        sceneStart=scenePosition;sceneStartOpacity=sceneOpacity;targetScene=scene;
        if(!StudioStyle.MotionAllowed()){
            scenePosition=Math.Max(0,scene);sceneOpacity=scene<0?0:1;
            motion.Stop();navPosition=Array.IndexOf(pages,page);hourPosition=hours24?1:0;secondPosition=seconds?1:0;Invalidate();return;
        }
        elapsed.Restart();motion.Start();
    }
    void AnimationTick(object sender,EventArgs args)
    {
        float t=Math.Min(1,elapsed.ElapsedMilliseconds/165f);
        float ease=1-(1-t)*(1-t)*(1-t);
        scenePosition=targetScene<0?sceneStart:sceneStart+(targetScene-sceneStart)*ease;
        sceneOpacity=sceneStartOpacity+((targetScene<0?0:1)-sceneStartOpacity)*ease;
        navPosition=navStart+(Array.IndexOf(pages,page)-navStart)*ease;
        hourPosition=hourStart+((targetHour?1:0)-hourStart)*ease;
        secondPosition=secondStart+((targetSeconds?1:0)-secondStart)*ease;
        Invalidate();
        if(t>=1)motion.Stop();
    }
    bool VisibleHit(Hit h){return (!h.Overview || overview) && (!h.SettingsOnly || !overview);}

    public StudioSurface(ClockWindow owner, ClockEngine engine, SettingsViewport settingsPanel, Action<string> show)
    {
        window=owner; clock=engine; settings=settingsPanel; navigate=show;
        Dock=DockStyle.Fill; BackColor=white; TabStop=true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable,true);
        skin=Artwork("Host.png"); faces=Artwork("Cathodes.png");
        CleanMaterial(skin,new Rectangle(9,192,256,323),true);
        CleanMaterial(skin,new Rectangle(1107,704,359,88),false);
        CleanMaterial(skin,new Rectangle(965,529,67,42),true);
        CleanMaterial(skin,new Rectangle(1148,529,67,42),true);
        for(int i=0;i<2;i++) {
            var r=new Rectangle(i==0?717:1000,341,38,74);
            separators[i]=skin.Clone(r,PixelFormat.Format32bppArgb);
            CleanMaterial(skin,r,true);
        }
        Controls.Add(settings); settings.Dock=DockStyle.None; settings.Visible=false;
        Add("Close",60,18,22,32,false,window.Close);
        Add("Minimize",86,18,22,32,false,delegate {window.WindowState=FormWindowState.Minimized;});
        Add("Maximize or restore",112,18,22,32,false,Maximize);
        Add("USB connection",1347,15,52,39,false,Ports);
        Add("Settings",1415,15,50,39,false,delegate{Go("Device");});
        Add("More options",1478,15,52,39,false,More);
        for(int i=0;i<pages.Length;i++){string target=pages[i];Add(target,60,215+i*60,243,57,false,delegate{Go(target);});}
        Add("Clock connection details",63,883,238,71,false,Ports);
        Add("Sync from Computer",1245,100,272,49,true,delegate{Write(delegate{clock.SetTime(DateTime.Now);});});
        Add("Brightness",464,540,355,43,true,delegate{});
        Add("24-hour",920,540,155,43,true,delegate{
            if(clock.Synchronized) Write(delegate{clock.Toggle(0x13);});
            else { hours24=!hours24; Animate(); }
        });
        Add("Seconds",1100,540,159,43,true,delegate{seconds=!seconds;Animate(); Say("Preview seconds " + (seconds?"on":"off"));});
        Add("Display settings",1317,539,193,46,true,delegate{Go("Display");});
        Add("Serial port",492,693,207,41,true,Ports);
        Add("Connect or refresh",361,755,338,48,true,Read);
        Add("Date",814,690,282,40,true,delegate{Pick(true);});
        Add("Time",814,736,282,40,true,delegate{Pick(false);});
        Add("Apply Time",763,788,333,44,true,delegate{DateTime selected=edit;Write(delegate{clock.SetTime(selected);});});
        for(int i=0;i<3;i++){int id=i;Add(new[]{"Daylight","Nightfall","Focus"}[i],1155+i*117,722,117,78,true,delegate{SetBrightness(new[]{72,30,60}[id]);});}
        Resize+=delegate{LayoutSettings();};
        MouseDoubleClick+=delegate(object s,MouseEventArgs e){Point p=Board(e.Location);if(p.Y<57&&p.X>160&&p.X<1340) Maximize();};
        menu.Closed+=delegate{editing=false;};
        motion.Tick+=AnimationTick;
        Add("Clock connection",1245,100,272,49,false,Read);
        hits[hits.Count-1].SettingsOnly=true;
    }
    static Bitmap Artwork(string name)
    {
        using(var stream=typeof(StudioSurface).Assembly.GetManifestResourceStream(name))
        using(var original=new Bitmap(stream)) return new Bitmap(original);
    }
    void Add(string name,int x,int y,int w,int h,bool home,Action action)
    { hits.Add(new Hit{Name=name,Bounds=new Rectangle(x,y,w,h),Overview=home,Action=action}); }
    void Go(string target){if(target==page)return; navigate(target);page=target;focusIndex=hits.FindIndex(h=>h.Name==target);LayoutSettings();Animate();Invalidate();}
    void Maximize(){window.WindowState=window.WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    void LayoutSettings()
    {
        settings.Visible=!overview;
        settings.Bounds=Rectangle.FromLTRB(Pixel(338,190).X,Pixel(338,190).Y,Pixel(1521,903).X,Pixel(1521,903).Y);
    }
    Point Pixel(int x,int y){return new Point((int)((x-45)*Width/1497f),(int)((y-12)*Height/966f));}
    Point Board(Point p){return new Point(45+(int)(p.X*1497f/Math.Max(1,Width)),12+(int)(p.Y*966f/Math.Max(1,Height)));}
    Hit At(Point p){return hits.Find(delegate(Hit h){return VisibleHit(h)&&h.Bounds.Contains(p);});}
    void Say(string message){notice=message;noticeUntil=DateTime.UtcNow.AddSeconds(6);Invalidate();}
    void Write(Action fn)
    {
        if(!clock.Ready){Say("Connect the clock and wait for its settings.");return;}
        fn();nextRead=DateTime.UtcNow.AddSeconds(90);
    }
    void Read(){if(clock.PortOpen){if(!clock.Busy)clock.ReadClock();}else Ports();nextRead=DateTime.UtcNow.AddSeconds(90);}
    void SetBrightness(int value)
    {
        int desired=Math.Max(0,Math.Min(100,value));
        if(clock.Synchronized)
        {
            if(!clock.Ready){Say("Wait for the clock to finish reading.");return;}
            int level=Math.Max(1,Math.Min(10,(int)Math.Round(desired/10.0)));
            clock.Field(9,2,level); desired=level*10;
        }
        percent=desired; scene=(percent==70||percent==72)?0:percent==30?1:percent==60?2:-1;
        if(targetScene!=scene)Animate();
    }
    void Ports()
    {
        menu.Items.Clear();
        string[] ports=SerialPort.GetPortNames();
        foreach(string p in ports){
            var item=menu.Items.Add(p+(p==clock.Port?"   ✓":""));
            string selected=p;
            item.Click+=delegate{if(selected==clock.Port && clock.PortOpen)Read();else clock.SelectPort(selected);};
        }
        if(ports.Length==0)menu.Items.Add("No serial ports found").Enabled=false;
        menu.Items.Add("Find a Bluetooth clock…",null,delegate{clock.ChooseBluetooth(window);});
        if(clock.PortOpen)menu.Items.Add("Disconnect",null,delegate{clock.Disconnect();}).Enabled=!clock.Busy;
        ShowMenu();
    }
    void More()
    {
        menu.Items.Clear();
        menu.Items.Add("Read settings from clock",null,delegate{Read();});
        menu.Items.Add("Display settings",null,delegate{Go("Display");});
        menu.Items.Add("Device settings",null,delegate{Go("Device");});
        menu.Items.Add("Maximize or restore",null,delegate{Maximize();});
        ShowMenu();
    }
    void ContextFor(Hit hit,Point board)
    {
        if(hit!=null&&(hit.Name=="Serial port"||hit.Name=="USB connection")){Ports();return;}
        if(overview && board.Y>=190 && board.Y<=600 && board.X>=337)
        {
            menu.Items.Clear();
            menu.Items.Add(seconds?"Hide seconds in preview":"Show seconds in preview",null,delegate{seconds=!seconds;Animate();Invalidate();});
            menu.Items.Add("Read settings from clock",null,delegate{Read();});
            menu.Items.Add("Display settings",null,delegate{Go("Display");});
            ShowMenu();
        }else More();
    }
    void ShowMenu(){editing=true; Point where=PointToClient(Cursor.Position);if(!ClientRectangle.Contains(where))where=Pixel(840,530);menu.Show(this,where);}
    void Pick(bool date)
    {
        if(picker!=null)picker.Dispose();
        picker=new ToolStripDropDown();editing=true;
        if(date){
            var day=new MonthCalendar{MinDate=new DateTime(2000,1,1),MaxDate=new DateTime(2099,12,31),MaxSelectionCount=1,SelectionStart=edit.Date,SelectionEnd=edit.Date};
            day.DateSelected+=delegate(object s,DateRangeEventArgs e){edit=e.Start.Date+edit.TimeOfDay;picker.Close();};
            picker.Items.Add(new ToolStripControlHost(day));
        }else{
            var panel=new FlowLayoutPanel{Size=new Size(222,40),BackColor=white};
            var time=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm:ss",ShowUpDown=true,Value=edit,Width=122};
            var ok=new Button{Text="Done",Width=72};
            ok.Click+=delegate{edit=edit.Date+time.Value.TimeOfDay;picker.Close();};panel.Controls.Add(time);panel.Controls.Add(ok);
            picker.Items.Add(new ToolStripControlHost(panel));
        }
        picker.Closed+=delegate{editing=false;Invalidate();};picker.Show(this,Pixel(815,date?731:775));
    }
    public void UpdateView(string active,string error)
    {
        if(page!=active){page=active;LayoutSettings();Animate();}
        if(clock.Synchronized){
            bool? shortHours=clock.SwitchState(0x13);
            if(shortHours.HasValue)hours24=!shortHours.Value;
            if(!dragging && clock.Ready){
                int max=10;
                if(max>0){percent=Math.Max(0,Math.Min(100,100*clock.Get(9).C/max));scene=(percent==70||percent==72)?0:percent==30?1:percent==60?2:-1;}
            }
            if(!dragging && !editing && Form.ActiveForm==window && clock.Ready && DateTime.UtcNow>=nextRead)Read();
        }
        if(targetHour!=hours24||targetSeconds!=seconds||targetScene!=scene)Animate();
        Invalidate();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int m,IntPtr w,IntPtr l);
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);Point p=Board(e.Location);Hit h=At(p);Focus();keyFocus=false;
        if(e.Button==MouseButtons.Right){ContextFor(h,p);return;}
        if(e.Button!=MouseButtons.Left)return;
        if(h!=null){focusIndex=hits.IndexOf(h);if(h.Name=="Brightness"){dragging=true;Capture=true;Drag(p);}return;}
        if(p.Y<57){ReleaseCapture();SendMessage(window.Handle,0xA1,(IntPtr)2,IntPtr.Zero);}
        else if(p.X>1523&&p.Y>953){ReleaseCapture();SendMessage(window.Handle,0xA1,(IntPtr)17,IntPtr.Zero);}
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if(!overview && settings.Bounds.Contains(e.Location))settings.Wheel(e.Delta);
        base.OnMouseWheel(e);
    }
    void Drag(Point p){percent=Math.Max(0,Math.Min(100,(p.X-478)*100/314));Invalidate();}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);Point p=Board(e.Location);if(dragging){Drag(p);return;}
        Hit h=At(p);string name=h==null?"":h.Name;
        if(name!=hovered){hovered=name;tips.SetToolTip(this,name);Cursor=h==null?Cursors.Default:Cursors.Hand;Invalidate();}
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;
        try{
            if(dragging){dragging=false;Capture=false;SetBrightness(percent);}
            else{Hit h=At(Board(e.Location));if(h!=null&&focusIndex==hits.IndexOf(h))h.Action();}
        }catch(Exception ex){Say(ex.GetBaseException().Message);}Invalidate();
    }
    protected override bool IsInputKey(Keys key){return (key&Keys.KeyCode)==Keys.Left||(key&Keys.KeyCode)==Keys.Right||base.IsInputKey(key);}
    protected override AccessibleObject CreateAccessibilityInstance(){return new SurfaceAccess(this);}
    sealed class SurfaceAccess : ControlAccessibleObject
    {
        readonly StudioSurface s;
        public SurfaceAccess(StudioSurface surface):base(surface){s=surface;}
        List<Hit> Targets(){return s.hits.FindAll(s.VisibleHit);}
        public override int GetChildCount(){return Targets().Count+Math.Max(0,base.GetChildCount());}
        public override AccessibleObject GetChild(int index){
            var a=Targets();if(index<0)return null;
            return index<a.Count?new TargetAccess(s,a[index]):base.GetChild(index-a.Count);
        }
    }
    sealed class TargetAccess : AccessibleObject
    {
        readonly StudioSurface s;readonly Hit hit;
        public TargetAccess(StudioSurface surface,Hit target){s=surface;hit=target;}
        public override string Name{get{return hit.Name;}set{}}
        public override string DefaultAction{get{return "Activate";}}
        public override AccessibleRole Role{get{return hit.Name=="Brightness"?AccessibleRole.Slider:AccessibleRole.PushButton;}}
        public override Rectangle Bounds{get{
            Rectangle r=hit.Bounds;Point a=s.Pixel(r.Left,r.Top),b=s.Pixel(r.Right,r.Bottom);
            return s.RectangleToScreen(Rectangle.FromLTRB(a.X,a.Y,b.X,b.Y));
        }}
        public override void DoDefaultAction(){s.BeginInvoke(new Action(delegate{
            try{hit.Action();}catch(Exception e){s.Say(e.GetBaseException().Message);}
        }));}
        public override string Value{get{return hit.Name=="Brightness"?s.percent+"%":null;}set{
            int n;if(hit.Name=="Brightness" && int.TryParse((value??"").TrimEnd('%'),out n))
                s.BeginInvoke(new Action(delegate{s.SetBrightness(n);}));
        }}
    }
    protected override bool ProcessCmdKey(ref Message m,Keys key)
    {
        if(!overview && settings.ScrollKey(key))return true;
        if(!overview && settings.ContainsFocus)return base.ProcessCmdKey(ref m,key);
        if(key==Keys.Tab||key==(Keys.Shift|Keys.Tab)){
            int direction=key==Keys.Tab?1:-1;
            if(!overview && key==Keys.Tab && focusIndex==hits.Count-1 && settings.SelectNextControl(null,true,true,true,false))return true;
            do{focusIndex=(focusIndex+direction+hits.Count)%hits.Count;}while(!VisibleHit(hits[focusIndex]));
            keyFocus=true;Invalidate();return true;
        }
        if(keyFocus&&focusIndex>=0){
            Hit h=hits[focusIndex];
            try{
                if(key==Keys.Enter||key==Keys.Space){h.Action();Invalidate();return true;}
                if(h.Name=="Brightness"&&(key==Keys.Left||key==Keys.Right)){SetBrightness(percent+(key==Keys.Left?-10:10));return true;}
            }catch(Exception ex){Say(ex.GetBaseException().Message);return true;}
        }
        if(key==Keys.Apps){More();return true;}
        return base.ProcessCmdKey(ref m,key);
    }
    void PaintText(Graphics g,RectangleF r,string text,Font font,Color color,bool centered=false)
    {
        using(var b=new SolidBrush(color))using(var f=new StringFormat{Alignment=centered?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
            g.DrawString(text,font,b,r,f);
    }
    void Fill(Graphics g,Color c,int x,int y,int w,int h){using(var b=new SolidBrush(c))g.FillRectangle(b,x,y,w,h);}
    void TogglePaint(Graphics g,int x,float amount)
    {
        StudioStyle.Box(g,new RectangleF(x,546,54,30),15,StudioStyle.Blend(Color.FromArgb(97,109,107),teal,amount),Color.Transparent);
        using(var b=new SolidBrush(white))g.FillEllipse(b,x+3+25*amount,549,24,24);
    }
    // Reconstruct the material between the undisturbed edges of each baked
    // control once. Sampling by scanline keeps the original gradients continuous.
    static void CleanMaterial(Bitmap bitmap,Rectangle r,bool horizontal)
    {
        int spans=horizontal?r.Height:r.Width, length=horizontal?r.Width:r.Height;
        for(int i=0;i<spans;i++){
            int x=r.X+(horizontal?0:i),y=r.Y+(horizontal?i:0);
            Color start=bitmap.GetPixel(horizontal?x-1:x,horizontal?y:y-1);
            Color end=bitmap.GetPixel(horizontal?x+length:x,horizontal?y:y+length);
            for(int j=0;j<length;j++)
                bitmap.SetPixel(horizontal?x+j:x,horizontal?y:y+j,StudioStyle.Blend(start,end,(j+1f)/(length+1)));
        }
    }
    void DrawRail(Graphics g)
    {
        for(int i=0;i<pages.Length;i++) {
            if(hovered==pages[i] && i!=Array.IndexOf(pages,page))
                StudioStyle.Box(g,new RectangleF(61,218+i*60,242,54),12,Color.FromArgb(80,Color.White),Color.Transparent);
        }
        float y=218+navPosition*60;
        StudioStyle.Box(g,new RectangleF(61,y,242,54),12,Color.FromArgb(250,251,250),Color.FromArgb(220,228,223));
        StudioStyle.Box(g,new RectangleF(62,y+13,3,28),1.5f,teal,Color.Transparent);
        for(int i=0;i<pages.Length;i++){
            bool active=pages[i]==page;
            PaintText(g,new RectangleF(81,217+i*60,32,56),navIcons[i],StudioStyle.Symbol,active?teal:ink,true);
            PaintText(g,new RectangleF(127,217+i*60,174,56),captions[i],active?navActive:regular,ink);
        }
    }
    void DrawScenes(Graphics g)
    {
        var bounds=new RectangleF(1155,722,350,77);
        using(var outer=Theme.Round(bounds,13)){
            var state=g.Save();g.SetClip(outer,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var fill=new SolidBrush(Color.FromArgb(246,248,246)))g.FillRectangle(fill,bounds);
            float w=bounds.Width/3;
            string[] labels={"Daylight","Nightfall","Focus"};
            string[] icons={"\uE706","\uE708","\uE81D"};
            int hot=Array.IndexOf(labels,hovered);
            if(hot>=0)using(var fill=new SolidBrush(StudioStyle.Soft))g.FillRectangle(fill,bounds.X+w*hot,bounds.Y,w,bounds.Height);
            using(var line=new Pen(StudioStyle.Line))
                for(int i=1;i<3;i++)g.DrawLine(line,bounds.X+w*i,bounds.Top,bounds.X+w*i,bounds.Bottom);
            var selection=new RectangleF(bounds.X+scenePosition*w,bounds.Y,w+.5f,bounds.Height);
            using(var fill=new SolidBrush(Color.FromArgb((int)(255*sceneOpacity),teal)))g.FillRectangle(fill,selection);
            for(int i=0;i<3;i++){
                float x=bounds.X+w*i;
                var glyph=new RectangleF(x,729,w,32); var label=new RectangleF(x,762,w,29);
                PaintText(g,glyph,icons[i],StudioStyle.Symbol,ink,true);
                PaintText(g,label,labels[i],regular,ink,true);
                if(sceneOpacity>0 && selection.IntersectsWith(label)){
                    var crop=g.Save();g.SetClip(selection,CombineMode.Intersect);
                    Color reverse=StudioStyle.Blend(ink,Color.White,sceneOpacity);
                    PaintText(g,glyph,icons[i],StudioStyle.Symbol,reverse,true);
                    PaintText(g,label,labels[i],regular,reverse,true);
                    g.Restore(crop);
                }
            }
            g.Restore(state);
            using(var edge=new Pen(StudioStyle.Line))g.DrawPath(edge,outer);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Width<2||Height<2||skin==null)return;
        var g=e.Graphics;g.ScaleTransform(Width/1497f,Height/966f);g.TranslateTransform(-45,-12);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.DrawImage(skin,new Rectangle(45,12,1497,966));
        if(window.WindowState==FormWindowState.Maximized){
            // Extend the neighboring material over the reference image’s outer corners.
            foreach(int x in new[]{45,1524})foreach(int y in new[]{12,960})
                g.DrawImage(skin,new Rectangle(x,y,18,18),new Rectangle(x==45?19:1477,y-12,1,18),GraphicsUnit.Pixel);
        }
        DrawRail(g);
        if(!overview){
            Fill(g,Theme.Back,318,58,1224,866);
            int idx=Array.IndexOf(pages,page);
            PaintText(g,new RectangleF(353,78,870,59),captions[idx],headline,ink);
            PaintText(g,new RectangleF(355,133,868,35),subtitles[idx],regular,muted);
            StudioStyle.Box(g,new RectangleF(1245,101,271,48),11,hovered=="Clock connection"?Color.FromArgb(15,96,85):teal,Color.Transparent);
            PaintText(g,new RectangleF(1245,101,271,48),clock.Busy?"Reading settings…":clock.PortOpen?"Read settings":"Connect clock",bold,Color.White,true);
        } else DrawOverview(g);
        string state=clock.Synchronized?"Connected":clock.PortOpen?"Reading clock":"Disconnected";
        
        PaintText(g,new RectangleF(120,917,178,26),clock.PortOpen?clock.Port+" · "+state:"Preview · Offline",regular,ink);
        
        string footer=DateTime.UtcNow<noticeUntil?notice:clock.Status;
        if(clock.Synchronized && !clock.Busy && DateTime.UtcNow>=noticeUntil)
            footer+="  ·  "+(clock.DisplayMode>=2?"Timer / anniversary preview unavailable":clock.PreviewEpoch.HasValue?"Set-time preview":"Computer-time preview");
        using(var f=new StringFormat{Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Center})
        using(var b=new SolidBrush(muted))g.DrawString(footer,regular,b,new RectangleF(741,931,773,34),f);
        if(keyFocus&&focusIndex>=0&&VisibleHit(hits[focusIndex])){
            var r=Rectangle.Inflate(hits[focusIndex].Bounds,-2,-2);
            using(var path=Theme.Round(r,10))using(var pen=new Pen(teal,2))g.DrawPath(pen,path);
        }
    }
    void DrawOverview(Graphics g)
    {
        DateTime time=clock.PreviewEpoch.HasValue?clock.PreviewEpoch.Value+(DateTime.UtcNow-clock.EpochReceived):DateTime.Now;
        string digits=clock.DisplayMode==1?time.ToString("yyMMdd"):time.ToString(hours24?"HHmmss":"hhmmss");
        bool powered=clock.SwitchState(0x23)!=false;
        for(int i=0;i<6;i++){
            int d=clock.DisplayMode>=2||!powered||(i>3&&!seconds)?10:digits[i]-'0';
            var dest=new Rectangle(centers[i]-43,321,86,125);
            using(var attr=new ImageAttributes()){
                var matrix=new ColorMatrix();matrix.Matrix33=d==10?1f:.1f+.9f*percent/100f;attr.SetColorMatrix(matrix);
                g.DrawImage(faces,dest,86*d,0,86,125,GraphicsUnit.Pixel,attr);
            }
            if(clock.PreviewColor.HasValue&&powered){using(var path=new GraphicsPath()){
                path.AddEllipse(centers[i]-51,452,102,38);
                using(var glow=new PathGradientBrush(path)){glow.CenterColor=System.Drawing.Color.FromArgb(142,clock.PreviewColor.Value);glow.SurroundColors=new[]{System.Drawing.Color.Transparent};g.FillPath(glow,path);}
            }}
        }
        if(powered && !(clock.DisplayMode>=2)){
            g.DrawImage(separators[0],762,353,38,74);
            if(seconds)g.DrawImage(separators[1],1045,353,38,74);
        }
         PaintText(g,new RectangleF(371,230,369,32),time.ToString("dddd, MMM d"),bold,white);
        PaintText(g,new RectangleF(1332,204,175,30),clock.Synchronized?"●  Connected":clock.PortOpen?"◌  Reading":"○  Not connected",bold,white);
        PaintText(g,new RectangleF(1366,234,147,27),clock.PortOpen?clock.Port:"Preview mode",regular,white);
        
        using(var p=new Pen(Color.Gray,8)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,482,562,788,562);}
        int thumb=482+306*percent/100;
        using(var p=new Pen(Color.FromArgb(44,163,151),8)){p.StartCap=p.EndCap=LineCap.Round;g.DrawLine(p,482,562,thumb,562);}
        using(var b=new SolidBrush(white))g.FillEllipse(b,thumb-12,550,24,24);
        PaintText(g,new RectangleF(814,544,73,36),percent+"%",regular,white);
        TogglePaint(g,1014,hourPosition);TogglePaint(g,1198,secondPosition);
        PaintText(g,new RectangleF(509,697,143,32),clock.PortOpen?clock.Port:"Choose port",regular,ink);
        PaintText(g,new RectangleF(383,757,294,42),clock.Busy?"Please wait…":clock.PortOpen?"Read settings":"Connect",bold,white,true);
        PaintText(g,new RectangleF(385,850,219,36),clock.Synchronized?"Clock connected":clock.PortOpen?"Reading settings":"Ready for preview",regular,muted);
        PaintText(g,new RectangleF(596,850,116,36),clock.IsBluetooth?"BLE": "115200 baud",regular,muted);
        PaintText(g,new RectangleF(830,692,226,36),edit.ToString("MMM d, yyyy"),regular,ink);
        PaintText(g,new RectangleF(830,737,226,36),edit.ToString("HH:mm"),regular,ink);
        DrawScenes(g);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){motion.Dispose(); navActive.Dispose();foreach(var lamp in separators)lamp.Dispose();tips.Dispose();menu.Dispose();if(picker!=null)picker.Dispose();skin.Dispose();faces.Dispose();regular.Dispose();bold.Dispose();headline.Dispose();}
        base.Dispose(disposing);
    }
}

